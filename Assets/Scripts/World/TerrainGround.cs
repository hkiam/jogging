using UnityEngine;

namespace Jogging.World
{
    /// <summary>
    /// Height lookup on Unity Terrains (e.g. the tiles MapMagic generates around the camera).
    /// Engine-only, so gameplay code doesn't depend on the MapMagic assembly.
    /// </summary>
    public static class TerrainGround
    {
        /// <summary>A streamed terrain tile with its bounds, read once per frame.</summary>
        public struct Tile
        {
            public Terrain terrain;
            public TerrainData data;
            public float x0, z0, x1, z1;
            public int resolution;
            public bool generated; // MapMagic shows a tile before its heights exist (flat at 0)
            public bool Contains(float x, float z) => x >= x0 && z >= z0 && x <= x1 && z <= z1;
        }

        private static Tile[] tiles = new Tile[0];
        private static int tileCount, tileFrame = -1;

        /// <summary>
        /// The drawn terrain tiles of this frame. <c>Terrain.activeTerrains</c> builds a new array on every
        /// call and each property is a native call — with ~80 tiles and thousands of height lookups per
        /// frame (trail ribbon, runners, trees) that cost 50–150 ms spikes and constant garbage.
        /// </summary>
        public static int Tiles(out Tile[] list)
        {
            if (tileFrame != Time.frameCount) Refresh();
            list = tiles;
            return tileCount;
        }

        private static void Refresh()
        {
            tileFrame = Time.frameCount;
            var all = Terrain.activeTerrains;
            if (tiles.Length < all.Length) tiles = new Tile[all.Length + 16];
            tileCount = 0;
            foreach (var t in all)
            {
                if (t == null || !t.drawHeightmap) continue;
                var d = t.terrainData;
                if (d == null) continue;
                Vector3 p = t.transform.position, sz = d.size;
                tiles[tileCount++] = new Tile
                {
                    terrain = t, data = d, x0 = p.x, z0 = p.z, x1 = p.x + sz.x, z1 = p.z + sz.z,
                    resolution = d.heightmapResolution, generated = HasHeights(d),
                };
            }
        }

        // MapMagic shows a tile before its heights exist (all 0). Not TerrainData.bounds: detailed tiles are
        // written with SetHeightsDelayLOD, which leaves the bounds flat — they looked "not generated"
        // forever, and everything fell back to the coarse drafts (25–35 m higher: the trail on a dam).
        private static bool HasHeights(TerrainData d)
        {
            int r = d.heightmapResolution;
            return d.GetHeight(r / 2, r / 2) > 0f || d.GetHeight(r / 4, r / 4) > 0f || d.GetHeight(3 * r / 4, 3 * r / 4) > 0f
                || d.bounds.size.y >= 0.1f;
        }

        /// <summary>The finest drawn tile covering (x, z) with at least minResolution; null if none.</summary>
        public static Terrain FinestAt(float x, float z, int minResolution = 0, bool generatedOnly = false)
        {
            int n = Tiles(out var list), best = -1;
            for (int i = 0; i < n; i++)
            {
                ref var t = ref list[i];
                if (t.resolution < minResolution || (generatedOnly && !t.generated) || !t.Contains(x, z)) continue;
                if (best < 0 || t.resolution > list[best].resolution) best = i;
            }
            if (best < 0) return null;
            var terrain = list[best].terrain;
            if (terrain == null) { tileFrame = -1; return FinestAtUncached(x, z, minResolution, generatedOnly); } // destroyed during this frame
            // MapMagic may have moved (recycled) the tile since the list was read this frame
            Vector3 p = terrain.transform.position;
            if (p.x != list[best].x0 || p.z != list[best].z0) { tileFrame = -1; return FinestAtUncached(x, z, minResolution, generatedOnly); }
            return terrain;
        }

        private static Terrain FinestAtUncached(float x, float z, int minResolution, bool generatedOnly)
        {
            Refresh();
            int best = -1;
            for (int i = 0; i < tileCount; i++)
            {
                ref var t = ref tiles[i];
                if (t.resolution < minResolution || (generatedOnly && !t.generated) || !t.Contains(x, z)) continue;
                if (best < 0 || t.resolution > tiles[best].resolution) best = i;
            }
            return best >= 0 ? tiles[best].terrain : null;
        }

        /// <summary>World-space ground height at (x, z); false if no terrain covers that point yet.</summary>
        public static bool TryHeight(float x, float z, out float height) => TryHeight(x, z, out height, 0);

        /// <summary>As above, but only counts terrains of at least <paramref name="minResolution"/>
        /// heightmap samples (skip MapMagic's coarse draft tiles).</summary>
        public static bool TryHeight(float x, float z, out float height, int minResolution)
        {
            // MapMagic keeps coarse draft tiles alongside full-res main tiles; always read the finest
            // one covering the point, otherwise objects follow a surface that isn't the rendered one.
            var best = FinestAt(x, z, minResolution);
            if (best == null) { height = 0f; return false; }
            height = best.SampleHeight(new Vector3(x, 0f, z)) + best.transform.position.y;
            return true;
        }

        /// <summary>Grade in % along +Z around z (central difference over ±halfSpan metres).</summary>
        public static bool TryGrade(float x, float z, float halfSpan, out float gradePercent)
        {
            gradePercent = 0f;
            if (!TryHeight(x, z - halfSpan, out float a) || !TryHeight(x, z + halfSpan, out float b)) return false;
            gradePercent = (b - a) / (2f * halfSpan) * 100f;
            return true;
        }
    }
}
