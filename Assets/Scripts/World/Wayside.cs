using System.Collections.Generic;
using UnityEngine;
using Jogging.Route;

namespace Jogging.World
{
    /// <summary>
    /// What stands beside the trail — sparingly, the way a real forest path looks. Planned once per route
    /// (deterministic from its seed, <see cref="MakePlan"/>) and built in a window around the runner:
    /// <list type="bullet">
    /// <item>visual side paths (hiking spurs and forest roads with tyre tracks) that lead off into the
    /// landscape and fade out; trees and grass keep off them,</item>
    /// <item>signs of people: signposts at junctions, benches (sometimes with a bin), an info board at the
    /// start, boundary stones, barriers on forest roads, a shelter,</item>
    /// <item>forestry: log piles, felled logs, fresh stumps, a plantation with tree guards, a fallen tree,</item>
    /// <item>forest edge and fields: hunting stands, power lines, pasture fences, hay bales, a field barn, crows,</item>
    /// <item>near a village (route light pollution): street lamps, a traffic sign, bus stop, dog-bag dispenser,
    /// garden fence, a parked car,</item>
    /// <item>at most two landmarks: wayside cross, chapel, ruin, lookout at the highest point,</item>
    /// <item>right at the path (per 40 m): ferns, nettles, branches, stumps, roots, leaf or needle piles,
    /// mushrooms in autumn, ant hills, rooted-up soil, puddles and mud (more after rain).</item>
    /// </list>
    /// Noticeable things keep at least 120 m apart, so nothing is crowded.
    /// </summary>
    public class Wayside : MonoBehaviour
    {
        public enum Kind
        {
            Spur, ForestRoad, Signpost, Bench, Shelter, InfoBoard, HuntingStand, LogPile, FelledLogs, FallenTree, Barrier,
            BoundaryStone, TreeGuards, PowerLine, Fence, HayBales, FieldBarn, WaysideCross, Chapel, Ruin, Lookout, AntHill,
            Diggings, Village, Crows, TreeMark, NestBox,
        }

        public struct Feature
        {
            public Kind kind;
            public float s, side, lateral, length, angle;
            public int seed;
            public List<Vector2> line; // spurs, roads, power lines, fences: the ground plan (world XZ)
        }

        public static readonly List<Feature> Plan = new List<Feature>();
        private static readonly List<(Vector2 c, float r)> blockers = new List<(Vector2, float)>();
        private static float routeLength;
        private static bool routeLoop;

        private const int MinRes = 200;          // only on detailed terrain tiles (as the trees)
        private const float Chunk = 40f, Ahead = 480f, Behind = 60f;

        // ------------------------------------------------------------------ plan
        /// <summary>No wayside for this route (tests: -nowayside): plan and blockers empty.</summary>
        public static void ClearPlan() { Plan.Clear(); blockers.Clear(); grid.Clear(); }

