using UnityEngine;

namespace Jogging.World
{
    /// <summary>
    /// Removes terrain detail grass along the trail (within halfWidth of the centre line) on every
    /// terrain tile the trail crosses near the runner — at once when MapMagic has applied a tile (so no
    /// grass shows on the trail for a moment), and as a periodic re-check because MapMagic re-applies
    /// details when a tile is regenerated (draft → main). halfWidth is a little wider than the trail:
    /// a grass tuft reaches beyond its cell. Works along the curved
    /// <see cref="TrailPath"/>; without one the trail is the line x = 0.
    /// </summary>
    public class TrailGrassClearer : MonoBehaviour
    {
        [SerializeField] private float halfWidth = 3f;
        [SerializeField] private float interval = 0.75f;
        [SerializeField] private float behind = 40f;
        [SerializeField] private float ahead = 320f;

        private float next;

        private void OnEnable() => MapMagic.Terrains.TerrainTile.OnTileApplied += OnTileApplied;
        private void OnDisable() => MapMagic.Terrains.TerrainTile.OnTileApplied -= OnTileApplied;

        // A tile has just got its grass: clear the trail on it right away (main thread).
        private void OnTileApplied(MapMagic.Terrains.TerrainTile tile, MapMagic.Products.TileData data, MapMagic.Products.StopToken stop)
        {
            var level = data != null && data.isDraft ? tile.draft : tile.main;
            var t = level?.terrain;
            var d = t != null ? t.terrainData : null;
            if (d == null || d.detailPrototypes.Length == 0) return;
            var path = TrailPath.Active;
            float s0 = (path != null ? path.RunnerS : 0f) - behind, s1 = s0 + behind + ahead;
            if (SegmentInTile(t, d, path, s0, s1, out float a, out float b)) Clear(t, d, path, a, b);
            foreach (var st in strips) ClearStrip(st.pts, st.hw); // side paths on this tile keep clear too
        }

        private void Update()
        {
            long t0 = FrameWork.Start();
            UpdateWork();
            FrameWork.Stop("Gras", t0);
        }

        private void UpdateWork()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + interval;
            var path = TrailPath.Active;
            float s0 = (path != null ? path.RunnerS : 0f) - behind, s1 = s0 + behind + ahead;
            foreach (var t in Terrain.activeTerrains)
            {
                var d = t != null ? t.terrainData : null;
                if (d == null || d.detailPrototypes.Length == 0) continue;
                if (!SegmentInTile(t, d, path, s0, s1, out float a, out float b)) continue;
                if (NeedsClearing(t, d, path, a, b)) Clear(t, d, path, a, b);
            }
        }

        /// <summary>Clears the grass along a polyline (side paths) on every loaded tile it touches.</summary>
        private static readonly System.Collections.Generic.List<(System.Collections.Generic.List<Vector3> pts, float hw)> strips = new System.Collections.Generic.List<(System.Collections.Generic.List<Vector3>, float)>();

        /// <summary>Forget the side paths (new route).</summary>
        public static void ForgetStrips() => strips.Clear();

