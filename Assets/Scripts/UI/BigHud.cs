using UnityEngine;
using UnityEngine.UI;
using Jogging.Core;
using Jogging.Locomotion;
using Jogging.Training;
using Jogging.World;

namespace Jogging.UI
{
    /// <summary>
    /// Treadmill view: a large bar at the bottom with time, distance, speed and pace, incline and pulse,
    /// readable from the belt. Replaces the small HUD panel while on. Settings (Geräte → Anzeige) or H
    /// during the run (remembered). Added by <see cref="RunSessionUI"/> at runtime.
    /// </summary>
    public class BigHud : MonoBehaviour
    {
        private RunStats stats;
        private TrackManager track;
        private LocomotionRouter router;
        private GameObject bar, smallPanel;
        private Text time, dist, speed, pace, grade, pulse, pulseCap;
        private GameObject pulseCell;

        public static bool On => AppSettings.Current.bigHud;

        private void Start()
        {
            stats = FindFirstObjectByType<RunStats>();
            track = FindFirstObjectByType<TrackManager>();
            router = FindFirstObjectByType<LocomotionRouter>();
            var hud = FindFirstObjectByType<HudController>();
            smallPanel = hud != null ? hud.ValuePanel : null;
            Build();
        }

        private void Update()
        {
            bool menu = StartMenuUI.IsOpen || WorkshopUI.Active;
            if (!menu && Input.GetKeyDown(KeyCode.H))
            {
                AppSettings.Current.bigHud = !AppSettings.Current.bigHud;
                AppSettings.Save();
            }
            bool show = On && !menu && !(track != null && track.IsFinished);
            if (bar.activeSelf != show) bar.SetActive(show);
            if (smallPanel != null && smallPanel.activeSelf == (On && !menu)) smallPanel.SetActive(!(On && !menu));
            if (!show || stats == null) return;

            int s = Mathf.FloorToInt(stats.ElapsedSeconds);
            time.text = s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
            dist.text = Jogging.Core.Units.Dist(stats.DistanceMeters).ToString("0.00", Jogging.Core.Loc.Culture);
            float kmh = router != null ? router.SpeedMps * 3.6f : 0f;
            speed.text = Jogging.Core.Units.Speed(kmh).ToString("0.0", Jogging.Core.Loc.Culture);
            pace.text = kmh > 1f ? Jogging.Core.Units.FmtPace(1000f, 3600f / kmh) : "–";
            grade.text = $"{stats.CurrentGradePercent:0}";
            var hrm = HeartRateMonitor.Current;
            bool hr = hrm != null && hrm.HasData;
            if (pulseCell.activeSelf != hr) pulseCell.SetActive(hr);
            if (hr) { pulse.text = hrm.Bpm.ToString(); pulse.color = HeartRateMonitor.ZoneColor(hrm.Zone); pulseCap.text = Jogging.Core.Loc.F("PULS · ZONE {0}", hrm.Zone); }
        }

        private void Build()
        {
            var canvasGo = new GameObject("Big HUD Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            bar = UiTheme.Panel(canvasGo.transform, new Color(0.06f, 0.08f, 0.11f, 0.82f));
            var rt = bar.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f); rt.anchorMax = new Vector2(0.5f, 0f); rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 84f);
            rt.sizeDelta = new Vector2(1500f, 170f);

            const float cw = 300f;
            Text Cell(int i, string caption, out Text cap, float size = 84)
            {
                var cell = new GameObject("cell", typeof(RectTransform));
                cell.transform.SetParent(bar.transform, false);
                UiControls.Place(cell.GetComponent<RectTransform>(), i * cw, 0f, cw, 170f);
                cap = UiControls.Label(cell.transform, caption, 22, 0f, -12f, cw, 30f, UiTheme.TextMuted, TextAnchor.MiddleCenter, bold: true);
                return UiControls.Label(cell.transform, "", (int)size, 0f, -44f, cw, 110f, UiTheme.TextPrimary, TextAnchor.MiddleCenter, bold: true);
            }
            time = Cell(0, "ZEIT", out _);
            dist = Cell(1, Jogging.Core.Units.DistUnit.ToUpperInvariant(), out _);
            speed = Cell(2, Jogging.Core.Units.SpeedUnit.ToUpperInvariant(), out var spCap);
            pace = UiControls.Label(speed.transform.parent, "", 22, 0f, -140f, cw, 28f, UiTheme.TextMuted, TextAnchor.MiddleCenter);
            speed.rectTransform.sizeDelta = new Vector2(cw, 96f);
            grade = Cell(3, "STEIGUNG %", out _);
            pulse = Cell(4, "PULS", out pulseCap);
            pulseCell = pulse.transform.parent.gameObject;
            bar.SetActive(false);
        }
    }
}