        /// <summary>Plans everything along the route (before trees are scattered: they keep off it).</summary>
        public static void MakePlan(RouteDoc doc, TrailPath path)
        {
            Plan.Clear(); blockers.Clear(); grid.Clear(); TrailGrassClearer.ForgetStrips();
            if (doc == null || path == null) return;
            var p = doc.@params;
            float L = path.Length;
            routeLength = L; routeLoop = path.Loop;
            var rnd = new System.Random(doc.generator.seed * 7 + 991);
            float R() => (float)rnd.NextDouble();
            var noticeable = new List<float>();
            bool Free(float s, float gap) { foreach (var o in noticeable) if (Mathf.Abs(Dist(o, s)) < gap) return false; return true; }
            bool Forest(float s) => Vegetation.At(s).trees > 0.55f;
            bool Open(float s) => Vegetation.At(s).trees < 0.3f;
            bool NearWater(float s) { foreach (var st in Lakes.Streams) if (Mathf.Abs(Dist(st.s, s)) < 40f) return true; return Lakes.Inside(path.Point(s), 12f); }
            float end = p.endless ? Mathf.Min(L, 30000f) : L;
            bool Inside(float s) => p.endless || routeLoop || (s > 60f && s < end - 60f);
            void Add(Feature f, bool big = true) { Plan.Add(f); if (big) noticeable.Add(f.s); }
            float Side() => R() < 0.5f ? -1f : 1f;

            // near a village: the first stretch shows it (lamps, sign, bus stop …)
            if (p.lightPollution >= 0.45f && end > 1200f)
                Add(new Feature { kind = Kind.Village, s = 80f, side = Side(), length = 170f, seed = rnd.Next() });
            if (end > 2000f)
                Add(new Feature { kind = Kind.InfoBoard, s = 30f, side = 1f, lateral = 3.4f, seed = rnd.Next() });

            // side paths and forest roads, with a signpost or a barrier and a log pile
            for (float s = 220f + R() * 200f; s < end - 120f; s += 380f + R() * 480f)
            {
                if (!Inside(s) || NearWater(s) || !Free(s, 110f)) continue;
                bool road = Forest(s) && R() < 0.4f;
                var f = new Feature { kind = road ? Kind.ForestRoad : Kind.Spur, s = s, side = Side(), angle = (35f + R() * 40f) * (R() < 0.5f ? 1f : -1f), length = road ? 45f + R() * 40f : 30f + R() * 40f, seed = rnd.Next() };
                f.line = SpurLine(path, f, R);
                if (!SpurClear(path, f)) continue;
                Add(f);
                foreach (var q in f.line) blockers.Add((q, road ? 3.2f : 2.2f));
                if (!road && R() < 0.6f) Plan.Add(new Feature { kind = Kind.Signpost, s = s - 3.5f * Mathf.Sign(f.angle), side = f.side, lateral = 2.3f, angle = f.angle, seed = rnd.Next() });
                if (road && R() < 0.35f) Plan.Add(new Feature { kind = Kind.Barrier, s = s, side = f.side, lateral = 9f, angle = f.angle, seed = rnd.Next(), line = f.line });
                if (road && R() < 0.45f) Plan.Add(new Feature { kind = Kind.LogPile, s = s, side = f.side, lateral = 16f, angle = f.angle, seed = rnd.Next(), line = f.line });
            }

            // benches: where there is something to see (clearing, meadow, water); once a shelter in the forest
            bool shelter = end > 4000f && R() < 0.5f;
            for (float s = 400f + R() * 400f; s < end - 100f; s += 900f + R() * 600f)
            {
                float best = -1f;
                for (float t = s; t < s + 300f && t < end - 100f; t += 25f) if (Inside(t) && (Open(t) || NearWater(t)) && Free(t, 120f)) { best = t; break; }
                if (best < 0f && shelter && Forest(s) && Free(s, 120f)) { float ss = Side(); Add(new Feature { kind = Kind.Shelter, s = s, side = ss, lateral = 6.5f, seed = rnd.Next() }); shelter = false; blockers.Add((path.Offset(s, ss * 6.5f).XZ(), 3.5f)); continue; }
                if (best < 0f) continue;
                Add(new Feature { kind = Kind.Bench, s = best, side = Side(), lateral = 2.7f, seed = rnd.Next(), length = R() < 0.4f ? 1f : 0f });
            }

            // along the way: forest, forest edge, open land
            for (float s = 150f; s < end - 80f; s += 20f)
            {
                if (!Inside(s)) continue;
                bool forest = Forest(s), open = Open(s);
                bool edge = open && (Forest(s - 90f) || Forest(s + 90f));
                if (edge && R() < 0.05f && Free(s, 700f))
                {
                    float side = Side(), lat = 22f + R() * 16f;
                    Add(new Feature { kind = Kind.HuntingStand, s = s, side = side, lateral = lat, seed = rnd.Next() });
                    blockers.Add((path.Offset(s, side * lat).XZ(), 3f));
                }
                else if (forest && R() < 0.018f && Free(s, 250f))
                {
                    Kind k = R() < 0.5f ? Kind.LogPile : R() < 0.6f ? Kind.FelledLogs : Kind.FallenTree;
                    float side = Side(), lat = k == Kind.FallenTree ? 7f + R() * 6f : 4.5f + R() * 2.5f;
                    Add(new Feature { kind = k, s = s, side = side, lateral = lat, angle = R() * 30f - 15f, seed = rnd.Next() });
                    blockers.Add((path.Offset(s, side * lat).XZ(), k == Kind.FallenTree ? 5f : 4f));
                }
                else if (open && R() < 0.012f && Free(s, 300f))
                {
                    float side = Side(), lat = 26f + R() * 30f;
                    if (p.season != "winter" && p.season != "spring" && R() < 0.55f) { Add(new Feature { kind = Kind.HayBales, s = s, side = side, lateral = lat, seed = rnd.Next() }); blockers.Add((path.Offset(s, side * lat).XZ(), 9f)); }
                    else if (R() < 0.5f) { Add(new Feature { kind = Kind.Crows, s = s, side = side, lateral = 14f + R() * 20f, seed = rnd.Next() }, false); }
                    else { float gl = 18f + R() * 10f; Add(new Feature { kind = Kind.TreeGuards, s = s, side = side, lateral = gl, seed = rnd.Next() }); blockers.Add((path.Offset(s, side * (gl + 4f)).XZ(), 10f)); }
                }
                // small things: boundary stones, ant hills (pines), rooted-up soil, tree marks and nest boxes
                if (R() < 0.022f && Free(s, 40f)) Add(new Feature { kind = Kind.BoundaryStone, s = s, side = Side(), lateral = 1.95f, seed = rnd.Next() }, false);
                if (forest && Vegetation.At(s).pine > 0.5f && R() < 0.02f) Add(new Feature { kind = Kind.AntHill, s = s, side = Side(), lateral = 4f + R() * 4f, seed = rnd.Next() }, false);
                if (forest && R() < 0.015f) Add(new Feature { kind = Kind.Diggings, s = s, side = Side(), lateral = 3f + R() * 6f, seed = rnd.Next() }, false);
                if (forest && R() < 0.035f) Add(new Feature { kind = Kind.TreeMark, s = s, side = Side(), seed = rnd.Next() }, false);
                if (forest && R() < 0.012f) Add(new Feature { kind = Kind.NestBox, s = s, side = Side(), seed = rnd.Next() }, false);
            }

            // open stretches ≥ 250 m: a power line or a pasture fence now and then; once a field barn
            bool barn = R() < 0.4f;
            for (float s = 150f; s < end - 250f; s += 50f)
            {
                if (!Inside(s) || !Open(s)) continue;
                float run = 0f;
                while (s + run < end - 60f && Open(s + run) && run < 600f) run += 25f;
                if (run < 250f) { s += run; continue; }
                float side = Side();
                if (R() < 0.45f)
                {
                    var line = new List<Vector2>();
                    float lat = 24f + R() * 12f;
                    for (float t = s; t < s + run; t += 45f) { var q = path.Offset(t, side * (lat + Mathf.Sin(t * 0.01f) * 4f)).XZ(); if (!Lakes.Inside(q.X0Z(), 4f)) line.Add(q); }
                    if (line.Count >= 3) Add(new Feature { kind = Kind.PowerLine, s = s, side = side, length = run, seed = rnd.Next(), line = line }, false);
                }
                else if (R() < 0.6f)
                {
                    var line = new List<Vector2>();
                    float lat = 7f + R() * 2f, len = Mathf.Min(run, 80f + R() * 90f);
                    for (float t = s + 20f; t < s + 20f + len; t += 3f) line.Add(path.Offset(t, -side * lat).XZ());
                    Add(new Feature { kind = Kind.Fence, s = s + 20f, side = -side, length = len, seed = rnd.Next(), line = line }, false);
                }
                if (barn && Free(s + run * 0.5f, 150f))
                {
                    float lat = 32f + R() * 25f;
                    Add(new Feature { kind = Kind.FieldBarn, s = s + run * 0.5f, side = side, lateral = lat, seed = rnd.Next() });
                    blockers.Add((path.Offset(s + run * 0.5f, side * lat).XZ(), 6f));
                    barn = false;
                }
                s += run;
            }

            // landmarks: at most two — a lookout on the highest point, a wayside cross, a chapel, a ruin
            int landmarks = 0;
            var prof = doc.profile;
            if (p.relief >= 0.5f && prof != null && prof.heightsM != null && prof.heightsM.Length > 10 && R() < 0.7f)
            {
                int hi = 0;
                for (int i = 1; i < prof.heightsM.Length; i++) if (prof.heightsM[i] > prof.heightsM[hi]) hi = i;
                float s = hi * prof.stepM;
                if (Inside(s) && Free(s, 120f)) { Add(new Feature { kind = Kind.Lookout, s = s, side = Side(), lateral = 3f, seed = rnd.Next() }); landmarks++; }
            }
            foreach (var k in new[] { Kind.WaysideCross, Kind.Chapel, Kind.Ruin })
            {
                if (landmarks >= 2) break;
                float chance = k == Kind.WaysideCross ? 0.45f : k == Kind.Chapel ? 0.25f : 0.18f;
                if (R() > chance) continue;
                for (int tries = 0; tries < 12; tries++)
                {
                    float s = 300f + R() * Mathf.Max(1f, end - 600f);
                    bool ok = k == Kind.Ruin ? Forest(s) : Open(s) || k == Kind.WaysideCross;
                    if (!ok || !Inside(s) || NearWater(s) || !Free(s, 150f)) continue;
                    float side = Side(), lat = k == Kind.WaysideCross ? 3.2f : k == Kind.Chapel ? 22f + R() * 15f : 18f + R() * 14f;
                    Add(new Feature { kind = k, s = s, side = side, lateral = lat, seed = rnd.Next() });
                    blockers.Add((path.Offset(s, side * lat).XZ(), k == Kind.Ruin ? 7f : k == Kind.Chapel ? 5f : 1.5f));
                    landmarks++;
                    break;
                }
            }
            // placed in the workshop: exactly where the runner put them (no spacing rules)
            if (doc.edits != null)
                foreach (var e in doc.edits)
                {
                    if (e.type != "object" || !System.Enum.TryParse(e.what, out Kind k)) continue;
                    float side = e.side == "left" ? -1f : 1f;
                    float lat = k == Kind.Bench ? 2.7f : k == Kind.Signpost ? 2.3f : k == Kind.WaysideCross ? 3.2f : k == Kind.InfoBoard ? 3.4f
                              : k == Kind.LogPile ? 5.5f : k == Kind.Shelter ? 6.5f : k == Kind.HuntingStand ? 24f : k == Kind.HayBales ? 30f : 26f;
                    Plan.Add(new Feature { kind = k, s = e.atM, side = side, lateral = lat, angle = 50f, seed = Mathf.RoundToInt(e.atM * 13f) });
                    blockers.Add((path.Offset(e.atM, side * lat).XZ(), k == Kind.HayBales ? 9f : k == Kind.Chapel || k == Kind.FieldBarn ? 6f : k == Kind.Shelter ? 3.5f : 2f));
                }
            Plan.Sort((a, b) => a.s.CompareTo(b.s));
            var counts = new Dictionary<Kind, int>();
            foreach (var f in Plan) counts[f.kind] = counts.TryGetValue(f.kind, out int c) ? c + 1 : 1;
            var sb = new System.Text.StringBuilder();
            foreach (var kv in counts) sb.Append($"{kv.Key} {kv.Value}, ");
            Debug.Log($"[Wegrand] Plan für {end / 1000f:0.0} km: {sb}");
        }

