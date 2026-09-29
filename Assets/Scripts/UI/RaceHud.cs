using UnityEngine;
using UnityEngine.UI;
using Jogging.World;

namespace Jogging.UI
{
    /// <summary>
    /// Race overlay: live rank (Platz X/Y), overtake counter and rival gap, plus transient popups
    /// for race events (overtook a runner, caught the rival, got overtaken).
    /// </summary>
    public class RaceHud : MonoBehaviour
    {
        [SerializeField] private AiRunnerManager ai;

        private Text info;
        private Text popup;
        private float popupTimer;

        private void Start()
        {
            BuildUI();
            RaceMessages.OnMessage += ShowPopup;
        }

        private void OnDestroy()
        {
            RaceMessages.OnMessage -= ShowPopup;
        }

        private void ShowPopup(string msg)
        {
            if (popup == null) return;
            popup.text = msg;
            var c = popup.color; c.a = 1f; popup.color = c;
            popupTimer = 1.8f;
        }

        private void BuildUI()
        {
            var canvasGo = new GameObject("Race Canvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 11;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var panel = UiTheme.Panel(canvasGo.transform);
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 1f);
            prt.pivot = new Vector2(0.5f, 1f);
            prt.anchoredPosition = new Vector2(0f, -84f); // under the route progress bar
            prt.sizeDelta = new Vector2(560f, 44f);

            info = UiTheme.Label(panel.transform, "", 22, UiTheme.TextPrimary, TextAnchor.MiddleCenter, bold: true);
            UiTheme.Stretch(info.GetComponent<RectTransform>(), 8f);

            popup = UiTheme.Label(canvasGo.transform, "", 40, UiTheme.Accent, TextAnchor.MiddleCenter, bold: true);
            var poprt = popup.GetComponent<RectTransform>();
            poprt.anchorMin = poprt.anchorMax = new Vector2(0.5f, 0.5f);
            poprt.pivot = new Vector2(0.5f, 0.5f);
            poprt.anchoredPosition = new Vector2(0f, 180f);
            poprt.sizeDelta = new Vector2(900f, 60f);
            var pc = popup.color; pc.a = 0f; popup.color = pc;
        }

        private void Update()
        {
            if (info == null) return;

            if (ai == null)
            {
                if (info.text.Length > 0) info.text = "";
            }
            else
            {
            string rival = ai.HasRival
                ? (ai.RivalGapMeters >= 0f
                    ? Jogging.Core.Loc.F("   •   Rivale +{0}", Jogging.Core.Units.FmtShort(ai.RivalGapMeters))
                    : Jogging.Core.Loc.F("   •   Rivale {0}", Jogging.Core.Units.FmtShort(ai.RivalGapMeters)))
                : "";
            info.text = Jogging.Core.Loc.F("Platz {0}/{1}   •   Überholt: {2}{3}", ai.PlayerRank, ai.FieldSize, ai.OvertakenCount, rival);
            }

            if (popupTimer > 0f)
            {
                popupTimer -= Time.deltaTime;
                var c = popup.color; c.a = Mathf.Clamp01(popupTimer / 1.8f); popup.color = c;
            }
        }
    }
}
