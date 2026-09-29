using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using MapMagic.Terrains;

namespace Jogging.World
{
    /// <summary>
    /// Frame rate diagnostics (-diag or -fps): every 10 s the average frame rate and the slowest 1 %
    /// of frames ("1% low") into the log — during the run only (menus and loading don't count).
    /// -perf adds where the time goes: CPU main/render thread and GPU per frame (FrameTimingManager),
    /// draw calls, SetPass calls, triangles, and how many figures and trees are drawn.
    /// </summary>
    [DefaultExecutionOrder(10000)] // LateUpdate after everyone else: the frame is complete
    public class FrameStats : MonoBehaviour
    {
        private readonly List<float> times = new List<float>();
        private float next = 10f;

        /// <summary>Average and 1 % low of the last window (for the settings page / tests).</summary>
        public static float LastAvgFps { get; private set; }
        public static float LastLowFps { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var a = Jogging.Core.Args.All;
            if (System.Array.IndexOf(a, "-mem") >= 0) MemLog.Start();
            if (System.Array.IndexOf(a, "-diag") < 0 && System.Array.IndexOf(a, "-fps") < 0 && System.Array.IndexOf(a, "-hitch") < 0
                && System.Array.IndexOf(a, "-perf") < 0) return;
            var go = new GameObject("FrameStats");
            DontDestroyOnLoad(go);
            go.AddComponent<FrameStats>();
        }

        // what happened since the last frame (tile events arrive on the main thread)
        private int applied, appliedDraft, moved, lod, gc0;
        private int hitches;
        private static bool hitchLog;

        // -perf
        private bool perf;
        private ProfilerRecorder drawCalls, setPass, tris;
        private double sumMain, sumRender, sumGpu, sumDraw, sumSetPass, sumTris; private int perfFrames, gpuFrames;
        private readonly FrameTiming[] timing = new FrameTiming[1];

        private void OnEnable()
        {
            perf = System.Array.IndexOf(Jogging.Core.Args.All, "-perf") >= 0;
            if (perf)
            {
                drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
                setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
                tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            }
            hitchLog = System.Array.IndexOf(Jogging.Core.Args.All, "-hitch") >= 0;
            FrameWork.On = hitchLog;
            TerrainTile.OnTileApplied += OnApplied;
            TerrainTile.OnTileMoved += OnMoved;
            TerrainTile.OnLodSwitched += OnLod;
            gc0 = System.GC.CollectionCount(0);
        }

        private void OnDisable()
        {
            TerrainTile.OnTileApplied -= OnApplied;
            TerrainTile.OnTileMoved -= OnMoved;
            TerrainTile.OnLodSwitched -= OnLod;
            if (perf) { drawCalls.Dispose(); setPass.Dispose(); tris.Dispose(); }
        }

