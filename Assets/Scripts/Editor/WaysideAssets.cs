using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Jogging.EditorTools
{
    /// <summary>
    /// Prepares the wayside assets (Poly Haven, CC0) in Assets/PhotoReal/Wayside for the app:
    /// <list type="bullet">
    /// <item>models: Mesh LODs (like <see cref="MeshLods"/>), no animation/cameras, each FBX material remapped
    /// to a URP Lit material built from its texture set (base colour, normal map; plants with an
    /// alpha mask get it merged into the base map and are cut out and double sided),</item>
    /// <item>one prefab per model in Resources/Wayside (loaded at runtime by World/Wayside),</item>
    /// <item>materials for the procedurally built props (planks, bark, log ends, mud, tracks, leaves, …)
    /// as Resources/Wayside/Mat_&lt;texture&gt;.</item>
    /// </list>
    /// Textures are limited to 1024 px. Menu: Jogging → Build → Wegrand-Assets aufbereiten.
    /// </summary>
    public static class WaysideAssets
    {
        private const string Root = "Assets/PhotoReal/Wayside";
        private const string Out = Root + "/Resources/Wayside";
        private const string MatDir = Root + "/Materials";

        // Surface feel per model (URP Lit smoothness / metallic); the scans' roughness maps aren't used.
        private static readonly Dictionary<string, (float smooth, float metal)> Surface = new Dictionary<string, (float, float)>
        {
            ["metal_trash_can"] = (0.35f, 0.6f), ["street_lamp_01"] = (0.35f, 0.5f), ["utility_box_01"] = (0.3f, 0.3f),
            ["covered_car"] = (0.15f, 0f), ["painted_wooden_bench"] = (0.18f, 0f), ["outdoor_table_chair_set_01"] = (0.2f, 0f),
        };

        [MenuItem("Jogging/Build/Wegrand-Assets aufbereiten")]
        public static void Run()
        {
            Directory.CreateDirectory(Out); Directory.CreateDirectory(MatDir);
            AssetDatabase.Refresh();
            var log = new System.Text.StringBuilder("[Wegrand] Assets:\n");
            foreach (var dir in Directory.GetDirectories(Root + "/Models").OrderBy(d => d))
                PrepareModel(dir.Replace('\\', '/'), log);
            foreach (var dir in Directory.GetDirectories(Root + "/Textures").OrderBy(d => d))
                PrepareTextureMaterial(dir.Replace('\\', '/'), log);
            // Spots on and beside the trail: a texture with an irregular, cut-out edge
            MakeDecal("mud", Root + "/Textures/brown_mud_leaves_01/brown_mud_leaves_01_diffuse.jpg", Root + "/Textures/brown_mud_leaves_01/brown_mud_leaves_01_nor_gl.jpg", 11, 0.55f, 0.3f, log);
            MakeDecal("leaves", Root + "/Textures/forest_leaves_02/forest_leaves_02_diffuse.jpg", Root + "/Textures/forest_leaves_02/forest_leaves_02_nor_gl.jpg", 23, 0.5f, 0.1f, log);
            MakeDecal("needles", Root + "/Textures/dry_decay_leaves/dry_decay_leaves_diffuse.jpg", Root + "/Textures/dry_decay_leaves/dry_decay_leaves_nor_gl.jpg", 37, 0.5f, 0.1f, log);
            MakeDecal("puddle", null, null, 53, 0.6f, 0.94f, log);
            MeasureTrunks(log);
            AssetDatabase.SaveAssets();
            Debug.Log(log.ToString());
        }

        /// <summary>
        /// Trunk radius at 1.7 m of every tree the scatterers place (for marks and nest boxes on the bark):
        /// the vertices at that height near the pivot, their 60th-percentile distance from the trunk axis.
        /// Written to Resources/Wayside/trunks.txt as "prefab = radius".
        /// </summary>
        private static void MeasureTrunks(System.Text.StringBuilder log)
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/PhotoRun.unity");
            var lines = new List<string>();
            foreach (var sc in Object.FindObjectsByType<Jogging.World.TerrainTreeScatter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(sc);
                if (so.FindProperty("undergrowth").boolValue) continue;
                var arr = so.FindProperty("prefabs");
                for (int i = 0; i < arr.arraySize; i++)
                {
                    if (!(arr.GetArrayElementAtIndex(i).objectReferenceValue is GameObject g)) continue;
                    if (g.name.Contains("Double") || g.name.Contains("Tripple") || g.name.Contains("Split")) continue; // several trunks: no axis at the pivot
                    var pts = new List<Vector2>();
                    foreach (var mf in g.GetComponentsInChildren<MeshFilter>(true))
                    {
                        var m = mf.sharedMesh; if (m == null) continue;
                        var mtx = g.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                        foreach (var v in m.vertices)
                        {
                            var p = mtx.MultiplyPoint3x4(v);
                            if (p.y > 1.5f && p.y < 1.9f && p.x * p.x + p.z * p.z < 0.36f) pts.Add(new Vector2(p.x, p.z)); // the trunk, not the branches
                        }
                    }
                    if (pts.Count < 6) continue;
                    var d = pts.Select(p => p.magnitude).OrderBy(x => x).ToList(); // trunk axis at the pivot
                    float r = d[(int)(d.Count * 0.5f)];
                    if (r > 0.02f && r < 0.8f) lines.Add($"{g.name} = {r.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)}");
                }
            }
            File.WriteAllText(Out + "/trunks.txt", string.Join("\n", lines.Distinct()));
            AssetDatabase.ImportAsset(Out + "/trunks.txt");
            log.AppendLine($"  Stämme gemessen: {string.Join(", ", lines.Distinct())}");
        }

        /// <summary>
        /// A decal material: the texture (or dark water for a puddle) with a soft blob as alpha (noise edge),
        /// cut out. The blob fills the square with some margin, so a quad of any size shows one spot.
        /// </summary>
        private static void MakeDecal(string name, string diffPath, string nrmPath, int seed, float threshold, float smooth, System.Text.StringBuilder log)
        {
            const int n = 512;
            string png = $"{Root}/Textures/decal_{name}.png";
            var src = new Texture2D(2, 2);
            if (diffPath != null) src.LoadImage(File.ReadAllBytes(diffPath));
            var o = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var rnd = new System.Random(seed);
            float ox = (float)rnd.NextDouble() * 50f, oy = (float)rnd.NextDouble() * 50f;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n, v = y / (float)n;
                    float dx = (u - 0.5f) * 2f, dy = (v - 0.5f) * 2f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float edge = Mathf.PerlinNoise(ox + u * 4f, oy + v * 4f) * 0.55f + Mathf.PerlinNoise(ox + u * 13f, oy + v * 13f) * 0.3f + Mathf.PerlinNoise(ox + u * 31f, oy + v * 31f) * 0.15f;
                    float a = Mathf.Clamp01((1f - r) * 1.6f + (edge - 0.5f) * 1.2f);
                    Color c = diffPath != null ? src.GetPixelBilinear(u * 1.3f, v * 1.3f) : new Color(0.05f, 0.06f, 0.055f);
                    if (diffPath == null) c = Color.Lerp(c, new Color(0.18f, 0.14f, 0.10f), Mathf.Clamp01((r - 0.55f) * 3f)); // muddy rim
                    px[y * n + x] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)(a >= threshold ? 255 : 0));
                }
            o.SetPixels32(px); o.Apply();
            File.WriteAllBytes(png, o.EncodeToPNG());
            AssetDatabase.ImportAsset(png);
            if (AssetImporter.GetAtPath(png) is TextureImporter ti)
            {
                ti.alphaIsTransparency = true; ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = 0.5f; ti.maxTextureSize = 512;
                ti.wrapMode = TextureWrapMode.Clamp; ti.SaveAndReimport();
            }
            string path = $"{Out}/Decal_{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
            Setup(mat, AssetDatabase.LoadAssetAtPath<Texture2D>(png), nrmPath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(nrmPath) : null, smooth, 0f, true);
            mat.SetFloat("_Cull", 2f);
            EditorUtility.SetDirty(mat);
            log.AppendLine($"  Decal_{name}");
        }

        public static void RunBatch() { Run(); EditorApplication.Exit(0); }

        /// <summary>Children of each wayside prefab with their bounds (to see variants and orientation).</summary>
        public static void ListBatch()
        {
            var sb = new System.Text.StringBuilder("[Wegrand] Aufbau:\n");
            foreach (var p in Directory.GetFiles(Out, "*.prefab").OrderBy(x => x))
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(p.Replace('\\', '/')));
                sb.AppendLine("  " + go.name);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    sb.AppendLine($"    {r.name}: Mitte {r.bounds.center}, Größe {r.bounds.size}, Rot {r.transform.rotation.eulerAngles}");
                Object.DestroyImmediate(go);
            }
            Debug.Log(sb.ToString());
            EditorApplication.Exit(0);
        }

        private static void PrepareModel(string dir, System.Text.StringBuilder log)
        {
            string id = Path.GetFileName(dir);
            string fbx = $"{dir}/{id}.fbx";
            if (!(AssetImporter.GetAtPath(fbx) is ModelImporter imp)) { log.AppendLine($"  {id}: kein FBX"); return; }
            foreach (var t in Directory.GetFiles(dir + "/textures")) ConfigureTexture(t.Replace('\\', '/'));
            imp.importAnimation = false; imp.importCameras = false; imp.importLights = false; imp.importBlendShapes = false;
            imp.animationType = ModelImporterAnimationType.None;
            imp.generateMeshLods = true; imp.maximumMeshLod = 4;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            imp.SaveAndReimport();

            // FBX material names → our materials (by the texture set of the same name)
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var names = new HashSet<string>();
            foreach (var r in model.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) if (m != null) names.Add(m.name);
            foreach (var n in names)
            {
                var mat = MaterialFor(id, n, dir);
                if (mat != null) imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), mat);
            }
            imp.SaveAndReimport();

            model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            foreach (var old in Directory.GetFiles(Out, id + "*.prefab")) AssetDatabase.DeleteAsset(old.Replace('\\', '/'));
            var variants = SplitVariants(model, Scale.TryGetValue(id, out float sc) ? sc : 1f);
            for (int k = 0; k < variants.Count; k++)
            {
                var v = variants[k];
                var b = Bounds(v);
                int tris = 0;
                foreach (var mf in v.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
                string name = variants.Count == 1 ? id : $"{id}_{(char)('a' + k)}";
                v.name = name;
                PrefabUtility.SaveAsPrefabAsset(v, $"{Out}/{name}.prefab");
                log.AppendLine($"  {name}: Größe {b.size.x:0.00}×{b.size.y:0.00}×{b.size.z:0.00} m, {tris} Dreiecke");
                Object.DestroyImmediate(v);
            }
        }

        // Real-world size corrections (m): the nettles and the small shrub come in far too small
        private static readonly Dictionary<string, float> Scale = new Dictionary<string, float>
        {
            ["nettle_plant"] = 4.2f, ["shrub_03"] = 2.2f, ["dead_tree_trunk_02"] = 1.5f,
        };

        /// <summary>
        /// One object per variant: the scans put several variants side by side (four ferns, a clean and a
        /// rusty trash can …) and some carry their LOD levels as extra meshes. LOD1+ meshes go (Unity's
        /// Mesh LOD makes the levels), the rest is grouped by overlapping footprints (a can with its lid and
        /// handles stays one), each group gets a root at its bottom centre.
        /// </summary>
        private static List<GameObject> SplitVariants(GameObject model, float scale)
        {
            var inst = (GameObject)Object.Instantiate(model);
            var parts = new List<Renderer>();
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(r.name, @"_LOD[1-9]\d*$")) continue;
                parts.Add(r);
            }
            int n = parts.Count;
            var group = Enumerable.Range(0, n).ToArray();
            int Find(int i) { while (group[i] != i) i = group[i] = group[group[i]]; return i; }
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    Bounds a = parts[i].bounds, b = parts[j].bounds;
                    a.Expand(new Vector3(0.04f, 100f, 0.04f));
                    if (a.Intersects(b)) group[Find(i)] = Find(j);
                }
            var result = new List<GameObject>();
            foreach (var g in Enumerable.Range(0, n).GroupBy(Find).OrderBy(g => parts[g.First()].bounds.center.x).ThenBy(g => parts[g.First()].bounds.center.z))
            {
                var bb = parts[g.First()].bounds;
                foreach (int i in g) bb.Encapsulate(parts[i].bounds);
                var root = new GameObject("variant");
                root.transform.position = new Vector3(bb.center.x, bb.min.y, bb.center.z);
                foreach (int i in g)
                {
                    var copy = Object.Instantiate(parts[i].gameObject, parts[i].transform.position, parts[i].transform.rotation);
                    foreach (Transform c in copy.transform) Object.DestroyImmediate(c.gameObject); // children are parts of their own
                    copy.name = parts[i].name.Replace("_LOD0", "");
                    copy.transform.localScale = parts[i].transform.lossyScale;
                    copy.transform.SetParent(root.transform, true);
                }
                root.transform.position = Vector3.zero;
                root.transform.localScale = Vector3.one * scale;
                // bake the scale into a child holder so the prefab root stays at scale 1
                var holder = new GameObject("variant");
                root.transform.SetParent(holder.transform, false);
                root.name = "model";
                result.Add(holder);
            }
            Object.DestroyImmediate(inst);
            return result;
        }

        private static Material MaterialFor(string id, string fbxMat, string dir)
        {
            // texture prefix: the material name if a set exists for it, else the model id
            string tex = dir + "/textures/";
            string prefix = File.Exists($"{tex}{fbxMat}_diff_1k.jpg") || File.Exists($"{tex}{fbxMat}_diff_1k.png") ? fbxMat : id;
            string diff = File.Exists($"{tex}{prefix}_diff_1k.jpg") ? $"{tex}{prefix}_diff_1k.jpg" : $"{tex}{prefix}_diff_1k.png";
            if (!File.Exists(diff)) return null;
            string nrm = new[] { $"{tex}{prefix}_nor_gl_1k.exr", $"{tex}{prefix}_nor_gl_1k.png", $"{tex}{prefix}_nor_gl_1k.jpg" }.FirstOrDefault(File.Exists);
            string alpha = new[] { $"{tex}{prefix}_alpha_1k.png", $"{tex}{prefix}_opacity_1k.png" }.FirstOrDefault(File.Exists);
            if (alpha != null) diff = MergeAlpha(diff, alpha, $"{tex}{prefix}_diffa_1k.png");

            string path = $"{MatDir}/{prefix}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
            var s = Surface.TryGetValue(id, out var sv) ? sv : (0.12f, 0f);
            Setup(mat, AssetDatabase.LoadAssetAtPath<Texture2D>(diff), nrm != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(nrm) : null, s.Item1, s.Item2, alpha != null);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void PrepareTextureMaterial(string dir, System.Text.StringBuilder log)
        {
            string id = Path.GetFileName(dir);
            foreach (var t in Directory.GetFiles(dir)) ConfigureTexture(t.Replace('\\', '/'));
            var diff = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{id}_diffuse.jpg");
            var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{id}_nor_gl.jpg");
            if (diff == null) { log.AppendLine($"  Textur {id}: fehlt"); return; }
            string path = $"{Out}/Mat_{id}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
            bool metal = id.Contains("metal") || id.Contains("corrugated");
            Setup(mat, diff, nrm, id.Contains("mud") || id.Contains("tracks") ? 0.3f : metal ? 0.3f : 0.1f, metal ? 0.4f : 0f, false);
            EditorUtility.SetDirty(mat);
            log.AppendLine($"  Material Mat_{id}");
        }

        private static void Setup(Material m, Texture2D baseMap, Texture2D normal, float smooth, float metal, bool cutout)
        {
            m.SetTexture("_BaseMap", baseMap);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            if (normal != null) { m.SetTexture("_BumpMap", normal); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
            else m.DisableKeyword("_NORMALMAP");
            m.SetFloat("_AlphaClip", cutout ? 1f : 0f);
            m.SetFloat("_Cutoff", 0.45f);
            m.SetFloat("_Cull", cutout ? 0f : 2f);
            if (cutout) { m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = 2450; m.SetOverrideTag("RenderType", "TransparentCutout"); }
            else { m.DisableKeyword("_ALPHATEST_ON"); m.renderQueue = -1; m.SetOverrideTag("RenderType", "Opaque"); }
            m.enableInstancing = true;
        }

        private static void ConfigureTexture(string path)
        {
            if (path.EndsWith(".meta")) return;
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return;
            bool normal = path.Contains("_nor_gl");
            bool changed = false;
            if (ti.maxTextureSize != 1024) { ti.maxTextureSize = 1024; changed = true; }
            if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; changed = true; }
            bool data = path.Contains("_rough") || path.Contains("_metal") || path.Contains("_alpha") || path.Contains("_opacity");
            if (data && ti.sRGBTexture) { ti.sRGBTexture = false; changed = true; }
            if (path.Contains("_diffa_") && !ti.alphaIsTransparency) { ti.alphaIsTransparency = true; ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = 0.45f; changed = true; }
            if (changed) ti.SaveAndReimport();
        }

        // Base colour + separate alpha mask → one RGBA texture (URP Lit cuts out by base alpha)
        private static string MergeAlpha(string diffPath, string alphaPath, string outPath)
        {
            if (File.Exists(outPath)) return outPath;
            var d = new Texture2D(2, 2); d.LoadImage(File.ReadAllBytes(diffPath));
            var a = new Texture2D(2, 2); a.LoadImage(File.ReadAllBytes(alphaPath));
            if (a.width != d.width || a.height != d.height) { var rt = Resize(a, d.width, d.height); a = rt; }
            var pd = d.GetPixels32(); var pa = a.GetPixels32();
            for (int i = 0; i < pd.Length; i++) pd[i].a = pa[i].r;
            var o = new Texture2D(d.width, d.height, TextureFormat.RGBA32, false); o.SetPixels32(pd); o.Apply();
            File.WriteAllBytes(outPath, o.EncodeToPNG());
            AssetDatabase.ImportAsset(outPath);
            ConfigureTexture(outPath);
            return outPath;
        }

        private static Texture2D Resize(Texture2D src, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 0);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false); t.ReadPixels(new Rect(0, 0, w, h), 0, 0); t.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            return t;
        }

        private static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds();
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