        private static float Dist(float a, float b)
        {
            float d = b - a;
            if (routeLoop && routeLength > 0f) { d = Mathf.Repeat(d + routeLength * 0.5f, routeLength) - routeLength * 0.5f; }
            return d;
        }

        // A side path: leaves the trail at an angle, wanders a little, points every 2.5 m (world XZ)
        private static List<Vector2> SpurLine(TrailPath path, Feature f, System.Func<float> R)
        {
            var pts = new List<Vector2>();
            Vector3 start = path.Offset(f.s, f.side * 0.6f);
            Vector3 fwd = path.Tangent(f.s), right = path.Right(f.s);
            float ang = Mathf.Abs(f.angle) * Mathf.Deg2Rad;
            Vector3 dir = (right * f.side * Mathf.Sin(ang) + fwd * Mathf.Cos(ang) * Mathf.Sign(f.angle)).normalized;
            Vector2 p = new Vector2(start.x, start.z), d = new Vector2(dir.x, dir.z);
            float turn = (R() - 0.5f) * 0.05f;
            for (float t = 0f; t <= f.length; t += 2.5f)
            {
                pts.Add(p);
                turn += (R() - 0.5f) * 0.03f; turn = Mathf.Clamp(turn, -0.05f, 0.05f);
                d = Rotate(d, turn); p += d * 2.5f;
            }
            return pts;
        }

        private static Vector2 Rotate(Vector2 v, float a) => new Vector2(v.x * Mathf.Cos(a) - v.y * Mathf.Sin(a), v.x * Mathf.Sin(a) + v.y * Mathf.Cos(a));

        // A spur must not come back near the trail, nor run into water
        private static bool SpurClear(TrailPath path, Feature f)
        {
            for (int i = 4; i < f.line.Count; i++)
            {
                var w = f.line[i].X0Z();
                if (Lakes.Inside(w, 3f)) return false;
                path.Project(w, f.s, 400f, out float lat);
                if (Mathf.Abs(lat) < 6f + i * 0.4f) return false;
            }
            return true;
        }

        /// <summary>True if a tree (or bush) at w would stand on a side path or a planned object.</summary>
        public static bool Blocks(Vector3 w, float margin)
        {
            if (grid.Count == 0 && blockers.Count > 0) BuildGrid();
            var q = new Vector2(w.x, w.z);
            if (!grid.TryGetValue(Cell(q), out var list)) return false;
            foreach (var i in list) { var b = blockers[i]; if ((q - b.c).sqrMagnitude < (b.r + margin) * (b.r + margin)) return true; }
            return false;
        }

        // blockers by 16 m cell (each blocker in every cell its circle + a margin of 3 m touches): a lookup is a few checks
        private const float GridCell = 16f;
        private static readonly Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();
        private static long Cell(Vector2 q) => ((long)Mathf.FloorToInt(q.x / GridCell) << 32) ^ (uint)Mathf.FloorToInt(q.y / GridCell);
        private static void BuildGrid()
        {
            grid.Clear();
            for (int i = 0; i < blockers.Count; i++)
            {
                var b = blockers[i]; float r = b.r + 3f;
                int x0 = Mathf.FloorToInt((b.c.x - r) / GridCell), x1 = Mathf.FloorToInt((b.c.x + r) / GridCell);
                int y0 = Mathf.FloorToInt((b.c.y - r) / GridCell), y1 = Mathf.FloorToInt((b.c.y + r) / GridCell);
                for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++)
                {
                    long key = ((long)x << 32) ^ (uint)y;
                    if (!grid.TryGetValue(key, out var l)) grid[key] = l = new List<int>();
                    l.Add(i);
                }
            }
        }

        // ------------------------------------------------------------------ build around the runner
        private readonly Dictionary<int, GameObject> chunks = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> built = new Dictionary<int, GameObject>(); // feature index → object
        private readonly List<int> scratch = new List<int>();
        private readonly Dictionary<int, float> retryAt = new Dictionary<int, float>();
        private float nextRibbonLookup;
        private Material trailMat;
        private static readonly bool diag = Jogging.Core.Args.Has("-diag");

        public static int BuiltCount { get; private set; }
        private static Wayside active;
        private void Awake()
        {
            active = this;
            Sights.Clear(); System.Array.Clear(SurfaceMetres, 0, SurfaceMetres.Length); lastRunnerS = -1f; // a new scene = a new run
        }

        // ------------------------------------------------------------------ what the run passed (logbook)
        /// <summary>Sights passed in this run (German keys, counted) — for the finish screen and the logbook.</summary>
        public static readonly Dictionary<string, int> Sights = new Dictionary<string, int>();
        /// <summary>Metres run on each surface in this run (asphalt, gravel, earth, forest floor, meadow path).</summary>
        public static readonly float[] SurfaceMetres = new float[TrailSurface.Count];
        private static float lastRunnerS = -1f;

