using UnityEngine;
using UnityEngine.UI;
using Jogging.Core;
using Jogging.Locomotion;
using Jogging.Locomotion.Treadmill;
using Jogging.Profile;

namespace Jogging.UI
{
    /// <summary>
    /// Treadmill in the HUD — status only (dot + one line: speed and incline, or the connection
    /// state). Setup lives in the menu (Einstellungen → Laufband, see <see cref="StartMenuUI"/>).
    /// Applies the saved treadmill settings before the transport starts: connect on start and the
    /// control mode (<see cref="BeltControl"/>: the belt, or the route sets the incline); belt
    /// writes are only enabled when the app controls something.
    /// </summary>
    public class TreadmillConnectUI : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour transportBehaviour; // ITreadmillTransport
        [SerializeField] private TreadmillLocomotionSource treadmill;
        [SerializeField] private LocomotionRouter router;

        private ITreadmillTransport transport;
        private FitShowBeltControl fitShow;
        private TreadmillController ftms;
        private MacBleBridgeTransport bridge;

        private void OnDestroy()
        {
            if (bridge != null) bridge.DeviceConnected -= OnDeviceConnected;
            if (ProfileService.Instance != null) ProfileService.Instance.ActiveChanged -= OnRunnerChanged;
        }

        // Remember the family's treadmill; pair a free strap with the active runner.
        private void OnDeviceConnected(string kind, string id, string name)
        {
            var st = AppSettings.Current;
            if (kind == "tm")
            {
                if (st.treadmillId != id) { st.treadmillId = id; st.treadmillName = name; AppSettings.Save(); Debug.Log($"[Jogging] Laufband gemerkt: {name}"); }
                return;
            }
            var ps = ProfileService.Instance;
            if (ps == null || !ps.HasRunners) return;
            var me = ps.Profile;
            if (string.IsNullOrEmpty(me.hrDeviceId) && ps.StrapOwner(id) == null)
            {
                me.hrDeviceId = id; me.hrDeviceName = name;
                ps.SaveActive();
                Debug.Log($"[Jogging] Pulsgurt „{name}“ gehört jetzt {me.playerName}");
            }
        }

        // Another runner: their own strap (a strap of someone else is ignored by the monitor).
        private void OnRunnerChanged(ProfileData p)
        {
            if (bridge != null && bridge.HeartRateWanted) bridge.ConnectHeartRate(p.hrDeviceId);
        }
        private Text status;
        private Image dot;

        private void Awake()
        {
            transport = transportBehaviour as ITreadmillTransport;
            fitShow = FindFirstObjectByType<FitShowBeltControl>();
            bridge = transport as MacBleBridgeTransport;
            if (bridge != null) bridge.DeviceConnected += OnDeviceConnected;
            if (ProfileService.Instance != null) ProfileService.Instance.ActiveChanged += OnRunnerChanged;
            ftms = FindFirstObjectByType<TreadmillController>();
            if (GetComponent<Training.HeartRateMonitor>() == null) gameObject.AddComponent<Training.HeartRateMonitor>();
            Apply(sceneStart: true); // Awake runs before the transport's Start (which connects)
        }

        /// <summary>
        /// Push the app settings into the control authority and the transport. At a scene start the mode
        /// always comes from the settings (no workout runs yet — nothing of a previous scene carries
        /// over); later a running workout keeps control when the settings change.
        /// </summary>
        public static void Apply(bool sceneStart = false)
        {
            var st = AppSettings.Current;
            if (sceneStart) BeltControl.Workout = null;
            BeltControl.SpeedAllowed = st.beltSpeed;
            if (sceneStart || BeltControl.Mode != ControlMode.Workout)
                BeltControl.Mode = st.beltIncline ? ControlMode.Route : ControlMode.Treadmill;
            var bridge = FindFirstObjectByType<MacBleBridgeTransport>();
            if (bridge != null)
            {
                bridge.ConnectOnStart = st.treadmillAutoConnect;
                var who = ProfileService.Instance != null ? ProfileService.Instance.Profile : null;
                bridge.SetPreferred(st.treadmillId, who != null ? who.hrDeviceId : "");
                bridge.ConnectHeartRateOnStart = st.heartRateAutoConnect;
                bridge.AllowBeltControl = BeltControl.Mode != ControlMode.Treadmill;
            }
        }

        private void Start() => BuildUI();

        private void BuildUI()
        {
            var canvasGo = new GameObject("Treadmill Canvas", typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 12;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var panel = UiTheme.Panel(canvasGo.transform);
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(1f, 1f);
            prt.anchoredPosition = new Vector2(-24f, -92f);
            prt.sizeDelta = new Vector2(480f, 44f);

            var dotGo = UiTheme.Panel(panel.transform, UiTheme.Danger);
            var drt = dotGo.GetComponent<RectTransform>();
            drt.anchorMin = drt.anchorMax = drt.pivot = new Vector2(0f, 0.5f);
            drt.anchoredPosition = new Vector2(16f, 0f); drt.sizeDelta = new Vector2(14f, 14f);
            dot = dotGo.GetComponent<Image>();

            status = UiTheme.Label(panel.transform, "", 19, UiTheme.TextPrimary, TextAnchor.MiddleLeft);
            var srt = status.rectTransform;
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
            srt.offsetMin = new Vector2(40f, 0f); srt.offsetMax = new Vector2(-10f, 0f);
        }

        /// <summary>Who controls the belt right now, or what the control is doing.</summary>
        private string ControlText()
        {
            if (BeltControl.Mode == ControlMode.Treadmill) return Jogging.Core.Loc.T("du steuerst");
            string s = fitShow != null && fitShow.StateText != "" ? fitShow.StateText : ftms != null ? ftms.StateText : "";
            return Jogging.Core.Loc.T(s != "" ? s : BeltControl.Describe(BeltControl.Mode));
        }

        private static string Short(string s) => s.Length > 34 ? s.Substring(0, 34) + "…" : s;

        private void Update()
        {
            if (status == null) return;
            bool connected = transport != null && transport.IsConnected;
            bool using_ = router != null && router.UsingTreadmill;
            dot.color = using_ ? UiTheme.Success : (connected ? UiTheme.Accent : UiTheme.Danger);

            var bridge = transport as MacBleBridgeTransport;
            if (using_ && treadmill != null)
            {
                status.text = Jogging.Core.Loc.F("Band <b>{0}</b>  {1:0} %  ·  {2}", Jogging.Core.Units.FmtSpeed(treadmill.SpeedMps * 3.6f), treadmill.InclinePercent, ControlText());
            }
            else if (connected) status.text = Jogging.Core.Loc.T("Laufband verbunden …");
            else if (!Core.Platform.HasBleBridge && !Jogging.Locomotion.Treadmill.NativeBle.Available && !(bridge != null && bridge.Simulator != null))
                status.text = Jogging.Core.Loc.T("Tempo mit − / +  (Laufband folgt)"); // iPad/Android: no Bluetooth to the belt yet
            else if (bridge != null && bridge.Wanted) status.text = bridge.StatusText.StartsWith("Bridge") || bridge.StatusText.StartsWith("UDP") ? Short(bridge.StatusText) : Jogging.Core.Loc.T("Suche Laufband …");
            else status.text = Jogging.Core.Loc.T("Tastatur  (Laufband: Einstellungen)");
        }
    }
}
