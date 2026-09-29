using System.Collections.Generic;
using UnityEngine;

namespace Jogging.World
{
    /// <summary>
    /// Shapes the streamed terrain around the route so that the trail follows the route's height
    /// profile exactly (the profile is designed and runnable; see Jogging.Route.RouteGenerator):
    ///   1. Base height: a broad, smooth offset field lifts/lowers the natural terrain so that,
    ///      near the trail, it lies on the profile on average — a 100 m climb becomes a hill
    ///      instead of a 100 m embankment. The field is a Gaussian-weighted average of the per-sample
    ///      offsets (profile − smoothed natural ground) blended to zero far away, so it only depends
    ///      on world position and is seamless across tiles.
    ///   2. Path bed: along the centre line the ground is set to the profile (flat bed of halfWidth)
    ///      and blends into the offset terrain over a shoulder that widens with the local difference.
    ///
    /// Works on MapMagic's detailed tiles as they stream in. Natural ground heights along the trail
    /// are cached before any tile near them is touched; a tile is shaped once every trail sample
    /// within the offset's reach has its natural height; a regenerated tile is shaped again.
    /// Nothing here assumes a direction of travel (loops, bends, returning trails all work).
    /// </summary>
    public class TrailShaper : MonoBehaviour
    {
        public static TrailShaper Active { get; private set; }

        [Header("Base height (offset field)")]
        [SerializeField] private float sigma = 140f;              // width of the lifted/lowered area
        [SerializeField] private float background = 2.5f;        // weight of "no offset" (fades it out far away)
        [SerializeField] private float offsetGrid = 8f;          // m between offset-field samples in a tile

        [Header("Path bed")]
        [SerializeField] private float halfWidth = 2.4f;          // flat bed (trail 3.4 m + margin)
        [SerializeField] private float minShoulder = 6f;
        [Tooltip("Extra shoulder width per metre of remaining difference (≈ 1 : 3 slope).")]
        [SerializeField] private float shoulderPerMetre = 3f;
        [SerializeField] private float maxShoulder = 50f;
        [Tooltip("Natural ground is also read this far left and right of the trail: across a slope or a crest the " +
                 "difference out there sets the shoulder, not the one on the centre line (else: a wall).")]
        [SerializeField] private float sideReach = 30f;

        [Header("Streaming")]
        [SerializeField] private int minResolution = 200;         // detailed tiles only (drafts are 65)
        [SerializeField] private float interval = 0.3f;
        [SerializeField] private float terrainHeightRange = 400f; // shaped tiles get this vertical range

        private const float Ds = 2f;       // trail sample spacing (m)
        private const float HashCell = 64f;

        // Route
        private float[] profile;           // relative heights, step profileStep
        private float profileStep = 2f;
        private bool loop;
        private float length;

        // Trail samples
        private float sA;                  // arc length of sample 0
        private int count;
        private Vector2[] pos;
        private float[] raw;               // natural ground (NaN = unknown)
        private float[] rawL, rawR;        // natural ground sideReach left/right of the trail (NaN = unknown)
        private Vector2[] nrm;             // unit normal of the trail (to the right)
        private int unknownSide;           // side samples still NaN
        private int unknownCount;          // samples whose natural ground is still NaN
        private readonly Dictionary<(int, int), List<int>> hash = new Dictionary<(int, int), List<int>>();
        private bool baseKnown;
        private float baseHeight;          // world height of profile 0

        // Streams: natural ground along each stream's centre line (2 m steps), then its water level.
        private readonly List<Vector2[]> sPos = new List<Vector2[]>();
        private readonly List<float[]> sRaw = new List<float[]>();
        private readonly List<float[]> sLevel = new List<float[]>();
        private const float StreamStep = 2f;

        private class Done { public TerrainData data; public Vector3 position; public float checkH; public int cx, cz; }
        private readonly Dictionary<Terrain, Done> shaped = new Dictionary<Terrain, Done>();
        private float next;

        /// <summary>True if the ground at this point is final (detailed tile, shaped).</summary>
        public bool IsShapedAt(Vector3 w) { var t = FinestAt(w); return t != null && IsShaped(t); }

        /// <summary>World height of the route profile's start (known once the natural ground there is).</summary>
        public bool BaseKnown => baseKnown;
        public float BaseHeight => baseHeight;

        private void Awake() => Active = this;
        private void OnDestroy() { if (Active == this) Active = null; }

