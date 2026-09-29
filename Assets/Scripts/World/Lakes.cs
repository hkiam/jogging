using System.Collections.Generic;
using UnityEngine;
using Jogging.Route;

namespace Jogging.World
{
    /// <summary>
    /// Water beside and across the trail. Lakes (water = "lake"/"both", or placed in the workshop):
    /// one per ~6 km, placed
    /// deterministically from the seed at a point of the course, 14–30 m from the trail edge, with
    /// the water surface a little below the trail's height there. <see cref="TrailShaper"/> digs the
    /// basin and shore into the terrain; this component shows the water once the trail's absolute
    /// height is known; scatterers keep out via <see cref="Inside"/>. Streams (water = "stream"/"both")
    /// cross the trail at low points of the profile; their channel is dug by the shaper too, the
    /// water ribbon is clipped where the ground would not hold it, and a small wooden railing marks
    /// the crossing.
    /// </summary>
    public class Lakes : MonoBehaviour
    {
        public struct Lake
        {
            public Vector2 centre;
            public float radius;
            public float relLevel; // water level relative to the route profile's start height
        }

        public static readonly List<Lake> All = new List<Lake>();

        /// <summary>A stream crossing the trail at a low point of the profile (a valley).</summary>
        public struct Stream
        {
            public float s;          // crossing (arc length)
            public Vector2 centre;   // crossing point
            public Vector2 dir;      // along the stream (perpendicular to the trail)
            public Vector2 along;    // trail direction at the crossing
            public float halfLength, relLevel, slope, phase;

            /// <summary>Centre line of the stream at u metres from the crossing (meanders, straight under the trail).</summary>
            public Vector2 At(float u)
            {
                float fade = Mathf.Clamp01((Mathf.Abs(u) - 6f) / 14f);
                return centre + dir * u + along * (3f * Mathf.Sin(u / 17f + phase) * fade);
            }

        }

        public static readonly List<Stream> Streams = new List<Stream>();
        private static Material water;
        private bool shown;

        /// <summary>Plan the lakes for a route (call after the trail line is set).</summary>
        public static void Plan(RouteDoc doc, TrailPath path, float[] profile, float profileStep)
        {
            All.Clear();
            Streams.Clear();
            var p = doc.@params;
            if (path == null) return;
            float L = path.Length;

            // Lakes placed in the workshop.
            if (doc.edits != null)
                foreach (var e in doc.edits)
                {
                    if (e.type != "lake" || e.atM < 0f || e.atM > L) continue;
                    float r = 42f, lat = (e.side == "left" ? -1f : 1f) * (r + 18f);
                    var c3 = path.Offset(e.atM, lat);
                    var c = new Vector2(c3.x, c3.z);
                    if (!Clear(path, c, r + 12f)) { Debug.Log($"[Jogging] See bei {e.atM:0} m passt nicht (Strecke zu nah)"); continue; }
                    int i = Mathf.Clamp(Mathf.RoundToInt(e.atM / profileStep), 0, profile.Length - 1);
                    All.Add(new Lake { centre = c, radius = r, relLevel = profile[i] - 1.3f });
                }
            if (p.water == "lake" || p.water == "both") PlanAutoLakes(doc, path, profile, profileStep, L);
            PlanStreams(doc, path, profile, profileStep, p.water == "stream" || p.water == "both");
        }

