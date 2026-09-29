using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using MapMagic.Core;

namespace Jogging.World
{
    /// <summary>
    /// Graphics level for weaker computers: "low" / "medium" / "high" (default high = as built).
    /// Render resolution, shadow distance and cascades, grass density and distance, tree distance.
    /// Applied at scene start and to terrain tiles as they stream in. Geräte → Grafik; test: -graphics low.
    /// </summary>
    public class GraphicsQuality : MonoBehaviour
    {
        private float next;

        /// <summary>Render scale in use (diagnostics).</summary>
        public static float RenderScale => GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset u ? u.renderScale : 1f;

        public static string Level
        {
            get
            {
                var a = Jogging.Core.Args.All;
                for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-graphics") return a[i + 1];
                var st = Jogging.Core.AppSettings.Current;
                return st.graphicsChosen ? st.graphics : Jogging.Core.AppSettings.DefaultGraphics;
            }
        }

        /// <summary>Share of rain/snow particles (low: 40 %, medium: 70 %).</summary>
        public static float ParticleFactor => Level == "minimal" ? 0.25f : Level == "low" ? 0.4f : Level == "medium" ? 0.7f : 1f;

        /// <summary>Share of scattered trees, bushes and stones that is placed (the rest keep their spots).</summary>
        public static float ScatterFactor => Level == "minimal" ? 0.4f : 1f;

        // Low-memory devices: textures at half resolution (a quarter of the memory), set before the
        // scene's textures are loaded.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BeforeScene()
        {
            // The render settings of the level already for the first frames: until Start ran, the first frames
            // rendered at full resolution with HDR and a shadow map — buffers that alone made up a good part
            // of the Fire HD 10's loading peak.
            ApplyUrp(Level);
            // test: -tfr N — target frame rate (Android default 30; frame pacing then locks to 60/3 = 20
            // as soon as frames take a bit more than 33 ms)
            // Tablets aim at 60: with the default of 30 the frame pacing locked the Fire HD 10 to 60/3 = 20 fps
            // as soon as a frame took a bit more than 33 ms (measured: 20 → 29 fps, far fewer slow frames).
            float tfr = Arg("-tfr");
            if (tfr > 0f) { Application.targetFrameRate = (int)tfr; Debug.Log($"[Jogging] Test: Ziel-Bildrate {tfr}"); }
            else if (Application.isMobilePlatform) Application.targetFrameRate = 60;
            if (!Jogging.Core.Platform.LowMemory) return;
            QualitySettings.globalTextureMipmapLimit = 1;
            Debug.Log($"[Jogging] Wenig Arbeitsspeicher ({SystemInfo.systemMemorySize} MB): Texturen halbe Auflösung, kleineres Geländeraster");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            // For every scene, not just the first: each run start and "Fertig" reloads the scene — before,
            // the level (tile range, base maps, grass, terrain detail) applied only to the start screen,
            // and every run on the Fire HD 10 had 81 coarse and 25 detailed tiles at full settings.
            SetupScene();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) => SetupScene();
        }

