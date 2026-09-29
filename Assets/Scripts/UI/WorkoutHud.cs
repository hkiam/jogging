using UnityEngine;
using UnityEngine.UI;
using Jogging.Core;
using Jogging.Locomotion;
using Jogging.Locomotion.Treadmill;
using Jogging.Training;
using Jogging.World;

namespace Jogging.UI
{
    /// <summary>
    /// Runs the selected workout during the run and shows it: current segment with target speed,
    /// time/distance left and progress, the next segment, and a speed hint (↑ schneller / ↓ langsamer).
    /// The workout only advances while the run runs; at its end the run finishes.
    /// On the treadmill (control "Strecke") the workout takes control: a segment with its own incline
    /// sets it, else the route's grade applies — always through the belt safety layer. The speed is
    /// only a hint (the runner sets it at the belt).
    /// Added by <see cref="RunSessionUI"/> at runtime (no scene object needed).
    /// </summary>
    public class WorkoutHud : MonoBehaviour
    {
        private const float HintTolerance = 0.5f; // km/h

        private RunStats stats;
        private TrackManager track;
        private LocomotionRouter router;
        private WorkoutRunner runner;
        private float lastDistance, lastClock;
        private bool started;

        private GameObject panel;
        private Text header, current, remaining, next, hint;
        private RectTransform bar;
        private Image barFill;
        // treadmill view (BigHud on): a wide strip above the big value bar, readable from the belt
        private GameObject bigPanel;
        private Text bigTitle, bigRemaining, bigHint, bigNext;
        private RectTransform bigBar;
        private Image bigBarFill;

        private static bool workoutArgUsed;

        /// <summary>The large workout strip is showing (tests).</summary>
        public static bool BigVisible { get; private set; }

        private void Awake() => WorkoutRuntime.Clear(); // a new scene: the previous run's workout is gone

        private void Start()
        {
            stats = FindFirstObjectByType<RunStats>();
            track = FindFirstObjectByType<TrackManager>();
            router = FindFirstObjectByType<LocomotionRouter>();
            BuildUI();
            // Tests: -workout <id> (e.g. builtin:intervalle-30) starts that workout with the run.
            var a = Jogging.Core.Args.All;
            int i = System.Array.IndexOf(a, "-workout");
            if (i >= 0 && i + 1 < a.Length && !workoutArgUsed && WorkoutRuntime.Selected == null && runner == null && !StartMenuUI.IsOpen)
            {
                var w = WorkoutPresets.All().Find(x => x.id == a[i + 1]);
                workoutArgUsed = true; // the first run only, not again after every scene reload
                if (w != null) { WorkoutRuntime.Selected = w.Clone(); WorkoutRuntime.Armed = true; }
                else Debug.LogWarning($"[Jogging] -workout {a[i + 1]}: unbekannt");
            }
        }

        private void OnDestroy()
        {
            // Hand control back as the settings say (not to "Treadmill": that would drop the route coupling).
            if (BeltControl.Mode == ControlMode.Workout) { BeltControl.Workout = null; TreadmillConnectUI.Apply(sceneStart: true); }
        }

        private void Begin(WorkoutDoc doc)
        {
            WorkoutRuntime.Begin(doc);
            runner = WorkoutRuntime.Runner;
            runner.SegmentStarted += s =>
            {
                RaceMessages.Post($"{s.Title}{(s.speedKmh > 0f ? " · " + Jogging.Core.Units.FmtSpeed(s.speedKmh) : "")} · {Amount(s)}");
                Announcer.Current?.Segment(s, runner.Index == 0);
            };
            runner.Completed += () =>
            {
                RaceMessages.Post(Jogging.Core.Loc.T("Workout geschafft!"));
                Announcer.Current?.Beep(high: true);
                RunSessionUI.Session?.Finish();
            };
            lastDistance = stats != null ? stats.DistanceMeters : 0f;
            lastClock = stats != null ? stats.ElapsedSeconds : 0f;
            if (track != null) track.KeepLapping = true; // loops: next lap instead of the finish; the workout ends the run

            // Belt: the workout controls when the app may control ("Strecke" in the settings).
            if (AppSettings.Current.beltIncline)
            {
                BeltControl.Workout = Targets;
                BeltControl.Mode = ControlMode.Workout;
                TreadmillConnectUI.Apply();
            }
            Debug.Log($"[Jogging] Workout „{doc.name}“: {doc.segments.Count} Abschnitte, ~{doc.Minutes:0} min");
        }

        private bool Targets(out float? incline, out float? speed)
        {
            incline = speed = null;
            var s = runner?.Current;
            if (s == null) return false;
            incline = s.setIncline ? s.inclinePercent : Mathf.Max(0f, track != null ? track.CurrentInclinePercent : 0f);
            // Speed only if allowed in the settings (else it stays a hint on the HUD).
            if (BeltControl.SpeedAllowed && s.speedKmh > 0f) speed = s.speedKmh;
            return true;
        }