        private static string SightName(Kind k) => k switch
        {
            Kind.Chapel => "Kapelle", Kind.WaysideCross => "Wegkreuz", Kind.Ruin => "Ruine", Kind.Lookout => "Aussichtspunkt",
            Kind.Shelter => "Schutzhütte", Kind.HuntingStand => "Hochsitz", Kind.FieldBarn => "Feldscheune", Kind.LogPile => "Holzpolter",
            Kind.FallenTree => "umgestürzter Baum", Kind.Village => "Ortsrand", Kind.Crows => "Krähen", Kind.AntHill => "Ameisenhaufen",
            _ => null,
        };

        private static void Note(string key) => Sights[key] = Sights.TryGetValue(key, out int n) ? n + 1 : 1;

        private void LogRun(TrailPath path)
        {
            var sm = Jogging.UI.RunSessionUI.Session;
            float z = path.RunnerS;
            if (sm == null || !sm.Moves) { lastRunnerS = z; return; }
            if (lastRunnerS < 0f) { lastRunnerS = z; return; }
            float ds = z - lastRunnerS;
            if (ds <= 0f || ds > 60f) { lastRunnerS = z; return; } // standing, or a jump (tests)
            SurfaceMetres[(int)TrailSurface.At(z)] += ds;
            float a = lastRunnerS, b = z;
            float L = path.Loop ? path.Length : 0f;
            bool Crossed(float s) { if (L > 0f) { s = Mathf.Repeat(s, L); float aa = Mathf.Repeat(a, L); float bb = aa + ds; return s > aa && s <= bb || s + L > aa && s + L <= bb; } return s > a && s <= b; }
            foreach (var f in Plan) { var n = SightName(f.kind); if (n != null && Crossed(f.s)) Note(n); }
            foreach (var st in Lakes.Streams) if (Crossed(st.s)) Note("Brücke");
            lastRunnerS = z;
        }

        /// <summary>"Kapelle · 2× Brücke · Hochsitz" from logbook entries ("Brücke:2" …), translated; "" if none.</summary>
        public static string SightsText(IEnumerable<string> entries)
        {
            var parts = new List<string>();
            foreach (var e in entries)
            {
                int c = e.IndexOf(':');
                string key = c > 0 ? e.Substring(0, c) : e, n = c > 0 ? e.Substring(c + 1) : "";
                parts.Add(n != "" ? $"{n}× {Jogging.Core.Loc.T(key)}" : Jogging.Core.Loc.T(key));
            }
            return string.Join(" · ", parts);
        }

        private static readonly string[] SurfaceNames = { "Asphalt", "Schotter", "Erdweg", "Waldweg", "Wiesenweg" };

        /// <summary>"Waldweg 2,1 km · Asphalt 0,4 km" (surfaces with a share of at least 5 %), "" if unknown.</summary>
        public static string SurfacesText(IList<float> metres)
        {
            if (metres == null || metres.Count < TrailSurface.Count) return "";
            float total = 0f; foreach (var m in metres) total += m;
            if (total < 50f) return "";
            var idx = new List<int> { 0, 1, 2, 3, 4 };
            idx.Sort((x, y) => metres[y].CompareTo(metres[x]));
            var parts = new List<string>();
            foreach (int i in idx) if (metres[i] >= total * 0.05f) parts.Add($"{Jogging.Core.Loc.T(SurfaceNames[i])} {Jogging.Core.Units.FmtDist(metres[i])}");
            return string.Join(" · ", parts);
        }

        /// <summary>After a workshop change: plan again, rebuild what stands, trees keep off the new objects.</summary>
        public static void Replan()
        {
            MakePlan(RouteRuntime.Current, TrailPath.Active);
            if (active != null)
            {
                foreach (var g in active.built.Values) if (g != null) Destroy(g);
                active.built.Clear(); active.retryAt.Clear();
                foreach (var g in active.chunks.Values) if (g != null) Destroy(g);
                active.chunks.Clear();
            }
            TerrainTreeScatter.RebuildAll();
        }

        /// <summary>Tests: has the first planned object of this kind been built (in the window)?</summary>
        public static bool IsBuilt(Kind kind)
        {
            if (active == null) return false;
            for (int i = 0; i < Plan.Count; i++) if (Plan[i].kind == kind) return active.built.TryGetValue(i, out var g) && g != null;
            return false;
        }
        private float nextFlock = 25f;
        private readonly System.Random flockRnd = new System.Random(77);

        private void Update()
        {
            long t0 = FrameWork.Start();
            UpdateWork();
            FrameWork.Stop("Wegrand", t0);
        }

        private void UpdateWork()
        {
            var path = TrailPath.Active;
            if (path == null || RouteRuntime.Current == null) return;
            float z = path.RunnerS;
            LogRun(path);
            if (trailMat == null && Time.unscaledTime >= nextRibbonLookup)
            {
                nextRibbonLookup = Time.unscaledTime + 1f;
                var rib = FindFirstObjectByType<TrailRibbon>();
                if (rib != null) trailMat = rib.GetComponent<MeshRenderer>().sharedMaterial;
            }

            // features: build when in the window, drop when behind
            int budget = 2, attempts = 6;
            for (int i = 0; i < Plan.Count; i++)
            {
                float d = Dist(z, Plan[i].s);
                bool want = d > -Behind - Plan[i].length && d < Ahead;
                if (!want) { if (built.TryGetValue(i, out var go)) { if (go != null) Destroy(go); built.Remove(i); } continue; }
                if (built.ContainsKey(i) || budget <= 0 || attempts <= 0) continue;
                // a failed build (no detailed ground yet, no tree for a mark) waits a moment before it tries again
                if (retryAt.TryGetValue(i, out float t) && Time.unscaledTime < t) continue;
                attempts--;
                var obj = Build(Plan[i], path);
                if (obj != null) { built[i] = obj; budget--; retryAt.Remove(i); }
                else retryAt[i] = Time.unscaledTime + 0.8f;
            }

            // small things right at the path, per chunk
            float len = ChunkLen(path);
            int first = Mathf.FloorToInt((z - Behind) / len), last = Mathf.FloorToInt((z + Ahead) / len);
            scratch.Clear();
            foreach (var k in chunks.Keys) if (k < first || k > last) scratch.Add(k);
            foreach (var k in scratch) { if (chunks[k] != null) Destroy(chunks[k]); chunks.Remove(k); }
            for (int c = first; c <= last; c++)
            {
                if (chunks.ContainsKey(c)) continue;
                var go = BuildEdge(c, path);
                if (go != null) { chunks[c] = go; break; }
            }
            BuiltCount = built.Count + chunks.Count;

            // now and then a flock high overhead (by day, not in rain)
            var sky = Sky.Instance; var cam = Camera.main;
            if (cam != null && sky != null && Time.time > nextFlock)
            {
                nextFlock = Time.time + 70f + (float)flockRnd.NextDouble() * 110f;
                if (sky.SunElevation > 3f && RouteRuntime.Current.@params.weather != "rain" && RouteRuntime.Current.@params.weather != "snow")
                {
                    Vector3 fwd = cam.transform.forward; fwd.y = 0f; fwd.Normalize();
                    Vector3 across = Vector3.Cross(Vector3.up, fwd) * (flockRnd.NextDouble() < 0.5 ? 1f : -1f);
                    Vector3 start = cam.transform.position + fwd * (120f + (float)flockRnd.NextDouble() * 120f) - across * 260f + Vector3.up * (55f + (float)flockRnd.NextDouble() * 50f);
                    WaysideBirds.Flock(transform, start, (across + fwd * 0.2f).normalized, flockRnd.Next());
                }
            }
        }