        /// <summary>Ground under [s0, s1] is final (or there is no shaper): convenience for callers.</summary>
        public static bool Ready(float s0, float s1) => Active == null || Active.IsReady(s0, s1);

        /// <summary>Set the route (call once, before tiles stream in).</summary>
        public void SetRoute(float[] heights, float step, bool isLoop, float routeLength)
        {
            profile = heights; profileStep = step; loop = isLoop; length = routeLength;
            var path = TrailPath.Active;
            sA = loop ? 0f : -150f;
            float sB = loop ? length - Ds : length + 150f;
            count = Mathf.FloorToInt((sB - sA) / Ds) + 1;
            pos = new Vector2[count]; raw = new float[count];
            rawL = new float[count]; rawR = new float[count]; nrm = new Vector2[count]; unknownSide = 2 * count;
            hash.Clear(); unknownCount = count; shaped.Clear(); baseKnown = false;
            for (int i = 0; i < count; i++)
            {
                var p = path.Point(sA + i * Ds);
                pos[i] = new Vector2(p.x, p.z);
                raw[i] = float.NaN; rawL[i] = float.NaN; rawR[i] = float.NaN;
                var k = Key(pos[i]);
                if (!hash.TryGetValue(k, out var l)) hash[k] = l = new List<int>();
                l.Add(i);
            }
            for (int i = 0; i < count; i++)
            {
                int a = loop ? (i - 1 + count) % count : Mathf.Max(0, i - 1), b = loop ? (i + 1) % count : Mathf.Min(count - 1, i + 1);
                Vector2 dir = (pos[b] - pos[a]).normalized;
                nrm[i] = new Vector2(dir.y, -dir.x);
            }
            sPos.Clear(); sRaw.Clear(); sLevel.Clear();
            foreach (var st in Lakes.Streams)
            {
                int n = Mathf.FloorToInt(2f * st.halfLength / StreamStep) + 1;
                var ps = new Vector2[n]; var rs = new float[n];
                for (int k = 0; k < n; k++) { ps[k] = st.At(-st.halfLength + k * StreamStep); rs[k] = float.NaN; }
                sPos.Add(ps); sRaw.Add(rs); sLevel.Add(null);
            }
        }

        /// <summary>Water level (world m) of stream i at u metres from the crossing; false until known.</summary>
        public bool StreamLevel(int i, float u, out float level)
        {
            level = 0f;
            if (i < 0 || i >= sLevel.Count || sLevel[i] == null) return false;
            var lv = sLevel[i];
            float x = Mathf.Clamp((u + Lakes.Streams[i].halfLength) / StreamStep, 0f, lv.Length - 1.001f);
            int k = Mathf.FloorToInt(x);
            level = Mathf.Lerp(lv[k], lv[k + 1], x - k);
            return true;
        }

        // Once the natural ground along a stream is known: it flows from its higher end to the lower
        // one, always ≥ 0.7 m below the (offset) ground and ≥ 1.3 m below the trail at the crossing,
        // never rising — so the channel always cuts in and holds its water.
        private void TryStreamLevels()
        {
            for (int i = 0; i < sLevel.Count; i++)
            {
                if (sLevel[i] != null) continue;
                var rs = sRaw[i]; var ps = sPos[i];
                bool all = true; foreach (var r in rs) if (float.IsNaN(r)) { all = false; break; }
                if (!all) continue;
                int n = rs.Length;
                var nat = new float[n];
                float reach = 2.5f * sigma;
                bool known = true;
                for (int k = 0; k < n && known; k++)
                {
                    var near = new List<int>();
                    foreach (int j in SamplesNear(ps[k].x - reach, ps[k].y - reach, ps[k].x + reach, ps[k].y + reach))
                    {
                        if (float.IsNaN(raw[j])) { known = false; break; } // the offset field there isn't final yet
                        if (j % 3 == 0) near.Add(j);
                    }
                    nat[k] = rs[k] + OffsetAt(ps[k].x, ps[k].y, near);
                }
                if (!known) continue;
                var sm = new float[n];
                for (int k = 0; k < n; k++) { float a = 0f; int c = 0; for (int d = -3; d <= 3; d++) { int j = k + d; if (j >= 0 && j < n) { a += nat[j]; c++; } } sm[k] = a / c; }
                var st = Lakes.Streams[i];
                float cap = baseHeight + Rel(st.s) - 1.3f;          // under the trail's bed
                int mid = n / 2;
                bool downPositive = sm[n - 1] < sm[0];             // flows towards +u
                var lv = new float[n];
                int k0 = downPositive ? 0 : n - 1, dk = downPositive ? 1 : -1;
                float prev = float.MaxValue;
                for (int k = k0, c = 0; c < n; k += dk, c++)
                {
                    float v = Mathf.Min(sm[k] - 0.7f, prev - 0.01f); // gentle minimum fall
                    if (Mathf.Abs(k - mid) <= 4) v = Mathf.Min(v, cap);
                    lv[k] = prev = v;
                }
                sLevel[i] = lv;
            }
        }

