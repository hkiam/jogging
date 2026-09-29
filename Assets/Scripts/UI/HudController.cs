using UnityEngine;
using UnityEngine.UI;
using Jogging.Core;
using Jogging.Locomotion;
using Jogging.Profile;
using Jogging.World;

namespace Jogging.UI
{
    /// <summary>
    /// Fills the on-screen HUD each frame: time, distance, speed, elevation gain, current grade.
    /// Reads from <see cref="RunStats"/> (distance/time/elevation) and the
    /// <see cref="ILocomotionSource"/> (live speed). Uses legacy uGUI Text so the one-click
    /// scene builder works without a TextMeshPro import step; swap for TMP later if desired.
    /// </summary>
    public class HudController : MonoBehaviour
    {
        [SerializeField] private RunStats stats;
        [SerializeField] private MonoBehaviour locomotionSourceBehaviour; // ILocomotionSource

        [Header("Value labels")]
        [SerializeField] private Text nameLabel;
        [SerializeField] private Text timeValue;
        [SerializeField] private Text distanceValue;
        [SerializeField] private Text speedValue;
        [SerializeField] private Text elevationValue;
        [SerializeField] private Text gradeValue;

        [Header("Achievement toast")]
        [SerializeField] private Text toastLabel;
        [SerializeField] private float toastSeconds = 3.5f;

        /// <summary>The small value panel (hidden while the treadmill view is on).</summary>
        public GameObject ValuePanel => timeValue != null ? timeValue.transform.parent.gameObject : null;

        private ILocomotionSource locomotion;
        private float toastTimer;

        // Runtime-built route progress bar (top center) — works in existing scenes without rebuild.
        private TrackManager track;
        private GameObject progressRoot;
        private RectTransform progressFill;
        private Text progressText;
        private const float BarWidth = 560f;

        private void Awake()
        {
            locomotion = locomotionSourceBehaviour as ILocomotionSource;
        }

        private void OnEnable()
        {
            if (ProfileService.Instance != null)
                ProfileService.Instance.AchievementUnlocked += ShowToast;
        }

        private void OnDisable()
        {
            if (ProfileService.Instance != null)
            {
                ProfileService.Instance.AchievementUnlocked -= ShowToast;
                ProfileService.Instance.ActiveChanged -= OnRunnerChanged;
            }
        }

        private void OnRunnerChanged(ProfileData p) { if (nameLabel != null) nameLabel.text = p.playerName; }

        private void Start()
        {
            // the labels placed in the scene are German: in English they are swapped for their translation
            if (Jogging.Core.Loc.En) foreach (var t in GetComponentsInChildren<Text>(true)) t.text = Jogging.Core.Loc.T(t.text);
            // ProfileService may have subscribed after our OnEnable; (re)bind + set name here.
            if (ProfileService.Instance != null)
            {
                ProfileService.Instance.AchievementUnlocked -= ShowToast;
                ProfileService.Instance.AchievementUnlocked += ShowToast;
                if (nameLabel != null) nameLabel.text = ProfileService.Instance.Profile.playerName;
                ProfileService.Instance.ActiveChanged -= OnRunnerChanged;
                ProfileService.Instance.ActiveChanged += OnRunnerChanged;
            }
            if (toastLabel != null) toastLabel.text = "";

            track = FindFirstObjectByType<TrackManager>();
            BuildProgressBar();
        }

        // A top-center "gelaufen / gesamt" bar for finite routes (hidden on endless routes).
        private void BuildProgressBar()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                       ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

            progressRoot = new GameObject("RouteProgress", typeof(RectTransform));
            var rt = progressRoot.GetComponent<RectTransform>();
            rt.SetParent(transform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -20f);
            rt.sizeDelta = new Vector2(BarWidth, 52f);

            progressText = MakeChildText(rt, "Label", font, new Vector2(0f, 0f),
                new Vector2(BarWidth, 26f), TextAnchor.MiddleCenter, 22);