        private static bool Ground(Vector2 q, out float h) => TerrainGround.TryHeight(q.x, q.y, out h, MinRes);

        // Loops: whole chunks per lap (lap / n), so chunk c + n is exactly one lap after c (a fixed 40 m slid per lap)
        private static float ChunkLen(TrailPath path) =>
            path.Loop && path.Length > 0f ? path.Length / Mathf.Max(1, Mathf.RoundToInt(path.Length / Chunk)) : Chunk;

        private GameObject Build(Feature f, TrailPath path)
        {
            Vector3 foot = path.Offset(f.s, f.side * f.lateral);
            if (!Ground(foot.XZ(), out float h)) { Why(f, "kein Boden (Kachel noch grob)"); return null; }
            float span = f.kind == Kind.Village || f.kind == Kind.Fence || f.kind == Kind.PowerLine ? f.length + 20f : 10f;
            if (!TrailShaper.Ready(f.s - 10f, f.s + span)) { Why(f, "Wegbett noch nicht fertig"); return null; }
            foot.y = h;
            // facing the trail (+z of the prop looks at the trail)
            Quaternion toTrail = Quaternion.LookRotation(-path.Right(f.s) * f.side, Vector3.up);
            Quaternion along = path.Rotation(f.s);
            GameObject go = null;
            var parent = transform;
            switch (f.kind)
            {
                case Kind.Spur: case Kind.ForestRoad: go = BuildSpur(f); break;
                case Kind.Signpost:
                {
                    var names = Destinations(f.seed);
                    float spurYaw = SpurYaw(f, path);
                    float fwdYaw = along.eulerAngles.y;
                    go = WaysideProps.Signpost(parent, new[] { (fwdYaw, names[0]), (fwdYaw + 180f, names[1]), (spurYaw, names[2]) }, f.seed);
                    go.transform.position = foot + Vector3.down * 0.05f;
                    break;
                }
                case Kind.Bench:
                    go = new GameObject("Bank"); go.transform.SetParent(parent, false);
                    WaysideProps.Place("painted_wooden_bench", go.transform, Vector3.zero, 0f);
                    if (f.length > 0f) WaysideProps.Place("metal_trash_can_" + (f.seed % 2 == 0 ? "a" : "b"), go.transform, new Vector3(1.1f, 0f, 0.1f), 180f);
                    go.transform.SetPositionAndRotation(foot, toTrail);
                    break;
                case Kind.Shelter: go = WaysideProps.Shelter(parent, f.seed); go.transform.SetPositionAndRotation(foot, toTrail); break;
                case Kind.InfoBoard:
                    go = WaysideProps.InfoBoard(parent, RouteRuntime.Current.meta.name, RouteShape(path), f.seed);
                    go.transform.SetPositionAndRotation(foot, Quaternion.LookRotation((-path.Tangent(f.s) - path.Right(f.s) * f.side * 0.7f).normalized, Vector3.up)); break; // faces the runners coming along
                case Kind.HuntingStand:
                    go = WaysideProps.HuntingStand(parent, f.seed);
                    go.transform.SetPositionAndRotation(foot, Quaternion.LookRotation(path.Right(f.s) * f.side, Vector3.up) * Quaternion.Euler(0f, (f.seed % 60) - 30f, 0f)); // looks out over the open land
                    break;
                case Kind.LogPile:
                    if (f.line != null && f.line.Count > 8)
                    {
                        // beside the forest road, 15 m in: ground first, then build (no half-built pile left behind)
                        var a = f.line[6]; var b = f.line[7]; Vector2 dd = (b - a).normalized; Vector2 sideV = new Vector2(dd.y, -dd.x) * 3.2f;
                        foot = (a + sideV).X0Z(); if (!Ground(foot.XZ(), out h)) return null; foot.y = h;
                        go = WaysideProps.LogPile(parent, f.seed, 4 + f.seed % 3);
                        go.transform.SetPositionAndRotation(foot, Quaternion.LookRotation(new Vector3(-dd.y, 0f, dd.x)));
                    }
                    else { go = WaysideProps.LogPile(parent, f.seed, 4 + f.seed % 3); go.transform.SetPositionAndRotation(foot, along * Quaternion.Euler(0f, 90f + f.angle, 0f)); }
                    break;
                case Kind.FelledLogs: go = WaysideProps.FelledLogs(parent, f.seed); go.transform.SetPositionAndRotation(foot, along * Quaternion.Euler(0f, 90f + f.angle, 0f)); break;
                case Kind.FallenTree:
                    go = new GameObject("Umgestürzt"); go.transform.SetParent(parent, false);
                    WaysideProps.Place("dead_tree_trunk_02", go.transform, Vector3.zero, 0f);
                    go.transform.SetPositionAndRotation(foot + Vector3.down * 0.15f, along * Quaternion.Euler(0f, 60f + f.angle * 2f, 0f));
                    break;
                case Kind.Barrier:
                {
                    if (f.line == null || f.line.Count < 5) return null;
                    Vector2 a = f.line[3], b = f.line[4];
                    foot = a.X0Z(); if (!Ground(a, out h)) return null; foot.y = h;
                    go = WaysideProps.Barrier(parent, f.seed);
                    go.transform.SetPositionAndRotation(foot, Quaternion.LookRotation((b - a).X0Z().normalized));
                    break;
                }
                case Kind.BoundaryStone: go = WaysideProps.BoundaryStone(parent, f.seed); go.transform.SetPositionAndRotation(foot + Vector3.down * 0.05f, toTrail); break;
                case Kind.TreeGuards: go = WaysideProps.TreeGuards(parent, f.seed); go.transform.SetPositionAndRotation(foot, toTrail * Quaternion.Euler(0f, 180f, 0f)); SnapChildren(go); break;
                case Kind.HayBales: go = WaysideProps.HayBales(parent, f.seed); go.transform.SetPositionAndRotation(foot, toTrail); break;
                case Kind.FieldBarn: go = WaysideProps.FieldBarn(parent, f.seed); go.transform.SetPositionAndRotation(foot + Vector3.down * 0.1f, toTrail * Quaternion.Euler(0f, (f.seed % 40) - 20f, 0f)); break;
                case Kind.WaysideCross: go = WaysideProps.WaysideCross(parent, f.seed); go.transform.SetPositionAndRotation(foot, toTrail); break;
                case Kind.Chapel: go = WaysideProps.Chapel(parent, f.seed); go.transform.SetPositionAndRotation(foot + Vector3.down * 0.1f, toTrail); break;
                case Kind.Ruin: go = WaysideProps.Ruin(parent, f.seed); go.transform.SetPositionAndRotation(foot, toTrail * Quaternion.Euler(0f, f.seed % 90, 0f)); break;
                case Kind.Lookout: go = WaysideProps.Lookout(parent, f.seed); go.transform.SetPositionAndRotation(foot, toTrail * Quaternion.Euler(0f, 180f, 0f)); break;
                case Kind.AntHill: go = WaysideProps.AntHill(parent, f.seed); go.transform.position = foot; break;
                case Kind.Diggings:
                    if (SeasonAssets.Current == "winter") return new GameObject("Wühlstelle (Schnee)"); // under the snow
                    go = new GameObject("Wühlstelle"); go.transform.SetParent(parent, false); go.transform.position = foot + Vector3.up * 0.02f;
                    var rr = new System.Random(f.seed);
                    for (int i = 0; i < 3; i++) { var sp = WaysideProps.Spot(go.transform, "mud", 1.2f + (float)rr.NextDouble() * 1.4f, (float)rr.NextDouble() * 360f); sp.transform.localPosition = new Vector3(((float)rr.NextDouble() - 0.5f) * 2.5f, 0.01f * i, ((float)rr.NextDouble() - 0.5f) * 2.5f); }
                    SnapChildren(go, 0.03f);
                    break;
                case Kind.PowerLine:
                {
                    var poles = new List<Vector3>();
                    foreach (var q in f.line) if (Ground(q, out float ph)) poles.Add(new Vector3(q.x, ph, q.y)); else return null;
                    go = WaysideProps.PowerLine(parent, poles, f.seed);
                    break;
                }
                case Kind.Fence:
                {
                    var posts = new List<Vector3>();
                    foreach (var q in f.line) if (Ground(q, out float ph)) posts.Add(new Vector3(q.x, ph, q.y)); else return null;
                    go = WaysideProps.Fence(parent, posts, f.seed);
                    break;
                }
                case Kind.Village: go = BuildVillage(f, path); break;
                case Kind.Crows: go = WaysideBirds.Crows(parent, foot, f.seed); break;
                case Kind.TreeMark: case Kind.NestBox: go = OnTree(f, path); break;
            }
            if (go != null && diag) Debug.Log($"[Wegrand] {f.kind} bei {f.s:0} m ({f.side * f.lateral:+0.0;-0.0} m)");
            return go;
        }