        /// <summary>Profile grade (%) at arc length s.</summary>
        public bool TryGrade(float s, out float percent)
        {
            percent = 0f;
            if (profile == null || profile.Length < 2) return false;
            percent = (Rel(s + 2f) - Rel(s - 2f)) / 4f * 100f;
            return true;
        }

        /// <summary>True when the ground along [s0, s1] is final (detailed tiles, already shaped).</summary>
        public bool IsReady(float s0, float s1)
        {
            var path = TrailPath.Active;
            if (path == null || profile == null) return true;
            for (float s = s0; s <= s1 + 0.01f; s += 5f)
            {
                var t = FinestAt(path.Point(s));
                if (t == null || !IsShaped(t)) return false;
            }
            return true;
        }

        // ---- profile helpers -------------------------------------------------------------------

        // Relative profile height at arc length s (loop wraps; open courses hold the end values).
        private float Rel(float s)
        {
            int n = profile.Length;
            if (loop)
            {
                // Loop profiles end at the start height (profile[n-1] == profile[0]); wrap on [0, length).
                float x = Mathf.Repeat(s, length) / profileStep;
                int i = Mathf.Min(Mathf.FloorToInt(x), n - 2);
                return Mathf.Lerp(profile[i], profile[i + 1], Mathf.Clamp01(x - i));
            }
            float xo = Mathf.Clamp(s, 0f, (n - 1) * profileStep) / profileStep;
            int io = Mathf.Min(Mathf.FloorToInt(xo), n - 2);
            return Mathf.Lerp(profile[io], profile[io + 1], xo - io);
        }

        private float Abs(int i) => baseHeight + Rel(sA + i * Ds);

        private float RawSmooth(int i)
        {
            float sum = 0f; int c = 0;
            for (int k = -5; k <= 5; k++)
            {
                int j = i + k;
                if (loop) j = (j % count + count) % count; else if (j < 0 || j >= count) continue;
                if (!float.IsNaN(raw[j])) { sum += raw[j]; c++; }
            }
            return c > 0 ? sum / c : raw[i];
        }

        // ---- update ----------------------------------------------------------------------------

        private void Update()
        {
            long t0 = FrameWork.Start();
            UpdateWork();
            FrameWork.Stop("Wegformung", t0);
        }

        private void UpdateWork()
        {
            if (profile == null || Time.unscaledTime < next) return;
            next = Time.unscaledTime + interval;
            SampleRaw();
            if (!baseKnown) TryBase();
            if (!baseKnown && diagDams && Time.unscaledTime > nextWaitLog)
            {
                nextWaitLog = Time.unscaledTime + 3f;
                int i0 = Mathf.RoundToInt((0f - sA) / Ds);
                var t0 = FinestAt(new Vector3(pos[i0].x, 0f, pos[i0].y), 0);
                Debug.Log($"[Weg] warte auf Start: unbekannt {unknownCount}/{count}, seitlich {unknownSide}, Start {pos[i0]} raw {raw[i0]} · Kachel dort {(t0 != null ? t0.name + " " + t0.terrainData.heightmapResolution + (IsShaped(t0) ? " geformt" : "") : "keine")}");
            }
            if (!baseKnown) return;
            TryStreamLevels();

            foreach (var t in Terrain.activeTerrains)
            {
                if (t == null || t.terrainData == null || !t.drawHeightmap) continue;
                if (t.terrainData.heightmapResolution < minResolution) continue;
                if (IsShaped(t)) continue;
                if (!InputsKnown(t, out bool touches)) continue;
                if (touches) Shape(t);
                Remember(t);
            }
            if (shaped.Count > 96) // forget tiles MapMagic has destroyed
                foreach (var k in new List<Terrain>(shaped.Keys)) if (k == null) shaped.Remove(k);
        }

