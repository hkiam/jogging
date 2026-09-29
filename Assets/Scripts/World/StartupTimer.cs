using UnityEngine;
using MapMagic.Terrains;

namespace Jogging.World
{
    /// <summary>
    /// Diagnostics (-diag): when the scene starts and when the first draft / first detailed tile /
    /// the 25 detailed tiles are done — to see where the waiting time before the run goes.
    /// </summary>
    public class StartupTimer : MonoBehaviour
    {
        private int drafts, mains;
        private float t0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (System.Array.IndexOf(Jogging.Core.Args.All, "-diag") < 0) return;
            new GameObject("StartupTimer").AddComponent<StartupTimer>();
        }

        private void Awake()
        {
            t0 = Time.realtimeSinceStartup;
            Debug.Log($"[Startup] Szene geladen nach {t0:0.0} s");
            TerrainTile.OnTileApplied += OnApplied;
            TerrainTile.OnBeforeTileStart += OnStart;
            TerrainTile.OnBeforeTileGenerate += OnGenerate;
        }

        private void OnDestroy()
        {
            TerrainTile.OnTileApplied -= OnApplied;
            TerrainTile.OnBeforeTileStart -= OnStart;
            TerrainTile.OnBeforeTileGenerate -= OnGenerate;
        }

        private bool started, generating;
        private void OnStart(TerrainTile t, MapMagic.Products.TileData d)
        { if (!started) { started = true; Debug.Log($"[Startup] erste Kachel angestoßen nach +{Time.realtimeSinceStartup - t0:0.0} s (Frame {Time.frameCount})"); } }
        // worker thread: only a flag + the time, logged from Update
        private volatile float generateAt = -1f;
        private void OnGenerate(TerrainTile t, MapMagic.Products.TileData d, MapMagic.Products.StopToken s)
        { if (!generating) { generating = true; generateAt = (float)(System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency); } }

        private int frames; private float firstFrameAt = -1f;
        private void Update()
        {
            frames++;
            if (frames == 1) { firstFrameAt = Time.realtimeSinceStartup; Debug.Log($"[Startup] erstes Bild nach +{firstFrameAt - t0:0.0} s"); }
            if (generateAt > 0f)
            {
                double now = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
                Debug.Log($"[Startup] erste Kachel rechnet seit {now - generateAt:0.00} s (jetzt +{Time.realtimeSinceStartup - t0:0.0} s)");
                generateAt = -2f;
            }
            if (frames == 3) Debug.Log($"[Startup] Kachelraster aufgebaut in {MapMagicExt.DeployMs:0} ms (Bild 2: +{Time.realtimeSinceStartup - t0:0.0} s)");
            if (frames == 60) Debug.Log($"[Startup] 60 Bilder nach +{Time.realtimeSinceStartup - t0:0.0} s");
        }

        // The slowest graph nodes of the last detailed tile (one thread per tile: these set the wait).
        private static void LogSlowNodes()
        {
            var mm = FindFirstObjectByType<MapMagic.Core.MapMagicObject>();
            if (mm == null || mm.graph == null) return;
            var list = new System.Collections.Generic.List<MapMagic.Nodes.Generator>(mm.graph.generators);
            list.Sort((a, b) => MapMagicExt.MainTime(b).CompareTo(MapMagicExt.MainTime(a)));
            double total = 0; foreach (var g in list) total += MapMagicExt.MainTime(g);
            var sb = new System.Text.StringBuilder($"[Startup] Knoten (Detail, gesamt {total / 1000.0:0.0} s):");
            for (int i = 0; i < Mathf.Min(8, list.Count); i++) sb.Append($" {list[i].GetType().Name} {MapMagicExt.MainTime(list[i]):0} ms ·");
            Debug.Log(sb.ToString());
        }

        // Draft vs detailed tile at the same spots (the trail reads whichever exists first).
        private static void LogDraftVsMain(TerrainTile tile)
        {
            var m = tile.main?.terrain; var d = tile.draft?.terrain;
            if (m == null || d == null) { Debug.Log("[Startup] Entwurf/Detail: eine fehlt"); return; }
            Vector3 p = m.transform.position, sz = m.terrainData.size;
            float sum = 0f, mx = 0f; int n = 0;
            for (int i = 1; i < 8; i++) for (int j = 1; j < 8; j++)
            {
                var w = new Vector3(p.x + sz.x * i / 8f, 0f, p.z + sz.z * j / 8f);
                float dm = d.SampleHeight(w) + d.transform.position.y - (m.SampleHeight(w) + p.y);
                sum += dm; mx = Mathf.Max(mx, Mathf.Abs(dm)); n++;
            }
            Debug.Log($"[Startup] Entwurf − Detail (Kachel 0,0): Ø {sum / n:+0.0;-0.0} m, max {mx:0.0} m · Größe Entwurf {d.terrainData.size} Auflösung {d.terrainData.heightmapResolution} · Detail {m.terrainData.size} {m.terrainData.heightmapResolution} · Lage {d.transform.position} / {p}");
        }

        private void OnApplied(TerrainTile tile, MapMagic.Products.TileData data, MapMagic.Products.StopToken stop)
        {
            float t = Time.realtimeSinceStartup - t0;
            if (data.isDraft) { drafts++; if (drafts == 1 || drafts == 25 || drafts == 81) Debug.Log($"[Startup] Draft {drafts} nach +{t:0.0} s"); }
            else
            {
                if (tile.coord.x == 0 && tile.coord.z == 0 && tile.main?.terrain != null) // same land? (compare with -erosionref)
                {
                    var d = tile.main.terrain.terrainData;
                    var hs = d.GetHeights(0, 0, d.heightmapResolution, d.heightmapResolution);
                    ulong hash = 1469598103934665603UL;
                    // interior only: the edges are welded to the neighbours, depending on which one came first
                    int n = hs.GetLength(0);
                    for (int z = 8; z < n - 8; z++) for (int x = 8; x < n - 8; x++)
                    { hash ^= (uint)System.BitConverter.SingleToInt32Bits(hs[z, x]); hash *= 1099511628211UL; }
                    Debug.Log($"[Startup] Kachel 0,0 Höhen-Prüfsumme (innen) {hash:X16}");
                }
                mains++;
                if (mains == 1 || mains == 9 || mains == 25) Debug.Log($"[Startup] Detail {mains} nach +{t:0.0} s");
                if (mains == 1) LogSlowNodes();
                if (tile.coord.x == 0 && tile.coord.z == 0) LogDraftVsMain(tile);
            }
        }
    }
}