        private readonly Dictionary<float, float> whyLogged = new Dictionary<float, float>();
        private void Why(Feature f, string reason)
        {
            if (!diag) return;
            if (whyLogged.TryGetValue(f.s, out float t) && Time.unscaledTime - t < 20f) return;
            whyLogged[f.s] = Time.unscaledTime;
            Debug.Log($"[Wegrand] {f.kind} bei {f.s:0} m wartet: {reason} (Läufer bei {TrailPath.Active.RunnerS:0} m)");
        }

        // Keep every direct child on the ground (spread-out groups on uneven land)
        private static void SnapChildren(GameObject go, float lift = 0f)
        {
            foreach (Transform c in go.transform)
                if (TerrainGround.TryHeight(c.position.x, c.position.z, out float h, MinRes)) c.position = new Vector3(c.position.x, h + lift, c.position.z);
        }

        private float SpurYaw(Feature f, TrailPath path)
        {
            foreach (var o in Plan)
                if ((o.kind == Kind.Spur || o.kind == Kind.ForestRoad) && Mathf.Abs(o.s - f.s) < 4f && o.side == f.side && o.line != null && o.line.Count > 3)
                {
                    var d = o.line[3] - o.line[0];
                    return Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
                }
            return path.Rotation(f.s).eulerAngles.y + 90f * f.side;
        }

        private static readonly string[] Places = { "Waldhütte", "Mühlbach", "Eichenkreuz", "Buchenhain", "Hirschgrund", "Am Weiher", "Sonnenhang", "Kapellenweg", "Rabenfels", "Birkenried", "Wolfsschlucht", "Talblick" };

        private static string[] Destinations(int seed)
        {
            var r = new System.Random(seed);
            string D() { string n = Places[r.Next(Places.Length)]; return $"{n}  {(0.6 + r.NextDouble() * 5.5):0.0} km".Replace('.', ','); }
            return new[] { D(), D(), D() };
        }

        private static List<Vector2> RouteShape(TrailPath path)
        {
            var pts = new List<Vector2>();
            float L = Mathf.Min(path.Length, 30000f);
            for (float s = 0f; s <= L; s += Mathf.Max(10f, L / 300f)) pts.Add(path.Point(s).XZ());
            return pts;
        }

        // Side path or forest road: a strip that drapes over the terrain and narrows out at the end
        private GameObject BuildSpur(Feature f)
        {
            bool road = f.kind == Kind.ForestRoad;
            var line = f.line;
            int n = line.Count;
            var verts = new Vector3[n * 2]; var uvs = new Vector2[n * 2]; var tris = new int[(n - 1) * 6];
            float w0 = road ? 3.0f : 1.7f;
            for (int i = 0; i < n; i++)
            {
                Vector2 d = (i + 1 < n ? line[i + 1] - line[i] : line[i] - line[i - 1]).normalized;
                Vector2 side = new Vector2(d.y, -d.x);
                float t = i / (float)(n - 1);
                float w = w0 * (road ? Mathf.Lerp(1f, 0.6f, Mathf.SmoothStep(0.6f, 1f, t)) : Mathf.Lerp(1f, 0.25f, t * t)) * 0.5f;
                for (int s = 0; s < 2; s++)
                {
                    Vector2 q = line[i] + side * (s == 0 ? -w : w);
                    if (!Ground(q, out float h)) return null;
                    float sink = t > 0.85f ? (t - 0.85f) * 0.6f : 0f;       // the end dips under the grass
                    verts[i * 2 + s] = new Vector3(q.x, h + 0.035f - sink, q.y);
                    // road: tyre-track texture across; footpath: the trail shader (x 0/1 = edges, y = metres)
                    uvs[i * 2 + s] = road ? new Vector2(s, i * 2.5f / 4f) : new Vector2(s, i * 2.5f);
                }
                if (i + 1 < n) { int a = i * 2, k = i * 6; tris[k] = a; tris[k + 1] = a + 2; tris[k + 2] = a + 1; tris[k + 3] = a + 1; tris[k + 4] = a + 2; tris[k + 5] = a + 3; }
            }
            var mesh = new Mesh { name = road ? "Rückegasse" : "Pfad" };
            mesh.vertices = verts; mesh.uv = uvs; mesh.triangles = tris;
            if (!road)
            {
                // surface weights for the trail shader: a footpath of earth (forest floor among pines, meadow path in the open)
                var mix = Vegetation.At(f.s);
                int k = mix.trees < 0.3f ? (int)TrailSurface.Kind.Meadow : mix.pine > 0.55f ? (int)TrailSurface.Kind.Forest : (int)TrailSurface.Kind.Earth;
                var col = new Color(k == 0 ? 1f : 0f, k == 1 ? 1f : 0f, k == 2 ? 1f : 0f, k == 3 ? 1f : 0f);
                var cs = new Color[verts.Length]; var u2 = new Vector2[verts.Length];
                for (int j = 0; j < verts.Length; j++) { cs[j] = col; u2[j] = new Vector2(k == 4 ? 1f : 0f, 0f); }
                mesh.colors = cs; mesh.uv2 = u2;
            }
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.RecalculateTangents();
            // facing up? (winding depends on the side the spur leaves to)
            if (mesh.normals[0].y < 0f) { System.Array.Reverse(tris); mesh.triangles = tris; mesh.RecalculateNormals(); }
            var go = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.GetComponent<MeshRenderer>();
            // in winter a forest road is snowed over like the land: two faint tyre tracks, no dark band
            bool snowy = SeasonAssets.Current == "winter";
            mr.sharedMaterial = road && snowy ? WaysideProps.Paint("snowroad", new Color(0.80f, 0.82f, 0.86f), 0.3f)
                              : road || trailMat == null ? WaysideProps.Tex("Mat_muddy_tracks") : trailMat;
            go.AddComponent<OwnedAssets>().Add(mesh);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var pts = new List<Vector3>(); foreach (var q in line) pts.Add(q.X0Z());
            TrailGrassClearer.ClearStrip(pts, w0 * 0.5f + 0.3f, true);
            return go;
        }

