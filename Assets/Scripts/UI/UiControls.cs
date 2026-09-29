using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Jogging.UI
{
    /// <summary>
    /// Themed form controls on top of <see cref="UiTheme"/>: labelled slider rows with a live value,
    /// on/off toggles, text fields, and small layout helpers for top-anchored rows.
    /// </summary>
    public static class UiControls
    {
        /// <summary>Place a rect in its parent: anchored top-left at (x, y) with size (w, h). w &lt;= 0 → stretch minus |w|.</summary>
        public static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            bool stretch = w <= 0f;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(stretch ? 1f : 0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(stretch ? w - x : w, h);
        }

        public static Text Label(Transform parent, string text, int size, float x, float y, float w, float h,
            Color? color = null, TextAnchor align = TextAnchor.MiddleLeft, bool bold = false)
        {
            var t = UiTheme.Label(parent, text, size, color, align, bold);
            Place(t.rectTransform, x, y, w, h);
            return t;
        }

        public static Button Button(Transform parent, string text, UnityAction onClick, float x, float y, float w, float h = 48f, Color? c = null, int font = 22)
        {
            var b = UiTheme.Button(parent, text, onClick, c, font);
            Place(b.GetComponent<RectTransform>(), x, y, w, h);
            return b;
        }

        /// <summary>Row: caption left, slider middle, value text right. Returns the slider.</summary>
        public static Slider SliderRow(Transform parent, string caption, float min, float max, float value, bool whole,
            System.Func<float, string> format, UnityAction<float> onChange, float x, float y, float w)
        {
            Label(parent, caption, 20, x, y, 210f, 40f, UiTheme.TextMuted);
            var go = DefaultControls.CreateSlider(new DefaultControls.Resources());
            go.transform.SetParent(parent, false);
            Place(go.GetComponent<RectTransform>(), x + 215f, y - 12f, w - 215f - 110f, 18f);
            var s = go.GetComponent<Slider>();
            Style(s);
            s.minValue = min; s.maxValue = max; s.wholeNumbers = whole; s.value = value;
            var val = Label(parent, format(value), 20, x + w - 100f, y, 100f, 40f, UiTheme.TextPrimary, TextAnchor.MiddleRight, bold: true);
            s.onValueChanged.AddListener(v => { val.text = Jogging.Core.Loc.T(format(v)); onChange(v); });
            return s;
        }

        private static void Style(Slider s)
        {
            var bg = s.transform.Find("Background")?.GetComponent<Image>();
            if (bg != null) { bg.sprite = UiTheme.Rounded(); bg.type = Image.Type.Sliced; bg.color = UiTheme.ButtonBg; }
            var fill = s.fillRect != null ? s.fillRect.GetComponent<Image>() : null;
            if (fill != null) { fill.sprite = UiTheme.Rounded(); fill.type = Image.Type.Sliced; fill.color = UiTheme.Accent; }
            var knob = s.handleRect != null ? s.handleRect.GetComponent<Image>() : null;
            if (knob != null) { knob.sprite = UiTheme.Rounded(); knob.color = Color.white; s.handleRect.sizeDelta = new Vector2(24f, 0f); }
        }

        /// <summary>Two-state button ("Rundkurs" / "Strecke"); returns a setter to update it from code.</summary>
        public static System.Action<bool> Toggle(Transform parent, string caption, string on, string off, bool value,
            UnityAction<bool> onChange, float x, float y, float w)
        {
            Label(parent, caption, 20, x, y, 210f, 40f, UiTheme.TextMuted);
            bool state = value;
            Button btn = null;
            System.Action<bool> apply = v =>
            {
                state = v;
                btn.GetComponentInChildren<Text>().text = Jogging.Core.Loc.T(v ? on : off);
                btn.GetComponent<Image>().color = v ? UiTheme.Accent : UiTheme.ButtonBg;
            };
            btn = Button(parent, "", () => { apply(!state); onChange(state); }, x + 215f, y + 2f, w - 215f, 42f, null, 20);
            apply(value);
            return apply;
        }

        /// <summary>Row: caption left, [-] value [+] right. <paramref name="text"/> is re-read after every step.</summary>
        public static Text Stepper(Transform parent, string caption, System.Func<string> text, UnityAction minus, UnityAction plus,
            float x, float y, float w, float captionW = 260f)
        {
            Label(parent, caption, 20, x, y, captionW, 44f, UiTheme.TextMuted);
            var val = Label(parent, text(), 22, x + captionW + 64f, y, w - captionW - 128f, 44f, UiTheme.TextPrimary, TextAnchor.MiddleCenter, bold: true);
            Button(parent, "-", () => { minus(); val.text = Jogging.Core.Loc.T(text()); }, x + captionW, y + 2f, 56f, 42f);
            Button(parent, "+", () => { plus(); val.text = Jogging.Core.Loc.T(text()); }, x + w - 56f, y + 2f, 56f, 42f);
            return val; // for values that depend on others (refresh from outside)
        }

        public static InputField TextField(Transform parent, string value, float x, float y, float w, float h = 44f)
        {
            var go = DefaultControls.CreateInputField(new DefaultControls.Resources());
            go.transform.SetParent(parent, false);
            Place(go.GetComponent<RectTransform>(), x, y, w, h);
            var img = go.GetComponent<Image>();
            img.sprite = UiTheme.Rounded(); img.type = Image.Type.Sliced; img.color = new Color(0.92f, 0.94f, 0.97f, 1f);
            var f = go.GetComponent<InputField>();
            f.textComponent.font = UiTheme.Font; f.textComponent.fontSize = 22;
            if (f.placeholder is Text ph) { ph.font = UiTheme.Font; ph.fontSize = 22; }
            f.text = value;
            return f;
        }
    }
}
