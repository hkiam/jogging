using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Jogging.EditorTools
{
    /// <summary>
    /// Builds the seasonal variants of the scenery materials into Assets/PhotoReal/Seasons:
    ///   • Birch (atlas: bark left quarter, twigs and leaf cards right): spring = fresh light green,
    ///     autumn = leaves recoloured in patches of yellow / orange / red, winter = leaf pixels cut
    ///     out so bark and bare twigs remain.
    ///   • Pine: winter = needles dusted towards frost white; spring/autumn = unchanged.
    ///   • Toned-down bush/plant materials: colour properties shifted per season.
    /// Returns parallel arrays for <see cref="Jogging.World.SeasonAssets"/>.
    /// </summary>
    public static class SeasonBuilder
    {
        private const string Dir = "Assets/PhotoReal/Seasons";

        public static void Build(IList<Material> bushes, out Material[] bases, out Material[] spring, out Material[] autumn, out Material[] winter)
        {
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/PhotoReal", "Seasons");
            var b = new List<Material>(); var sp = new List<Material>(); var au = new List<Material>(); var wi = new List<Material>();

            var birch = AssetDatabase.LoadAssetAtPath<Material>("Assets/MapMagic/Demo/Trees/Birch/Materials/Birch.mat");
            if (birch != null)
            {
                b.Add(birch);
                sp.Add(TextureVariant(birch, "Birch_Spring", (c, x, y, w, h) => Leaf(c, x, w) ? new Color(Mathf.Min(1f, c.r * 1.12f), Mathf.Min(1f, c.g * 1.22f), c.b * 0.85f, c.a) : c));
                au.Add(TextureVariant(birch, "Birch_Autumn", (c, x, y, w, h) => Leaf(c, x, w) ? Autumn(c, x, y) : c));
                wi.Add(TextureVariant(birch, "Birch_Winter", (c, x, y, w, h) => Leaf(c, x, w) ? new Color(c.r, c.g, c.b, 0f) : c));
            }
            var pine = AssetDatabase.LoadAssetAtPath<Material>("Assets/MapMagic/Demo/Trees/Pine/Materials/Pine.mat");
            if (pine != null)
            {
                b.Add(pine); sp.Add(pine); au.Add(pine);
                wi.Add(TextureVariant(pine, "Pine_Winter", (c, x, y, w, h) =>
                {
                    if (!Green(c)) return c;
                    float l = 0.3f * c.r + 0.59f * c.g + 0.11f * c.b;
                    var frost = new Color(0.86f, 0.90f, 0.93f) * Mathf.Max(0.55f, l * 1.7f);
                    var m = Color.Lerp(c, frost, 0.3f); m.a = c.a; return m;
                }));
            }
            foreach (var m in bushes)
            {
                if (m == null) continue;
                b.Add(m);
                sp.Add(ColourVariant(m, "Spring", (h, s, v) => (Mathf.Lerp(h, 0.26f, 0.5f), s * 1.1f, v * 1.2f)));
                au.Add(ColourVariant(m, "Autumn", (h, s, v) => (Mathf.Lerp(h, 0.08f, 0.8f), Mathf.Min(1f, s * 1.15f), v * 1.05f)));
                wi.Add(ColourVariant(m, "Winter", (h, s, v) => (0.07f, s * 0.3f, v * 0.75f)));
            }
            AssetDatabase.SaveAssets();
            bases = b.ToArray(); spring = sp.ToArray(); autumn = au.ToArray(); winter = wi.ToArray();
        }

        private static bool Green(Color c) => c.a > 0.05f && c.g > c.r * 1.04f && c.g > c.b * 1.1f;
        private static bool Leaf(Color c, int x, int w) => x > w / 4 && Green(c); // bark lives in the left quarter

        private static Color Autumn(Color c, int x, int y)
        {
            // Patches (≈ 24 px) of one colour each, so a tree has clusters instead of noise.
            int cell = (x / 24) * 73856093 ^ (y / 24) * 19349663;
            var rng = new System.Random(cell);
            Color[] pal = { new Color(0.95f, 0.76f, 0.22f), new Color(0.92f, 0.50f, 0.14f), new Color(0.74f, 0.24f, 0.11f), new Color(0.62f, 0.66f, 0.24f) };
            var p = pal[rng.Next(pal.Length)];
            float l = 0.3f * c.r + 0.59f * c.g + 0.11f * c.b;
            var n = Color.Lerp(p * Mathf.Clamp(l * 1.9f, 0.25f, 1.2f), c, 0.12f);
            n.a = c.a; return n;
        }

        private delegate Color PixelFn(Color c, int x, int y, int w, int h);

        private static Material TextureVariant(Material baseMat, string name, PixelFn fn)
        {
            var src = baseMat.GetTexture("_BaseMap") as Texture2D;
            string texPath = Dir + "/" + name + ".png";
            if (src != null)
            {
                string srcPath = AssetDatabase.GetAssetPath(src);
                var ti = (TextureImporter)AssetImporter.GetAtPath(srcPath);
                bool wasReadable = ti.isReadable;
                // From the source file itself (PNG/JPG): the imported texture is compressed per build
                // target, so reading it made the season images differ with the last platform built.
                var file = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                bool fromFile = (srcPath.EndsWith(".png") || srcPath.EndsWith(".jpg") || srcPath.EndsWith(".jpeg"))
                                && file.LoadImage(File.ReadAllBytes(srcPath));
                if (!fromFile)
                {
                    Object.DestroyImmediate(file); file = null;
                    if (!wasReadable) { ti.isReadable = true; ti.SaveAndReimport(); }
                    src = AssetDatabase.LoadAssetAtPath<Texture2D>(srcPath);
                }
                var read = fromFile ? file : src;
                int w = read.width, h = read.height;
                var px = read.GetPixels();
                if (file != null) Object.DestroyImmediate(file);
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = fn(px[y * w + x], x, y, w, h);
                var dst = new Texture2D(w, h, TextureFormat.RGBA32, true);
                dst.SetPixels(px); dst.Apply();
                File.WriteAllBytes(texPath, dst.EncodeToPNG());
                Object.DestroyImmediate(dst);
                if (!fromFile && !wasReadable) { ti.isReadable = false; ti.SaveAndReimport(); }
                AssetDatabase.ImportAsset(texPath);
                var nti = (TextureImporter)AssetImporter.GetAtPath(texPath);
                nti.alphaIsTransparency = true; nti.mipmapEnabled = true; nti.SaveAndReimport();
            }
            string matPath = Dir + "/" + name + ".mat";
            var mat = new Material(baseMat);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex != null) mat.SetTexture("_BaseMap", tex);
            return AssetUtil.Upsert(mat, matPath);
        }

        private static Material ColourVariant(Material baseMat, string season, System.Func<float, float, float, (float, float, float)> hsv)
        {
            string path = Dir + "/" + baseMat.name + "_" + season + ".mat";
            var m = new Material(baseMat);
            foreach (var prop in new[] { "_BaseColor", "_Color", "_Top_Color", "_Bottom_Color", "_TopColor", "_BottomColor" })
            {
                if (!m.HasProperty(prop)) continue;
                var c = m.GetColor(prop);
                Color.RGBToHSV(c, out float h, out float s, out float v);
                var (nh, ns, nv) = hsv(h, s, v);
                var n = Color.HSVToRGB(Mathf.Repeat(nh, 1f), Mathf.Clamp01(ns), Mathf.Clamp01(nv)); n.a = c.a;
                m.SetColor(prop, n);
            }
            return AssetUtil.Upsert(m, path);
        }
    }
}