        // Natural ground along the trail (and beside it), read from the finest tile there that is not shaped
        // yet. Far samples may come from a coarse draft tile — they only feed the broad offset field.
        // Visits only the samples that lie on a streamed tile (spatial hash): a long course has tens
        // of thousands of samples, most of them far from any tile.
        private void SampleRaw()
        {
            if (unknownCount > 0 || unknownSide > 0)
                foreach (var tile in Terrain.activeTerrains)
                {
                    if (tile == null || tile.terrainData == null || !tile.drawHeightmap) continue;
                    Vector3 p = tile.transform.position, size = tile.terrainData.size;
                    foreach (int i in SamplesNear(p.x - sideReach, p.z - sideReach, p.x + size.x + sideReach, p.z + size.z + sideReach))
                    {
                        if (float.IsNaN(raw[i]) && SampleNatural(pos[i], out raw[i])) unknownCount--;
                        if (float.IsNaN(rawL[i]) && SampleNatural(pos[i] - nrm[i] * sideReach, out rawL[i])) unknownSide--;
                        if (float.IsNaN(rawR[i]) && SampleNatural(pos[i] + nrm[i] * sideReach, out rawR[i])) unknownSide--;
                    }
                }
            for (int i = 0; i < sRaw.Count; i++)
            {
                if (sLevel[i] != null) continue;
                var rs = sRaw[i]; var ps = sPos[i];
                for (int k = 0; k < rs.Length; k++)
                {
                    if (!float.IsNaN(rs[k])) continue;
                    var w = new Vector3(ps[k].x, 0f, ps[k].y);
                    var t = FinestAt(w, 0);
                    if (t == null || IsShaped(t)) continue;
                    rs[k] = t.SampleHeight(w) + t.transform.position.y;
                }
            }
        }

        // Natural ground at a point from the finest tile there that is not shaped yet (NaN = not yet).
        // Drafts and detailed tiles agree since both are written over the CPU path (the GPU path lost the
        // detailed tiles' height scale: they lay 25–35 m below the drafts, and the trail stood on a dam).
        private bool SampleNatural(Vector2 q, out float h)
        {
            h = float.NaN;
            var w = new Vector3(q.x, 0f, q.y);
            var t = FinestAt(w, 0);
            if (t == null || IsShaped(t)) return false;
            h = t.SampleHeight(w) + t.transform.position.y;
            return true;
        }

        // The profile starts on the natural ground at the start, but never below 5 m above the
        // terrain's floor (the route may drop below its start height).
        private void TryBase()
        {
            int i0 = Mathf.RoundToInt((0f - sA) / Ds);
            for (int k = -5; k <= 5; k++)
            {
                int j = loop ? (i0 + k + count) % count : i0 + k;
                if (j >= 0 && j < count && float.IsNaN(raw[j])) return;
            }
            float min = 0f, max = 0f;
            foreach (var h in profile) { min = Mathf.Min(min, h); max = Mathf.Max(max, h); }
            baseHeight = Mathf.Max(RawSmooth(i0), 5f - min);
            baseKnown = true;
            Debug.Log($"[Jogging] Strecke: Start auf {baseHeight:0} m, Profil {min:0}…{max:0} m");
        }

        private bool InputsKnown(Terrain t, out bool touches)
        {
            touches = false;
            Vector3 p = t.transform.position, size = t.terrainData.size;
            for (int i = 0; i < Lakes.Streams.Count; i++)
            {
                var st = Lakes.Streams[i]; float r = st.halfLength + 20f;
                if (st.centre.x + r < p.x || st.centre.x - r > p.x + size.x || st.centre.y + r < p.z || st.centre.y - r > p.z + size.z) continue;
                touches = true;
                if (sLevel.Count <= i || sLevel[i] == null) { WhyWaiting(t, $"Bach {i} ohne Wasserstand ({UnknownStream(i)} Punkte offen)"); return false; }
            }
            float reach = 2.5f * sigma;
            foreach (int i in SamplesNear(p.x - reach, p.z - reach, p.x + size.x + reach, p.z + size.z + reach))
            {
                touches = true;
                if (float.IsNaN(raw[i])) { WhyWaiting(t, $"Wegpunkt {pos[i]} ohne Höhe · {Cover(pos[i])}"); return false; }
            }
            float bedReach = halfWidth + maxShoulder;
            foreach (int i in SamplesNear(p.x - bedReach, p.z - bedReach, p.x + size.x + bedReach, p.z + size.z + bedReach))
                if (float.IsNaN(rawL[i]) || float.IsNaN(rawR[i])) { WhyWaiting(t, $"Seitenpunkt bei {pos[i]} ohne Höhe"); return false; }
            return true;
        }