        private void Update()
        {
            if (runner == null && WorkoutRuntime.Armed && WorkoutRuntime.Selected != null)
            {
                var doc = WorkoutRuntime.Selected;
                WorkoutRuntime.Selected = null;
                Begin(doc);
            }
            if (runner == null)
            {
                if (panel.activeSelf) panel.SetActive(false);
                if (bigPanel.activeSelf) bigPanel.SetActive(false);
                BigVisible = false;
                return;
            }

            float d = stats != null ? stats.DistanceMeters : 0f;
            var sm = RunSessionUI.Session;
            if (sm != null && sm.Moves)
            {
                if (!started) { started = true; runner.Start(); } // first segment's message once the run moves
                // With the run clock (it stops while the ground ahead is still loading), not frame time.
                float clock = stats != null ? stats.ElapsedSeconds : lastClock + Time.deltaTime;
                runner.Advance(Mathf.Max(0f, clock - lastClock), Mathf.Max(0f, d - lastDistance));
            }
            lastDistance = d;
            lastClock = stats != null ? stats.ElapsedSeconds : lastClock + Time.deltaTime;

            bool show = !StartMenuUI.IsOpen && !WorkshopUI.Active && !(track != null && track.IsFinished);
            bool big = BigHud.On;
            if (panel.activeSelf != (show && !big)) panel.SetActive(show && !big);
            if (bigPanel.activeSelf != (show && big)) bigPanel.SetActive(show && big);
            BigVisible = show && big;
            if (!show) return;
            Refresh();
            if (big) RefreshBig();
        }

        private void Refresh()
        {
            header.text = $"{Jogging.Core.Loc.T(runner.Doc.name).ToUpper(Jogging.Core.Loc.Culture)}   ·   {Mathf.Min(runner.Index + 1, runner.Count)} / {runner.Count}";
            var s = runner.Current;
            if (s == null)
            {
                current.text = Jogging.Core.Loc.T("Geschafft!"); remaining.text = ""; next.text = ""; hint.text = "";
                SetBar(1f, UiTheme.Success);
                return;
            }
            current.text = s.Title + (s.speedKmh > 0f ? "  ·  " + Jogging.Core.Units.FmtSpeed(s.speedKmh) : "") + (s.setIncline ? $"  ·  {s.inclinePercent:0} %" : "")
                         + (s.hrZone > 0 ? Jogging.Core.Loc.F("  ·  Zone {0}", s.hrZone) : "");
            remaining.text = s.ByDistance ? Jogging.Core.Units.FmtDistAuto(runner.Remaining) : Clock(runner.Remaining);
            SetBar(runner.SegmentProgress, WorkoutSegment.KindColor(s.kind));
            var n = runner.Next;
            next.text = n != null ? Jogging.Core.Loc.F("Nächstes: {0}{1} · {2}", n.Title, (n.speedKmh > 0f ? " · " + Jogging.Core.Units.FmtSpeed(n.speedKmh) : ""), Amount(n)) : Jogging.Core.Loc.T("Danach: geschafft");

            // A heart rate target wins over the speed target (if there is a heart rate).
            var hrm = HeartRateMonitor.Current;
            if (s.hrZone > 0 && hrm != null && hrm.HasData)
            {
                var (lo, hi) = HeartRateZones.Range(s.hrZone, HeartRateMonitor.MaxHr);
                if (hrm.Bpm < lo) { hint.text = Jogging.Core.Loc.T("Puls zu niedrig"); hint.color = new Color(1f, 0.78f, 0.35f); }
                else if (hrm.Bpm >= hi) { hint.text = Jogging.Core.Loc.T("Puls zu hoch"); hint.color = new Color(0.55f, 0.8f, 1f); }
                else { hint.text = Jogging.Core.Loc.T("Zone passt"); hint.color = UiTheme.Success; }
                return;
            }
            if (s.hrZone > 0 && s.speedKmh <= 0f) { hint.text = Jogging.Core.Loc.T("ohne Pulsgurt"); hint.color = UiTheme.TextMuted; return; }

            float kmh = router != null ? router.SpeedMps * 3.6f : 0f;
            if (s.speedKmh > 0f && BeltControl.SpeedAllowed && BeltControl.Mode == ControlMode.Workout && router != null && router.UsingTreadmill)
            { hint.text = Jogging.Core.Loc.F("Band → {0:0.0}", s.speedKmh); hint.color = UiTheme.Accent; return; } // the belt follows by itself
            if (s.speedKmh <= 0f) { hint.text = ""; }
            else if (kmh < s.speedKmh - HintTolerance) { hint.text = Jogging.Core.Loc.T("↑  schneller"); hint.color = new Color(1f, 0.78f, 0.35f); }
            else if (kmh > s.speedKmh + HintTolerance) { hint.text = Jogging.Core.Loc.T("↓  langsamer"); hint.color = new Color(0.55f, 0.8f, 1f); }
            else { hint.text = Jogging.Core.Loc.T("Tempo passt"); hint.color = UiTheme.Success; }
        }