        private static void SetupScene()
        {
            new GameObject("GraphicsQuality").AddComponent<GraphicsQuality>();
            if (Jogging.Core.Args.Has("-notrees"))
                foreach (var t in FindObjectsByType<TerrainTreeScatter>(FindObjectsSortMode.None)) t.gameObject.SetActive(false);
            if (Jogging.Core.Args.Has("-nofigures"))
            {
                foreach (var m in FindObjectsByType<AiRunnerManager>(FindObjectsSortMode.None)) m.gameObject.SetActive(false);
                foreach (var m in FindObjectsByType<SpectatorManager>(FindObjectsSortMode.None)) m.gameObject.SetActive(false);
                foreach (var m in FindObjectsByType<SpectatorGroups>(FindObjectsSortMode.None)) m.gameObject.SetActive(false);
            }
            // Before MapMagic's first Update deploys the grid: 3×3 detailed tiles instead of 5×5 — the
            // terrain is the other big memory user. The coarse draft tiles (65 px, cheap) must still
            // reach ~350 m beyond the detailed ones: TrailShaper blends the ground towards the trail
            // over that distance and waits for those heights (with 5×5 drafts it waited forever).
            if (Jogging.Core.Platform.LowMemory)
            {
                var mm = FindFirstObjectByType<MapMagicObject>();
                // Every terrain tile's base map (the texture used in the distance) is a 1024² render target
                // of its own — with dozens of tiles most of the graphics memory the Fire HD 10 died of.
                // It only shows beyond 50 m there (ApplyTerrains): 256² is plenty.
                if (mm != null) { mm.tiles.generateRange = 3; mm.mainRange = 1; mm.terrainSettings.baseMapResolution = 256; }
            }
            // test: -far N — camera view distance in m
            for (int i = 0; i < Jogging.Core.Args.All.Length - 1; i++)
                if (Jogging.Core.Args.All[i] == "-far" && float.TryParse(Jogging.Core.Args.All[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float far))
                    foreach (var c in Camera.allCameras) c.farClipPlane = far;
            // test: -range N — how many coarse tiles around the runner (finding the Fire's loading peak)
            var ra = Jogging.Core.Args.All;
            for (int i = 0; i < ra.Length - 1; i++)
                if (ra[i] == "-range" && int.TryParse(ra[i + 1], out int rg))
                {
                    var mm2 = FindFirstObjectByType<MapMagicObject>();
                    if (mm2 != null) { mm2.tiles.generateRange = rg; Debug.Log($"[Jogging] Test: Kachelbereich {rg}"); }
                }
        }

        private void Start() => Apply();

        public static void ApplyNow() { var g = FindFirstObjectByType<GraphicsQuality>(); if (g != null) g.Apply(); }

        private void Apply()
        {
            string l = Level;
            bool min = l == "minimal"; // weak tablet GPUs (e.g. Fire HD 10): half resolution, no shadows, no post FX
            ApplyUrp(l);
            // Mesh LOD (Editor/MeshLods.cs): trees, stones and figures switch to coarser levels — the
            // higher the threshold, the sooner
            QualitySettings.meshLodThreshold = min ? 3f : l == "low" ? 2f : l == "medium" ? 1.4f : 1f;
            foreach (var v in FindObjectsByType<Volume>(FindObjectsSortMode.None)) v.enabled = !min;
            foreach (var cam in Camera.allCameras)
            {
                var data = cam.GetComponent<UniversalAdditionalCameraData>();
                if (data != null) data.renderPostProcessing = !min;
            }
            var mm = FindFirstObjectByType<MapMagicObject>();
            if (mm != null)
            {
                mm.terrainSettings.detailDensity = Density(l);
                mm.terrainSettings.detailDistance = Distance(l);
            }
            ApplyTerrains();
        }

        private static void ApplyUrp(string l)
        {
            bool min = l == "minimal";
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.renderScale = rsArg > 0f ? rsArg : min ? 0.5f : l == "low" ? 0.7f : l == "medium" ? 0.85f : 1f;
                urp.shadowDistance = min ? 0f : l == "low" ? 80f : l == "medium" ? 140f : 220f;
                urp.shadowCascadeCount = min || l == "low" ? 2 : 4;
                urp.supportsHDR = !min;
                urp.msaaSampleCount = 1;
            }
        }

        private void Update()
        {
            long t0 = FrameWork.Start();
            UpdateWork();
            FrameWork.Stop("Grafik", t0);
        }

        private void UpdateWork()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 2f;
            ApplyTerrains(); // newly streamed tiles
        }

        // Test switches (finding what takes the memory on weak tablets): -nodetail, -notrees, -nofigures
        private static readonly bool noDetail = Jogging.Core.Args.Has("-nodetail");
        // … and -pe N (terrain pixel error), -bmd N (base map distance), -rs N (render scale)
        private static float Arg(string name)
        {
            var a = Jogging.Core.Args.All;
            for (int i = 0; i < a.Length - 1; i++)
                if (a[i] == name && float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) return v;
            return -1f;
        }
        private static readonly float peArg = Arg("-pe"), bmdArg = Arg("-bmd"), rsArg = Arg("-rs");

        private static float Density(string l) => noDetail ? 0f : l == "minimal" ? 0.2f : l == "low" ? 0.45f : l == "medium" ? 0.75f : 1f;
        private static float Distance(string l) => l == "minimal" ? 35f : l == "low" ? 60f : l == "medium" ? 90f : 110f;

        private static void ApplyTerrains()
        {
            string l = Level;
            float density = Density(l), dist = Distance(l);
            float pixelError = peArg > 0f ? peArg : l == "minimal" ? 10f : 2f, basemap = bmdArg >= 0f ? bmdArg : l == "minimal" ? 50f : -1f; // beyond: the cheap pre-baked terrain texture
            foreach (var t in Terrain.activeTerrains)
            {
                if (t == null) continue;
                if (!Mathf.Approximately(t.detailObjectDensity, density)) t.detailObjectDensity = density;
                if (!Mathf.Approximately(t.detailObjectDistance, dist)) t.detailObjectDistance = dist;
                if (l == "minimal")
                {
                    if (!Mathf.Approximately(t.heightmapPixelError, pixelError)) t.heightmapPixelError = pixelError; // coarser far terrain
                    if (!Mathf.Approximately(t.basemapDistance, basemap)) t.basemapDistance = basemap;
                    if (Jogging.Core.Platform.LowMemory && t.terrainData != null && t.terrainData.baseMapResolution > 256) t.terrainData.baseMapResolution = 256;
                    if (t.shadowCastingMode != ShadowCastingMode.Off) t.shadowCastingMode = ShadowCastingMode.Off;
                }
            }
        }
    }
}