        public static void ClearStrip(System.Collections.Generic.List<Vector3> pts, float halfWidth, bool remember = false)
        {
            if (pts == null || pts.Count < 2) return;
            if (remember) { if (strips.Count > 200) strips.RemoveAt(0); strips.Add((pts, halfWidth)); } // cleared again when a tile gets new grass
            foreach (var t in Terrain.activeTerrains)
            {
                var d = t != null ? t.terrainData : null;
                if (d == null || d.detailPrototypes.Length == 0) continue;
                int w = d.detailWidth, h = d.detailHeight;
                float cellX = d.size.x / w, cellZ = d.size.z / h;
                Vector3 o = t.transform.position;
                int x0 = w, z0 = h, x1 = -1, z1 = -1;
                foreach (var p in pts)
                {
                    int cx = Mathf.FloorToInt((p.x - o.x) / cellX), cz = Mathf.FloorToInt((p.z - o.z) / cellZ);
                    int rx = Mathf.CeilToInt(halfWidth / cellX) + 1, rz = Mathf.CeilToInt(halfWidth / cellZ) + 1;
                    x0 = Mathf.Min(x0, cx - rx); x1 = Mathf.Max(x1, cx + rx); z0 = Mathf.Min(z0, cz - rz); z1 = Mathf.Max(z1, cz + rz);
                }
                x0 = Mathf.Max(0, x0); z0 = Mathf.Max(0, z0); x1 = Mathf.Min(w - 1, x1); z1 = Mathf.Min(h - 1, z1);
                if (x1 < x0 || z1 < z0) continue;
                int rw = x1 - x0 + 1, rh = z1 - z0 + 1;
                var mask = new bool[rh, rw];
                bool any = false;
                for (int iz = 0; iz < rh; iz++)
                    for (int ix = 0; ix < rw; ix++)
                    {
                        var c = new Vector2(o.x + (x0 + ix + 0.5f) * cellX, o.z + (z0 + iz + 0.5f) * cellZ);
                        for (int k = 0; k + 1 < pts.Count; k++)
                        {
                            Vector2 a = new Vector2(pts[k].x, pts[k].z), b = new Vector2(pts[k + 1].x, pts[k + 1].z);
                            Vector2 ab = b - a; float tt = Mathf.Clamp01(Vector2.Dot(c - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                            float taper = Mathf.Lerp(1f, 0.4f, (k + tt) / (pts.Count - 1)); // the path narrows towards its end
                            if ((a + ab * tt - c).sqrMagnitude < halfWidth * halfWidth * taper * taper) { mask[iz, ix] = true; any = true; break; }
                        }
                    }
                if (!any) continue;
                for (int layer = 0; layer < d.detailPrototypes.Length; layer++)
                {
                    var m = d.GetDetailLayer(x0, z0, rw, rh, layer);
                    for (int iz = 0; iz < rh; iz++) for (int ix = 0; ix < rw; ix++) if (mask[iz, ix]) m[iz, ix] = 0;
                    d.SetDetailLayer(x0, z0, layer, m);
                }
            }
        }

        /// <summary>Grass cells on the trail (centre and ±1.5 m, every 2 m) between the arc lengths (tests).</summary>
        public static int GrassOnTrail(float from, float to)
        {
            var path = TrailPath.Active;
            int n = 0;
            for (float s = from; s <= to; s += 2f)
            for (int k = -1; k <= 1; k++)
            {
                float lat = k * Mathf.Min(1.5f, TrailSurface.HalfWidth(s) - 0.3f); // on the surface, however wide it is here
                Vector3 w = path != null ? path.Offset(s, lat) : new Vector3(lat, 0f, s);
                foreach (var t in Terrain.activeTerrains)
                {
                    var d = t != null ? t.terrainData : null;
                    if (d == null || d.detailPrototypes.Length == 0 || !Cell(t, d, w, out int cx, out int cz)) continue;
                    for (int layer = 0; layer < d.detailPrototypes.Length; layer++)
                        if (d.GetDetailLayer(cx, cz, 1, 1, layer)[0, 0] > 0) { n++; break; }
                }
            }
            return n;
        }

        private static Vector3 P(TrailPath path, float s) => path != null ? path.Point(s) : new Vector3(0f, 0f, s);

        // Arc-length range [a, b] of the trail window that lies on this tile (plus margin).
        private bool SegmentInTile(Terrain t, TerrainData d, TrailPath path, float s0, float s1, out float a, out float b)
        {
            Vector3 p = t.transform.position, sz = d.size;
            float m = halfWidth + 1f;
            a = float.MaxValue; b = float.MinValue;
            for (float s = s0; s <= s1; s += 2f)
            {
                var c = P(path, s);
                if (c.x < p.x - m || c.z < p.z - m || c.x > p.x + sz.x + m || c.z > p.z + sz.z + m) continue;
                a = Mathf.Min(a, s); b = Mathf.Max(b, s);
            }
            if (a > b) return false;
            a -= 2f; b += 2f;
            return true;
        }

        private static bool Cell(Terrain t, TerrainData d, Vector3 w, out int cx, out int cz)
        {
            Vector3 p = t.transform.position, sz = d.size;
            cx = Mathf.FloorToInt((w.x - p.x) / sz.x * d.detailWidth);
            cz = Mathf.FloorToInt((w.z - p.z) / sz.z * d.detailHeight);
            return cx >= 0 && cz >= 0 && cx < d.detailWidth && cz < d.detailHeight;
        }

        // every 2 m at the centre and near both edges: where grass is sparse, centre probes alone could all
        // hit empty cells while single tufts stand on the trail (the E2E check looks at these same points)
        private static readonly System.Collections.Generic.List<Vector2Int> probe = new System.Collections.Generic.List<Vector2Int>();

        // probe cells every 2 m (centre and both edges); each 20 m piece reads every layer once as a small block
        private static bool NeedsClearing(Terrain t, TerrainData d, TrailPath path, float a, float b)
        {
            for (float p0 = a; p0 <= b; p0 += 20f)
            {
                probe.Clear();
                int x0 = int.MaxValue, z0 = int.MaxValue, x1 = -1, z1 = -1;
                for (float s = p0; s < p0 + 20f && s <= b; s += 2f)
                {
                    float edge = Mathf.Min(1.5f, TrailSurface.HalfWidth(s) - 0.3f);
                    for (int k = -1; k <= 1; k++)
                    {
                        Vector3 w = path != null ? path.Offset(s, k * edge) : new Vector3(k * edge, 0f, s);
                        if (!Cell(t, d, w, out int cx, out int cz)) continue;
                        probe.Add(new Vector2Int(cx, cz));
                        x0 = Mathf.Min(x0, cx); z0 = Mathf.Min(z0, cz); x1 = Mathf.Max(x1, cx); z1 = Mathf.Max(z1, cz);
                    }
                }
                if (probe.Count == 0) continue;
                for (int layer = 0; layer < d.detailPrototypes.Length; layer++)
                {
                    var m = d.GetDetailLayer(x0, z0, x1 - x0 + 1, z1 - z0 + 1, layer);
                    foreach (var c in probe) if (m[c.y - z0, c.x - x0] > 0) return true;
                }
            }
            return false;
        }

        // Stamp a disc of radius halfWidth at every metre of the segment into the tile's detail maps.
        private void Clear(Terrain t, TerrainData d, TrailPath path, float a, float b)
        {
            int w = d.detailWidth, h = d.detailHeight;
            float cellX = d.size.x / w, cellZ = d.size.z / h;
            // every cell that touches the strip (not only those whose centre lies in it): on a coarse grass
            // grid (tablets) a cell half on the trail would keep its tufts
            float reach = halfWidth + 0.5f * Mathf.Sqrt(cellX * cellX + cellZ * cellZ);
            int rx = Mathf.CeilToInt(reach / cellX), rz = Mathf.CeilToInt(reach / cellZ);

            // Bounding cell rect of the segment.
            int x0 = w, z0 = h, x1 = -1, z1 = -1;
            for (float s = a; s <= b; s += 1f)
            {
                Vector3 c = P(path, s);
                Vector3 tp = t.transform.position;
                int cx = Mathf.FloorToInt((c.x - tp.x) / cellX), cz = Mathf.FloorToInt((c.z - tp.z) / cellZ);
                x0 = Mathf.Min(x0, cx - rx); x1 = Mathf.Max(x1, cx + rx);
                z0 = Mathf.Min(z0, cz - rz); z1 = Mathf.Max(z1, cz + rz);
            }
            x0 = Mathf.Clamp(x0, 0, w - 1); x1 = Mathf.Clamp(x1, 0, w - 1);
            z0 = Mathf.Clamp(z0, 0, h - 1); z1 = Mathf.Clamp(z1, 0, h - 1);
            if (x1 < x0 || z1 < z0) return;
            int rw = x1 - x0 + 1, rh = z1 - z0 + 1;

            var mask = new bool[rh, rw];
            float r2 = reach * reach;
            Vector3 origin = t.transform.position;
            float cellHalf = 0.5f * Mathf.Sqrt(cellX * cellX + cellZ * cellZ);
            for (float s = a; s <= b; s += 0.5f)
            {
                // cleared a little beyond the surface's own edge (narrow forest and meadow paths keep their grass)
                float rs = Mathf.Min(halfWidth, TrailSurface.HalfWidth(s) + 0.6f) + cellHalf;
                r2 = rs * rs;
                Vector3 c = P(path, s);
                int cx = Mathf.FloorToInt((c.x - origin.x) / cellX), cz = Mathf.FloorToInt((c.z - origin.z) / cellZ);
                for (int dz = -rz; dz <= rz; dz++)
                for (int dx = -rx; dx <= rx; dx++)
                {
                    int ix = cx + dx - x0, iz = cz + dz - z0;
                    if (ix < 0 || iz < 0 || ix >= rw || iz >= rh) continue;
                    float wx = origin.x + (cx + dx + 0.5f) * cellX - c.x, wz = origin.z + (cz + dz + 0.5f) * cellZ - c.z;
                    if (wx * wx + wz * wz <= r2) mask[iz, ix] = true;
                }
            }

            for (int layer = 0; layer < d.detailPrototypes.Length; layer++)
            {
                var m = d.GetDetailLayer(x0, z0, rw, rh, layer);
                for (int iz = 0; iz < rh; iz++)
                for (int ix = 0; ix < rw; ix++)
                    if (mask[iz, ix]) m[iz, ix] = 0;
                d.SetDetailLayer(x0, z0, layer, m);
            }
        }
    }
}