        // The village stretch: lamps on one side, a traffic sign at its start, bus stop, dog bags, a garden fence, a car
        private GameObject BuildVillage(Feature f, TrailPath path)
        {
            var root = new GameObject("Ortsrand");
            root.transform.SetParent(transform, false);
            var rnd = new System.Random(f.seed);
            bool ok = true;
            void At(GameObject g, float s, float lat, float yawOff = 0f)
            {
                var p = path.Offset(s, f.side * lat);
                if (!Ground(p.XZ(), out float h)) { ok = false; Destroy(g); return; }
                g.transform.SetPositionAndRotation(new Vector3(p.x, h, p.z), Quaternion.LookRotation(-path.Right(s) * f.side, Vector3.up) * Quaternion.Euler(0f, yawOff, 0f));
                g.transform.SetParent(root.transform, true);
            }
            for (float s = f.s; s < f.s + f.length; s += 32f)
            {
                var lamp = new GameObject("Laterne"); WaysideProps.Place("street_lamp_01", lamp.transform, Vector3.zero, 90f);
                NightLights.AddLamp(lamp);
                At(lamp, s, 2.4f);
            }
            At(WaysideProps.TrafficSign(transform), f.s + f.length + 6f, 2.2f);
            At(WaysideProps.DogBags(transform), f.s + 20f, -2.3f);
            At(WaysideProps.BusStop(transform), f.s + 50f, -2.5f);
            var box = new GameObject("Stromkasten"); WaysideProps.Place("utility_box_01", box.transform, Vector3.zero, 0f); At(box, f.s + 75f, -3.2f);
            var car = new GameObject("Auto"); WaysideProps.Place("covered_car", car.transform, Vector3.zero, 0f); At(car, f.s + 95f, -7.5f, 90f);
            var line = new List<Vector3>();
            for (float s = f.s + 110f; s < f.s + 150f; s += 2.5f) { var p = path.Offset(s, -f.side * 5f); if (Ground(p.XZ(), out float h)) line.Add(new Vector3(p.x, h, p.z)); }
            if (line.Count > 2) WaysideProps.PicketFence(root.transform, line);
            if (!ok) { Destroy(root); return null; }
            return root;
        }

        // Mark or nest box on the nearest tree beside the trail (the trees stream in: retried until one stands)
        private GameObject OnTree(Feature f, TrailPath path)
        {
            Vector3 near = path.Offset(f.s, f.side * 6f);
            if (!TerrainTreeScatter.NearestTrunk(near, 9f, out Vector3 trunk, out float radius)) return null;
            Vector3 toTrail = path.Point(f.s) - trunk; toTrail.y = 0f;
            if (toTrail.sqrMagnitude < 1f) return null;
            toTrail.Normalize();
            var rot = Quaternion.LookRotation(toTrail, Vector3.up);
            GameObject go;
            if (f.kind == Kind.NestBox)
            {
                go = WaysideProps.NestBox(transform);
                go.transform.SetPositionAndRotation(trunk + Vector3.up * 3.2f + toTrail * (radius * 0.9f), rot);
            }
            else
            {
                go = new GameObject("Markierung"); go.transform.SetParent(transform, false);
                var k = new MeshKit();
                // white rectangle with a coloured bar (the local hiking mark), painted on the bark
                int colour = f.seed % 3;
                k.Box(0, Vector3.zero, new Vector3(0.1f, 0.14f, 0.006f), Quaternion.identity);
                k.Box(1, new Vector3(0f, 0f, 0.002f), new Vector3(0.1f, 0.035f, 0.006f), Quaternion.identity);
                k.Make("Zeichen", go.transform, WaysideProps.Paint("white", new Color(0.86f, 0.86f, 0.83f), 0.35f),
                       colour == 0 ? WaysideProps.Paint("red", new Color(0.62f, 0.07f, 0.06f), 0.4f) : colour == 1 ? WaysideProps.Paint("blue", new Color(0.1f, 0.22f, 0.55f), 0.4f) : WaysideProps.Paint("yellowmark", new Color(0.85f, 0.7f, 0.1f), 0.4f));
                go.transform.SetPositionAndRotation(trunk + Vector3.up * 1.7f + toTrail * (radius + 0.004f), rot);
            }
            return go;
        }

