using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Jogging.EditorTools
{
    /// <summary>
    /// Prepares the companion dogs in Assets/PhotoReal/Dogs/&lt;breed&gt;/ (model &lt;breed&gt;.fbx, Animations/*.fbx,
    /// Textures/*_B|_N.png; Tools/dogs/import-*.sh) for World/DogCompanion: legacy clips made "in place" (the
    /// forward travel taken out of the root bone, measured as the clip's natural speed), a URP material, the model
    /// scaled to the breed's real size and turned so its nose points along +Z, saved as
    /// Dogs/Resources/Dogs/&lt;breed&gt;.prefab with a <see cref="Jogging.World.DogRig"/> naming the clips.
    /// The dogs are not part of the public repository (Fab licence) – without them the app simply has no dog.
    /// Menu: Jogging → Build → Hunde aufbereiten.
    /// </summary>
    public static class DogAssets
    {
        private const string Root = "Assets/PhotoReal/Dogs";
        private const string Out = Root + "/Resources/Dogs";

        // height to the top of the head (ears) in metres, standing
        private static readonly Dictionary<string, float> Height = new Dictionary<string, float> { ["germanshepherd"] = 0.86f };

        [MenuItem("Jogging/Build/Hunde aufbereiten")]
        public static void Run()
        {
            if (!AssetDatabase.IsValidFolder(Root)) { Debug.Log("[Hunde] keine Hunde in " + Root); return; }
            Directory.CreateDirectory(Out);
            foreach (var dir in Directory.GetDirectories(Root).Where(d => !d.EndsWith("Resources")))
            {
                string breed = Path.GetFileName(dir);
                string fbx = $"{Root}/{breed}/{breed}.fbx";
                if (!(AssetImporter.GetAtPath(fbx) is ModelImporter mi)) continue;
                mi.animationType = ModelImporterAnimationType.Legacy;
                mi.importAnimation = false;
                mi.generateMeshLods = true; mi.maximumMeshLod = 2;
                var mat = Material(breed);
                foreach (var m in AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Material>().Select(x => x.name).Distinct().ToList())
                    mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m), mat);
                mi.SaveAndReimport();
                foreach (var m in mi.GetExternalObjectMap().Keys.Where(k => k.type == typeof(Material)).ToList()) mi.AddRemap(m, mat);
                mi.SaveAndReimport();

                // clips: legacy, looping, in place; the natural ground speed from the root's travel
                var clips = new Dictionary<string, (AnimationClip clip, float speed)>();
                string clipDir = $"{Root}/{breed}/Clips";
                if (!AssetDatabase.IsValidFolder(clipDir)) AssetDatabase.CreateFolder($"{Root}/{breed}", "Clips");
                foreach (var af in Directory.GetFiles($"{Root}/{breed}/Animations", "*.fbx"))
                {
                    var ai = (ModelImporter)AssetImporter.GetAtPath(af);
                    ai.animationType = ModelImporterAnimationType.Legacy;
                    ai.importAnimation = true;
                    ai.materialImportMode = ModelImporterMaterialImportMode.None;
                    var defs = ai.defaultClipAnimations;
                    foreach (var c in defs) { c.loopTime = true; c.wrapMode = WrapMode.Loop; }
                    ai.clipAnimations = defs;
                    ai.SaveAndReimport();
                    var src = AssetDatabase.LoadAllAssetsAtPath(af).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                    if (src == null) continue;
                    string name = Path.GetFileNameWithoutExtension(af);
                    var (inPlace, travel) = InPlace(src);
                    inPlace.name = name;
                    string ap = $"{clipDir}/{name}.anim";
                    AssetDatabase.DeleteAsset(ap);
                    AssetDatabase.CreateAsset(inPlace, ap);
                    clips[name] = (AssetDatabase.LoadAssetAtPath<AnimationClip>(ap), travel / Mathf.Max(0.01f, src.length));
                }

                var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                var inst = (GameObject)Object.Instantiate(model);
                inst.name = "model";
                var anim = inst.GetComponent<Animation>() ?? inst.AddComponent<Animation>();
                AnimationUtility.SetAnimationClips(anim, clips.Values.Select(c => c.clip).ToArray());
                anim.playAutomatically = false;
                anim.cullingType = AnimationCullingType.BasedOnRenderers;
                var idle = clips.FirstOrDefault(c => c.Key.Contains("Idle_Breathing")).Value.clip ?? clips.Values.FirstOrDefault().clip;
                if (idle != null) idle.SampleAnimation(inst, 0f);

                // measure the standing pose
                var pts = new List<Vector3>();
                foreach (var sr in inst.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var m = new Mesh(); sr.BakeMesh(m, true);
                    var mtx = sr.transform.localToWorldMatrix;
                    foreach (var v in m.vertices) pts.Add(mtx.MultiplyPoint3x4(v));
                    Object.DestroyImmediate(m);
                }
                if (pts.Count == 0) { Object.DestroyImmediate(inst); Debug.LogWarning("[Hunde] kein Mesh: " + breed); continue; }
                Vector3 lo = pts[0], hi = pts[0];
                foreach (var p in pts) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
                Vector3 size = hi - lo, centre = (lo + hi) * 0.5f;
                bool alongX = size.x > size.z;
                Vector3 top = pts.OrderByDescending(p => p.y).First(); // ears: the head end
                float sideSign = alongX ? Mathf.Sign(top.x - centre.x) : Mathf.Sign(top.z - centre.z);
                Vector3 front = alongX ? new Vector3(sideSign, 0f, 0f) : new Vector3(0f, 0f, sideSign);
                float scale = (Height.TryGetValue(breed, out float hm) ? hm : 0.8f) / Mathf.Max(1e-4f, size.y);

                var root = new GameObject(breed);
                inst.transform.SetParent(root.transform, false);
                inst.transform.localRotation = Quaternion.FromToRotation(front, Vector3.forward);
                inst.transform.localScale = Vector3.one * scale;
                inst.transform.localPosition = -(inst.transform.localRotation * (new Vector3(centre.x, lo.y, centre.z) * scale));
                foreach (var sr in inst.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    sr.updateWhenOffscreen = false;
                    sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                }
                var rig = root.AddComponent<Jogging.World.DogRig>();
                rig.anim = anim;
                AnimationClip C(string key) => clips.FirstOrDefault(c => c.Key == key).Value.clip;
                float V(string key) => clips.TryGetValue(key, out var c) ? c.speed * scale : 0f;
                rig.idle = C("Idle_Breathing"); rig.play = C("Idle_Playing");
                rig.walk = C("Walk_Loop"); rig.walkLeft = C("Walk_Turn_Left"); rig.walkRight = C("Walk_Turn_Right");
                rig.run = C("Run_Loop"); rig.runLeft = C("Run_Lean_Left"); rig.runRight = C("Run_Lean_Right");
                rig.walkSpeed = V("Walk_Loop") > 0.3f ? V("Walk_Loop") : 1.3f;
                rig.runSpeed = V("Run_Loop") > 1f ? V("Run_Loop") : 6f;
                rig.lengthM = Mathf.Max(size.x, size.z) * scale;
                PrefabUtility.SaveAsPrefabAsset(root, $"{Out}/{breed}.prefab");
                Object.DestroyImmediate(root);
                Debug.Log($"[Hunde] {breed}: Maß {size.x:0.00}×{size.y:0.00}×{size.z:0.00} → ×{scale:0.0000}, vorn {front}, Länge {rig.lengthM:0.00} m, " +
                          $"Gehen {rig.walkSpeed:0.00} m/s, Laufen {rig.runSpeed:0.00} m/s, Clips [{string.Join(", ", clips.Select(c => $"{c.Key} {c.Value.clip.length:0.00}s {c.Value.speed:0.00}"))}]");
            }
            AssetDatabase.SaveAssets();
        }

        public static void RunBatch() { Run(); EditorApplication.Exit(0); }

        // The clip without the forward travel: the root bone's horizontal position curves (the one that travels
        // most) are dropped, the bob stays. Returns the copy and the distance travelled in one cycle (model units).
        private static (AnimationClip clip, float travel) InPlace(AnimationClip src)
        {
            var copy = Object.Instantiate(src);
            copy.legacy = true;
            copy.wrapMode = WrapMode.Loop;
            var bindings = AnimationUtility.GetCurveBindings(copy);
            string best = null; float bestTravel = 0f;
            foreach (var path in bindings.Select(b => b.path).Distinct())
            {
                float Delta(string prop)
                {
                    var b = bindings.FirstOrDefault(x => x.path == path && x.propertyName == prop);
                    if (b.propertyName == null) return 0f;
                    var c = AnimationUtility.GetEditorCurve(copy, b);
                    return c == null || c.length < 2 ? 0f : c.keys[c.length - 1].value - c.keys[0].value;
                }
                // the travel axis depends on the exporter (y or z forward): the largest steady drift of any axis
                float t = Mathf.Max(Mathf.Abs(Delta("m_LocalPosition.x")), Mathf.Max(Mathf.Abs(Delta("m_LocalPosition.y")), Mathf.Abs(Delta("m_LocalPosition.z"))));
                if (t > bestTravel) { bestTravel = t; best = path; }
            }
            if (best != null && bestTravel > 0.01f)
                foreach (var b in bindings.Where(x => x.path == best && x.propertyName.StartsWith("m_LocalPosition")))
                {
                    var c = AnimationUtility.GetEditorCurve(copy, b);
                    float first = c.keys[0].value, last = c.keys[c.length - 1].value;
                    if (Mathf.Abs(last - first) < bestTravel * 0.5f) continue; // the bob axis stays
                    // take the steady travel out, keep the small sway around it
                    var keys = c.keys;
                    float len = Mathf.Max(1e-4f, keys[keys.Length - 1].time - keys[0].time);
                    for (int k = 0; k < keys.Length; k++) keys[k].value -= (last - first) * (keys[k].time - keys[0].time) / len;
                    c.keys = keys;
                    AnimationUtility.SetEditorCurve(copy, b, c);
                }
            return (copy, bestTravel);
        }

        private static Material Material(string breed)
        {
            string dir = $"{Root}/{breed}";
            string path = $"{dir}/{breed}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
            Texture2D Tex(string suffix, bool normal)
            {
                var f = Directory.GetFiles($"{dir}/Textures", $"*_{suffix}.png").FirstOrDefault();
                if (f == null) return null;
                if (AssetImporter.GetAtPath(f) is TextureImporter ti)
                {
                    bool changed = false;
                    if (ti.maxTextureSize != 1024) { ti.maxTextureSize = 1024; changed = true; }
                    if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; changed = true; }
                    if (!normal && !ti.alphaIsTransparency) { ti.alphaIsTransparency = true; ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = 0.4f; changed = true; }
                    if (changed) ti.SaveAndReimport();
                }
                return AssetDatabase.LoadAssetAtPath<Texture2D>(f.Replace('\\', '/'));
            }
            mat.SetTexture("_BaseMap", Tex("B", false));
            mat.SetColor("_BaseColor", Color.white);
            var n = Tex("N", true);
            if (n != null) { mat.SetTexture("_BumpMap", n); mat.EnableKeyword("_NORMALMAP"); }
            mat.SetFloat("_Smoothness", 0.18f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Cull", 0f);                      // fur cards: both sides
            mat.SetFloat("_AlphaClip", 1f); mat.SetFloat("_Cutoff", 0.4f); mat.EnableKeyword("_ALPHATEST_ON");
            mat.renderQueue = 2450; mat.SetOverrideTag("RenderType", "TransparentCutout");
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
