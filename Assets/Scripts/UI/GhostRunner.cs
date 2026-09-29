using UnityEngine;
using UnityEngine.UI;
using Jogging.Core;
using Jogging.Profile;
using Jogging.World;

namespace Jogging.UI
{
    /// <summary>
    /// Ghost runner on saved routes: the runner's fastest complete run on this route (else the latest
    /// one) runs along as a see-through figure (<see cref="GhostLook"/>) with a "GEIST" tag, placed by its 1 Hz samples at the same running
    /// time; the HUD shows the gap in seconds (+ = behind, − = ahead). Not during workouts or free
    /// runs. Can be switched off per runner (profile, or G during the run — remembered).
    /// Added by <see cref="RunSessionUI"/> at runtime.
    /// </summary>
    public class GhostRunner : MonoBehaviour
    {
        private static readonly int SpeedId = Animator.StringToHash("Speed");

        private GhostTrack ghost;

        /// <summary>A ghost run was found for this route (tests).</summary>
        public bool HasGhost => ghost != null;
        private RunStats stats;
        private TrackManager track;
        private Transform figure;
        private Animator anim;
        private GameObject panel;
        private Text label, gapText;
        private bool looked;

        private void Start()
        {
            stats = FindFirstObjectByType<RunStats>();
            track = FindFirstObjectByType<TrackManager>();
            BuildUI();
        }

        private static bool Off => ProfileService.Instance == null || ProfileService.Instance.Profile.ghostOff;

        // Once the run starts: find the ghost (the workout of this run is known by then).
        private void Find()
        {
            looked = true;
            var ps = ProfileService.Instance;
            var route = RouteRuntime.Current;
            if (ps == null || route == null || string.IsNullOrEmpty(route.id) || Training.WorkoutRuntime.Current != null) return;
            var pick = GhostTrack.Pick(ps.SummariesOf(ps.Profile.id), route.id, route.revision);
            if (pick == null) return;
            var rec = ps.Sessions.Load(pick.runnerId, pick.file);
            if (rec == null) return;
            var g = new GhostTrack(rec);
            if (!g.Valid) return;
            ghost = g;
            Debug.Log($"[Jogging] Geist: {(pick.completed ? "Bestzeit" : "letzter Lauf")} {RunnerStats.Duration(pick.seconds)} vom {pick.start}");
        }

        private void Spawn()
        {
            var player = FindFirstObjectByType<RealPlayerFigure>();
            if (player == null || player.CurrentModel == null) return;
            var root = new GameObject("Ghost Runner").transform;
            anim = RealFigure.Spawn(root, player.CurrentModel, player.MaleController, player.FemaleController);
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            GhostLook.Apply(anim.gameObject); // see-through: clearly not a real runner
            RivalTag.Create(root).SetText(Jogging.Core.Loc.T("▼ GEIST"), new Color(0.65f, 0.85f, 1f), fadeNear: false);
            figure = root;
        }

        private void Update()
        {
            var sm = RunSessionUI.Session;
            bool running = sm != null && sm.Started && !(track != null && track.IsFinished);
            if (!looked && sm != null && sm.Started) Find();

            if (ghost != null && running && Input.GetKeyDown(KeyCode.G) && !StartMenuUI.IsOpen) Toggle();

            bool show = ghost != null && running && !Off && stats != null;
            if (figure != null && figure.gameObject.activeSelf != show) figure.gameObject.SetActive(show);
            if (panel.activeSelf != show) panel.SetActive(show);
            if (!show) return;
            if (figure == null) { Spawn(); if (figure == null) return; }

            float t = stats.ElapsedSeconds;
            float gd = ghost.DistanceAt(t);
            // Place on the trail, one lane to the side (it's a ghost, not an obstacle).
            var path = TrailPath.Active;
            if (path != null)
            {
                Vector3 w = path.Offset(gd, -0.9f);
                if (TerrainGround.TryHeight(w.x, w.z, out float h)) w.y = h;
                figure.SetPositionAndRotation(w, path.Rotation(gd));
            }
            RealFigure.Drive(anim, ghost.KmhAt(t) / 3.6f);

            // Gap by time at my distance: + = I'm behind the ghost.
            float tg = ghost.TimeAt(stats.DistanceMeters);
            if (float.IsNaN(tg)) { gapText.text = Jogging.Core.Loc.T("Geist im Ziel"); gapText.color = UiTheme.TextMuted; }
            else
            {
                float gap = t - tg;
                string abs = RunnerStats.Duration(Mathf.Abs(gap)).Replace(" min", "").Replace(" h", "");
                gapText.text = Mathf.Abs(gap) < 0.5f ? Jogging.Core.Loc.T("gleichauf") : (gap > 0f ? "+" : "−") + abs;
                gapText.color = gap > 0.5f ? new Color(1f, 0.6f, 0.4f) : gap < -0.5f ? UiTheme.Success : UiTheme.TextPrimary;
            }
            label.text = ghost.Summary.completed ? Jogging.Core.Loc.F("GEIST · Bestzeit {0}", RunnerStats.Duration(ghost.Summary.seconds)) : Jogging.Core.Loc.T("GEIST · letzter Lauf");
        }

        // G on the Mac, a tap on the ghost panel on iPad/Android (remembered in the profile)
        private void Toggle()
        {
            var ps = ProfileService.Instance;
            if (ps == null) return;
            ps.Profile.ghostOff = !ps.Profile.ghostOff;
            ps.SaveActive();
            RaceMessages.Post(ps.Profile.ghostOff ? Jogging.Core.Loc.T("Geist aus") : Jogging.Core.Loc.T("Geist an"));
        }

        private void BuildUI()
        {
            var canvasGo = new GameObject("Ghost Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)); // raycaster: the panel is tappable on tablets
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
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-24f, -196f);
            rt.sizeDelta = new Vector2(340f, 96f);
            label = UiControls.Label(panel.transform, "", 15, 16f, -8f, -16f, 22f, new Color(0.65f, 0.85f, 1f), bold: true);
            gapText = UiControls.Label(panel.transform, "", 36, 16f, -32f, 200f, 48f, UiTheme.TextPrimary, bold: true);
            if (Platform.IsMobile) panel.AddComponent<Button>().onClick.AddListener(Toggle);
            UiControls.Label(panel.transform, Platform.IsMobile ? "Tippen: aus" : "G: aus", 15, 200f, -44f, 124f, 30f, UiTheme.TextMuted, TextAnchor.MiddleRight);
            panel.SetActive(false);
        }

        private void OnDestroy() { if (figure != null) Destroy(figure.gameObject); }
    }
}
