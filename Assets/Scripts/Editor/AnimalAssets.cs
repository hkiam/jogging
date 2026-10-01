using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Jogging.EditorTools
{
    /// <summary>
    /// Prepares the animals in Assets/PhotoReal/Animals/&lt;name&gt;/&lt;name&gt;.fbx (converted from Sketchfab GLBs by
    /// Tools/animals/glb2fbx.py) for World/Animals: legacy animation (clips played by name), looping idle/walk/
    /// run clips, Mesh LODs, then a prefab in Resources/Animals whose model is scaled to the animal's real size
    /// (by its name) and turned so its head points along +Z – measured in the idle pose.
    /// Menu: Jogging → Build → Tiere aufbereiten.
    /// </summary>
    public static class AnimalAssets
    {
        private const string Root = "Assets/PhotoReal/Animals";
        private const string Out = Root + "/Resources/Animals";

        // real-world size: (measure, metres) – "height" with the head up, or "length" nose to tail tip
        private static readonly Dictionary<string, (string measure, float metres)> Size = new Dictionary<string, (string, float)>
        {
            ["deer"] = ("length", 1.8f),    // a red deer stag, nose to tail (1.6–2.1 m); antlers not counted in the length
            ["hare"] = ("length", 0.55f),   // brown hare, body
            ["rabbit"] = ("length", 0.40f), // wild rabbit
            ["fox"] = ("length", 1.05f),    // red fox, nose to tail tip
            ["squirrel"] = ("length", 0.40f),
        };

        // colour correction by material: the deer model is coloured like a North American wapiti (pale body);
        // warmed towards the red-brown of a European red deer
        private static readonly Dictionary<string, Color> Tint = new Dictionary<string, Color>
        {
            ["Deer_M"] = new Color(0.82f, 0.62f, 0.47f),
        };

        [MenuItem("Jogging/Build/Tiere aufbereiten")]
        public static void Run()
        {
            Directory.CreateDirectory(Out);
            var log = new System.Text.StringBuilder("[Tiere] ");
            foreach (var dir in Directory.GetDirectories(Root).Where(d => !d.EndsWith("Resources")))
            {
                string name = Path.GetFileName(dir);
                string fbx = $"{Root}/{name}/{name}.fbx";
                if (!(AssetImporter.GetAtPath(fbx) is ModelImporter imp)) continue;
                imp.animationType = ModelImporterAnimationType.Legacy;
                imp.importAnimation = true;
                imp.generateMeshLods = true; imp.maximumMeshLod = 3;
                imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                // the Sketchfab models keep their colour in the specular-glossiness extension, which the GLB→FBX
                // step loses: URP materials from the textures pulled out of the GLB (materials.json, by name)
                foreach (var (matName, mat) in Materials(name))
                    imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), matName), mat);
                // clips: loop everything that is a pose, idle, walk or run (not the one-off reactions)
                var clips = imp.defaultClipAnimations;
                foreach (var c in clips)
                {
                    string n = c.name.ToLowerInvariant();
                    c.loopTime = !(n.Contains("dead") || n.Contains("to_") || n.Contains("howl") || n.Contains("jump"));
                    c.wrapMode = c.loopTime ? WrapMode.Loop : WrapMode.ClampForever;
                }
                imp.clipAnimations = clips;
                imp.SaveAndReimport();
                foreach (var t in AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Texture2D>()) { } // embedded textures come along with the model

                var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                var inst = (GameObject)Object.Instantiate(model);
                var anim = inst.GetComponentInChildren<Animation>();
                var idle = Clips(fbx).FirstOrDefault(c => Has(c.name, "idle", "stand")) ?? Clips(fbx).FirstOrDefault();
                if (idle != null) idle.SampleAnimation(inst, 0f);

                // measure the idle pose: every skinned mesh baked
                var pts = new List<Vector3>();
                foreach (var sr in inst.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var m = new Mesh(); sr.BakeMesh(m, true);
                    var mtx = sr.transform.localToWorldMatrix;
                    var v = m.vertices;
                    for (int i = 0; i < v.Length; i += 3) pts.Add(mtx.MultiplyPoint3x4(v[i]));
                    Object.DestroyImmediate(m);
                }
                foreach (var mr in inst.GetComponentsInChildren<MeshFilter>())
                    foreach (var p in mr.sharedMesh.vertices) pts.Add(mr.transform.localToWorldMatrix.MultiplyPoint3x4(p));
                if (pts.Count == 0) { Object.DestroyImmediate(inst); log.Append($"{name}: kein Mesh; "); continue; }
                Vector3 lo = pts[0], hi = pts[0];
                foreach (var p in pts) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
                Vector3 size = hi - lo;
                // front: the long horizontal axis, towards the side of the highest point (head / ears)
                bool alongX = size.x > size.z;
                Vector3 top = pts.OrderByDescending(p => p.y).First();
                Vector3 centre = (lo + hi) * 0.5f;
                float side = alongX ? Mathf.Sign(top.x - centre.x) : Mathf.Sign(top.z - centre.z);
                Vector3 front = alongX ? new Vector3(side, 0f, 0f) : new Vector3(0f, 0f, side);
                var (measure, metres) = Size.TryGetValue(name, out var sz) ? sz : ("length", 1f);
                float current = measure == "height" ? size.y : Mathf.Max(size.x, size.z);
                float scale = metres / Mathf.Max(1e-4f, current);

                // prefab: root (animal's position, faces +Z) → model (turned, scaled, feet at 0)
                var root = new GameObject(name);
                inst.name = "model";
                inst.transform.SetParent(root.transform, false);
                inst.transform.localRotation = Quaternion.FromToRotation(front, Vector3.forward);
                inst.transform.localScale = Vector3.one * scale;
                inst.transform.localPosition = Vector3.zero;
                Vector3 low = inst.transform.localRotation * (new Vector3(centre.x, lo.y, centre.z) * scale);
                inst.transform.localPosition = -low; // centred, standing on the ground
                if (anim != null) { anim.playAutomatically = false; anim.cullingType = AnimationCullingType.BasedOnRenderers; }
                foreach (var sr in inst.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    sr.updateWhenOffscreen = false;
                    sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    foreach (var m in sr.sharedMaterials) if (m != null) m.enableInstancing = true;
                }
                PrefabUtility.SaveAsPrefabAsset(root, $"{Out}/{name}.prefab");
                Object.DestroyImmediate(root);
                int tris = 0;
                foreach (var sr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) tris += sr.sharedMesh.triangles.Length / 3;
                log.Append($"{name}: Maß {size.x:0.00}×{size.y:0.00}×{size.z:0.00} → ×{scale:0.0000} ({measure} {metres} m), vorn {front}, {tris} Dreiecke, Clips [{string.Join(", ", Clips(fbx).Select(c => c.name))}]; ");
            }
            AssetDatabase.SaveAssets();
            Debug.Log(log.ToString());
        }

        public static void RunBatch() { Run(); EditorApplication.Exit(0); }

        [System.Serializable] private class MatInfo { public string name, @base, normal; public bool alpha; public float cutoff = 0.5f, gloss; }
        [System.Serializable] private class MatList { public MatInfo[] items; }

        private static IEnumerable<(string, Material)> Materials(string animal)
        {
            string dir = $"{Root}/{animal}", json = $"{dir}/materials.json";
            if (!File.Exists(json)) yield break;
            var list = JsonUtility.FromJson<MatList>("{\"items\":" + File.ReadAllText(json) + "}");
            Directory.CreateDirectory($"{dir}/Materials");
            foreach (var m in list.items)
            {
                Texture2D Tex(string file, bool normal)
                {
                    if (string.IsNullOrEmpty(file)) return null;
                    string p = $"{dir}/textures/{file}";
                    if (AssetImporter.GetAtPath(p) is TextureImporter ti)
                    {
                        bool changed = false;
                        if (ti.maxTextureSize != 1024) { ti.maxTextureSize = 1024; changed = true; }
                        if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; changed = true; }
                        if (!normal && m.alpha && !ti.alphaIsTransparency) { ti.alphaIsTransparency = true; ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = m.cutoff; changed = true; }
                        if (changed) ti.SaveAndReimport();
                    }
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                }
                string path = $"{dir}/Materials/{m.name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
                mat.SetTexture("_BaseMap", Tex(m.@base, false));
                mat.SetColor("_BaseColor", Tint.TryGetValue(m.name, out var tint) ? tint : Color.white);
                var n = Tex(m.normal, true);
                if (n != null) { mat.SetTexture("_BumpMap", n); mat.EnableKeyword("_NORMALMAP"); } else mat.DisableKeyword("_NORMALMAP");
                mat.SetFloat("_Smoothness", Mathf.Clamp(m.gloss, 0.05f, 0.35f));
                mat.SetFloat("_Metallic", 0f);
                mat.SetFloat("_Cull", 0f); // the scans are double-sided
                if (m.alpha)
                {
                    mat.SetFloat("_AlphaClip", 1f); mat.SetFloat("_Cutoff", m.cutoff); mat.EnableKeyword("_ALPHATEST_ON");
                    mat.renderQueue = 2450; mat.SetOverrideTag("RenderType", "TransparentCutout");
                }
                else { mat.SetFloat("_AlphaClip", 0f); mat.DisableKeyword("_ALPHATEST_ON"); mat.renderQueue = -1; }
                mat.enableInstancing = true;
                EditorUtility.SetDirty(mat);
                yield return (m.name, mat);
            }
        }

        private static IEnumerable<AnimationClip> Clips(string fbx) =>
            AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__"));

        private static bool Has(string n, params string[] keys) { n = n.ToLowerInvariant(); return keys.Any(k => n.Contains(k)); }
    }
}
