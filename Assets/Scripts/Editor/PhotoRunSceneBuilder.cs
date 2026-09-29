using System.Linq;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using MapMagic.Core;
using MapMagic.Terrains;
using Jogging.World;

namespace Jogging.EditorTools
{
    /// <summary>
    /// (Re)configures the run scene (Assets/Scenes/PhotoRun.unity, the app's only scene). The scene
    /// itself holds the hand-made parts (runner, HUD, menus, treadmill, profile); this builder
    /// resets its own generated objects to defaults and configures them again — reusing the objects
    /// and components (same file IDs), so a build leaves the scene file unchanged unless the setup
    /// really changed. It can be run any number of times:
    ///   • MapMagic 2 infinite terrain generated around the camera (demo graph, real terrain layers,
    ///     erosion, detail grass), the runner now MOVES over it (TrackManager real-terrain mode),
    ///   • a terrain-hugging dirt trail and scattered realistic birch/pine trees (converted to URP),
    ///   • physically-plausible light (HDR sun, sky ambient, aerial-perspective fog) and post
    ///     processing (ACES tonemapping, bloom, colour grading, vignette), longer shadows.
    /// Menu: Jogging → Photoreal → Build PhotoRun Scene. Headless: BuildBatch.
    /// </summary>
    public static class PhotoRunSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/PhotoRun.unity";
        private const string Dir = "Assets/PhotoReal";
        // Grass-dominated demo graph (5 layers: grass green/yellow, cliffs, gravel).
        private const string DemoGraph = "Assets/MapMagic/Demo/Graphs/SimpleGraph.asset";

        [MenuItem("Jogging/Photoreal/Build PhotoRun Scene")]
        public static void BuildMenu()
        {
            Build();
            EditorUtility.DisplayDialog("Jogging", "PhotoRun-Szene gebaut: " + ScenePath + "\nÖffnen und Play.", "OK");
        }

        public static void BuildBatch() => Build();

