using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Jogging.EditorTools
{
    /// <summary>
    /// Import rules for the Microsoft Rocketbox avatars under Assets/Rocketbox (MIT): Generic rig
    /// (avatars and animations share the Bip01 skeleton, so clips bind by bone path), only the
    /// "hipoly" LOD active, URP/Lit materials wired to the downscaled PNG textures, looping clips.
    /// Scoped to Assets/Rocketbox so other packs are untouched (unlike Rocketbox's own script).
    /// </summary>
    public class RocketboxImport : AssetPostprocessor
    {
        public const string Root = "Assets/Rocketbox/";
        private bool Mine => assetPath.StartsWith(Root);

        private void OnPreprocessModel()
        {
            if (!Mine) return;
            var mi = (ModelImporter)assetImporter;
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            mi.importCameras = false;
            mi.importLights = false;
            bool anim = assetPath.Contains("/Animations/");
            mi.importAnimation = anim;
            if (anim) mi.materialImportMode = ModelImporterMaterialImportMode.None;
        }

        private void OnPreprocessAnimation()
        {
            if (!Mine) return;
            var mi = (ModelImporter)assetImporter;
            // The Bip01 root travels forward during a cycle (≈2–3.5 m) and snaps back on loop.
            // Make Bip01 the root-motion node: horizontal travel becomes root motion (dropped, since
            // the Animators run with applyRootMotion = false and the game moves the figures), while
            // height bob and body rotation stay in the pose.
            mi.motionNodeName = "Bip01";
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.loopTime = true; c.loopPose = false;
                c.lockRootPositionXZ = false;                              // extract XZ travel
                c.lockRootHeightY = true; c.keepOriginalPositionY = true;  // keep the bob
                c.lockRootRotation = true; c.keepOriginalOrientation = true;
            }
            mi.clipAnimations = clips;
        }

        private void OnPreprocessTexture()
        {
            if (!Mine) return;
            var ti = (TextureImporter)assetImporter;
            ti.maxTextureSize = 1024;
            if (assetPath.Contains("_normal")) { ti.textureType = TextureImporterType.NormalMap; ti.convertToNormalmap = false; }
            if (assetPath.Contains("_opacity")) ti.alphaIsTransparency = true;
        }

        private void OnPostprocessMeshHierarchy(GameObject g)
        {
            if (!Mine) return;
            string n = g.name.ToLower();
            if (n.Contains("poly") && !n.Contains("hipoly")) g.SetActive(false);
        }

        private void OnPostprocessModel(GameObject g)
        {
            if (!Mine) return;
            if (g.transform.Find("Bip02") != null) RenameBip(g.transform);
        }

        private static void RenameBip(Transform t)
        {
            t.name = t.name.Replace("Bip02", "Bip01");
            for (int i = 0; i < t.childCount; i++) RenameBip(t.GetChild(i));
        }
    }

    /// <summary>
    /// Builds the animator controllers for the Rocketbox figures:
    /// RB_Run_{Male,Female}: 1D blend on "Speed" (m/s) idle → slow → neutral → fast run.
    /// RB_Spectator_{Male,Female}: looping states Idle / Clap / Cheer / Wave (runtime picks one).
    /// Menu: Jogging → Rocketbox → Setup. Headless: -executeMethod Jogging.EditorTools.RocketboxSetup.SetupBatch
    /// </summary>
    public static class RocketboxSetup
    {
        public const string AvatarDir = RocketboxImport.Root + "Avatars";
        private const string AnimDir = RocketboxImport.Root + "Animations/";

        public static string RunController(bool female) => RocketboxImport.Root + (female ? "RB_Run_Female.controller" : "RB_Run_Male.controller");
        public static string SpectatorController(bool female) => RocketboxImport.Root + (female ? "RB_Spectator_Female.controller" : "RB_Spectator_Male.controller");

        [MenuItem("Jogging/Rocketbox/Setup")]
        public static void Setup()
        {
            // Textures first, then the models again so their materials find the PNGs.
            AssetDatabase.ImportAsset(RocketboxImport.Root, ImportAssetOptions.ImportRecursive);
            foreach (var p in ModelPaths()) BuildMaterials(p);

            foreach (bool f in new[] { false, true })
            {
                string g = f ? "f_" : "m_";
                // One walk and one run cycle, each played faster or slower to the speed (RealFigure.Drive):
                // blending two walk (or run) clips that aren't in step cancels the legs out — the figure
                // looked as if it stood. Idle → walk from 0.9 m/s, walk → run in a short band at ~7.5 km/h.
                var walk = Clip(g + "walk_neutral_01"); var run = Clip(g + "run_neutral");
                BuildRun(RunController(f), Clip(g + "idle_neutral_01"),
                    (walk, Jogging.World.RealFigure.WalkMin), (walk, Jogging.World.RealFigure.WalkTop),
                    (run, Jogging.World.RealFigure.RunFrom), (run, 6f));
                BuildSpectator(SpectatorController(f), ("Idle", Clip(g + "idle_neutral_01")),
                    ("Clap", Clip(g + "claphands_01")), ("Cheer", Clip(g + "cheer_01")), ("Wave", Clip(g + "wave_01")));
            }
            AssetDatabase.SaveAssets();

            foreach (var m in Models())
            {
                var go = Object.Instantiate(m);
                var b = new Bounds(go.transform.position, Vector3.zero);
                foreach (var r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                Debug.Log($"[Jogging] Rocketbox {m.name}: Höhe {b.size.y:0.00} m, fwd-Tiefe {b.size.z:0.00}, Renderer {go.GetComponentsInChildren<SkinnedMeshRenderer>().Length}");
                Object.DestroyImmediate(go);
            }
        }

        // One URP/Lit material per FBX material ("f003_body" → Textures/f003_body_color.png + _normal),
        // remapped onto the model so the importer uses it.
        private static void BuildMaterials(string modelPath)
        {
            string dir = Path.GetDirectoryName(modelPath).Replace('\\', '/');
            string matDir = dir + "/Materials";
            if (!AssetDatabase.IsValidFolder(matDir)) AssetDatabase.CreateFolder(dir, "Materials");
            var mi = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var names = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Material>().Select(m => m.name)
                .Concat(mi.GetExternalObjectMap().Keys.Where(k => k.type == typeof(Material)).Select(k => k.name))
                .Distinct().ToArray();
            foreach (var n in names)
            {
                string mp = matDir + "/" + n + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
                if (m == null) { m = new Material(lit); AssetDatabase.CreateAsset(m, mp); }
                m.shader = lit;
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_Metallic", 0f);
                m.SetFloat("_Smoothness", 0.28f);
                var baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "/Textures/" + n + "_color.png");
                if (baseTex != null) m.SetTexture("_BaseMap", baseTex);
                else Debug.LogWarning("[Jogging] Rocketbox-Textur fehlt: " + n);
                var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "/Textures/" + n + "_normal.png");
                if (nrm != null) { m.SetTexture("_BumpMap", nrm); m.EnableKeyword("_NORMALMAP"); }
                if (n.EndsWith("_opacity"))
                {
                    // Hair, lashes, brows: cutout + two-sided (keeps shadows/depth, no sorting issues).
                    m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.45f); m.EnableKeyword("_ALPHATEST_ON");
                    m.SetFloat("_Cull", 0f); m.SetFloat("_Smoothness", 0.35f);
                    m.renderQueue = 2450;
                }
                EditorUtility.SetDirty(m);
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), m);
            }
            AssetDatabase.SaveAssets();
            mi.SaveAndReimport();
        }

        public static void SetupBatch()
        {
            Setup();
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        public static string[] ModelPaths() =>
            Directory.GetFiles(AvatarDir, "*.fbx", SearchOption.AllDirectories).Select(p => p.Replace('\\', '/')).OrderBy(p => p).ToArray();

        public static GameObject[] Models() =>
            ModelPaths().Select(AssetDatabase.LoadAssetAtPath<GameObject>).Where(g => g != null).ToArray();

        private static AnimationClip Clip(string name)
        {
            string path = AnimDir + name + ".max.fbx";
            var c = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(a => !a.name.StartsWith("__preview__"));
            if (c == null) Debug.LogError("[Jogging] Rocketbox-Clip fehlt: " + path);
            return c;
        }

        private static AnimatorController Fresh(string path)
        {
            AssetDatabase.DeleteAsset(path);
            return AnimatorController.CreateAnimatorControllerAtPath(path);
        }

        private static void BuildRun(string path, AnimationClip idle, params (AnimationClip clip, float speed)[] runs)
        {
            var ac = Fresh(path);
            ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
            var state = ac.CreateBlendTreeInController("Locomotion", out var tree);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            foreach (var r in runs) tree.AddChild(r.clip, r.speed);
            ac.layers[0].stateMachine.defaultState = state;
        }

        private static void BuildSpectator(string path, params (string name, AnimationClip clip)[] states)
        {
            var ac = Fresh(path);
            var sm = ac.layers[0].stateMachine;
            foreach (var s in states) sm.AddState(s.name).motion = s.clip;
            sm.defaultState = sm.states[0].state;
        }
    }
}