        private IEnumerable<int> SamplesNear(float x0, float z0, float x1, float z1)
        {
            int a = Mathf.FloorToInt(x0 / HashCell), b = Mathf.FloorToInt(x1 / HashCell);
            int c = Mathf.FloorToInt(z0 / HashCell), d = Mathf.FloorToInt(z1 / HashCell);
            for (int x = a; x <= b; x++)
            for (int z = c; z <= d; z++)
                if (hash.TryGetValue((x, z), out var l))
                    foreach (int i in l)
                        if (pos[i].x >= x0 && pos[i].x <= x1 && pos[i].y >= z0 && pos[i].y <= z1) yield return i;
        }

        private static (int, int) Key(Vector2 p) => (Mathf.FloorToInt(p.x / HashCell), Mathf.FloorToInt(p.y / HashCell));

        // Offset field at a world point (m): Gaussian-weighted mean of the sample offsets, faded out.
        private float OffsetAt(float x, float z, List<int> near)
        {
            float s2 = sigma * sigma, wSum = 0f, oSum = 0f;
            foreach (int i in near)
            {
                float dx = pos[i].x - x, dz = pos[i].y - z;
                float w = Mathf.Exp(-(dx * dx + dz * dz) / s2);
                if (w < 1e-4f || float.IsNaN(raw[i])) continue;
                wSum += w;
                oSum += w * (Abs(i) - RawSmooth(i));
            }
            return oSum / (wSum + background);
        }