        // ------------------------------------------------------------------ right at the path
        private GameObject BuildEdge(int c, TrailPath path)
        {
            int cc = c;
            float chunk = ChunkLen(path);
            if (path.Loop) { int n = Mathf.Max(1, Mathf.RoundToInt(path.Length / Chunk)); cc = ((c % n) + n) % n; }
            float z0 = cc * chunk;
            if (!path.Loop && (z0 < 20f || z0 > path.Length)) return new GameObject($"Rand_{c}");
            Vector3 pa = path.Point(z0), pb = path.Point(z0 + chunk);
            if (!Ground(pa.XZ(), out _) || !Ground(pb.XZ(), out _) || !TrailShaper.Ready(z0, z0 + chunk)) return null;
            var p = RouteRuntime.Current.@params;
            var root = new GameObject($"Rand_{c}");
            root.transform.SetParent(transform, false);
            var rnd = new System.Random(RouteRuntime.Current.generator.seed * 31337 ^ cc * 7919);
            float R() => (float)rnd.NextDouble();
            var mix = Vegetation.At(z0 + Chunk * 0.5f);
            bool forest = mix.trees > 0.55f, pines = mix.pine > 0.55f;
            bool autumn = p.season == "autumn", winter = p.season == "winter";
            bool light = GraphicsQuality.Level == "minimal"; // weak tablets: no heavy scans (stumps, roots) right at the path
            float wet = p.weather == "rain" ? 3f : p.weather == "cloudy" || autumn || p.season == "spring" ? 1.5f : winter ? 0.6f : 1f;
            int count = Mathf.RoundToInt((forest ? 4f : 2.5f) * (0.6f + mix.undergrowth * 0.5f) * GraphicsQuality.ScatterFactor + R());
            for (int i = 0; i < count; i++)
            {
                float s = z0 + R() * chunk, side = R() < 0.5f ? -1f : 1f;
                float r = R();
                string model = null; float lat = 1.7f + R() * 1.8f, scale = 1f;
                GameObject made = null;
                if (forest)
                {
                    if (r < 0.26f && !winter) { model = "fern_02_" + "abcd"[rnd.Next(4)]; scale = 0.9f + R() * 0.5f; }
                    else if (r < 0.42f) model = "dry_branches_medium_01_" + "abc"[rnd.Next(3)];
                    else if (r < 0.48f && !light) { model = R() < 0.5f ? "tree_stump_01" : "tree_stump_02"; lat = 2.6f + R() * 3f; scale = 0.7f + R() * 0.5f; }
                    else if (r < 0.48f) { made = WaysideProps.CutStump(root.transform, rnd.Next()); lat = 2.2f + R() * 3f; }
                    else if (r < 0.54f) { made = WaysideProps.CutStump(root.transform, rnd.Next()); lat = 2.2f + R() * 3f; }
                    else if (r < 0.58f && pines && !light) { model = "single_root"; lat = 1.55f; scale = 0.8f + R() * 0.4f; }
                    else if (r < (autumn ? 0.72f : 0.62f) && !winter) { made = WaysideProps.LeafPile(root.transform, rnd.Next(), pines); lat = 2.2f + R() * 2f; }
                    else if (r < (autumn ? 0.8f : 0.63f) && !winter) { made = WaysideProps.Mushrooms(root.transform, rnd.Next()); lat = 1.6f + R() * 2.5f; }
                    else if (r < 0.86f && !winter) { model = "nettle_plant_" + "cdef"[rnd.Next(4)]; scale = 0.9f + R() * 0.4f; }
                    else if (r < 0.92f && !winter) model = "shrub_03_" + "abcd"[rnd.Next(4)];
                }
                else
                {
                    if (r < 0.3f && !winter) { model = "nettle_plant_" + "cdef"[rnd.Next(4)]; scale = 0.9f + R() * 0.4f; }
                    else if (r < 0.5f && !winter) model = "shrub_03_" + "abcd"[rnd.Next(4)];
                    else if (r < 0.58f) model = "dry_branches_medium_01_" + "abc"[rnd.Next(3)];
                    else if (r < 0.64f && !winter) { model = "fern_02_" + "abcd"[rnd.Next(4)]; }
                }
                // nettles are dense scans (up to 24k triangles): half as often, and none on weak tablets
                if (model != null && model.StartsWith("nettle") && (light || R() < 0.5f)) model = light ? "fern_02_" + "bc"[rnd.Next(2)] : "shrub_03_" + "abcd"[rnd.Next(4)];
                if (model == null && made == null) continue;
                Vector3 w = path.Offset(s, side * lat);
                if (Lakes.Inside(w, 1f) || Blocks(w, 0.5f)) { if (made != null) Destroy(made); continue; }
                path.Project(w, s, 200f, out float realLat);
                if (Mathf.Abs(realLat) < 1.5f) { if (made != null) Destroy(made); continue; } // a bend brings another part of the trail close
                if (!Ground(w.XZ(), out float h)) { if (made != null) Destroy(made); continue; }
                float yaw = R() * 360f;
                if (model == "single_root") yaw = path.Rotation(s).eulerAngles.y + (side > 0 ? 90f : -90f) + (R() - 0.5f) * 40f; // roots run out from the forest towards the path
                if (made == null) made = WaysideProps.Place(model, root.transform, Vector3.zero, 0f, scale);
                if (made == null) continue;
                made.transform.SetPositionAndRotation(new Vector3(w.x, h - 0.03f, w.z), Quaternion.Euler(0f, yaw, 0f));
            }
            // on the path: puddles and mud, more when wet; autumn leaves and needles on the edge
            float sp = z0 + R() * chunk, halfW = TrailSurface.HalfWidth(sp);
            bool paved = TrailSurface.At(sp) == TrailSurface.Kind.Asphalt;
            if (R() < (paved ? 0.03f : 0.05f) * wet && !winter) Decal(root.transform, path, sp, (R() - 0.5f) * (halfW - 0.4f) * 2f, "puddle", 0.7f + R() * 0.8f, R() * 360f);
            sp = z0 + R() * chunk; halfW = TrailSurface.HalfWidth(sp);
            if (forest && !winter && TrailSurface.At(sp) != TrailSurface.Kind.Asphalt && R() < 0.07f * wet) Decal(root.transform, path, sp, (R() - 0.5f) * (halfW - 0.3f) * 2f, "mud", 1.0f + R() * 1.2f, R() * 360f);
            if (forest && (autumn ? R() < 0.5f : R() < 0.12f) && !winter)
            {
                float side = R() < 0.5f ? -1f : 1f;
                float sl = z0 + R() * chunk;
                Decal(root.transform, path, sl, side * (TrailSurface.HalfWidth(sl) - 0.2f + R() * 0.6f), pines ? "needles" : "leaves", 1.4f + R() * 1.2f, R() * 360f);
            }
            return root;
        }

        private static void Decal(Transform parent, TrailPath path, float s, float lateral, string kind, float size, float yaw)
        {
            Vector3 w = path.Offset(s, lateral);
            if (!Ground(w.XZ(), out float h)) return;
            var go = WaysideProps.Spot(parent, kind, size, yaw);
            go.transform.position = new Vector3(w.x, h + 0.065f, w.z); // just above the trail band (lift 0.05)
            // follow the ground's tilt across the spot
            if (TerrainGround.TryHeight(w.x + 0.5f, w.z, out float hx, MinRes) && TerrainGround.TryHeight(w.x, w.z + 0.5f, out float hz, MinRes))
            {
                var n = new Vector3(-(hx - h) * 2f, 1f, -(hz - h) * 2f).normalized;
                go.transform.rotation = Quaternion.FromToRotation(Vector3.up, n);
            }
        }
    }

    internal static class WaysideVec
    {
        public static Vector2 XZ(this Vector3 v) => new Vector2(v.x, v.z);
        public static Vector3 X0Z(this Vector2 v) => new Vector3(v.x, 0f, v.y);
    }
}