        private void RefreshBig()
        {
            var s = runner.Current;
            bigTitle.text = s == null ? Jogging.Core.Loc.T("Geschafft!") : s.Title.ToUpperInvariant()
                + (s.speedKmh > 0f ? "  " + Jogging.Core.Units.FmtSpeed(s.speedKmh) : "") + (s.setIncline ? $"  {s.inclinePercent:0} %" : "")
                + (s.hrZone > 0 ? Jogging.Core.Loc.F("  Zone {0}", s.hrZone) : "");
            bigRemaining.text = remaining.text;
            // the last 5 s of a timed segment: orange, a change is coming
            bool soon = s != null && !s.ByDistance && runner.Remaining <= 5f;
            bigRemaining.color = soon ? new Color(1f, 0.7f, 0.25f) : UiTheme.TextPrimary;
            bigHint.text = hint.text; bigHint.color = hint.color;
            bigNext.text = $"{Mathf.Min(runner.Index + 1, runner.Count)}/{runner.Count}   ·   {next.text}";
            bigBar.anchorMax = bar.anchorMax;
            bigBarFill.color = barFill.color;
        }

        private void SetBar(float p, Color c)
        {
            bar.anchorMax = new Vector2(Mathf.Clamp01(p), 1f);
            barFill.color = c;
        }

        private static string Amount(WorkoutSegment s) => s.ByDistance ? Jogging.Core.Units.FmtDistAuto(s.distanceM) : Clock(s.durationS);

        private static string Clock(float sec)
        {
            int t = Mathf.CeilToInt(sec);
            return $"{t / 60}:{t % 60:00}";
        }

        private void BuildUI()
        {
            var canvasGo = new GameObject("Workout Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 11;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            panel = UiTheme.Panel(canvasGo.transform);
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(24f, -330f);
            rt.sizeDelta = new Vector2(420f, 214f);
            var t = panel.transform;

            header = UiControls.Label(t, "", 15, 18f, -10f, -18f, 22f, UiTheme.TextMuted, bold: true);
            current = UiControls.Label(t, "", 22, 18f, -36f, -18f, 30f, UiTheme.TextPrimary, bold: true);
            remaining = UiControls.Label(t, "", 44, 18f, -68f, 200f, 54f, UiTheme.TextPrimary, bold: true);
            hint = UiControls.Label(t, "", 22, 200f, -80f, 200f, 36f, UiTheme.Success, TextAnchor.MiddleRight, bold: true);

            var barBg = UiTheme.Panel(t, UiTheme.ButtonBg);
            UiControls.Place(barBg.GetComponent<RectTransform>(), 18f, -130f, 384f, 12f);
            var fill = UiTheme.Panel(barBg.transform, UiTheme.Accent);
            bar = fill.GetComponent<RectTransform>();
            bar.anchorMin = Vector2.zero; bar.anchorMax = new Vector2(0f, 1f);
            bar.offsetMin = bar.offsetMax = Vector2.zero;
            barFill = fill.GetComponent<Image>();

            next = UiControls.Label(t, "", 17, 18f, -156f, -18f, 44f, UiTheme.TextMuted);
            next.horizontalOverflow = HorizontalWrapMode.Wrap;
            panel.SetActive(false);

            // Treadmill view: 1500 wide like the BigHud bar (bottom 84, 170 high), directly above it.
            bigPanel = UiTheme.Panel(canvasGo.transform, new Color(0.06f, 0.08f, 0.11f, 0.82f));
            var brt = bigPanel.GetComponent<RectTransform>();
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0f);
            brt.anchoredPosition = new Vector2(0f, 84f + 170f + 12f);
            brt.sizeDelta = new Vector2(1500f, 190f);
            var b = bigPanel.transform;
            bigTitle = UiControls.Label(b, "", 44, 30f, -14f, 900f, 60f, UiTheme.TextPrimary, bold: true);
            bigNext = UiControls.Label(b, "", 24, 30f, -80f, 900f, 36f, UiTheme.TextMuted);
            bigRemaining = UiControls.Label(b, "", 110, 920f, -6f, 330f, 130f, UiTheme.TextPrimary, TextAnchor.MiddleRight, bold: true);
            bigHint = UiControls.Label(b, "", 36, 1260f, -40f, 220f, 60f, UiTheme.Success, TextAnchor.MiddleCenter, bold: true);
            bigHint.horizontalOverflow = HorizontalWrapMode.Wrap;
            var bbg = UiTheme.Panel(b, UiTheme.ButtonBg);
            UiControls.Place(bbg.GetComponent<RectTransform>(), 30f, -148f, 1440f, 24f);
            var bfill = UiTheme.Panel(bbg.transform, UiTheme.Accent);
            bigBar = bfill.GetComponent<RectTransform>();
            bigBar.anchorMin = Vector2.zero; bigBar.anchorMax = new Vector2(0f, 1f);
            bigBar.offsetMin = bigBar.offsetMax = Vector2.zero;
            bigBarFill = bfill.GetComponent<Image>();
            bigPanel.SetActive(false);
        }
    }
}