        private void Shape(Terrain t)
        {
            var d = t.terrainData;
            int res = d.heightmapResolution;
            Vector3 origin = t.transform.position, size = d.size;
            float cx = size.x / (res - 1), cz = size.z / (res - 1);
            float reach = 2.5f * sigma;

            // Samples for the offset field (every 3rd = 6 m is plenty for a 140 m kernel).
            var near = new List<int>();
            foreach (int i in SamplesNear(origin.x - reach, origin.z - reach, origin.x + size.x + reach, origin.z + size.z + reach))
                if (i % 3 == 0) near.Add(i);

            // Coarse offset grid over the tile, bilinear per cell.
            int g = Mathf.CeilToInt(size.x / offsetGrid) + 1;
            var grid = new float[g, g];
            for (int gz = 0; gz < g; gz++)
            for (int gx = 0; gx < g; gx++)
                grid[gz, gx] = OffsetAt(origin.x + gx * offsetGrid, origin.z + gz * offsetGrid, near);

            // Natural heights in metres plus the offset.
            var h = d.GetHeights(0, 0, res, res);
            var world = new float[res, res];
            for (int iz = 0; iz < res; iz++)
            for (int ix = 0; ix < res; ix++)
            {
                float fx = ix * cx / offsetGrid, fz = iz * cz / offsetGrid;
                int x0 = Mathf.Min((int)fx, g - 2), z0 = Mathf.Min((int)fz, g - 2);
                float u = fx - x0, v = fz - z0;
                float off = Mathf.Lerp(Mathf.Lerp(grid[z0, x0], grid[z0, x0 + 1], u), Mathf.Lerp(grid[z0 + 1, x0], grid[z0 + 1, x0 + 1], u), v);
                world[iz, ix] = origin.y + h[iz, ix] * size.y + off;
            }

            // Lakes: a basin below the water level; around it the ground slopes gently down to the
            // water (so a rise between trail and lake doesn't hide it), with a low rim at the edge
            // so the water never floats above lower land.
            foreach (var lake in Lakes.All)
            {
                float wl = baseHeight + lake.relLevel, rOut = lake.radius + 42f;
                if (lake.centre.x + rOut < origin.x || lake.centre.x - rOut > origin.x + size.x ||
                    lake.centre.y + rOut < origin.z || lake.centre.y - rOut > origin.z + size.z) continue;
                for (int iz = 0; iz < res; iz++)
                for (int ix = 0; ix < res; ix++)
                {
                    float dx = origin.x + ix * cx - lake.centre.x, dz = origin.z + iz * cz - lake.centre.y;
                    float dl = Mathf.Sqrt(dx * dx + dz * dz);
                    if (dl >= rOut) continue;
                    float hgt = world[iz, ix];
                    if (dl < lake.radius)
                    {
                        float q = dl / lake.radius;
                        hgt = Mathf.Min(hgt, wl - 0.4f - 2.8f * (1f - q * q));
                    }
                    else
                    {
                        float e = dl - lake.radius;
                        float bank = wl + 0.25f + e * 0.16f;                        // gentle slope to the water
                        float lowered = Mathf.Min(hgt, bank);
                        float u = Mathf.Clamp01((e - 26f) / 16f), k = u * u * (3f - 2f * u);
                        hgt = Mathf.Lerp(lowered, hgt, k);                          // fade out 26–42 m from the water
                        if (e < 8f) hgt = Mathf.Max(hgt, Mathf.Lerp(wl + 0.2f, hgt, e / 8f)); // rim
                    }
                    world[iz, ix] = hgt;
                }
            }

            // Streams: a meandering channel across the trail (the bed below restores the trail on a
            // small embankment where it crosses).
            for (int si = 0; si < Lakes.Streams.Count; si++)
            {
                var st = Lakes.Streams[si];
                float reachS = st.halfLength + 20f;
                if (st.centre.x + reachS < origin.x || st.centre.x - reachS > origin.x + size.x ||
                    st.centre.y + reachS < origin.z || st.centre.y - reachS > origin.z + size.z) continue;
                for (int iz = 0; iz < res; iz++)
                for (int ix = 0; ix < res; ix++)
                {
                    Vector2 q = new Vector2(origin.x + ix * cx, origin.z + iz * cz) - st.centre;
                    float u = Vector2.Dot(q, st.dir);
                    if (Mathf.Abs(u) > st.halfLength + 8f) continue;
                    float v = Vector2.Dot(q + st.centre - st.At(u), st.along); // across the (meandering) channel
                    float av = Mathf.Abs(v);
                    if (av > 8f) continue;
                    if (!StreamLevel(si, u, out float wl)) continue;
                    float target = av < 1.8f ? wl - 0.55f - 0.25f * (1f - (v / 1.8f) * (v / 1.8f))
                                             : wl + 0.15f + (av - 1.8f) * 0.45f;
                    float lowered = Mathf.Min(world[iz, ix], target);
                    float e = Mathf.Clamp01((Mathf.Abs(u) - (st.halfLength - 10f)) / 18f), k = e * e * (3f - 2f * e);
                    world[iz, ix] = Mathf.Lerp(lowered, world[iz, ix], k); // fade out at the ends
                }
            }

            // Path bed: nearest bed sample per cell (stamped), smooth blend into the offset terrain.
            float maxR = halfWidth + maxShoulder;
            var bestD = new float[res, res];
            var bestK = new int[res, res];
            for (int iz = 0; iz < res; iz++) for (int ix = 0; ix < res; ix++) { bestD[iz, ix] = float.MaxValue; bestK[iz, ix] = -1; }
            var bed = new List<int>(); var bedW = new List<float>(); var bedH = new List<float>();
            foreach (int i in SamplesNear(origin.x - maxR, origin.z - maxR, origin.x + size.x + maxR, origin.z + size.z + maxR))
            {
                float ph = Abs(i);
                float natural = raw[i] + OffsetAt(pos[i].x, pos[i].y, near);
                // what the shoulder has to close: on the centre line and — across a slope or a crest,
                // usually more — sideReach to the left and right (all cached per sample: seamless)
                Vector2 qL = pos[i] - nrm[i] * sideReach, qR = pos[i] + nrm[i] * sideReach;
                float gap = Mathf.Abs(ph - natural);
                if (!float.IsNaN(rawL[i])) gap = Mathf.Max(gap, Mathf.Abs(ph - (rawL[i] + OffsetAt(qL.x, qL.y, near))));
                if (!float.IsNaN(rawR[i])) gap = Mathf.Max(gap, Mathf.Abs(ph - (rawR[i] + OffsetAt(qR.x, qR.y, near))));
                bed.Add(i); bedH.Add(ph);
                bedW.Add(Mathf.Clamp(minShoulder + gap * shoulderPerMetre, minShoulder, maxShoulder));
            }
            if (diagDams && bed.Count > 0) LogDams(t, bed, bedH, near);

            // Stamp every sample and the midpoint to the next (samples are 2 m apart).
            for (int k = 0; k < bed.Count; k++)
            {
                int i = bed[k];
                int j = loop ? (i + 1) % count : Mathf.Min(i + 1, count - 1);
                bool link = j != i && (pos[j] - pos[i]).sqrMagnitude < 9f;
                for (int part = 0; part < (link ? 2 : 1); part++)
                {
                    Vector2 c = part == 0 ? pos[i] : (pos[i] + pos[j]) * 0.5f;
                    // near the bed every sample (and midpoint) counts; the wide outer shoulder only from
                    // every 4th sample (8 m apart — a few metres of distance error 50 m out don't show)
                    float r = halfWidth + bedW[k];
                    if (part == 1 || i % 4 != 0) r = Mathf.Min(r, halfWidth + 10f);
                    int ax = Mathf.Max(0, Mathf.FloorToInt((c.x - r - origin.x) / cx)), bx = Mathf.Min(res - 1, Mathf.CeilToInt((c.x + r - origin.x) / cx));
                    int az = Mathf.Max(0, Mathf.FloorToInt((c.y - r - origin.z) / cz)), bz = Mathf.Min(res - 1, Mathf.CeilToInt((c.y + r - origin.z) / cz));
                    for (int gz = az; gz <= bz; gz++)
                    for (int gx = ax; gx <= bx; gx++)
                    {
                        float dx = origin.x + gx * cx - c.x, dz = origin.z + gz * cz - c.y;
                        float dist = Mathf.Sqrt(dx * dx + dz * dz);
                        if (dist <= r && dist < bestD[gz, gx]) { bestD[gz, gx] = dist; bestK[gz, gx] = k; }
                    }
                }
            }
            var lakesHere = new List<Lakes.Lake>();
            foreach (var lake in Lakes.All)
                if (lake.centre.x + lake.radius + 12f >= origin.x && lake.centre.x - lake.radius - 12f <= origin.x + size.x &&
                    lake.centre.y + lake.radius + 12f >= origin.z && lake.centre.y - lake.radius - 12f <= origin.z + size.z) lakesHere.Add(lake);
            for (int iz = 0; iz < res; iz++)
            for (int ix = 0; ix < res; ix++)
            {
                int k = bestK[iz, ix];
                if (k < 0) continue;
                if (lakesHere.Count > 0 && NearLake(lakesHere, origin.x + ix * cx, origin.z + iz * cz)) continue; // the wide shoulder must not fill a lake
                float u = Mathf.Clamp01((bestD[iz, ix] - halfWidth) / bedW[k]);
                float blend = u * u * (3f - 2f * u); // 0 on the bed, 1 at the shoulder edge
                world[iz, ix] = Mathf.Lerp(bedH[k], world[iz, ix], blend);
            }

            // Write back with a vertical range that fits climbs of a few hundred metres.
            float range = Mathf.Max(size.y, terrainHeightRange);
            if (range != size.y) d.size = new Vector3(size.x, range, size.z);
            for (int iz = 0; iz < res; iz++)
            for (int ix = 0; ix < res; ix++)
                h[iz, ix] = Mathf.Clamp01((world[iz, ix] - origin.y) / range);
            d.SetHeightsDelayLOD(0, 0, h);
            d.SyncHeightmap();
        }