        private static void PlanAutoLakes(RouteDoc doc, TrailPath path, float[] profile, float profileStep, float L)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(L / 6000f), 1, 6);
            var rng = new System.Random(doc.generator.seed * 613 + 11);
            for (int k = 0; k < n; k++)
            {
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    float s = L * (k + 0.5f) / n + ((float)rng.NextDouble() - 0.5f) * L * 0.3f / n;
                    s = Mathf.Clamp(s, 300f, L - 300f);
                    float r = Mathf.Lerp(32f, 58f, (float)rng.NextDouble());
                    float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                    float lateral = side * (r + Mathf.Lerp(14f, 30f, (float)rng.NextDouble()));
                    var c3 = path.Offset(s, lateral);
                    var c = new Vector2(c3.x, c3.z);
                    if (!Clear(path, c, r + 12f)) continue; // too close to another part of the course
                    int i = Mathf.Clamp(Mathf.RoundToInt(s / profileStep), 0, profile.Length - 1);
                    All.Add(new Lake { centre = c, radius = r, relLevel = profile[i] - 1.3f });
                    Debug.Log($"[Jogging] See bei {s:0} m ({(side > 0 ? "rechts" : "links")}), Radius {r:0} m");
                    break;
                }
            }
        }

        // Streams: those placed in the workshop, then automatic ones at the lowest points of the
        // profile (valleys), ≥ 1.2 km apart, one per ~2.5 km (only for water = stream/both).
        private static void PlanStreams(RouteDoc doc, TrailPath path, float[] profile, float step, bool automatic)
        {
            float L = path.Length;
            var rng = new System.Random(doc.generator.seed * 389 + 5);
            if (doc.edits != null)
                foreach (var e in doc.edits)
                {
                    if (e.type != "stream" || e.atM < 0f || e.atM > L) continue;
                    if (TryMake(path, e.atM, profile, step, rng, out var st)) { Streams.Add(st); Debug.Log($"[Jogging] Bach (Werkstatt) bei {e.atM:0} m"); }
                    else Debug.Log($"[Jogging] Bach bei {e.atM:0} m passt nicht (Strecke zu nah)");
                }
            if (!automatic) return;

            int max = Streams.Count + Mathf.Clamp(Mathf.RoundToInt(L / 2500f), 1, 12);
            var cands = new List<int>();
            int w = Mathf.RoundToInt(150f / step);
            for (int i = w; i < profile.Length - w; i++)
            {
                float si = i * step;
                if (si < 350f || si > L - 350f) continue;
                bool min = true;
                for (int k = -w; k <= w && min; k += 3) if (profile[i + k] < profile[i] - 0.05f) min = false;
                if (min) cands.Add(i);
            }
            cands.Sort((x, y) => profile[x].CompareTo(profile[y]));
            foreach (int i in cands)
            {
                if (Streams.Count >= max) break;
                float sc = i * step;
                bool farEnough = true;
                foreach (var o in Streams) if (Mathf.Abs(o.s - sc) < 1200f) farEnough = false;
                if (!farEnough || !TryMake(path, sc, profile, step, rng, out var st)) continue;
                Streams.Add(st);
                Debug.Log($"[Jogging] Bach bei {sc:0} m");
            }
        }

        /// <summary>Can a stream cross the trail at arc length s (no other part of the course close by)?</summary>
        public static bool CanPlaceStream(TrailPath path, float s, int seed = 1) =>
            TryMake(path, s, null, 2f, new System.Random(seed), out _);

        private static bool TryMake(TrailPath path, float sc, float[] profile, float step, System.Random rng, out Stream st)
        {
            float L = path.Length;
            var c3 = path.Point(sc); var t3 = path.Tangent(sc); var r3 = path.Right(sc);
            int pi = profile != null ? Mathf.Clamp(Mathf.RoundToInt(sc / step), 0, profile.Length - 1) : 0;
            st = new Stream
            {
                s = sc, centre = new Vector2(c3.x, c3.z), along = new Vector2(t3.x, t3.z), dir = new Vector2(r3.x, r3.z),
                halfLength = 110f, relLevel = profile != null ? profile[pi] - 1.5f : 0f,
                slope = (rng.NextDouble() < 0.5 ? -1f : 1f) * 0.012f, phase = (float)rng.NextDouble() * 6.283f,
            };
            // It must not run into another part of the course, or into a lake.
            for (float u = -st.halfLength; u <= st.halfLength; u += 10f)
            {
                if (Mathf.Abs(u) < 25f) continue;
                var q = st.At(u);
                if (InLake(q)) return false;
                for (float s = 0f; s <= L; s += 6f)
                {
                    float ds = Mathf.Abs(s - sc);
                    if (path.Loop) ds = Mathf.Min(ds, L - ds);
                    if (ds < 60f) continue;
                    var pp = path.Point(s);
                    if ((pp.x - q.x) * (pp.x - q.x) + (pp.z - q.y) * (pp.z - q.y) < 144f) return false;
                }
            }
            return true;
        }

        private static bool InLake(Vector2 q)
        {
            foreach (var l in All) if ((q - l.centre).sqrMagnitude < (l.radius + 6f) * (l.radius + 6f)) return true;
            return false;
        }

        // No part of the trail (every 5 m) within 'dist' of the point.
        private static bool Clear(TrailPath path, Vector2 c, float dist)
        {
            float d2 = dist * dist;
            for (float s = 0f; s <= path.Length; s += 5f)
            {
                var q = path.Point(s);
                float dx = q.x - c.x, dz = q.z - c.y;
                if (dx * dx + dz * dz < d2) return false;
            }
            return true;
        }

        /// <summary>True if the point is on a lake or in a stream bed (plus margin, e.g. for trees).</summary>
        public static bool Inside(Vector3 w, float margin)
        {
            foreach (var st in Streams)
            {
                Vector2 q = new Vector2(w.x, w.z);
                float u = Vector2.Dot(q - st.centre, st.dir);
                if (Mathf.Abs(u) > st.halfLength) continue;
                if (Mathf.Abs(Vector2.Dot(q - st.At(u), st.along)) < 2.5f + margin) return true;
            }
            foreach (var l in All)
            {
                float dx = w.x - l.centre.x, dz = w.z - l.centre.y, r = l.radius + margin;
                if (dx * dx + dz * dz < r * r) return true;
            }
            return false;
        }

        private readonly HashSet<int> streamsShown = new HashSet<int>();

        private void Update()
        {
            long t0 = FrameWork.Start();
            UpdateWork();
            FrameWork.Stop("Seen", t0);
        }

        private void UpdateWork()
        {
            var shaper = TrailShaper.Active;
            if (shaper == null || !shaper.BaseKnown) return;
            if (!shown && All.Count > 0)
            {
                shown = true;
                foreach (var l in All) Spawn(l, shaper.BaseHeight + l.relLevel);
            }
            // A stream's water needs its final ground (it is clipped where the ground is lower).
            for (int i = 0; i < Streams.Count; i++)
            {
                if (streamsShown.Contains(i)) continue;
                var st = Streams[i];
                bool ready = true;
                for (float u = -st.halfLength; u <= st.halfLength && ready; u += 20f)
                {
                    var q = st.At(u);
                    if (!shaper.IsShapedAt(new Vector3(q.x, 0f, q.y))) ready = false;
                }
                if (!ready || !shaper.StreamLevel(i, 0f, out _)) continue;
                streamsShown.Add(i);
                SpawnStream(st, i, shaper);
            }
        }

        private static Material wood, streamWater;

        private void SpawnStream(Stream st, int index, TrailShaper shaper)
        {
            EnsureWater();
            // Water ribbon along the channel; quads only where the ground holds the water.
            var verts = new List<Vector3>(); var tris = new List<int>();
            const float half = 1.6f;
            int prevOk = -1;
            for (float u = -st.halfLength; u <= st.halfLength; u += 1.5f)
            {
                Vector2 c = st.At(u), d = (st.At(u + 0.5f) - st.At(u - 0.5f)).normalized, n = new Vector2(-d.y, d.x);
                shaper.StreamLevel(index, u, out float y);
                bool ok = TerrainGround.TryHeight(c.x, c.y, out float g) && g < y - 0.05f && g > y - 2.5f;
                int idx = verts.Count;
                verts.Add(new Vector3(c.x - n.x * half, y, c.y - n.y * half));
                verts.Add(new Vector3(c.x + n.x * half, y, c.y + n.y * half));
                // Wound so the face points up (Unity: clockwise seen from above).
                if (ok && prevOk == idx - 2) { tris.Add(idx - 2); tris.Add(idx - 1); tris.Add(idx); tris.Add(idx - 1); tris.Add(idx + 1); tris.Add(idx); }
                prevOk = ok ? idx : -1;
            }
            Debug.Log($"[Jogging] Bach bei {st.s:0} m: {tris.Count / 6} Wasserstücke von {verts.Count / 2 - 1}, " +
                      $"Pegel Mitte {(shaper.StreamLevel(index, 0f, out float lm) ? lm : 0f):0.0} m");
            var go = new GameObject("Stream", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            var m = new Mesh { name = "Stream" };
            m.SetVertices(verts); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            go.GetComponent<MeshFilter>().sharedMesh = m;
            var mr = go.GetComponent<MeshRenderer>();
            if (streamWater == null)
            {
                // Shallow running water: lighter than a lake and a bit rougher (winter: ice like the lakes).
                streamWater = new Material(water) { name = "StreamWater" };
                if (SeasonAssets.Current != "winter")
                {
                    streamWater.SetColor("_BaseColor", new Color(0.16f, 0.26f, 0.28f, 1f));
                    streamWater.SetFloat("_Smoothness", 0.9f);
                }
            }
            mr.sharedMaterial = streamWater;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // A small plank bridge where the trail crosses: deck planks on the trail, posts and hand rails.
            if (wood == null) wood = WaysideProps.Tex("Mat_rough_wood");
            var path = TrailPath.Active;
            var bridge = new GameObject("Brücke").transform;
            bridge.SetParent(go.transform, false);
            var deck = new MeshKit();
            var rails = new MeshKit();
            var rnd = new System.Random(index * 17 + 3);
            for (float a = -4.6f; a <= 4.6f; a += 0.27f)
            {
                Vector3 c = path.Point(st.s + a);
                if (!TerrainGround.TryHeight(c.x, c.z, out float h, 200) && !TerrainGround.TryHeight(c.x, c.z, out h)) continue;
                float tilt = ((float)rnd.NextDouble() - 0.5f) * 1.5f;
                deck.Box(0, c + Vector3.up * (h + 0.17f - c.y), // above the trail band (lifted 0.12 m)
                    new Vector3(3.1f + (float)rnd.NextDouble() * 0.2f, 0.05f, 0.24f), path.Rotation(st.s + a) * Quaternion.Euler(tilt, 0f, 0f), 0.7f);
            }
            foreach (float side in new[] { -1.55f, 1.55f })
            {
                Vector3 prev = default; bool havePrev = false;
                for (float a = -4.6f; a <= 4.61f; a += 2.3f)
                {
                    Vector3 f = path.Offset(st.s + a, side);
                    if (!TerrainGround.TryHeight(f.x, f.z, out float h, 200) && !TerrainGround.TryHeight(f.x, f.z, out h)) continue;
                    f.y = h;
                    rails.Box(0, f + Vector3.up * 0.45f, new Vector3(0.12f, 1.1f, 0.12f), path.Rotation(st.s + a), 0.8f);
                    Vector3 top = f + Vector3.up * 0.98f;
                    if (havePrev) { rails.Beam(0, prev, top, 0.08f, 0.1f, Vector3.up, 0.8f); rails.Beam(0, prev + Vector3.down * 0.45f, top + Vector3.down * 0.45f, 0.05f, 0.08f, Vector3.up, 0.8f); }
                    prev = top; havePrev = true;
                }
            }
            deck.Make("Planken", bridge, WaysideProps.Tex("Mat_weathered_planks"));
            rails.Make("Geländer", bridge, wood);
        }

        private static string waterSeason;

        private static void EnsureWater()
        {
            bool ice = SeasonAssets.Current == "winter";
            if (water != null && waterSeason == SeasonAssets.Current) return;
            waterSeason = SeasonAssets.Current;
            streamWater = null;
            water = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = ice ? "Ice" : "LakeWater" };
            water.SetColor("_BaseColor", ice ? new Color(0.70f, 0.78f, 0.84f, 1f) : new Color(0.05f, 0.11f, 0.13f, 1f));
            water.SetFloat("_Smoothness", ice ? 0.55f : 0.86f);
            water.SetFloat("_Metallic", 0f);
        }

        private void Spawn(Lake l, float level)
        {
            EnsureWater();
            var go = new GameObject("Lake", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(l.centre.x, level, l.centre.y);
            go.GetComponent<MeshFilter>().sharedMesh = Disc(l.radius + 1.5f, 96);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = water;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static Mesh Disc(float r, int seg)
        {
            var v = new Vector3[seg + 1]; var t = new int[seg * 3];
            v[0] = Vector3.zero;
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                v[i + 1] = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                t[i * 3] = 0; t[i * 3 + 1] = (i + 1) % seg + 1; t[i * 3 + 2] = i + 1;
            }
            var m = new Mesh { name = "LakeDisc", vertices = v, triangles = t };
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }
}
