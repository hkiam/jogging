using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Jogging.World
{
    /// <summary>
    /// The man-made and small natural things beside the trail, built from parts (<see cref="MeshKit"/>)
    /// with the scanned Poly Haven textures (Resources/Wayside/Mat_*), plus the scanned models themselves
    /// (Resources/Wayside/&lt;name&gt;). Every builder returns a root at the foot of the object, facing +z
    /// (usually towards the trail). Sizes are real-world: a signpost 2.4 m, a hunting stand 4.5 m.
    /// </summary>
    public static class WaysideProps
    {
        // ---------------------------------------------------------------- materials and models
        private static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        private static readonly Dictionary<string, GameObject> models = new Dictionary<string, GameObject>();

        public static Material Tex(string id)
        {
            if (mats.TryGetValue(id, out var m) && m != null) return m;
            m = Resources.Load<Material>("Wayside/" + id);
            if (m == null) { Debug.LogWarning("[Wegrand] Material fehlt: " + id); m = Paint("missing", Color.magenta); }
            return mats[id] = m;
        }

        public static Material Paint(string key, Color c, float smooth = 0.3f, float metal = 0f)
        {
            if (mats.TryGetValue("paint:" + key, out var m) && m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = key, enableInstancing = true };
            m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
            return mats["paint:" + key] = m;
        }

        public static GameObject Model(string name)
        {
            if (models.TryGetValue(name, out var g)) return g;
            g = Resources.Load<GameObject>("Wayside/" + name);
            if (g == null) Debug.LogWarning("[Wegrand] Modell fehlt: " + name);
            return models[name] = g;
        }

        /// <summary>A scanned model under parent, at local position/rotation/scale.</summary>
        public static GameObject Place(string name, Transform parent, Vector3 pos, float yaw, float scale = 1f)
        {
            var prefab = Model(name);
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, parent);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one * scale;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++) ms[i] = SeasonAssets.Swap(ms[i]);
                r.sharedMaterials = ms;
            }
            return go;
        }

        private static Material Planks => Tex("Mat_weathered_planks");
        private static Material Rough => Tex("Mat_rough_wood");
        private static Material Bark => Tex("Mat_pine_bark");
        private static Material Ends => Tex("Mat_wood_trunk_wall");
        private static Material White => Paint("white", new Color(0.86f, 0.86f, 0.83f), 0.35f);
        private static Material Red => Paint("red", new Color(0.62f, 0.07f, 0.06f), 0.4f);
        private static Material DarkGreen => Paint("darkgreen", new Color(0.10f, 0.22f, 0.13f), 0.35f);
        private static Material Metal => Paint("metal", new Color(0.42f, 0.43f, 0.44f), 0.45f, 0.6f);
        private static Material Black => Paint("black", new Color(0.03f, 0.03f, 0.03f), 0.2f);

        private static GameObject Root(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        // ---------------------------------------------------------------- text on boards
        /// <summary>Text on a board face (world-space canvas, unlit): width/height in metres, facing +z of 'at'.</summary>
        public static void Label(Transform parent, string text, Vector3 pos, Quaternion rot, float w, float h, Color color, TextAnchor anchor = TextAnchor.MiddleCenter, float fontFrac = 0.62f)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.localPosition = pos; rt.localRotation = rot * Quaternion.Euler(0f, 180f, 0f); // canvases face -z
            const float ppm = 500f; // pixels per metre
            rt.sizeDelta = new Vector2(w * ppm, h * ppm);
            rt.localScale = Vector3.one / ppm;
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var tgo = new GameObject("t", typeof(RectTransform), typeof(Text));
            tgo.transform.SetParent(go.transform, false);
            var trt = (RectTransform)tgo.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = new Vector2(8f, 2f); trt.offsetMax = new Vector2(-8f, -2f);
            var t = tgo.GetComponent<Text>();
            t.font = Jogging.UI.UiTheme.Font; t.text = text; t.color = color; t.alignment = anchor; t.fontStyle = FontStyle.Bold;
            t.fontSize = Mathf.RoundToInt(h * ppm * fontFrac); t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.resizeTextForBestFit = true; t.resizeTextMaxSize = t.fontSize; t.resizeTextMinSize = 8;
        }

        // ---------------------------------------------------------------- hiking signs
        /// <summary>Wooden signpost with finger boards pointing along the given yaws (degrees), each with a destination.</summary>
        public static GameObject Signpost(Transform parent, (float yaw, string text)[] fingers, int seed)
        {
            var root = Root("Wegweiser", parent);
            var k = new MeshKit();
            k.Box(0, new Vector3(0f, 1.15f, 0f), new Vector3(0.1f, 2.5f, 0.1f), Quaternion.identity, 0.8f);  // post, 0.2 m in the ground
            k.Box(0, new Vector3(0f, 2.43f, 0f), new Vector3(0.13f, 0.03f, 0.13f), Quaternion.identity, 0.8f); // cap
            k.Make("Pfahl", root.transform, Rough);
            // the hiking mark on the post: white plate with a red bar
            var mark = new MeshKit();
            mark.Box(0, new Vector3(0f, 1.55f, 0.052f), new Vector3(0.08f, 0.1f, 0.004f), Quaternion.identity);
            mark.Box(1, new Vector3(0f, 1.55f, 0.055f), new Vector3(0.08f, 0.025f, 0.003f), Quaternion.identity);
            mark.Make("Markierung", root.transform, White, Red);
            for (int i = 0; i < fingers.Length; i++)
            {
                float y = 2.2f - i * 0.2f;
                var q = Quaternion.Euler(0f, fingers[i].yaw, 0f);
                var fk = new MeshKit();
                // board with a pointed tip, 0.75 m long, starting at the post
                Vector3 c = q * new Vector3(0f, 0f, 0.42f) + Vector3.up * y;
                fk.Box(0, c, new Vector3(0.022f, 0.15f, 0.72f), q, 1f);
                fk.Beam(0, c + q * new Vector3(0f, 0.05f, 0.36f), c + q * new Vector3(0f, 0f, 0.44f), 0.022f, 0.06f, Vector3.up);
                fk.Beam(0, c + q * new Vector3(0f, -0.05f, 0.36f), c + q * new Vector3(0f, 0f, 0.44f), 0.022f, 0.06f, Vector3.up);
                fk.Make("Schild", root.transform, White);
                var face = q * Quaternion.Euler(0f, 90f, 0f); // board side faces ±x of the finger
                Label(root.transform, fingers[i].text, c + q * new Vector3(0.0125f, 0f, 0f), face, 0.66f, 0.12f, new Color(0.08f, 0.08f, 0.08f));
                Label(root.transform, fingers[i].text, c + q * new Vector3(-0.0125f, 0f, 0f), face * Quaternion.Euler(0f, 180f, 0f), 0.66f, 0.12f, new Color(0.08f, 0.08f, 0.08f));
            }
            return root;
        }

        /// <summary>Info board with a little roof: the route as a map, its name and a greeting.</summary>
        public static GameObject InfoBoard(Transform parent, string title, List<Vector2> route, int seed)
        {
            var root = Root("Infotafel", parent);
            var k = new MeshKit();
            foreach (float x in new[] { -0.8f, 0.8f }) k.Box(0, new Vector3(x, 1.05f, 0f), new Vector3(0.12f, 2.4f, 0.12f), Quaternion.identity, 0.8f);
            k.Box(1, new Vector3(0f, 1.45f, -0.02f), new Vector3(1.5f, 1.0f, 0.04f), Quaternion.identity, 0.8f);     // board
            k.Box(0, new Vector3(0f, 2.28f, 0.12f), new Vector3(1.9f, 0.03f, 0.55f), Quaternion.Euler(-20f, 0f, 0f), 0.6f); // roof
            k.Make("Tafel", root.transform, Rough, Planks);
            // the map: drawn from the route itself
            var tex = MapTexture(route, seed);
            var mk = new MeshKit();
            mk.QuadUv(0, new Vector3(-0.62f, 1.03f, 0.001f), new Vector3(0.62f, 1.03f, 0.001f), new Vector3(0.62f, 1.78f, 0.001f), new Vector3(-0.62f, 1.78f, 0.001f),
                      new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1)); // seen from +z, world −x is on the right
            var mapMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Karte" };
            mapMat.SetTexture("_BaseMap", tex); mapMat.SetFloat("_Smoothness", 0.35f);
            mk.Make("Karte", root.transform, mapMat);
            OwnedAssets.Own(root, mapMat); OwnedAssets.Own(root, tex);
            Label(root.transform, title, new Vector3(0f, 1.87f, 0.002f), Quaternion.identity, 1.3f, 0.12f, new Color(0.1f, 0.18f, 0.1f));
            return root;
        }

        private static Texture2D MapTexture(List<Vector2> route, int seed)
        {
            const int w = 512, h = 320;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "Karte", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            float ox = seed % 97, oy = seed % 53;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float n = Mathf.PerlinNoise(ox + x * 0.012f, oy + y * 0.012f);
                    Color c = n > 0.52f ? new Color(0.62f, 0.76f, 0.52f) : new Color(0.93f, 0.91f, 0.82f); // forest / open land
                    if (Mathf.Abs(Mathf.PerlinNoise(ox * 2f + x * 0.03f, oy + y * 0.004f) - 0.5f) < 0.012f) c = new Color(0.45f, 0.62f, 0.85f); // a stream
                    px[y * w + x] = c;
                }
            if (route != null && route.Count > 1)
            {
                Vector2 lo = route[0], hi = route[0];
                foreach (var p in route) { lo = Vector2.Min(lo, p); hi = Vector2.Max(hi, p); }
                float sc = Mathf.Min((w - 60) / Mathf.Max(1f, hi.x - lo.x), (h - 60) / Mathf.Max(1f, hi.y - lo.y));
                Vector2 off = new Vector2(w, h) * 0.5f - (lo + hi) * 0.5f * sc;
                for (int i = 1; i < route.Count; i++)
                {
                    Vector2 a = route[i - 1] * sc + off, b = route[i] * sc + off;
                    int steps = Mathf.CeilToInt((b - a).magnitude);
                    for (int s = 0; s <= steps; s++)
                    {
                        Vector2 p = Vector2.Lerp(a, b, s / (float)Mathf.Max(1, steps));
                        if (((i * 7 + s) / 5) % 3 == 2) continue; // dashed
                        for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++)
                            {
                                int xx = (int)p.x + dx, yy = (int)p.y + dy;
                                if (xx >= 0 && yy >= 0 && xx < w && yy < h) px[yy * w + xx] = new Color32(200, 30, 30, 255);
                            }
                    }
                }
                // "you are here"
                Vector2 st = route[0] * sc + off;
                for (int dy = -6; dy <= 6; dy++) for (int dx = -6; dx <= 6; dx++)
                        if (dx * dx + dy * dy <= 36) { int xx = (int)st.x + dx, yy = (int)st.y + dy; if (xx >= 0 && yy >= 0 && xx < w && yy < h) px[yy * w + xx] = new Color32(20, 20, 160, 255); }
            }
            t.SetPixels32(px); t.Apply(true, true);
            return t;
        }

        /// <summary>Small granite boundary stone with a cut cross on top.</summary>
        public static GameObject BoundaryStone(Transform parent, int seed)
        {
            var root = Root("Grenzstein", parent);
            var k = new MeshKit();
            k.Box(0, new Vector3(0f, 0.18f, 0f), new Vector3(0.24f, 0.56f, 0.18f), Quaternion.Euler(0f, 0f, (seed % 7) - 3f), 1.5f);
            k.Box(1, new Vector3(0f, 0.463f, 0f), new Vector3(0.14f, 0.004f, 0.02f), Quaternion.identity);
            k.Box(1, new Vector3(0f, 0.463f, 0f), new Vector3(0.02f, 0.004f, 0.1f), Quaternion.identity);
            k.Make("Stein", root.transform, Tex("Mat_mossy_rock"), Black);
            return root;
        }

        // ---------------------------------------------------------------- forest
        /// <summary>Open hunting stand on four poles with a ladder, platform, railing and roof (faces +z).</summary>
        public static GameObject HuntingStand(Transform parent, int seed)
        {
            var root = Root("Hochsitz", parent);
            var poles = new MeshKit();
            float top = 3.1f;
            Vector3[] foot = { new Vector3(-0.95f, -0.3f, -0.95f), new Vector3(0.95f, -0.3f, -0.95f), new Vector3(-0.95f, -0.3f, 0.95f), new Vector3(0.95f, -0.3f, 0.95f) };
            Vector3[] head = { new Vector3(-0.6f, top + 1.2f, -0.6f), new Vector3(0.6f, top + 1.2f, -0.6f), new Vector3(-0.6f, top + 1.0f, 0.6f), new Vector3(0.6f, top + 1.0f, 0.6f) };
            for (int i = 0; i < 4; i++) poles.Cylinder(0, foot[i], head[i], 0.09f, 0.07f, 8, 1, 0.8f, 0.08f, seed + i, new Vector3(0.3f, 0.3f, 0.06f));
            // cross braces
            poles.Cylinder(0, new Vector3(-0.9f, 0.6f, 0.9f), new Vector3(0.75f, 2.3f, 0.75f), 0.045f, 0.045f, 6, 1, 0.8f);
            poles.Cylinder(0, new Vector3(0.9f, 0.6f, -0.9f), new Vector3(-0.75f, 2.3f, -0.75f), 0.045f, 0.045f, 6, 1, 0.8f);
            poles.Make("Stangen", root.transform, Bark, Ends);
            var wood = new MeshKit();
            wood.Box(0, new Vector3(0f, top, 0f), new Vector3(1.5f, 0.06f, 1.5f), Quaternion.identity, 0.6f);                 // platform
            for (int s = -1; s <= 1; s += 2)
            {
                wood.Box(0, new Vector3(s * 0.72f, top + 0.45f, 0f), new Vector3(0.03f, 0.55f, 1.45f), Quaternion.identity, 0.6f); // side boards
                wood.Box(0, new Vector3(0f, top + 0.45f, -0.72f), new Vector3(1.45f, 0.55f, 0.03f), Quaternion.identity, 0.6f);  // back
            }
            wood.Box(0, new Vector3(0f, top + 0.62f, 0.72f), new Vector3(1.45f, 0.2f, 0.03f), Quaternion.identity, 0.6f);       // front rail
            wood.Box(0, new Vector3(0f, top + 1.32f, 0f), new Vector3(1.75f, 0.04f, 1.85f), Quaternion.Euler(-12f, 0f, 0f), 0.6f); // roof
            // ladder in front
            for (int s = -1; s <= 1; s += 2) wood.Beam(1, new Vector3(s * 0.25f, -0.2f, 1.9f), new Vector3(s * 0.25f, top, 0.8f), 0.06f, 0.06f, Vector3.up, 0.8f);
            for (float t = 0.12f; t < 0.98f; t += 0.105f)
            {
                Vector3 p = Vector3.Lerp(new Vector3(0f, -0.2f, 1.9f), new Vector3(0f, top, 0.8f), t);
                wood.Beam(1, p + Vector3.left * 0.26f, p + Vector3.right * 0.26f, 0.04f, 0.04f, Vector3.up, 0.8f);
            }
            wood.Make("Kanzel", root.transform, Planks, Bark);
            return root;
        }

        /// <summary>Log pile (Holzpolter): rows of 4 m logs, bark sides, sawn ends, along +x.</summary>
        public static GameObject LogPile(Transform parent, int seed, int bottomRow = 6)
        {
            var root = Root("Holzpolter", parent);
            var rnd = new System.Random(seed);
            var k = new MeshKit();
            float y = 0f;
            for (int row = 0; row < bottomRow && row < 5; row++)
            {
                int count = bottomRow - row;
                float r = 0.22f - row * 0.015f;
                float width = count * r * 2f;
                for (int i = 0; i < count; i++)
                {
                    float rr = r * (0.85f + 0.3f * (float)rnd.NextDouble());
                    float z = -width * 0.5f + r + i * 2f * r;
                    float len = 4f + (float)rnd.NextDouble() * 0.4f, shift = ((float)rnd.NextDouble() - 0.5f) * 0.4f;
                    var cu = new Vector3(0.15f + 0.7f * (float)rnd.NextDouble(), 0.15f + 0.7f * (float)rnd.NextDouble(), 0.07f);
                    k.Cylinder(0, new Vector3(-len * 0.5f + shift, y + rr, z), new Vector3(len * 0.5f + shift, y + rr, z), rr, rr * 0.93f, 10, 1, 0.8f, 0.05f, rnd.Next(), cu);
                }
                y += r * 1.72f;
            }
            k.Make("Stämme", root.transform, Bark, Ends);
            return root;
        }

        /// <summary>Forest barrier: two posts and a red-and-white bar (closed), 4 m wide along x.</summary>
        public static GameObject Barrier(Transform parent, int seed)
        {
            var root = Root("Schranke", parent);
            var k = new MeshKit();
            foreach (float x in new[] { -2.1f, 2.1f }) k.Box(0, new Vector3(x, 0.45f, 0f), new Vector3(0.14f, 1.3f, 0.14f), Quaternion.identity, 0.8f);
            k.Make("Pfosten", root.transform, Rough);
            var bar = new MeshKit();
            for (int i = 0; i < 8; i++)
            {
                float x0 = -2.1f + i * 0.525f;
                bar.Box(i % 2, new Vector3(x0 + 0.2625f, 0.95f, 0f), new Vector3(0.525f, 0.09f, 0.09f), Quaternion.identity);
            }
            bar.Make("Balken", root.transform, Red, White);
            return root;
        }

        /// <summary>A young plantation (Schonung): saplings in protective tubes with stakes, in rows.</summary>
        public static GameObject TreeGuards(Transform parent, int seed, int rows = 5, int cols = 7)
        {
            var root = Root("Schonung", parent);
            var rnd = new System.Random(seed);
            var k = new MeshKit();
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    if (rnd.NextDouble() < 0.12) continue;
                    var p = new Vector3((c - cols * 0.5f) * 2f + (float)rnd.NextDouble() * 0.4f, 0f, r * 2f + (float)rnd.NextDouble() * 0.4f);
                    float hgt = 1.1f + (float)rnd.NextDouble() * 0.3f;
                    k.Cylinder(0, p + Vector3.down * 0.05f, p + Vector3.up * hgt, 0.065f, 0.06f, 8);
                    k.Box(1, p + new Vector3(0.09f, hgt * 0.55f, 0f), new Vector3(0.035f, hgt * 1.15f, 0.035f), Quaternion.Euler(0f, 0f, 2f), 0.8f);
                }
            k.Make("Hüllen", root.transform, Paint("tube", new Color(0.30f, 0.38f, 0.22f), 0.45f), Rough);
            return root;
        }

        /// <summary>Freshly sawn stump: bark sides, a pale cut surface on top.</summary>
        public static GameObject CutStump(Transform parent, int seed)
        {
            var root = Root("Stubben", parent);
            var rnd = new System.Random(seed);
            float r = 0.18f + (float)rnd.NextDouble() * 0.14f, h = 0.25f + (float)rnd.NextDouble() * 0.25f;
            var k = new MeshKit();
            var cu = new Vector3(0.2f + 0.6f * (float)rnd.NextDouble(), 0.2f + 0.6f * (float)rnd.NextDouble(), 0.07f);
            k.Cylinder(0, new Vector3(0f, -0.15f, 0f), new Vector3(0f, h, 0f), r * 1.25f, r, 10, 1, 0.8f, 0.07f, seed, cu);
            k.Make("Stubben", root.transform, Bark, Ends);
            return root;
        }

        /// <summary>A few felled logs lying side by side, bark on, sawn ends.</summary>
        public static GameObject FelledLogs(Transform parent, int seed)
        {
            var root = Root("Stämme", parent);
            var rnd = new System.Random(seed);
            var k = new MeshKit();
            int n = 2 + rnd.Next(3);
            for (int i = 0; i < n; i++)
            {
                float r = 0.14f + (float)rnd.NextDouble() * 0.12f, len = 5f + (float)rnd.NextDouble() * 4f, ang = ((float)rnd.NextDouble() - 0.5f) * 12f;
                var q = Quaternion.Euler(0f, ang, 0f);
                var c = new Vector3(0f, r - 0.03f, i * 0.55f);
                var cu = new Vector3(0.2f + 0.6f * (float)rnd.NextDouble(), 0.2f + 0.6f * (float)rnd.NextDouble(), 0.07f);
                k.Cylinder(0, c + q * new Vector3(-len * 0.5f, 0f, 0f), c + q * new Vector3(len * 0.5f, 0f, 0f), r, r * 0.8f, 10, 1, 0.8f, 0.05f, rnd.Next(), cu);
            }
            k.Make("Stämme", root.transform, Bark, Ends);
            return root;
        }

        /// <summary>Nest box for birds, hung on a trunk (faces +z, hole included).</summary>
        public static GameObject NestBox(Transform parent)
        {
            var root = Root("Nistkasten", parent);
            var k = new MeshKit();
            k.Box(0, new Vector3(0f, 0f, 0.1f), new Vector3(0.17f, 0.26f, 0.17f), Quaternion.identity, 1f);
            k.Box(0, new Vector3(0f, 0.15f, 0.12f), new Vector3(0.22f, 0.025f, 0.24f), Quaternion.Euler(12f, 0f, 0f), 1f);
            k.Cylinder(1, new Vector3(0f, 0.05f, 0.186f), new Vector3(0f, 0.05f, 0.19f), 0.017f, 0.017f, 10, 1);
            k.Make("Kasten", root.transform, Paint("nestbox", new Color(0.36f, 0.27f, 0.18f), 0.2f), Black);
            return root;
        }

        /// <summary>An ant hill of needles.</summary>
        public static GameObject AntHill(Transform parent, int seed)
        {
            var root = Root("Ameisenhaufen", parent);
            var k = new MeshKit();
            var rnd = new System.Random(seed);
            k.Mound(0, Vector3.down * 0.05f, 0.55f + (float)rnd.NextDouble() * 0.35f, 0.45f + (float)rnd.NextDouble() * 0.3f, 6, 16, 0.14f, seed, 1.2f);
            k.Make("Hügel", root.transform, Tex("Mat_dry_decay_leaves"));
            return root;
        }

        /// <summary>A pile of leaves (or needles) beside the trail.</summary>
        public static GameObject LeafPile(Transform parent, int seed, bool needles)
        {
            var root = Root("Laubhaufen", parent);
            var k = new MeshKit();
            var rnd = new System.Random(seed);
            k.Mound(0, Vector3.down * 0.04f, 0.7f + (float)rnd.NextDouble() * 0.6f, 0.14f + (float)rnd.NextDouble() * 0.12f, 4, 14, 0.25f, seed, 0.9f);
            string key = needles ? "pile:needles" : "pile:leaves";
            if (!mats.TryGetValue(key, out var mat) || mat == null)
            {
                mat = new Material(Tex(needles ? "Mat_dry_decay_leaves" : "Mat_forest_leaves_02")) { name = "Laub" };
                mat.SetColor("_BaseColor", needles ? new Color(0.55f, 0.48f, 0.42f) : new Color(0.62f, 0.52f, 0.40f)); // scans are bright: shade like the forest floor
                mats[key] = mat;
            }
            k.Make("Haufen", root.transform, mat);
            return root;
        }

        /// <summary>A cluster of mushrooms (autumn): brown caps, now and then a red fly agaric.</summary>
        public static GameObject Mushrooms(Transform parent, int seed)
        {
            var root = Root("Pilze", parent);
            var rnd = new System.Random(seed);
            bool agaric = rnd.NextDouble() < 0.25;
            var k = new MeshKit();
            int n = 3 + rnd.Next(5);
            for (int i = 0; i < n; i++)
            {
                var p = new Vector3(((float)rnd.NextDouble() - 0.5f) * 0.5f, 0f, ((float)rnd.NextDouble() - 0.5f) * 0.5f);
                float h = (agaric ? 0.1f : 0.05f) + (float)rnd.NextDouble() * 0.07f, cap = h * (agaric ? 0.7f : 0.8f);
                k.Cylinder(0, p + Vector3.down * 0.02f, p + Vector3.up * h, 0.012f + cap * 0.12f, 0.01f + cap * 0.1f, 6);
                k.Mound(1, p + Vector3.up * (h - cap * 0.15f), cap, cap * 0.55f, 3, 10, 0.05f, rnd.Next());
            }
            k.Make("Pilze", root.transform, Paint("stem", new Color(0.86f, 0.82f, 0.72f), 0.2f),
                   agaric ? Paint("agaric", new Color(0.70f, 0.08f, 0.05f), 0.45f) : Paint("cap", new Color(0.42f, 0.28f, 0.16f), 0.35f));
            return root;
        }

        /// <summary>Flat spot on the ground (mud, puddle, leaves, needles, rooted-up soil): a cut-out decal.</summary>
        public static GameObject Spot(Transform parent, string kind, float size, float yaw)
        {
            var root = Root("Fleck", parent);
            var k = new MeshKit();
            float h = size * 0.5f;
            var q = Quaternion.Euler(0f, yaw, 0f);
            k.QuadUv(0, q * new Vector3(-h, 0f, -h), q * new Vector3(-h, 0f, h), q * new Vector3(h, 0f, h), q * new Vector3(h, 0f, -h),
                     new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
            var go = k.Make(kind, root.transform, Tex("Decal_" + kind));
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return root;
        }

        // ---------------------------------------------------------------- field and forest edge
        /// <summary>Round bales in a loose group: white silage wrap, or straw.</summary>
        public static GameObject HayBales(Transform parent, int seed)
        {
            var root = Root("Heuballen", parent);
            var rnd = new System.Random(seed);
            bool silage = rnd.NextDouble() < 0.6;
            var k = new MeshKit();
            int n = 3 + rnd.Next(6);
            for (int i = 0; i < n; i++)
            {
                var p = new Vector3(((float)rnd.NextDouble() - 0.5f) * 14f, 0.6f, ((float)rnd.NextDouble() - 0.5f) * 8f);
                var q = Quaternion.Euler(0f, (float)rnd.NextDouble() * 180f, 0f);
                k.Cylinder(0, p + q * new Vector3(-0.6f, 0f, 0f), p + q * new Vector3(0.6f, 0f, 0f), 0.66f, 0.66f, 16, 0, 1f, 0.03f, rnd.Next());
            }
            k.Make("Ballen", root.transform, silage ? Paint("silage", new Color(0.82f, 0.84f, 0.80f), 0.62f) : Paint("straw", new Color(0.70f, 0.58f, 0.33f), 0.12f));
            return root;
        }

        /// <summary>Wooden power / phone line along a polyline (local points): poles, crossarms, sagging wires.</summary>
        public static GameObject PowerLine(Transform parent, List<Vector3> poles, int seed)
        {
            var root = Root("Leitung", parent);
            var k = new MeshKit();
            var w = new MeshKit();
            const float H = 8.5f;
            for (int i = 0; i < poles.Count; i++)
            {
                var p = poles[i];
                Vector3 dir = (i + 1 < poles.Count ? poles[i + 1] - p : p - poles[i - 1]); dir.y = 0f; dir.Normalize();
                Vector3 across = Vector3.Cross(Vector3.up, dir);
                k.Cylinder(0, p + Vector3.down * 0.6f, p + Vector3.up * H, 0.14f, 0.1f, 8, -1, 0.8f);
                k.Beam(1, p + Vector3.up * (H - 0.4f) - across * 0.8f, p + Vector3.up * (H - 0.4f) + across * 0.8f, 0.09f, 0.11f, Vector3.up, 0.8f);
                foreach (float a in new[] { -0.65f, 0f, 0.65f })
                    k.Cylinder(2, p + Vector3.up * (H - 0.33f) + across * a, p + Vector3.up * (H - 0.18f) + across * a, 0.035f, 0.03f, 6);
                if (i + 1 < poles.Count)
                {
                    var q = poles[i + 1];
                    Vector3 dq = q - p; dq.y = 0f; dq.Normalize();
                    Vector3 acrossQ = Vector3.Cross(Vector3.up, dq);
                    foreach (float a in new[] { -0.65f, 0f, 0.65f })
                    {
                        Vector3 s0 = p + Vector3.up * (H - 0.18f) + across * a, s1 = q + Vector3.up * (H - 0.18f) + acrossQ * a;
                        const int segs = 8;
                        for (int j = 0; j < segs; j++)
                        {
                            float t0 = j / (float)segs, t1 = (j + 1) / (float)segs;
                            Vector3 a0 = Vector3.Lerp(s0, s1, t0) + Vector3.down * 0.7f * 4f * t0 * (1f - t0);
                            Vector3 a1 = Vector3.Lerp(s0, s1, t1) + Vector3.down * 0.7f * 4f * t1 * (1f - t1);
                            w.Beam(0, a0, a1, 0.014f, 0.014f, Vector3.up);
                        }
                    }
                }
            }
            k.Make("Masten", root.transform, Tex("Mat_rough_wood"), Rough, Paint("insulator", new Color(0.85f, 0.85f, 0.82f), 0.8f));
            var wr = w.Make("Drähte", root.transform, Black);
            wr.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return root;
        }

        /// <summary>Pasture fence along local points: round posts every ~3 m and two wires, or split rails.</summary>
        public static GameObject Fence(Transform parent, List<Vector3> line, int seed)
        {
            var root = Root("Zaun", parent);
            var rnd = new System.Random(seed);
            bool rails = rnd.NextDouble() < 0.35;
            var k = new MeshKit();
            var w = new MeshKit();
            for (int i = 0; i < line.Count; i++)
            {
                var p = line[i];
                k.Cylinder(0, p + Vector3.down * 0.3f, p + Vector3.up * 1.15f, 0.055f, 0.05f, 7, 1, 0.8f, 0.1f, rnd.Next(), new Vector3(0.5f, 0.5f, 0.05f));
                if (i + 1 >= line.Count) continue;
                var q = line[i + 1];
                foreach (float y in new[] { 0.55f, 0.95f })
                    if (rails) k.Beam(0, p + Vector3.up * y, q + Vector3.up * y, 0.07f, 0.07f, Vector3.up, 0.8f);
                    else w.Beam(0, p + Vector3.up * y, q + Vector3.up * y, 0.008f, 0.008f, Vector3.up);
            }
            k.Make("Pfosten", root.transform, Bark, Ends);
            if (!rails) w.Make("Draht", root.transform, Metal).GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return root;
        }

        /// <summary>Small field barn: plank walls, corrugated iron gable roof, a door (faces +z).</summary>
        public static GameObject FieldBarn(Transform parent, int seed)
        {
            var root = Root("Feldscheune", parent);
            var k = new MeshKit();
            float W = 5f, D = 4f, H = 2.8f, R = 1.6f;
            k.Box(0, new Vector3(0f, H * 0.5f - 0.4f, 0f), new Vector3(W, H + 0.8f, D), Quaternion.identity, 0.5f);
            // gable ends
            for (int s = -1; s <= 1; s += 2)
            {
                float z = s * D * 0.5f;
                // front faces: +z runs −x → +x, −z runs +x → −x (as the box faces do)
                if (s > 0) k.Quad(0, new Vector3(-W * 0.5f, H, z), new Vector3(W * 0.5f, H, z), new Vector3(0f, H + R, z), new Vector3(0f, H + R, z), 0.5f);
                else k.Quad(0, new Vector3(W * 0.5f, H, z), new Vector3(-W * 0.5f, H, z), new Vector3(0f, H + R, z), new Vector3(0f, H + R, z), 0.5f);
            }
            k.Box(1, new Vector3(0f, 1.05f, D * 0.5f + 0.02f), new Vector3(2.2f, 2.1f, 0.04f), Quaternion.identity, 0.6f); // door
            var roof = new MeshKit();
            float ang = Mathf.Atan2(R, W * 0.5f) * Mathf.Rad2Deg, half = Mathf.Sqrt(R * R + W * W * 0.25f) * 0.5f + 0.3f;
            for (int s = -1; s <= 1; s += 2)
                roof.Box(0, new Vector3(s * W * 0.25f, H + R * 0.5f + 0.04f, 0f), new Vector3(half * 2f, 0.05f, D + 0.6f), Quaternion.Euler(0f, 0f, -s * ang), 0.5f);
            k.Make("Wände", root.transform, Tex("Mat_weathered_plank_siding"), Tex("Mat_rough_wood"));
            roof.Make("Dach", root.transform, Tex("Mat_rusty_corrugated_iron"));
            return root;
        }

        // ---------------------------------------------------------------- landmarks
        /// <summary>Wayside cross (Wegkreuz) with a little gable roof on a stone base.</summary>
        public static GameObject WaysideCross(Transform parent, int seed)
        {
            var root = Root("Wegkreuz", parent);
            var st = new MeshKit();
            st.Box(0, new Vector3(0f, 0.2f, 0f), new Vector3(0.6f, 0.6f, 0.45f), Quaternion.identity, 1.2f);
            st.Make("Sockel", root.transform, Tex("Mat_mossy_rock"));
            var k = new MeshKit();
            k.Box(0, new Vector3(0f, 1.8f, 0f), new Vector3(0.14f, 2.6f, 0.12f), Quaternion.identity, 0.8f);
            k.Box(0, new Vector3(0f, 2.55f, 0f), new Vector3(1.1f, 0.13f, 0.12f), Quaternion.identity, 0.8f);
            for (int s = -1; s <= 1; s += 2) k.Box(0, new Vector3(s * 0.2f, 3.2f, 0.1f), new Vector3(0.48f, 0.03f, 0.4f), Quaternion.Euler(0f, 0f, -s * 32f), 0.8f);
            k.Make("Kreuz", root.transform, Tex("Mat_rough_wood"));
            var plate = new MeshKit();
            plate.Box(0, new Vector3(0f, 1.55f, 0.065f), new Vector3(0.3f, 0.2f, 0.01f), Quaternion.identity);
            plate.Make("Tafel", root.transform, Paint("brass", new Color(0.55f, 0.45f, 0.22f), 0.6f, 0.8f));
            return root;
        }

        /// <summary>Small field chapel: plastered walls, a tiled gable roof, door and a cross on the ridge.</summary>
        public static GameObject Chapel(Transform parent, int seed)
        {
            var root = Root("Kapelle", parent);
            var k = new MeshKit();
            float W = 3.2f, D = 4.4f, H = 3.1f, R = 1.9f;
            k.Box(0, new Vector3(0f, H * 0.5f - 0.4f, 0f), new Vector3(W, H + 0.8f, D), Quaternion.identity, 0.5f);
            for (int s = -1; s <= 1; s += 2)
            {
                float z = s * D * 0.5f;
                // front faces: +z runs −x → +x, −z runs +x → −x (as the box faces do)
                if (s > 0) k.Quad(0, new Vector3(-W * 0.5f, H, z), new Vector3(W * 0.5f, H, z), new Vector3(0f, H + R, z), new Vector3(0f, H + R, z), 0.5f);
                else k.Quad(0, new Vector3(W * 0.5f, H, z), new Vector3(-W * 0.5f, H, z), new Vector3(0f, H + R, z), new Vector3(0f, H + R, z), 0.5f);
            }
            k.Box(1, new Vector3(0f, 1.0f, D * 0.5f + 0.02f), new Vector3(1.0f, 2.0f, 0.05f), Quaternion.identity, 0.8f);
            k.Make("Mauern", root.transform, Tex("Mat_painted_plaster_wall"), Tex("Mat_weathered_planks"));
            var roof = new MeshKit();
            float ang = Mathf.Atan2(R, W * 0.5f) * Mathf.Rad2Deg, half = Mathf.Sqrt(R * R + W * W * 0.25f) * 0.5f + 0.25f;
            for (int s = -1; s <= 1; s += 2)
                roof.Box(0, new Vector3(s * W * 0.25f, H + R * 0.5f + 0.05f, 0f), new Vector3(half * 2f, 0.08f, D + 0.5f), Quaternion.Euler(0f, 0f, -s * ang), 0.8f);
            roof.Box(1, new Vector3(0f, H + R + 0.45f, D * 0.5f), new Vector3(0.06f, 0.8f, 0.06f), Quaternion.identity);
            roof.Box(1, new Vector3(0f, H + R + 0.6f, D * 0.5f), new Vector3(0.4f, 0.06f, 0.06f), Quaternion.identity);
            roof.Make("Dach", root.transform, Paint("tiles", new Color(0.42f, 0.16f, 0.10f), 0.25f), Metal);
            return root;
        }

        /// <summary>A few broken walls of an old stone building, overgrown.</summary>
        public static GameObject Ruin(Transform parent, int seed)
        {
            var root = Root("Ruine", parent);
            var rnd = new System.Random(seed);
            var k = new MeshKit();
            float W = 7f, D = 5f;
            // four walls, each in pieces of random height with gaps
            for (int side = 0; side < 4; side++)
            {
                bool alongX = side % 2 == 0;
                float len = alongX ? W : D, off = (alongX ? D : W) * 0.5f * (side < 2 ? 1f : -1f);
                for (float t = -len * 0.5f; t < len * 0.5f - 0.3f;)
                {
                    float piece = 0.6f + (float)rnd.NextDouble() * 1.6f;
                    piece = Mathf.Min(piece, len * 0.5f - t);
                    float h = rnd.NextDouble() < 0.25 ? 0.3f : 0.6f + (float)rnd.NextDouble() * 2.4f;
                    var c = alongX ? new Vector3(t + piece * 0.5f, h * 0.5f - 0.3f, off) : new Vector3(off, h * 0.5f - 0.3f, t + piece * 0.5f);
                    k.Box(0, c, alongX ? new Vector3(piece, h + 0.6f, 0.55f) : new Vector3(0.55f, h + 0.6f, piece), Quaternion.identity, 0.6f);
                    t += piece + (rnd.NextDouble() < 0.2 ? 1.2f : 0f);
                }
            }
            k.Make("Mauern", root.transform, Tex("Mat_rustic_stone_wall"));
            return root;
        }

        /// <summary>Open forest shelter (Schutzhütte): posts, plank walls on three sides, a roof, a table set inside.</summary>
        public static GameObject Shelter(Transform parent, int seed)
        {
            var root = Root("Schutzhütte", parent);
            var k = new MeshKit();
            float W = 3.6f, D = 2.8f;
            foreach (var p in new[] { new Vector3(-W / 2, 0, -D / 2), new Vector3(W / 2, 0, -D / 2), new Vector3(-W / 2, 0, D / 2), new Vector3(W / 2, 0, D / 2) })
                k.Box(0, p + Vector3.up * (p.z > 0 ? 1.15f : 1.45f), new Vector3(0.14f, p.z > 0 ? 2.7f : 3.3f, 0.14f), Quaternion.identity, 0.8f);
            k.Box(1, new Vector3(0f, 1.2f, -D / 2), new Vector3(W, 2.6f, 0.04f), Quaternion.identity, 0.6f);                         // back wall
            for (int s = -1; s <= 1; s += 2) k.Box(1, new Vector3(s * W / 2, 0.9f, 0f), new Vector3(0.04f, 1.2f, D), Quaternion.identity, 0.6f); // side walls, half height
            k.Box(1, new Vector3(0f, 2.75f, 0f), new Vector3(W + 0.6f, 0.05f, D + 0.9f), Quaternion.Euler(-11f, 0f, 0f), 0.6f);       // roof
            k.Make("Hütte", root.transform, Tex("Mat_rough_wood"), Planks);
            Place("outdoor_table_chair_set_01", root.transform, new Vector3(0f, 0f, -0.2f), 90f);
            return root;
        }

        /// <summary>Lookout: a bench behind a wooden railing (railing along x in front).</summary>
        public static GameObject Lookout(Transform parent, int seed)
        {
            var root = Root("Aussicht", parent);
            var k = new MeshKit();
            for (float x = -3f; x <= 3.01f; x += 1.5f) k.Box(0, new Vector3(x, 0.45f, 1.2f), new Vector3(0.1f, 1.3f, 0.1f), Quaternion.identity, 0.8f);
            k.Beam(0, new Vector3(-3.1f, 1.0f, 1.2f), new Vector3(3.1f, 1.0f, 1.2f), 0.09f, 0.11f, Vector3.up, 0.8f);
            k.Beam(0, new Vector3(-3.1f, 0.55f, 1.2f), new Vector3(3.1f, 0.55f, 1.2f), 0.06f, 0.08f, Vector3.up, 0.8f);
            k.Make("Geländer", root.transform, Rough);
            Place("painted_wooden_bench", root.transform, Vector3.zero, 0f);
            return root;
        }

        // ---------------------------------------------------------------- near villages
        /// <summary>Round traffic sign on a pole: "Verbot für Fahrzeuge aller Art" with "Forstwirtschaftlicher Verkehr frei".</summary>
        public static GameObject TrafficSign(Transform parent)
        {
            var root = Root("Verkehrszeichen", parent);
            var k = new MeshKit();
            k.Cylinder(0, new Vector3(0f, -0.3f, 0f), new Vector3(0f, 2.3f, 0f), 0.03f, 0.03f, 8);
            k.Make("Mast", root.transform, Metal);
            var face = new MeshKit();
            face.QuadUv(0, new Vector3(-0.3f, 1.8f, 0.035f), new Vector3(0.3f, 1.8f, 0.035f), new Vector3(0.3f, 2.4f, 0.035f), new Vector3(-0.3f, 2.4f, 0.035f),
                        new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1));
            face.Make("Schild", root.transform, SignMaterial(false));
            var plate = new MeshKit();
            plate.Box(0, new Vector3(0f, 1.6f, 0.035f), new Vector3(0.6f, 0.26f, 0.01f), Quaternion.identity);
            plate.Make("Zusatz", root.transform, Paint("signwhite", new Color(0.9f, 0.9f, 0.88f), 0.5f));
            Label(root.transform, "Forstwirtschaftlicher\nVerkehr frei", new Vector3(0f, 1.6f, 0.042f), Quaternion.identity, 0.56f, 0.22f, new Color(0.05f, 0.05f, 0.05f), TextAnchor.MiddleCenter, 0.38f);
            return root;
        }

        /// <summary>Bus stop: the round "H" sign on a pole with a timetable box.</summary>
        public static GameObject BusStop(Transform parent)
        {
            var root = Root("Haltestelle", parent);
            var k = new MeshKit();
            k.Cylinder(0, new Vector3(0f, -0.3f, 0f), new Vector3(0f, 2.6f, 0f), 0.04f, 0.04f, 8);
            k.Box(1, new Vector3(0f, 1.45f, 0.06f), new Vector3(0.4f, 0.5f, 0.06f), Quaternion.identity);
            k.Make("Mast", root.transform, Metal, Paint("timetable", new Color(0.85f, 0.82f, 0.2f), 0.4f));
            var face = new MeshKit();
            face.QuadUv(0, new Vector3(-0.25f, 2.1f, 0.045f), new Vector3(0.25f, 2.1f, 0.045f), new Vector3(0.25f, 2.6f, 0.045f), new Vector3(-0.25f, 2.6f, 0.045f),
                        new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1));
            face.Make("Schild", root.transform, SignMaterial(true));
            return root;
        }

        private static Texture2D busSign, noEntrySign;

        // one cut-out Lit material per sign face (a retextured copy of the decal material), shared by all signs
        private static Material SignMaterial(bool bus)
        {
            string key = bus ? "sign:bus" : "sign:noentry";
            if (mats.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Tex("Decal_puddle")) { name = bus ? "Haltestelle" : "Verbot" };
            m.SetTexture("_BaseMap", RoundSign(bus)); m.SetFloat("_Smoothness", 0.5f); m.DisableKeyword("_NORMALMAP");
            return mats[key] = m;
        }

        // German road sign faces drawn once: "H" (yellow disc, green ring and letter) or the red ring on white
        private static Texture2D RoundSign(bool bus)
        {
            if (bus && busSign != null) return busSign;
            if (!bus && noEntrySign != null) return noEntrySign;
            const int n = 256;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = bus ? "H" : "Verbot", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f, r = Mathf.Sqrt(dx * dx + dy * dy);
                    Color32 c = new Color32(0, 0, 0, 0);
                    if (r < 0.98f)
                    {
                        if (bus)
                        {
                            c = r > 0.82f ? new Color32(20, 120, 60, 255) : new Color32(240, 200, 20, 255);
                            bool h = (Mathf.Abs(dx + 0.3f) < 0.09f || Mathf.Abs(dx - 0.3f) < 0.09f) && Mathf.Abs(dy) < 0.5f || Mathf.Abs(dy) < 0.08f && Mathf.Abs(dx) < 0.3f;
                            if (h) c = new Color32(20, 120, 60, 255);
                        }
                        else c = r > 0.74f && r < 0.94f ? new Color32(200, 20, 20, 255) : r >= 0.94f ? new Color32(235, 235, 235, 255) : new Color32(240, 240, 238, 255);
                    }
                    px[y * n + x] = c;
                }
            t.SetPixels32(px); t.Apply(true, true);
            if (bus) busSign = t; else noEntrySign = t;
            return t;
        }

        /// <summary>Dog-waste bag dispenser: green box with a bin on a post.</summary>
        public static GameObject DogBags(Transform parent)
        {
            var root = Root("Hundekotbeutel", parent);
            var k = new MeshKit();
            k.Cylinder(0, new Vector3(0f, -0.3f, 0f), new Vector3(0f, 1.5f, 0f), 0.035f, 0.035f, 8);
            k.Make("Mast", root.transform, Metal);
            var b = new MeshKit();
            b.Box(0, new Vector3(0f, 1.3f, 0.08f), new Vector3(0.24f, 0.32f, 0.12f), Quaternion.identity);
            b.Box(0, new Vector3(0f, 0.75f, 0.1f), new Vector3(0.3f, 0.45f, 0.18f), Quaternion.identity);
            b.Box(1, new Vector3(0f, 1.3f, 0.141f), new Vector3(0.16f, 0.1f, 0.004f), Quaternion.identity);
            b.Make("Spender", root.transform, Paint("dogbags", new Color(0.12f, 0.36f, 0.20f), 0.45f), White);
            return root;
        }

        /// <summary>Garden fence of pickets along local points.</summary>
        public static GameObject PicketFence(Transform parent, List<Vector3> line)
        {
            var root = Root("Gartenzaun", parent);
            var k = new MeshKit();
            for (int i = 0; i + 1 < line.Count; i++)
            {
                Vector3 a = line[i], b = line[i + 1];
                k.Beam(0, a + Vector3.up * 0.3f, b + Vector3.up * 0.3f, 0.03f, 0.06f, Vector3.up, 1f);
                k.Beam(0, a + Vector3.up * 0.8f, b + Vector3.up * 0.8f, 0.03f, 0.06f, Vector3.up, 1f);
                int n = Mathf.Max(1, Mathf.RoundToInt((b - a).magnitude / 0.12f));
                for (int j = 0; j < n; j++)
                {
                    Vector3 p = Vector3.Lerp(a, b, (j + 0.5f) / n);
                    k.Box(0, p + Vector3.up * 0.45f, new Vector3(0.07f, 1.0f, 0.02f), Quaternion.LookRotation(b - a) * Quaternion.Euler(0f, 90f, 0f), 1f);
                }
            }
            k.Make("Zaun", root.transform, Paint("picket", new Color(0.78f, 0.77f, 0.72f), 0.25f));
            return root;
        }
    }
}
