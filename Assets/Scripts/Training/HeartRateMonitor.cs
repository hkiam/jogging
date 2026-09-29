using UnityEngine;
using UnityEngine.UI;
using Jogging.Locomotion;
using Jogging.Locomotion.Treadmill;
using Jogging.Profile;
using Jogging.UI;
using Jogging.World;

namespace Jogging.Training
{
    /// <summary>
    /// The heart rate of the run, from whichever source there is: a BLE heart rate sensor (strap, arm
    /// band, a watch that broadcasts) via the bridge (<see cref="MacBleBridgeTransport"/>); else the
    /// treadmill's hand grips (in its data, or its own heart rate service) — a strap always wins; or,
    /// for tests without hardware, command line
    /// <c>-hrsim</c> — a simulation that follows speed and incline with a lag. Heart rate is optional:
    /// without a sensor everything works as before. Shows pulse and zone in the HUD.
    /// Added at runtime by <see cref="TreadmillConnectUI"/>.
    /// </summary>
    public class HeartRateMonitor : MonoBehaviour
    {
        public static HeartRateMonitor Current { get; private set; }

        /// <summary>Seconds without a reading before the heart rate counts as gone.</summary>
        private const float Timeout = 5f;

        public int Bpm { get; private set; }
        public bool HasData => Time.unscaledTime - lastTime <= Timeout && Bpm > 0;
        public string Source { get; private set; } = "";

        /// <summary>Max heart rate of the active runner (default if not set).</summary>
        public static int MaxHr
        {
            get
            {
                var p = ProfileService.Instance != null ? ProfileService.Instance.Profile : null;
                return p == null ? HeartRateZones.DefaultMax : HeartRateZones.ForRunner(p.maxHeartRate, p.birthYear, System.DateTime.Now.Year);
            }
        }

        public int Zone => HasData ? HeartRateZones.Zone(Bpm, MaxHr) : 0;

        private MacBleBridgeTransport bridge;
        private float lastTime = -999f;
        private bool simulate;
        private float simBpm = 72f;
        private LocomotionRouter router;
        private TrackManager track;

        private GameObject panel;
        private Text label;
        private Image dot;

        private void Awake()
        {
            Current = this;
            simulate = System.Array.IndexOf(Jogging.Core.Args.All, "-hrsim") >= 0;
        }

        private void OnDestroy() { if (Current == this) Current = null; }

        private void OnEnable()
        {
            bridge = FindFirstObjectByType<MacBleBridgeTransport>();
            if (bridge == null) return;
            bridge.HeartRateReceived += OnMeasurement;
            bridge.GripHeartRateReceived += OnGripMeasurement;
            bridge.TreadmillDataReceived += OnTreadmillData;
        }

        private void OnDisable()
        {
            if (bridge == null) return;
            bridge.HeartRateReceived -= OnMeasurement;
            bridge.GripHeartRateReceived -= OnGripMeasurement;
            bridge.TreadmillDataReceived -= OnTreadmillData;
        }

        private float lastStrapTime = -999f;

        /// <summary>The strap reports no skin contact (shown in the HUD: moisten the strap).</summary>
        public bool NoContact => Time.unscaledTime - noContactTime < 3f;
        private float noContactTime = -999f;

        // Hand grips: only while no strap delivers (the strap is more accurate and works hands-free).
        private void Grip(int bpm)
        {
            if (bpm < 30 || bpm > 230 || Time.unscaledTime - lastStrapTime <= Timeout) return;
            Bpm = bpm; lastTime = Time.unscaledTime; Source = "Handsensor";
        }

        private void OnTreadmillData(byte[] d)
        {
            if (TreadmillFrames.TryDecode(bridge.Protocol, d, out var t, out _) && t.HeartRateBpm > 0) Grip(t.HeartRateBpm);
        }

        private void OnGripMeasurement(byte[] d)
        {
            if (HeartRateParser.TryParse(d, out int bpm)) Grip(bpm);
        }

        private void Start()
        {
            router = FindFirstObjectByType<LocomotionRouter>();
            track = FindFirstObjectByType<TrackManager>();
            BuildUI();
        }

        /// <summary>The connected strap belongs to another runner (its pulse is not recorded for this one).</summary>
        public bool ForeignStrap
        {
            get
            {
                var ps = ProfileService.Instance;
                if (ps == null || bridge == null) return false;
                var owner = ps.StrapOwner(bridge.HeartRateDeviceId);
                return owner != null && owner != ps.Profile;
            }
        }