        private float nextWhy;
        private void WhyWaiting(Terrain t, string why)
        {
            if (!diagDams || Time.unscaledTime < nextWhy) return;
            var r = TrailPath.Active; if (r == null) return;
            Vector3 p = t.transform.position, sz = t.terrainData.size, q = r.Point(r.RunnerS);
            if (q.x < p.x || q.z < p.z || q.x > p.x + sz.x || q.z > p.z + sz.z) return; // only the runner's tile
            nextWhy = Time.unscaledTime + 3f;
            Debug.Log($"[Weg] Kachel des Läufers ({t.name} bei {p}) wartet: {why}");
        }

        private string Cover(Vector2 q)
        {
            var sb = new System.Text.StringBuilder();
            int n = TerrainGround.Tiles(out var list);
            for (int k = 0; k < n; k++)
                if (list[k].Contains(q.x, q.y))
                    sb.Append($"[{list[k].terrain.name} {list[k].resolution} gen={list[k].generated} geformt={IsShaped(list[k].terrain)} y={list[k].data.bounds.size.y:0.0}] ");
            return sb.Length > 0 ? sb.ToString() : "keine Kachel";
        }

        private int UnknownStream(int i)
        {
            int n = 0; if (i < sRaw.Count) foreach (var v in sRaw[i]) if (float.IsNaN(v)) n++;
            return n;
        }