            var bg = new GameObject("Bar", typeof(Image));
            var bgrt = bg.GetComponent<RectTransform>();
            bgrt.SetParent(rt, false);
            bgrt.anchorMin = new Vector2(0.5f, 0f); bgrt.anchorMax = new Vector2(0.5f, 0f);
            bgrt.pivot = new Vector2(0.5f, 0f);
            bgrt.anchoredPosition = new Vector2(0f, 0f);
            bgrt.sizeDelta = new Vector2(BarWidth, 14f);
            bg.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);

            var fill = new GameObject("Fill", typeof(Image));
            progressFill = fill.GetComponent<RectTransform>();
            progressFill.SetParent(bgrt, false);
            progressFill.anchorMin = new Vector2(0f, 0f); progressFill.anchorMax = new Vector2(0f, 1f);
            progressFill.pivot = new Vector2(0f, 0.5f);
            progressFill.anchoredPosition = Vector2.zero;
            progressFill.sizeDelta = new Vector2(0f, 0f);
            fill.GetComponent<Image>().color = new Color(0.36f, 0.80f, 0.42f, 0.95f);

            progressRoot.SetActive(false);
        }

        private static Text MakeChildText(RectTransform parent, string name, Font font,
            Vector2 pos, Vector2 size, TextAnchor align, int fontSize)
        {
            var go = new GameObject(name, typeof(Text));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.5f, 1f); rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var t = go.GetComponent<Text>();
            t.font = font; t.fontSize = fontSize; t.alignment = align; t.color = Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return t;
        }

        private void ShowToast(string title)
        {
            if (toastLabel == null) return;
            toastLabel.text = "🏆  " + Jogging.Core.Loc.T(title);
            toastTimer = toastSeconds;
        }

        private void Update()
        {
            if (stats == null) return;

            if (timeValue != null) timeValue.text = FormatTime(stats.ElapsedSeconds);
            if (distanceValue != null) distanceValue.text = FormatDistance(stats.DistanceMeters);
            if (elevationValue != null) elevationValue.text = "+" + Jogging.Core.Units.FmtElev(stats.ElevationGainMeters);
            if (gradeValue != null) gradeValue.text = $"{stats.CurrentGradePercent:0.0} %";

            if (speedValue != null)
            {
                float kmh = (locomotion != null ? locomotion.SpeedMps : 0f) * 3.6f;
                speedValue.text = Jogging.Core.Units.FmtSpeed(kmh);
            }

            // Route progress (finite routes only).
            if (progressRoot != null)
            {
                float len = track != null ? track.RouteLength : 0f;
                if (len > 0f)
                {
                    if (!progressRoot.activeSelf) progressRoot.SetActive(true);
                    bool laps = track.Lapping;
                    float into = laps ? Mathf.Repeat(track.DistanceTraveled, len) : track.DistanceTraveled;
                    float p = Mathf.Clamp01(into / len);
                    progressFill.sizeDelta = new Vector2(BarWidth * p, 0f);
                    progressText.text = (laps ? Jogging.Core.Loc.F("Runde {0}  ·  ", track.Lap) : "") + (Jogging.Core.Units.Dist(into).ToString("0.00", Jogging.Core.Loc.Culture) + " / " + Jogging.Core.Units.FmtDist(len, "0.00"));
                }
                else if (progressRoot.activeSelf)
                {
                    progressRoot.SetActive(false);
                }
            }

            // Fade out the achievement toast.
            if (toastLabel != null && toastTimer > 0f)
            {
                toastTimer -= Time.deltaTime;
                var c = toastLabel.color;
                c.a = Mathf.Clamp01(toastTimer);
                toastLabel.color = c;
                if (toastTimer <= 0f) toastLabel.text = "";
            }
        }

        private static string FormatTime(float seconds)
        {
            int total = Mathf.FloorToInt(seconds);
            int m = total / 60;
            int s = total % 60;
            return $"{m:00}:{s:00}";
        }

        private static string FormatDistance(float meters)
        {
            return Jogging.Core.Units.FmtDistAuto(meters);
        }
    }
}