        public static void Build()
        {
            if (!Directory.Exists(Dir)) { Directory.CreateDirectory(Dir); AssetDatabase.Refresh(); }

            ConvertDemoTreesToUrp();

            var scene = EditorSceneManager.OpenScene(ScenePath);

            // ---- start clean: generated objects are reset (kept: same file IDs), strays removed ----
            StripCartoonLeftovers();

            var runner = GameObject.Find("Runner");
            var track = Object.FindFirstObjectByType<TrackManager>();
            if (runner == null || track == null) { Debug.LogError("[Jogging] Runner/TrackManager fehlt in MvpRun."); return; }

            // ---- runner moves over real terrain -----------------------------------------
            var tso = new SerializedObject(track);
            tso.FindProperty("movingRunner").objectReferenceValue = runner.transform;
            tso.FindProperty("waitForTerrain").boolValue = true;
            tso.ApplyModifiedPropertiesWithoutUndo();
            runner.transform.position = new Vector3(0f, 60f, 0f); // dropped onto the ground once tiles exist
            if (runner.GetComponent<TerrainFollower>() == null) runner.AddComponent<TerrainFollower>();
            Set(Comp<TerrainLoadingHint>(Root("TerrainLoadingHint", typeof(TerrainLoadingHint))), "runner", runner.transform);

            // Crowd + spectators keep their relative logic; the group follows the runner and every
            // figure stands on the terrain (real-terrain mode of the managers).
            foreach (var c in Object.FindObjectsByType<AiRunnerManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            { c.gameObject.SetActive(true); Set(c, "followRunner", runner.transform); }
            foreach (var c in Object.FindObjectsByType<SpectatorManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            { c.gameObject.SetActive(true); Set(c, "followRunner", runner.transform); }

            // ---- MapMagic infinite terrain --------------------------------------------
            string graphPath = Dir + "/RunTerrain.asset";
            var graph = AssetUtil.CopyOf<MapMagic.Nodes.Graph>(DemoGraph, graphPath); // in place: no new GUID per build
            // Meadow instead of rock fields: the demo paints dark cliff wherever cavity/slope is high,
            // which on our gentle 70 m hills covered most ground. Swap that layer for dry grass; the
            // bright cliff layer stays for real outcrops on steep slopes.
            // Gravel (4 m tiles) also covered large flat areas → a cobble field; make that green grass.
            // Own copies with smaller tiles: the demo's 15–30 m grass tiles look blurry at runner height.
            var grassYellow = FineLayer("GrassYellow", 7f);
            var grassGreen = FineLayer("GrassGreen", 9f);
            foreach (var tex in graph.GeneratorsOfType<MapMagic.Nodes.MatrixGenerators.TexturesOutput200>())
                foreach (var layer in tex.layers)
                {
                    if (layer.prototype == null) continue;
                    string n = layer.prototype.name;
                    if (n == "CliffDark" || n == "GrassYellow") layer.prototype = grassYellow;
                    else if (n == "Gravel" || n == "GrassGreen") layer.prototype = grassGreen;
                }
            EditorUtility.SetDirty(graph);
            AssetDatabase.SaveAssets();

            var mmGo = Root("MapMagic", typeof(MapMagicObject), typeof(TerrainLook), typeof(PhotoDebugSwitches), typeof(SeasonAssets));
            var mm = Comp<MapMagicObject>(mmGo);
            var look = Comp<TerrainLook>(mmGo);
            var lso = new SerializedObject(look);
            lso.FindProperty("mapMagic").objectReferenceValue = mm;
            lso.FindProperty("grassGreen").objectReferenceValue = grassGreen;
            lso.FindProperty("grassYellow").objectReferenceValue = grassYellow;
            lso.FindProperty("forestFloor").objectReferenceValue = FineLayer("Dirt", 6f);
            lso.FindProperty("snow").objectReferenceValue = FineLayer("Snow", 10f);
            lso.ApplyModifiedPropertiesWithoutUndo();
            mm.graph = graph;
            mm.globals.height = 100f;                 // real hills; the trail profile caps the grade at ±10 %
            // Detailed tiles over the CPU path like the drafts: MapMagic's default "TextureToHeightmap" (GPU)
            // lost the height scale here — detailed tiles came out far below the drafts, and the trail,
            // shaped from one and shown on the other, stood on an embankment.
            mm.globals.heightMainApply = MapMagic.Nodes.MatrixGenerators.HeightOutput200.ApplyType.SetHeightsDelayLOD;
            mm.tileResolution = MapMagicObject.Resolution._257;
            mm.draftResolution = MapMagicObject.Resolution._65;
            mm.tileSize = new Den.Tools.Vector2D(250, 250); // same ~1 m detail as 500/513, but each tile ready 4× sooner
            mm.mainRange = 2;
            mm.tiles.generateInfinite = true;
            mm.tiles.genAroundMainCam = true;
            mm.tiles.generateRange = 4;
            mm.draftsInEditor = false;               // don't bake tiles into the scene file
            mm.instantGenerate = true;
            mm.shift = false;
            mm.terrainSettings.material = AssetDatabase.LoadAssetAtPath<Material>("Assets/MapMagic/Demo/Materials/TerrainURP.mat");
            mm.terrainSettings.pixelError = 2;
            mm.terrainSettings.detailDistance = 110f;
            mm.terrainSettings.detailDensity = 1f;
            var shadowField = typeof(TerrainSettings).GetField("shadowCastingMode");
            if (shadowField != null) shadowField.SetValue(mm.terrainSettings, ShadowCastingMode.On);

            AddShaderVariantAnchor(mm.terrainSettings.material);

            Comp<PhotoDebugSwitches>(mmGo); // test/diagnosis switches (-timescale, -camhigh, -diag, rendering)

            // ---- trail + trees ----------------------------------------------------------
            var trail = Root("Trail", typeof(TrailPath), typeof(TrailShaper), typeof(RouteRuntime), typeof(Lakes), typeof(MeshFilter), typeof(MeshRenderer), typeof(TrailRibbon), typeof(TrailGrassClearer));
            trail.GetComponent<MeshRenderer>().sharedMaterial = TrailMaterial();
            trail.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            Set(trail.GetComponent<TrailRibbon>(), "runner", runner.transform);
            var rso = new SerializedObject(trail.GetComponent<TrailRibbon>());
            rso.FindProperty("lift").floatValue = 0.12f; // clear of the rendered terrain LOD
            // the width comes from the surface (World/TrailSurface: 2.5 … 3.4 m)
            rso.ApplyModifiedPropertiesWithoutUndo();
            var clearer = Comp<TrailGrassClearer>(trail); // no terrain grass growing through the trail
            var cso = new SerializedObject(clearer);
            cso.FindProperty("halfWidth").floatValue = 3f; // a little wider than the trail: tufts reach beyond their cell
            cso.ApplyModifiedPropertiesWithoutUndo();

            var trees = Comp<TerrainTreeScatter>(Root("Trees", typeof(TerrainTreeScatter)));
            Set(trees, "runner", runner.transform);
            var tso2 = new SerializedObject(trees);
            var arr = tso2.FindProperty("prefabs");
            var list = new System.Collections.Generic.List<GameObject>();
            foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/MapMagic/Demo/Trees" }))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
                if (go != null && !go.name.Contains("Tiny") && !go.name.Contains("Stump")) list.Add(go);
            }
            tso2.FindProperty("footprintClear").floatValue = 2.0f;
            arr.arraySize = list.Count;
            for (int i = 0; i < list.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = list[i];
            tso2.ApplyModifiedPropertiesWithoutUndo();

            // ---- undergrowth: bushes, plants, small birches and stones close to the trail ---
            ConvertDemoStonesToUrp();
            var under = Comp<TerrainTreeScatter>(Root("Undergrowth", typeof(TerrainTreeScatter)));
            var uso = new SerializedObject(under);
            uso.FindProperty("runner").objectReferenceValue = runner.transform;
            var ulist = new System.Collections.Generic.List<GameObject>();
            foreach (var path in new[] {
                "Assets/MapMagic/Demo/Stones/Stone01.prefab", "Assets/MapMagic/Demo/Stones/Stone01big.prefab",
                "Assets/MapMagic/Demo/Trees/Birch/Prefabs/TinyBig.prefab", "Assets/MapMagic/Demo/Trees/Birch/Prefabs/TinySmall.prefab",
                "Assets/MapMagic/Demo/Trees/Pine/Prefabs/Stump.prefab" })
            { var g = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (g != null) ulist.Add(g); }
            foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Idyllic Fantasy Nature/Prefabs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(g), n = System.IO.Path.GetFileNameWithoutExtension(path);
                if (n.StartsWith("Bush_") || n.StartsWith("Plant_")) ulist.Add(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            }
            var uarr = uso.FindProperty("prefabs");
            uarr.arraySize = ulist.Count;
            for (int i = 0; i < ulist.Count; i++) uarr.GetArrayElementAtIndex(i).objectReferenceValue = ulist[i];
            uso.FindProperty("treesPerChunk").intValue = 34;
            uso.FindProperty("corridor").floatValue = 3.6f;
            uso.FindProperty("footprintClear").floatValue = 2.7f; // trail half-width 1.7 m + 1 m margin
            // Idyllic bushes/plants are stylised (neon yellow-green, solid red) → toned-down copies.
            var from = new System.Collections.Generic.List<Material>();
            foreach (var g in ulist)
                foreach (var r in g.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                        if (m != null && !from.Contains(m) && AssetDatabase.GetAssetPath(m).StartsWith("Assets/Idyllic")) from.Add(m);
            var sf = uso.FindProperty("swapFrom"); var st = uso.FindProperty("swapTo");
            sf.arraySize = from.Count; st.arraySize = from.Count;
            var naturals = new System.Collections.Generic.List<Material>();
            for (int i = 0; i < from.Count; i++)
            {
                var nat = NaturalCopy(from[i]);
                naturals.Add(nat);
                sf.GetArrayElementAtIndex(i).objectReferenceValue = from[i];
                st.GetArrayElementAtIndex(i).objectReferenceValue = nat;
            }

            // Seasonal variants (trees + toned-down bushes) → SeasonAssets on the MapMagic object.
            SeasonBuilder.Build(naturals, out var sBase, out var sSpring, out var sAutumn, out var sWinter);
            var seasons = Comp<SeasonAssets>(mmGo);
            var sso = new SerializedObject(seasons);
            void Fill(string prop, Material[] arr)
            {
                var a = sso.FindProperty(prop); a.arraySize = arr.Length;
                for (int k = 0; k < arr.Length; k++) a.GetArrayElementAtIndex(k).objectReferenceValue = arr[k];
            }
            Fill("baseMaterials", sBase); Fill("spring", sSpring); Fill("autumn", sAutumn); Fill("winter", sWinter);
            sso.ApplyModifiedPropertiesWithoutUndo();
            uso.FindProperty("spread").floatValue = 55f;
            uso.FindProperty("ahead").floatValue = 260f;
            uso.FindProperty("scaleRange").vector2Value = new Vector2(0.7f, 1.3f);
            uso.FindProperty("seed").intValue = 777;
            uso.FindProperty("undergrowth").boolValue = true;
            uso.ApplyModifiedPropertiesWithoutUndo();

            // ---- treadmill: follow the trail's grade changes a bit faster (profile ramps ≤ 0.15 %/m)
            foreach (var c in Object.FindObjectsByType<Jogging.Locomotion.Treadmill.FitShowBeltControl>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var cso2 = new SerializedObject(c);
                cso2.FindProperty("sendInterval").floatValue = 2f;
                cso2.ApplyModifiedPropertiesWithoutUndo();
            }

            // ---- realistic figures (Rocketbox): crowd, spectators and the player -----------
            WireRocketbox(runner);

            // ---- light, sky, fog --------------------------------------------------------
            Light sun = null;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { sun = l; break; }
            if (sun != null)
            {
                sun.intensity = 2.2f;
                sun.color = new Color(1f, 0.95f, 0.87f);
                sun.transform.rotation = Quaternion.Euler(32f, -38f, 0f);
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.85f;
                RenderSettings.sun = sun;
            }
            // Own copy of the sky material — tweaking the shared one would also change the cartoon scene.
            Material sky = null;
            if (RenderSettings.skybox != null)
            {
                string skyPath = Dir + "/PhotoSky.mat";
                sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
                if (sky == null) { sky = new Material(RenderSettings.skybox); AssetDatabase.CreateAsset(sky, skyPath); }
                RenderSettings.skybox = sky;
            }
            if (sky != null && sky.HasProperty("_AtmosphereThickness"))
            {
                sky.SetFloat("_AtmosphereThickness", 1.0f);
                if (sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", 1.25f);
                if (sky.HasProperty("_SunSize")) sky.SetFloat("_SunSize", 0.035f);
                if (sky.HasProperty("_SkyTint")) sky.SetColor("_SkyTint", new Color(0.5f, 0.5f, 0.5f));
                if (sky.HasProperty("_GroundColor")) sky.SetColor("_GroundColor", new Color(0.37f, 0.35f, 0.34f));
            }
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.3f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0022f;
            RenderSettings.fogColor = new Color(0.70f, 0.78f, 0.88f); // aerial perspective haze
            DynamicGI.UpdateEnvironment();

            // ---- camera + post processing ----------------------------------------------
            var cam = Camera.main;
            if (cam != null)
            {
                cam.farClipPlane = 2500f;
                cam.allowHDR = true;
                cam.clearFlags = CameraClearFlags.Skybox;
                var data = cam.GetComponent<UniversalAdditionalCameraData>() ?? cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.antialiasingQuality = AntialiasingQuality.High;
            }
            // ---- weather (rain / snow particles; light and haze come from Mood) ----------
            var wfx = Comp<WeatherFx>(Root("Weather", typeof(WeatherFx)));
            Set(wfx, "particleMaterial", WeatherMaterial());

            var vol = Comp<Volume>(Root("PostProcess Volume", typeof(Volume)));
            vol.isGlobal = true;
            vol.sharedProfile = PostProfile();

            // Longer, finer shadows for open landscape (shared URP asset — this branch only).
            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.shadowDistance = 220f;
                urp.shadowCascadeCount = 4;
                urp.supportsHDR = true;
                EditorUtility.SetDirty(urp);
            }

            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[Jogging] PhotoRun-Szene gebaut: MapMagic-Terrain, Trail, Bäume, HDR-Licht, Post-FX.");
        }

        // MapMagic creates every terrain at RUNTIME, so a built scene contains no Terrain and Unity
        // strips the URP terrain shader variants (incl. instanced drawing) → the ground simply isn't
        // drawn in the player (details/trees still are). A tiny terrain with the same material,
        // instancing and layer setup, parked far below the world, keeps those variants in the build.
        private static void AddShaderVariantAnchor(Material terrainMat)
        {
            string dataPath = Dir + "/VariantAnchorTerrain.asset";
            // Reuse the existing asset (same GUID; copying into a TerrainData made it grow every build).
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(dataPath);
            bool fresh = data == null;
            if (fresh) data = new TerrainData();
            if (data.heightmapResolution != 33) data.heightmapResolution = 33;
            if (data.alphamapResolution != 16) data.alphamapResolution = 16;
            if (data.baseMapResolution != 16) data.baseMapResolution = 16;
            data.size = new Vector3(4f, 1f, 4f);
            var layers = new System.Collections.Generic.List<TerrainLayer>();
            foreach (var n in new[] { "GrassGreen", "GrassYellow", "CliffDark", "CliffBright", "Gravel" }) // 5 → keeps the add-pass too
            {
                var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>($"Assets/MapMagic/Demo/TerrainLayers/{n}.terrainlayer");
                if (l != null) layers.Add(l);
            }
            data.terrainLayers = layers.ToArray();
            if (fresh) AssetDatabase.CreateAsset(data, dataPath); else EditorUtility.SetDirty(data);

            var go = Root(AnchorName, typeof(Terrain));
            go.transform.position = new Vector3(0f, -3000f, 0f);
            var t = Comp<Terrain>(go);
            t.terrainData = data;
            t.materialTemplate = terrainMat;
            t.drawInstanced = true;
            t.shadowCastingMode = ShadowCastingMode.Off;
            // (no TerrainCollider: Root removed every component not listed)
        }

        // ------------------------------------------------------------------ helpers
        private static TerrainLayer FineLayer(string demoName, float tile)
        {
            string dst = Dir + "/" + demoName + "Fine.terrainlayer";
            var l = AssetUtil.CopyOf<TerrainLayer>("Assets/MapMagic/Demo/TerrainLayers/" + demoName + ".terrainlayer", dst);
            l.tileSize = new Vector2(tile, tile);
            EditorUtility.SetDirty(l);
            return l;
        }

        // Copy of a stylised foliage material with natural colours: every colour property pulled
        // to a leaf-green hue, less saturated and darker. The original (used by the cartoon world) stays.
        private static Material NaturalCopy(Material src)
        {
            string dir = Dir + "/Undergrowth";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(Dir, "Undergrowth");
            string path = dir + "/" + src.name + "_Natural.mat";
            var m = new Material(src);
            foreach (var prop in new[] { "_BaseColor", "_Color", "_Top_Color", "_Bottom_Color", "_TopColor", "_BottomColor" })
            {
                if (!m.HasProperty(prop)) continue;
                var c = m.GetColor(prop);
                Color.RGBToHSV(c, out float h, out float sat, out float v);
                h = Mathf.Lerp(h, 0.27f, 0.85f);  // towards leaf green
                sat *= 0.55f;
                v *= 0.55f;
                var n = Color.HSVToRGB(h, sat, v); n.a = c.a;
                m.SetColor(prop, n);
            }
            return AssetUtil.Upsert(m, path);
        }

        // Demo stones use the Built-in Standard shader (pink under URP) → URP/Lit.
        private static void ConvertDemoStonesToUrp()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/MapMagic/Demo/Stones/Materials/Stones.mat");
            if (m == null || m.shader == lit) return;
            var albedo = m.GetTexture("_MainTex");
            var normal = m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null;
            m.shader = lit;
            m.SetTexture("_BaseMap", albedo);
            m.SetColor("_BaseColor", Color.white);
            if (normal != null) { m.SetTexture("_BumpMap", normal); m.EnableKeyword("_NORMALMAP"); }
            m.SetFloat("_Smoothness", 0.15f);
            m.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(m);
        }

        private static void WireRocketbox(GameObject runner)
        {
            var models = RocketboxSetup.Models();
            if (models.Length == 0) { Debug.LogWarning("[Jogging] Keine Rocketbox-Avatare – Menü Jogging/Rocketbox/Setup ausführen."); return; }
            RuntimeAnimatorController Ctrl(string p) => AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(p);

            foreach (var c in Object.FindObjectsByType<AiRunnerManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(c);
                var a = so.FindProperty("figureModels");
                a.arraySize = models.Length;
                for (int i = 0; i < models.Length; i++) a.GetArrayElementAtIndex(i).objectReferenceValue = models[i];
                so.FindProperty("maleRunController").objectReferenceValue = Ctrl(RocketboxSetup.RunController(false));
                so.FindProperty("femaleRunController").objectReferenceValue = Ctrl(RocketboxSetup.RunController(true));
                so.FindProperty("laneScale").floatValue = 0.55f;     // stay on the 3.4 m gravel trail
                so.FindProperty("minLaneOffset").floatValue = 0.85f; // …but not shoulder to shoulder with you
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            foreach (var c in Object.FindObjectsByType<SpectatorManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(c);
                var a = so.FindProperty("figureModels");
                a.arraySize = models.Length;
                for (int i = 0; i < models.Length; i++) a.GetArrayElementAtIndex(i).objectReferenceValue = models[i];
                so.FindProperty("maleController").objectReferenceValue = Ctrl(RocketboxSetup.SpectatorController(false));
                so.FindProperty("femaleController").objectReferenceValue = Ctrl(RocketboxSetup.SpectatorController(true));
                so.FindProperty("count").intValue = 10;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // Workshop spectator groups + the workshop itself.
            var groups = Comp<SpectatorGroups>(Root("SpectatorGroups", typeof(SpectatorGroups)));
            var gso = new SerializedObject(groups);
            var ga = gso.FindProperty("figureModels");
            ga.arraySize = models.Length;
            for (int i = 0; i < models.Length; i++) ga.GetArrayElementAtIndex(i).objectReferenceValue = models[i];
            gso.FindProperty("maleController").objectReferenceValue = Ctrl(RocketboxSetup.SpectatorController(false));
            gso.FindProperty("femaleController").objectReferenceValue = Ctrl(RocketboxSetup.SpectatorController(true));
            gso.ApplyModifiedPropertiesWithoutUndo();
            Comp<Jogging.UI.WorkshopUI>(Root("Workshop", typeof(Jogging.UI.WorkshopUI)));

            // Player: a Rocketbox figure, selectable in the customization panel (all models).
            var pf = runner.GetComponent<RealPlayerFigure>() ?? runner.AddComponent<RealPlayerFigure>();
            var pso = new SerializedObject(pf);
            pso.FindProperty("model").objectReferenceValue = models.FirstOrDefault(m => m.name == "Male_Adult_09") ?? models[0];
            var ch = pso.FindProperty("choices");
            ch.arraySize = models.Length;
            for (int i = 0; i < models.Length; i++) ch.GetArrayElementAtIndex(i).objectReferenceValue = models[i];
            pso.FindProperty("controller").objectReferenceValue = Ctrl(RocketboxSetup.RunController(false));
            pso.FindProperty("femaleController").objectReferenceValue = Ctrl(RocketboxSetup.RunController(true));
            if (pso.FindProperty("locomotionSourceBehaviour").objectReferenceValue == null)
                pso.FindProperty("locomotionSourceBehaviour").objectReferenceValue = Object.FindFirstObjectByType<Jogging.Locomotion.LocomotionRouter>();
            pso.ApplyModifiedPropertiesWithoutUndo();
        }
        private const string AnchorName = "TerrainVariantAnchor (keeps terrain shaders in build)";


        // The scene started as a copy of the cartoon MVP scene. Remove what only that world used
        // (by name / type name, so this keeps working after those classes are deleted).
        private static readonly string[] CartoonObjects = { "Scenery", "EnvironmentMood", "Milestones", "AvatarMount", "SegmentContainer", "UMA_GLIB" };
        private static readonly string[] CartoonComponents =
            { "WorldCurveDriver", "RunnerController", "UmaAvatarController", "MilestoneManager", "EnvironmentMood", "AvatarOverlay",
              "CharacterCustomizationUI" }; // (the HUD's "Läufer anpassen": replaced by the profile page)

        private static void StripCartoonLeftovers()
        {
            foreach (var g in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (g == null) continue;
                if (System.Array.IndexOf(CartoonObjects, g.name) >= 0) { Object.DestroyImmediate(g); continue; }
                foreach (var c in g.GetComponents<MonoBehaviour>())
                    if (c != null && System.Array.IndexOf(CartoonComponents, c.GetType().Name) >= 0) Object.DestroyImmediate(c);
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(g);
            }
            // The runner's placeholder capsule (the Rocketbox figure is the visible runner).
            var runner = GameObject.Find("Runner");
            if (runner != null)
            {
                var mr = runner.GetComponent<MeshRenderer>(); if (mr != null) Object.DestroyImmediate(mr);
                var mf = runner.GetComponent<MeshFilter>(); if (mf != null) Object.DestroyImmediate(mf);
            }
        }

        /// <summary>
        /// A generated root object, reused if it exists (duplicates removed): no children, default
        /// transform, active, only the listed components — each reset to its defaults. Kept objects
        /// and components keep their file IDs, so an unchanged setup leaves the scene file unchanged.
        /// </summary>
        private static GameObject Root(string name, params System.Type[] components)
        {
            GameObject go = null;
            foreach (var g in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (g.name != name) continue;
                if (go == null) go = g; else Object.DestroyImmediate(g);
            }
            if (go == null) go = new GameObject(name);
            for (int i = go.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
            go.SetActive(true);
            go.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            // Components not in the list go (dependents first: retry until nothing else can be removed).
            for (int pass = 0; pass < 4; pass++)
                foreach (var c in go.GetComponents<Component>())
                    if (c != null && !(c is Transform) && System.Array.IndexOf(components, c.GetType()) < 0) Object.DestroyImmediate(c);
            foreach (var type in components)
            {
                var c = go.GetComponent(type);
                if (c == null) go.AddComponent(type); // new: defaults already
                else { ResetToDefaults(c); if (c is Behaviour b) b.enabled = true; }
            }
            return go;
        }

        private static T Comp<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        // Every serialized field back to what a freshly added component has (the builder then sets
        // what it needs) — like recreating it, without a new file ID.
        private static void ResetToDefaults(Component c)
        {
            var tmp = new GameObject("__defaults") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var fresh = tmp.GetComponent(c.GetType());
                if (fresh == null) fresh = tmp.AddComponent(c.GetType());
                if (fresh == null) return;
                var src = new SerializedObject(fresh);
                var dst = new SerializedObject(c);
                var it = src.GetIterator();
                for (bool enter = true; it.Next(enter); enter = false)
                {
                    if (it.name == "m_GameObject" || it.name == "m_Script" || it.name == "m_ObjectHideFlags" || it.name == "m_Enabled") continue;
                    dst.CopyFromSerializedProperty(it);
                }
                dst.ApplyModifiedPropertiesWithoutUndo();
            }
            finally { Object.DestroyImmediate(tmp); }
        }

        private static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // MapMagic's demo birch/pine use a Built-in surface shader (pink under URP) → URP/Lit cutout.
        private static void ConvertDemoTreesToUrp()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var path in new[] { "Assets/MapMagic/Demo/Trees/Birch/Materials/Birch.mat",
                                         "Assets/MapMagic/Demo/Trees/Pine/Materials/Pine.mat" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null || m.shader == lit) continue;
                var albedo = m.GetTexture("_MainTex");
                var normal = m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null;
                float cutoff = m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : 0.33f;
                m.shader = lit;
                m.SetTexture("_BaseMap", albedo);
                m.SetColor("_BaseColor", Color.white);
                if (normal != null) { m.SetTexture("_BumpMap", normal); m.EnableKeyword("_NORMALMAP"); }
                m.SetFloat("_AlphaClip", 1f); m.EnableKeyword("_ALPHATEST_ON"); m.SetFloat("_Cutoff", cutoff);
                m.SetFloat("_Cull", 0f); m.SetFloat("_Smoothness", 0.12f);
                m.renderQueue = 2450;
                EditorUtility.SetDirty(m);
                Debug.Log("[Jogging] Baum-Material nach URP/Lit konvertiert: " + path);
            }
        }

        private static Material TrailMaterial()
        {
            string p = Dir + "/Trail.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, p); }
            // Realistic gravel forest path (photographic texture + normal map), slightly brightened
            // because it sits in the shade of the canopy most of the time.
            var gravel = AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/MapMagic/Demo/TerrainLayers/Gravel.terrainlayer");
            if (gravel != null)
            {
                m.SetTexture("_BaseMap", gravel.diffuseTexture);
                if (gravel.normalMapTexture != null) { m.SetTexture("_BumpMap", gravel.normalMapTexture); m.EnableKeyword("_NORMALMAP"); }
            }
            m.SetTextureScale("_BaseMap", new Vector2(1.8f, 1.8f)); // fine gravel, not cobblestones
            m.SetColor("_BaseColor", new Color(1.45f, 1.38f, 1.28f)); // >1 on purpose: lift the shaded gravel
            m.SetFloat("_Smoothness", 0.08f);
            EditorUtility.SetDirty(m);
            return m;
        }

        // Soft round dot, transparent URP particle material (drops are stretched from it).
        private static Material WeatherMaterial()
        {
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float a = Mathf.Clamp01(1f - d); a *= a;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            tex = AssetUtil.Upsert(tex, Dir + "/WeatherDot.asset");

            var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f);   // transparent
            m.SetFloat("_Blend", 0f);     // alpha
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            return AssetUtil.Upsert(m, Dir + "/WeatherParticle.mat");
        }

        private static VolumeProfile PostProfile()
        {
            string p = Dir + "/PhotoPost.asset";
            var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>(p);
            if (prof == null) { prof = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(prof, p); }

            T Get<T>() where T : VolumeComponent
            {
                if (!prof.TryGet(out T c)) { c = prof.Add<T>(true); AssetDatabase.AddObjectToAsset(c, prof); }
                return c;
            }
            var tm = Get<Tonemapping>(); tm.mode.Override(TonemappingMode.ACES);
            var bloom = Get<Bloom>(); bloom.threshold.Override(1.05f); bloom.intensity.Override(0.35f); bloom.scatter.Override(0.65f);
            var ca = Get<ColorAdjustments>(); ca.postExposure.Override(0.6f); ca.contrast.Override(12f); ca.saturation.Override(6f);
            var wb = Get<WhiteBalance>(); wb.temperature.Override(4f);
            var vig = Get<Vignette>(); vig.intensity.Override(0.2f); vig.smoothness.Override(0.45f);
            EditorUtility.SetDirty(prof);
            return prof;
        }
    }
}