        private static bool NearLake(List<Lakes.Lake> lakes, float x, float z)
        {
            foreach (var l in lakes)
            {
                float dx = x - l.centre.x, dz = z - l.centre.y, r = l.radius + 10f;
                if (dx * dx + dz * dz < r * r) return true;
            }
            return false;
        }

        // -diag: how far the profile still is from the lifted/lowered ground along the trail — what the
        // shoulder has to close (above ~8 m it gets steeper than 1 : 2.5 → embankments and cuts).
        private static readonly bool diagDams = Jogging.Core.Args.Has("-diag");
        private float nextWaitLog;
        private void LogDams(Terrain t, List<int> bed, List<float> bedH, List<int> near)
        {
            var diffs = new List<float>(bed.Count);
            float up = 0f, down = 0f;
            for (int k = 0; k < bed.Count; k++)
            {
                int i = bed[k];
                float d = bedH[k] - (raw[i] + OffsetAt(pos[i].x, pos[i].y, near));
                diffs.Add(Mathf.Abs(d));
                if (d > 0f) up = Mathf.Max(up, d); else down = Mathf.Max(down, -d);
            }
            // cached "natural" height (maybe from a coarse draft tile) vs this detailed tile before shaping
            float dm = 0f, dmax = 0f; int dn = 0;
            foreach (int i in bed)
            {
                Vector3 p = t.transform.position, sz = t.terrainData.size;
                if (pos[i].x < p.x || pos[i].y < p.z || pos[i].x > p.x + sz.x || pos[i].y > p.z + sz.z) continue;
                float mainH = t.SampleHeight(new Vector3(pos[i].x, 0f, pos[i].y)) + p.y;
                float dd = raw[i] - mainH; dm += dd; dmax = Mathf.Max(dmax, Mathf.Abs(dd)); dn++;
            }
            float pm = 0f, om = 0f; foreach (int i in bed) { pm += Abs(i) - raw[i]; om += OffsetAt(pos[i].x, pos[i].y, near); }
            Debug.Log($"[Weg] {t.name}: Profil − Boden Ø {pm / bed.Count:+0.0;-0.0} m, Ausgleichsfeld Ø {om / bed.Count:+0.0;-0.0} m, Basis {baseHeight:0.0} m");
            if (dn > 0) Debug.Log($"[Weg] {t.name}: gemerkte Höhe − Detailkachel Ø {dm / dn:+0.0;-0.0} m, max {dmax:0.0} m ({dn} Punkte)");
            diffs.Sort();
            float mean = 0f; foreach (var v in diffs) mean += v; mean /= diffs.Count;
            int steep = diffs.FindAll(v => v > (maxShoulder - minShoulder) / shoulderPerMetre).Count;
            Debug.Log($"[Weg] {t.name}: Rest zum Profil Ø {mean:0.0} m, 90 % {diffs[(int)(diffs.Count * 0.9f)]:0.0} m, Damm bis {up:0.0} m, Einschnitt bis {down:0.0} m, steil {100f * steep / diffs.Count:0} %");
        }

        // ---- shaped-tile bookkeeping -----------------------------------------------------------

        private void Remember(Terrain t)
        {
            var d = t.terrainData;
            int c = d.heightmapResolution / 2;
            shaped[t] = new Done { data = d, position = t.transform.position, cx = c, cz = c, checkH = d.GetHeight(c, c) };
        }

        // Still the tile we shaped? (MapMagic may regenerate or move a tile, reusing the object.)
        private bool IsShaped(Terrain t)
        {
            if (!shaped.TryGetValue(t, out var s)) return false;
            var d = t.terrainData;
            if (s.data != d || s.position != t.transform.position) return false;
            if (Mathf.Abs(d.GetHeight(s.cx, s.cz) - s.checkH) > 0.05f)
            {
                Debug.Log($"[Jogging] Weg: Kachel {t.name} neu erzeugt → erneut formen");
                shaped.Remove(t);
                return false;
            }
            return true;
        }

        private Terrain FinestAt(Vector3 w) => FinestAt(w, minResolution);

        private Terrain FinestAt(Vector3 w, int minRes)
        {
            // MapMagic shows a tile before its heights are generated (flat at 0) — not usable yet.
            var best = TerrainGround.FinestAt(w.x, w.z, 0, generatedOnly: true);
            return best != null && best.terrainData.heightmapResolution >= minRes ? best : null;
        }
    }
}
