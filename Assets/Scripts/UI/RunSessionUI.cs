using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Jogging.Core;
using Jogging.Locomotion.Treadmill;
using Jogging.Profile;
using Jogging.World;

namespace Jogging.UI
{
    /// <summary>
    /// Runs the <see cref="SessionStateMachine"/> for the current run and shows it:
    ///   • feeds it every frame (menu open, ground ready, finish line, what the belt reports),
    ///   • applies it: only a running session moves the runner, the crowd and the clock,
    ///   • overlay for "start the belt", the 3-2-1 countdown and every pause, with
    ///     Weiter / Beenden &amp; speichern / Verwerfen,
    ///   • "Ⅱ Pause (Esc)" in the HUD; Esc in the pause dialog resumes.
    /// </summary>
    public class RunSessionUI : MonoBehaviour
    {
        [SerializeField] private TreadmillLocomotionSource treadmill;
        [SerializeField] private RunStats stats;
        [SerializeField] private TrackManager track;

        /// <summary>The running session (null outside the run scene).</summary>
        public static SessionStateMachine Session { get; private set; }

        private readonly SessionStateMachine sm = new SessionStateMachine();
        private SessionRecorder recorder;
        private SessionState lastState = SessionState.Preparing;
        private bool confirmDiscard;

        private GameObject overlay, pauseButton, resumeButton, finishButton, discardButton;
        private Text title, sub, discardLabel;

        private void Awake()
        {
            Session = sm;
            sm.FinishRequested += () => { if (track != null) track.FinishNow(); }; // FinishController records + shows the result
        }

        private void OnDestroy() { if (Session == sm) Session = null; }

        private void Start()
        {
            recorder = FindFirstObjectByType<SessionRecorder>();
            gameObject.AddComponent<WorkoutHud>();
            gameObject.AddComponent<GhostRunner>();
            gameObject.AddComponent<Announcer>();
            gameObject.AddComponent<BigHud>();
            new GameObject("Ambience", typeof(AudioSource), typeof(Ambience)).transform.SetParent(transform, false);
            BuildUI();
        }

        private void Update()
        {
            bool menu = StartMenuUI.IsOpen || WorkshopUI.Active;
            bool active = treadmill != null && treadmill.IsActive;
            sm.Tick(Time.unscaledDeltaTime, menu, track != null && !track.WaitingForTerrain,
                    track != null && track.IsFinished, active, active ? Signal(treadmill.State) : BeltSignal.None);

            if (track != null) track.Frozen = !sm.Moves;
            if (stats != null) stats.Paused = !sm.Moves;

            if (!menu && Input.GetKeyDown(KeyCode.Escape))
            {
                if (sm.State == SessionState.Running) sm.PauseByUser();
                else if (sm.State == SessionState.Paused && sm.Reason == PauseReason.User) Resume();
            }

            if (sm.State != lastState)
            {
                if (sm.State == SessionState.Running)
                    RaceMessages.Post(lastState == SessionState.Paused ? Jogging.Core.Loc.T("Weiter geht's!") : Jogging.Core.Loc.T("Los geht's!"));
                if (sm.State != SessionState.Paused) confirmDiscard = false;
                lastState = sm.State;
            }
            Show(menu);
        }

        private static BeltSignal Signal(BeltState s)
        {
            switch (s)
            {
                case BeltState.Running: return BeltSignal.Running;
                case BeltState.Countdown: return BeltSignal.Countdown;
                case BeltState.Safety: return BeltSignal.Safety;
                case BeltState.Error: return BeltSignal.Error;
                default: return BeltSignal.Stopped; // stopped, paused, unknown
            }
        }

        // ------------------------------------------------------------------ commands

        private void Resume() { sm.Resume(); confirmDiscard = false; }

        /// <summary>"Verwerfen" (public for the end-to-end test: two calls = two clicks).</summary>
        public void Discard()
        {
            if (!confirmDiscard) { confirmDiscard = true; return; } // second click confirms
            if (recorder != null) recorder.Discard();
            sm.Abort();
            // Back to the runner's home on the same route (the menu opens there).
            RouteRuntime.Selected = Jogging.Route.RoutePresets.AfterRun(RouteRuntime.Current);
            Jogging.World.SceneReload.Now(); // frees the old landscape first
        }

        // ------------------------------------------------------------------ view