        private void OnMeasurement(byte[] d)
        {
            if (ForeignStrap) return; // someone else's pulse must not end up in this runner's logbook
            // After a runner switch the bridge moves to the other strap: until it reports that one,
            // beats may still come from the previous runner's — only a known, wanted strap counts.
            if (bridge.HeartRateDeviceId == "" || (bridge.HeartRateWantedId != "" && bridge.HeartRateDeviceId != bridge.HeartRateWantedId)) return;
            if (!HeartRateParser.TryParse(d, out int bpm)) { if (HeartRateParser.NoContact(d)) noContactTime = Time.unscaledTime; return; }
            Bpm = bpm; lastTime = lastStrapTime = Time.unscaledTime; Source = "Pulsgurt";
        }

        private string runnerId;

        private void Update()
        {
            // Another runner: the previous runner's last reading must not count for them (logbook).
            string id = ProfileService.Instance != null && ProfileService.Instance.HasRunners ? ProfileService.Instance.Profile.id : "";
            if (id != runnerId) { if (runnerId != null) { Bpm = 0; lastTime = -999f; } runnerId = id; }
            if (simulate && !(bridge != null && bridge.HeartRateConnected))
            {
                // Rest ~70, +8.5 bpm per km/h, +2 per % incline; heart rate lags ~20 s behind.
                float kmh = track != null ? track.SpeedMps * 3.6f : 0f;
                float target = 70f + kmh * 8.5f + Mathf.Max(0f, track != null ? track.CurrentInclinePercent : 0f) * 2f;
                simBpm = Mathf.Lerp(simBpm, target, 1f - Mathf.Exp(-Time.deltaTime / 20f));
                Bpm = Mathf.RoundToInt(simBpm + Mathf.Sin(Time.time * 0.7f) * 1.5f);
                lastTime = Time.unscaledTime; Source = "Simulation";
            }
            Refresh();
        }

        // ------------------------------------------------------------------ HUD
        private static readonly Color[] ZoneColors =
        {
            new Color(0.62f, 0.66f, 0.74f), new Color(0.40f, 0.70f, 0.95f), new Color(0.35f, 0.80f, 0.45f),
            new Color(0.95f, 0.70f, 0.25f), new Color(0.92f, 0.35f, 0.32f),
        };

        public static Color ZoneColor(int zone) => zone >= 1 && zone <= 5 ? ZoneColors[zone - 1] : UiTheme.TextMuted;

        private void Refresh()
        {
            if (panel == null) return;
            // Only with a heart rate: without a strap the HUD stays as before (the search shows under Geräte).
            bool show = (HasData || NoContact) && !StartMenuUI.IsOpen;
            if (panel.activeSelf != show) panel.SetActive(show);
            if (!show) return;
            if (!HasData) { label.text = Jogging.Core.Loc.T("Pulsgurt: kein Hautkontakt – Gurt anfeuchten"); dot.color = UiTheme.TextMuted; return; }
            int z = Zone;
            label.text = Jogging.Core.Loc.F("Puls <b>{0}</b>  ·  Zone {1} {2}", Bpm, z, HeartRateZones.Name(z)) + (Source == "Simulation" ? "  (Sim.)" : Source == "Handsensor" ? "  (Hand)" : "");
            dot.color = ZoneColor(z);
        }

        private void BuildUI()
        {
            var canvasGo = new GameObject("Heart Rate Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 12;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            panel = UiTheme.Panel(canvasGo.transform);
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(1f, 1f);
            prt.anchoredPosition = new Vector2(-24f, -144f);
            prt.sizeDelta = new Vector2(480f, 44f);

            var dotGo = UiTheme.Panel(panel.transform, UiTheme.TextMuted);
            var drt = dotGo.GetComponent<RectTransform>();
            drt.anchorMin = drt.anchorMax = drt.pivot = new Vector2(0f, 0.5f);
            drt.anchoredPosition = new Vector2(16f, 0f); drt.sizeDelta = new Vector2(14f, 14f);
            dot = dotGo.GetComponent<Image>();

            label = UiTheme.Label(panel.transform, "", 19, UiTheme.TextPrimary, TextAnchor.MiddleLeft);
            var lrt = label.rectTransform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(40f, 0f); lrt.offsetMax = new Vector2(-10f, 0f);
            panel.SetActive(false);
        }
    }
}
