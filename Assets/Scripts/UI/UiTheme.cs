using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace Jogging.UI
{
    /// <summary>
    /// Shared visual language for all in-game panels: a single color palette, a runtime-generated
    /// rounded-rectangle sprite (9-sliced, so corners stay crisp at any size) and helpers for
    /// themed panels, buttons and labels. Keeps every panel consistent without external assets.
    /// (Uses legacy uGUI Text so it renders reliably; can be swapped to TextMeshPro later.)
    /// </summary>
    public static class UiTheme
    {
        // Palette
        public static readonly Color PanelBg     = new Color(0.10f, 0.12f, 0.16f, 0.92f);
        public static readonly Color PanelSoft   = new Color(0.16f, 0.19f, 0.24f, 0.95f);
        public static readonly Color TextPrimary = new Color(0.96f, 0.97f, 0.99f, 1f);
        public static readonly Color TextMuted   = new Color(0.72f, 0.77f, 0.84f, 1f);
        public static readonly Color Accent      = new Color(0.30f, 0.66f, 0.98f, 1f); // blue
        public static readonly Color Success     = new Color(0.30f, 0.78f, 0.45f, 1f); // green
        public static readonly Color Danger      = new Color(0.90f, 0.36f, 0.34f, 1f);
        public static readonly Color ButtonBg    = new Color(0.24f, 0.28f, 0.34f, 1f);

        private static Sprite _rounded;
        private static Font _font;

        public static Font Font =>
            _font != null ? _font :
            (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                     ?? Resources.GetBuiltinResource<Font>("Arial.ttf"));

        /// <summary>A white 9-sliced rounded-rectangle sprite (reused everywhere).</summary>
        public static Sprite Rounded()
        {
            if (_rounded != null) return _rounded;
            const int size = 48, r = 14;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float a = CornerAlpha(x, y, size, r);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            _rounded = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f,
                0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
            return _rounded;
        }

        private static float CornerAlpha(int x, int y, int size, int r)
        {
            float cx = Mathf.Clamp(x, r, size - 1 - r);
            float cy = Mathf.Clamp(y, r, size - 1 - r);
            float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            return Mathf.Clamp01(r - d + 0.5f); // ~1px anti-aliased edge
        }

        // ---- builders ----
        public static GameObject Panel(Transform parent, Color? color = null)
        {
            var go = new GameObject("Panel", typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = Rounded();
            img.type = Image.Type.Sliced;
            img.color = color ?? PanelBg;
            return go;
        }

        public static Text Label(Transform parent, string text, int size, Color? color = null,
            TextAnchor align = TextAnchor.MiddleLeft, bool bold = false)
        {
            var go = new GameObject("Label", typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = Font; t.fontSize = size; t.alignment = align;
            t.color = color ?? TextPrimary;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.text = Jogging.Core.Loc.T(text); // German in the code, English from Loc when chosen
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.35f);
            outline.effectDistance = new Vector2(1f, -1f);
            return t;
        }

        public static Button Button(Transform parent, string label, UnityAction onClick,
            Color? accent = null, int fontSize = 22)
        {
            var go = new GameObject("Button", typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = Rounded();
            img.type = Image.Type.Sliced;
            img.color = accent ?? ButtonBg;

            var btn = go.GetComponent<Button>();
            var baseCol = accent ?? ButtonBg;
            var cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            cb.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            cb.selectedColor = Color.white;
            cb.fadeDuration = 0.08f;
            btn.colors = cb;
            img.color = baseCol;
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            var t = Label(go.transform, label, fontSize, TextPrimary, TextAnchor.MiddleCenter, bold: true);
            var rt = t.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return btn;
        }

        /// <summary>Convenience to stretch a RectTransform to fill its parent with padding.</summary>
        public static void Stretch(RectTransform rt, float pad = 0f)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pad, pad); rt.offsetMax = new Vector2(-pad, -pad);
        }
    }
}