        private void Show(bool menu)
        {
            if (overlay == null) return;
            bool running = !menu && sm.State == SessionState.Running;
            if (pauseButton.activeSelf != running) pauseButton.SetActive(running);
            if (touchSpeed != null)
            {
                bool touch = running && !(treadmill != null && treadmill.IsActive); // no belt: the runner sets the pace by touch
                if (touchSpeed.activeSelf != touch) touchSpeed.SetActive(touch);
                if (touch && keys != null) touchSpeedLabel.text = Jogging.Core.Units.FmtSpeed(keys.TargetSpeedMps * 3.6f);
            }

            string t = null, s = "";
            bool dialog = false;
            if (!menu && track != null && !track.WaitingForTerrain)
            {
                switch (sm.State)
                {
                    case SessionState.Preparing when sm.BeltRun:
                        t = "Starte das Laufband  ▶"; s = "Start am Band drücken – der Lauf beginnt automatisch"; break;
                    case SessionState.Countdown when sm.BeltRun:
                        t = "Band startet …"; s = "Gleich geht's los"; break;
                    case SessionState.Countdown:
                        t = Mathf.CeilToInt(sm.CountdownLeft).ToString(); s = Platform.IsMobile ? "Tempo mit − / + unten links" : "Tempo mit ↑ / ↓"; break;
                    case SessionState.Paused:
                        dialog = true;
                        switch (sm.Reason)
                        {
                            case PauseReason.BeltStopped:
                                t = "Pause – Band gestoppt";
                                s = Jogging.Core.Loc.F("Zum Weiterlaufen Band starten · Lauf endet in {0} s", Mathf.CeilToInt(sm.AutoFinishLeft)); break;
                            case PauseReason.BeltSafety: t = "Sicherheitsschlüssel fehlt"; s = "Clip am Laufband einstecken"; break;
                            case PauseReason.BeltError: t = "Fehler am Laufband"; s = "Bitte das Band prüfen"; break;
                            case PauseReason.ConnectionLost: t = "Verbindung zum Laufband verloren"; s = "Wird automatisch wieder verbunden …"; break;
                            default: t = "Pause"; s = sm.BeltRun ? "Weiter, sobald das Band läuft" : Jogging.Core.Platform.IsMobile ? "„Weiter“ tippen" : "Esc – weiter"; break;
                        }
                        break;
                }
            }

            bool on = t != null;
            if (overlay.activeSelf != on) overlay.SetActive(on);
            if (!on) return;
            title.text = Jogging.Core.Loc.T(t);
            title.fontSize = sm.State == SessionState.Countdown && !sm.BeltRun ? 110 : 44;
            sub.text = Jogging.Core.Loc.T(s);
            Set(resumeButton, dialog && sm.Reason == PauseReason.User);
            Set(finishButton, dialog && sm.Started);
            Set(discardButton, dialog);
            discardLabel.text = confirmDiscard ? Jogging.Core.Loc.T("Wirklich verwerfen?") : Jogging.Core.Loc.T("Verwerfen");
        }

        private static void Set(GameObject go, bool on) { if (go.activeSelf != on) go.SetActive(on); }

        private void BuildUI()
        {
            var canvasGo = new GameObject("Session Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            overlay = UiTheme.Panel(canvasGo.transform);
            var rt = overlay.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, -40f);
            rt.sizeDelta = new Vector2(860f, 300f);

            title = UiTheme.Label(overlay.transform, "", 44, UiTheme.TextPrimary, TextAnchor.MiddleCenter, bold: true);
            UiControls.Place(title.rectTransform, 20f, -20f, -20f, 120f);
            sub = UiTheme.Label(overlay.transform, "", 24, UiTheme.TextMuted, TextAnchor.MiddleCenter);
            UiControls.Place(sub.rectTransform, 20f, -150f, -20f, 40f);

            resumeButton = UiControls.Button(overlay.transform, "▶  Weiter", Resume, 40f, -218f, 250f, 56f, UiTheme.Success).gameObject;
            finishButton = UiControls.Button(overlay.transform, Jogging.Core.Loc.T("Beenden & speichern"), () => sm.Finish(), 305f, -218f, 250f, 56f, UiTheme.Accent).gameObject;
            var discard = UiControls.Button(overlay.transform, "Verwerfen", Discard, 570f, -218f, 250f, 56f, UiTheme.Danger);
            discardButton = discard.gameObject;
            discardLabel = discard.GetComponentInChildren<Text>();
            overlay.SetActive(false);

            var pause = UiTheme.Button(canvasGo.transform, Jogging.Core.Loc.T("Ⅱ  Pause (Esc)"), () => sm.PauseByUser(), null, 18);
            var prt = pause.GetComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(1f, 0f);
            prt.anchoredPosition = new Vector2(-24f, 24f);
            prt.sizeDelta = new Vector2(220f, 48f);
            pauseButton = pause.gameObject;
            pauseButton.SetActive(false);
            if (Platform.IsMobile)
            {
                pause.GetComponentInChildren<Text>().text = Jogging.Core.Loc.T("Ⅱ  Pause");
                BuildTouchSpeed(canvasGo.transform);
            }
        }

        // iPad/Android without a treadmill: pace by touch (the Mac uses ↑ / ↓).
        private GameObject touchSpeed;
        private Text touchSpeedLabel;
        private Jogging.Locomotion.KeyboardLocomotionSource keys;

        private void BuildTouchSpeed(Transform canvas)
        {
            keys = FindFirstObjectByType<Jogging.Locomotion.KeyboardLocomotionSource>();
            touchSpeed = new GameObject("Touch Speed", typeof(RectTransform));
            touchSpeed.transform.SetParent(canvas, false);
            var rt = touchSpeed.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(24f, 24f);
            rt.sizeDelta = new Vector2(270f, 170f);
            touchSpeedLabel = UiControls.Label(touchSpeed.transform, "", 30, 0f, 0f, 270f, 44f, UiTheme.TextPrimary, TextAnchor.MiddleCenter, bold: true);
            float step = Jogging.Core.Units.SpeedToKmh(0.5f) / 3.6f; // 0.5 km/h or 0.5 mph
            UiControls.Button(touchSpeed.transform, "−", () => { if (keys != null) keys.TargetSpeedMps -= step; }, 0f, -52f, 125f, 118f, UiTheme.Accent, 64);
            UiControls.Button(touchSpeed.transform, "+", () => { if (keys != null) keys.TargetSpeedMps += step; }, 145f, -52f, 125f, 118f, UiTheme.Accent, 64);
            touchSpeed.SetActive(false);
        }
    }
}