        private void SamplePerf()
        {
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timing) > 0)
            {
                sumMain += timing[0].cpuMainThreadFrameTime; sumRender += timing[0].cpuRenderThreadFrameTime;
                if (timing[0].gpuFrameTime > 0) { sumGpu += timing[0].gpuFrameTime; gpuFrames++; }
            }
            sumDraw += drawCalls.LastValue; sumSetPass += setPass.LastValue; sumTris += tris.LastValue;
            perfFrames++;
        }

        private static long Tris(Mesh m) { long n = 0; for (int i = 0; i < m.subMeshCount; i++) n += (long)m.GetIndexCount(i) / 3; return n; }

        private void LogPerf()
        {
            if (perfFrames == 0) return;
            int figures = 0, trees = 0; long figTris = 0, objTris = 0; int objs = 0;
            foreach (var r in FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
                if (r.isVisible) { figures++; if (r.sharedMesh != null) figTris += Tris(r.sharedMesh); }
            foreach (var g in FindObjectsByType<LODGroup>(FindObjectsSortMode.None)) if (g.gameObject.activeInHierarchy) trees++;
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (r.isVisible && r.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null) { objs++; objTris += Tris(mf.sharedMesh); }
            var byMesh = new Dictionary<string, long>();
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (r.isVisible && r.TryGetComponent<MeshFilter>(out var mf2) && mf2.sharedMesh != null)
                { string k = mf2.sharedMesh.name + (r.GetComponentInParent<LODGroup>() != null ? "[LOD]" : ""); byMesh.TryGetValue(k, out long v); byMesh[k] = v + Tris(mf2.sharedMesh); }
            var top = new List<KeyValuePair<string, long>>(byMesh); top.Sort((x, y) => y.Value.CompareTo(x.Value));
            var sb = new System.Text.StringBuilder("[Leistung] schwerste: ");
            for (int i = 0; i < Mathf.Min(8, top.Count); i++) sb.Append($"{top[i].Key} {top[i].Value / 1000}k · ");
            Debug.Log(sb.ToString());
            string gpu = gpuFrames > 0 ? $"{sumGpu / gpuFrames:0.0}" : "–";
            Debug.Log($"[Leistung] CPU Haupt {sumMain / perfFrames:0.0} ms · Render {sumRender / perfFrames:0.0} ms · GPU {gpu} ms · " +
                      $"Draw Calls {sumDraw / perfFrames:0} · SetPass {sumSetPass / perfFrames:0} · Dreiecke {sumTris / perfFrames / 1000:0}k · " +
                      $"davon Figuren {figTris / 1000}k ({figures} Teile), Objekte {objTris / 1000}k ({objs}; {trees} mit LOD), Rest (Gelände, Gras) {System.Math.Max(0, (long)(sumTris / perfFrames) - figTris - objTris) / 1000}k · " +
                      $"{Screen.width}×{Screen.height} × {GraphicsQuality.RenderScale:0.00}");
            sumMain = sumRender = sumGpu = sumDraw = sumSetPass = sumTris = 0; perfFrames = gpuFrames = 0;
        }

        // unscaledDeltaTime in Update is the PREVIOUS frame → describe each frame at its end
        private string last = "";
        private void LateUpdate()
        {
            if (!hitchLog) return;
            int gc = System.GC.CollectionCount(0);
            last = $"Kachel fertig {applied} (Entwurf {appliedDraft}) · verschoben {moved} · LOD {lod} · GC {gc - gc0} · " +
                   MapMagicExt.HitchLine() + $" · Skripte: {FrameWork.TakeFrame()}";
            applied = appliedDraft = moved = lod = 0; gc0 = gc;
        }

        private void OnApplied(TerrainTile t, MapMagic.Products.TileData d, MapMagic.Products.StopToken s) { if (d.isDraft) appliedDraft++; else applied++; }
        private void OnMoved(TerrainTile t) => moved++;
        private void OnLod(TerrainTile t, bool a, bool b) => lod++;

        private void Update()
        {
            var sm = Jogging.UI.RunSessionUI.Session;
            bool run = sm != null && sm.Moves;
            if (run) times.Add(Time.unscaledDeltaTime);
            if (run && perf) SamplePerf();
            // -hitch: every frame over 40 ms during the run, with what happened in it
            if (run && Time.unscaledDeltaTime > 0.04f)
            {
                hitches++;
                if (hitchLog) Debug.Log($"[Hitch] {Time.unscaledDeltaTime * 1000f:0} ms · {last}");
            }
            if (Time.realtimeSinceStartup < next) return;
            next = Time.realtimeSinceStartup + 10f;
            if (times.Count < 30) { times.Clear(); return; }
            float sum = 0f; foreach (var t in times) sum += t;
            times.Sort();
            int k = Mathf.Max(1, times.Count / 100);
            float worst = 0f; for (int i = times.Count - k; i < times.Count; i++) worst += times[i];
            LastAvgFps = times.Count / sum;
            LastLowFps = k / worst;
            Debug.Log($"[FPS] Ø {LastAvgFps:0} · 1% low {LastLowFps:0} · Ruckler {hitches} · {times.Count} Bilder · Grafik {GraphicsQuality.Level}");
            times.Clear(); hitches = 0;
            if (perf) LogPerf();
        }
    }

    /// <summary>
    /// -mem: where the memory goes while loading (weak tablets were killed at a short peak): every 0.5 s
    /// for the first 30 s after each scene load — Unity's allocated and reserved memory, textures,
    /// the graphics driver, and the biggest texture and mesh groups at the peak.
    /// </summary>
    public class MemLog : MonoBehaviour
    {
        private static MemLog instance;
        private float until, next; private long peak;

        public static void Start()
        {
            if (instance != null) return;
            var go = new GameObject("MemLog"); DontDestroyOnLoad(go);
            instance = go.AddComponent<MemLog>();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (sc, m) => { instance.until = Time.realtimeSinceStartup + 30f; instance.peak = 0; };
            instance.until = Time.realtimeSinceStartup + 30f;
        }

        private void Update()
        {
            float now = Time.realtimeSinceStartup;
            if (now > until || now < next) return;
            next = now + 0.5f;
            long mb = 1024 * 1024;
            long alloc = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong(), reserved = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong();
            long gfx = UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver();
            long tex = (long)Texture.currentTextureMemory, mono = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            Debug.Log($"[Speicher] {now:0.0} s · Unity belegt {alloc / mb} MB (reserviert {reserved / mb}) · Texturen {tex / mb} MB · Grafiktreiber {gfx / mb} MB · C# {mono / mb} MB");
            if (alloc + gfx > peak + 64 * mb) { peak = alloc + gfx; LogBiggest(); }
        }

        private static void LogBiggest()
        {
            var tex = new List<(long, string)>();
            foreach (var t in Resources.FindObjectsOfTypeAll<Texture>())
            {
                long b = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t);
                if (b > 4 * 1024 * 1024) tex.Add((b, $"{t.name} {t.width}×{t.height} {(t is Texture2D t2 ? t2.format.ToString() : t.GetType().Name)}"));
            }
            tex.Sort((x, y) => y.Item1.CompareTo(x.Item1));
            long meshes = 0; int nm = 0;
            foreach (var m in Resources.FindObjectsOfTypeAll<Mesh>()) { meshes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m); nm++; }
            var sb = new System.Text.StringBuilder($"[Speicher] Spitze: {nm} Meshes {meshes / (1024 * 1024)} MB · größte Texturen: ");
            for (int i = 0; i < Mathf.Min(10, tex.Count); i++) sb.Append($"{tex[i].Item2} {tex[i].Item1 / (1024 * 1024)} MB; ");
            Debug.Log(sb.ToString());
        }
    }
}
