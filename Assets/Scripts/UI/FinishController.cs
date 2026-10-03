using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using Jogging.World;
using Jogging.Core;
using Jogging.Profile;

namespace Jogging.UI
{
    /// <summary>
    /// Shows a themed finish screen when a finite route ends: records the run and displays a
    /// summary (distance, time, elevation, pace, new achievements); "Fertig" returns to the runner's
    /// home, "Nochmal laufen" runs the same route again.
    /// </summary>
    public class FinishController : MonoBehaviour
    {
        [SerializeField] private TrackManager track;
        [SerializeField] private RunStats stats;
        [SerializeField] private SessionRecorder recorder;

        private GameObject panel;
        private GameObject overlay; // full-screen dim + dialog; only visible at the finish
        private Text summary;
        private Text title;

        /// <summary>Title of the last finish screen (tests).</summary>
        public static string LastTitle { get; private set; } = "";
        private Button saveRoute;
        private Text saveLabel;

        private void Start()
        {
            if (FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            BuildUI();
            if (track != null) track.Finished += OnFinished;
        }

        private void OnDestroy()
        {
            if (track != null) track.Finished -= OnFinished;
        }

        private void OnFinished()
        {
            if (recorder != null) recorder.FinishNow();

            float dist = stats != null ? stats.DistanceMeters : 0f;
            float secs = stats != null ? stats.ElapsedSeconds : 0f;
            float gain = stats != null ? stats.ElevationGainMeters : 0f;
            float kmh = secs > 0.01f ? (dist / secs) * 3.6f : 0f;
            int m = Mathf.FloorToInt(secs / 60f), s = Mathf.FloorToInt(secs % 60f);

            // Best time for saved routes — only when the whole route was run (not a stop midway).
            string record = "";
            var route = RouteRuntime.Current;
            var ps = ProfileService.Instance;
            bool complete = track != null && track.RouteLength > 0f && track.DistanceTraveled >= track.RouteLength - 1f;
            // (not for a workout: its time is the workout's, not a lap of the route)
            if (route != null && !string.IsNullOrEmpty(route.id) && ps != null && complete && Jogging.Training.WorkoutRuntime.Current == null)
            {
                var prev = ps.RecordFor(route.id, route.revision);
                float prevBest = prev != null ? prev.bestSeconds : 0f;
                bool best = ps.RecordRouteTime(route.id, route.revision, secs);
                int pm = Mathf.FloorToInt(prevBest / 60f), psec = Mathf.FloorToInt(prevBest % 60f);
                record = best ? (prev != null ? Jogging.Core.Loc.F("\nNeue Bestzeit! (vorher {0:00}:{1:00})", pm, psec) : Jogging.Core.Loc.T("\nErste Bestzeit gesetzt"))
                              : Jogging.Core.Loc.F("\nBestzeit     {0:00}:{1:00}", pm, psec);
            }

            // Heart rate (from the recorder's logbook entry of this run).
            string pulse = "";
            if (recorder != null && recorder.LastAvgHr > 0) pulse = Jogging.Core.Loc.F("\nØ Puls     {0}  ·  max {1}", recorder.LastAvgHr, recorder.LastMaxHr);
            if (recorder != null && recorder.LastKcal > 0f) pulse += Jogging.Core.Loc.F("\nEnergie     {0:0} kcal", recorder.LastKcal);

            string plan = ProfileService.Instance != null ? ProfileService.Instance.LastPlanNote : "";
            if (plan != "") record += "\n" + plan;

            // what the way looked like: sights passed and the surfaces
            string sights = Jogging.World.Wayside.SightsText(SightEntries());
            string ways = Jogging.World.Wayside.SurfacesText(Jogging.World.Wayside.SurfaceMetres);
            if (sights != "") record += Jogging.Core.Loc.F("\nUnterwegs: {0}", sights);
            if (ways != "") record += Jogging.Core.Loc.F("\nWege: {0}", ways);

            string unlocked = "";
            if (recorder != null)
                foreach (var a in recorder.NewAchievements) unlocked += Jogging.Core.Loc.F("\nNeuer Erfolg: {0}", a);
            var (gameText, gameSaid) = GameLines();
            unlocked += gameText;

            // An endless run or one stopped midway has no finish line.
            var workout = Jogging.Training.WorkoutRuntime.Current;
            var wr = Jogging.Training.WorkoutRuntime.Runner;
            if (title != null)
                title.text = workout != null ? (wr.Done ? Jogging.Core.Loc.T("Workout geschafft!") : Jogging.Core.Loc.T("Workout beendet")) : complete ? Jogging.Core.Loc.T("Ziel erreicht!") : Jogging.Core.Loc.T("Lauf beendet");
            LastTitle = title != null ? title.text : "";
            if (workout != null)
                record = $"\n{Jogging.Core.Loc.T(workout.name)}   {(wr.Done ? Jogging.Core.Loc.T("vollständig") : Jogging.Core.Loc.F("{0} von {1} Abschnitten", wr.Index, wr.Count))}" + record;

            if (summary != null)
                summary.text =
                    Jogging.Core.Loc.F("Distanz     {0}\n", Jogging.Core.Units.FmtDist(dist, "0.00")) +
                    Jogging.Core.Loc.F("Zeit          {0:00}:{1:00}\n", m, s) +
                    Jogging.Core.Loc.F("Höhenmeter  +{0}\n", Jogging.Core.Units.FmtElev(gain)) +
                    Jogging.Core.Loc.F("Ø Tempo     {0}\n{1}", Jogging.Core.Units.FmtSpeed(kmh), RunnerStats.Pace(dist, secs)) + pulse;
            if (details != null) details.text = (record + unlocked).TrimStart('\n');

            if (exportRun != null) // a run too short to count is not in the logbook – nothing to export
            {
                bool auto = Jogging.Core.AppSettings.Current.autoExport;
                exportRun.gameObject.SetActive(recorder != null && recorder.LastRecord != null);
                exportLabel.text = Jogging.Core.Loc.T(auto ? "✓ exportiert" : "Export (TCX)");
            }

            if (saveRoute != null)
            {
                bool discovery = Jogging.Route.RoutePresets.IsUnsavedFreeRun(route);
                saveRoute.gameObject.SetActive(discovery);
                saveRoute.interactable = true;
                saveLabel.text = Jogging.Core.Loc.T("Strecke speichern");
            }

            // Spoken summary (distance, time, pace, plan progress).
            string said = $"{Jogging.Core.Loc.T(workout != null && wr.Done ? "Workout geschafft" : complete ? "Ziel erreicht" : "Lauf beendet")}. " +
                          $"{Jogging.Training.Speech.Km(dist)} in {Jogging.Training.Speech.Duration(secs)}. {Jogging.Training.Speech.Pace(dist, secs)}.";
            if (plan != "") said += " " + plan.Replace("„", "").Replace("“", "") + ".";
            if (gameSaid != "") said += " " + gameSaid;
            Announcer.Current?.Say(said);

            if (overlay != null) overlay.SetActive(true);
            RaceMessages.Post(Jogging.Core.Loc.T("🎉  Geschafft!"));
            StartCoroutine(Confetti());
        }

        // Simple UI confetti — colored squares falling and fading (no particle materials needed).
        private IEnumerator Confetti()
        {
            var canvasGo = new GameObject("Confetti Canvas",
                typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 25;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var pieces = new List<RectTransform>();
            var vel = new List<Vector2>();
            var spin = new List<float>();
            var cols = new[] { UiTheme.Accent, UiTheme.Success, UiTheme.Danger,
                new Color(0.95f,0.75f,0.25f), new Color(0.75f,0.40f,0.80f) };

            for (int i = 0; i < 60; i++)
            {
                var go = new GameObject("c", typeof(Image));
                go.transform.SetParent(canvasGo.transform, false);
                var img = go.GetComponent<Image>();
                img.color = cols[Random.Range(0, cols.Length)];
                var rt = go.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(Random.Range(10f, 20f), Random.Range(14f, 26f));
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(Random.Range(-900f, 900f), Random.Range(560f, 720f));
                pieces.Add(rt);
                vel.Add(new Vector2(Random.Range(-60f, 60f), Random.Range(-520f, -260f)));
                spin.Add(Random.Range(-240f, 240f));
            }

            float t = 0f;
            while (t < 3f)
            {
                float dt = Time.unscaledDeltaTime;
                t += dt;
                for (int i = 0; i < pieces.Count; i++)
                {
                    var v = vel[i]; v.y += -300f * dt; vel[i] = v;
                    pieces[i].anchoredPosition += v * dt;
                    pieces[i].Rotate(0f, 0f, spin[i] * dt);
                    var img = pieces[i].GetComponent<Image>();
                    var c = img.color; c.a = Mathf.Clamp01(1f - t / 3f); img.color = c;
                }
                yield return null;
            }
            Destroy(canvasGo);
        }

        private void BuildUI()
        {
            var canvasGo = new GameObject("Finish Canvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // Dim background. It is the overlay ROOT (dialog is its child): it must only be active at
            // the finish — a permanently active full-screen dim darkened the whole game and
            // swallowed every click on the HUD buttons (treadmill connect, avatar) underneath.
            var dim = UiTheme.Panel(canvasGo.transform, new Color(0f, 0f, 0f, 0.55f));
            UiTheme.Stretch(dim.GetComponent<RectTransform>());
            dim.GetComponent<Image>().sprite = null; // full-screen flat dim
            overlay = dim;

            panel = UiTheme.Panel(dim.transform);
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = Vector2.zero;
            prt.sizeDelta = new Vector2(1120f, 680f); // two columns: the run left, what it brought right

            // Accent header bar.
            var header = UiTheme.Panel(panel.transform, UiTheme.Success);
            var hrt = header.GetComponent<RectTransform>();
            hrt.anchorMin = new Vector2(0f, 1f); hrt.anchorMax = new Vector2(1f, 1f);
            hrt.pivot = new Vector2(0.5f, 1f);
            hrt.anchoredPosition = Vector2.zero; hrt.sizeDelta = new Vector2(0f, 84f);
            title = UiTheme.Label(header.transform, "Ziel erreicht!", 34, Color.white, TextAnchor.MiddleCenter, bold: true);
            UiTheme.Stretch(title.GetComponent<RectTransform>());

            summary = UiTheme.Label(panel.transform, "", 26, UiTheme.TextPrimary, TextAnchor.UpperLeft);
            var srt = summary.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0f, 0f); srt.anchorMax = new Vector2(0.42f, 1f);
            srt.offsetMin = new Vector2(48f, 200f); srt.offsetMax = new Vector2(-16f, -110f);
            details = UiTheme.Label(panel.transform, "", 20, UiTheme.TextPrimary, TextAnchor.UpperLeft);
            var drt = details.GetComponent<RectTransform>();
            drt.anchorMin = new Vector2(0.42f, 0f); drt.anchorMax = new Vector2(1f, 1f);
            drt.offsetMin = new Vector2(16f, 200f); drt.offsetMax = new Vector2(-40f, -110f);
            // long runs say a lot: smaller type when needed, and never across the buttons
            foreach (var t in new[] { summary, details })
            {
                t.resizeTextForBestFit = true; t.resizeTextMinSize = 13; t.resizeTextMaxSize = t == summary ? 26 : 21;
                t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
            }

            // A free run through a new landscape can be kept as a route (same seed = same landscape).
            saveRoute = UiTheme.Button(panel.transform, "Strecke speichern", SaveRoute, UiTheme.Success, 20);
            Place(saveRoute.GetComponent<RectTransform>(), new Vector2(0f, 162f), new Vector2(360f, 48f));
            saveLabel = saveRoute.GetComponentInChildren<Text>();
            saveRoute.gameObject.SetActive(false);

            var again = UiTheme.Button(panel.transform, "▶  Nochmal laufen",
                () =>
                {
                    // Same route again, straight into the run.
                    Jogging.World.RouteRuntime.Selected = Jogging.World.RouteRuntime.Current;
                    Jogging.Training.WorkoutRuntime.Selected = Jogging.Training.WorkoutRuntime.Current; // same workout again
                    StartMenuUI.SkipNextOpen();
                    Jogging.World.SceneReload.Now(); // frees the old landscape first
                }, UiTheme.Accent);
            Place(again.GetComponent<RectTransform>(), new Vector2(0f, 96f), new Vector2(360f, 56f));

            // Back to the runner's home (the menu opens there: the runner was chosen this session).
            var done = UiTheme.Button(panel.transform, "Fertig",
                () =>
                {
                    // After a free run a new landscape waits behind the home (Quick Run starts at once).
                    Jogging.World.RouteRuntime.Selected = Jogging.Route.RoutePresets.AfterRun(Jogging.World.RouteRuntime.Current);
                    Jogging.World.SceneReload.Now(); // frees the old landscape first
                });
            Place(done.GetComponent<RectTransform>(), new Vector2(-95f, 30f), new Vector2(170f, 48f));

            // The run as TCX for Strava, Garmin & co. (already done when automatic export is on)
            exportRun = UiTheme.Button(panel.transform, "Export (TCX)", ExportRun);
            Place(exportRun.GetComponent<RectTransform>(), new Vector2(95f, 30f), new Vector2(170f, 48f));
            exportLabel = exportRun.GetComponentInChildren<Text>();

            overlay.SetActive(false);
        }

        private Text details;
        private Button exportRun;
        private Text exportLabel;

        private void ExportRun()
        {
            var ps = Jogging.Profile.ProfileService.Instance;
            var rec = recorder != null ? recorder.LastRecord : null;
            if (rec == null) { exportLabel.text = Jogging.Core.Loc.T("Kein Lauf"); return; }
            try
            {
                string path = Jogging.Profile.TcxExport.SaveTo(Jogging.Profile.ProfileService.ExportFolder, rec, ps.Profile.playerName, true);
                exportLabel.text = Jogging.Core.Loc.T("✓ exportiert");
                Debug.Log($"[Jogging] Lauf exportiert: {path}");
                if (!Jogging.Core.Platform.IsMobile) Application.OpenURL("file://" + Jogging.Profile.ProfileService.ExportFolder);
            }
            catch (System.Exception e) { exportLabel.text = Jogging.Core.Loc.T("Export fehlgeschlagen"); Debug.LogWarning($"[Jogging] Export: {e.Message}"); }
        }

        /// <summary>
        /// What the run did for the game (Profile/Game): climb times, the family journey, new album cards,
        /// weekly quests done, a new level – as lines for the screen and a short spoken part.
        /// </summary>
        private (string text, string said) GameLines()
        {
            var ps = ProfileService.Instance;
            var rec = recorder != null ? recorder.LastRecord : null;
            if (ps == null || rec == null) return ("", "");
            var text = new System.Text.StringBuilder();
            var said = new List<string>();
            foreach (var c in RunGame.ClimbResults) text.Append("\n⛰ ").Append(c);

            var after = new Dictionary<string, List<SessionSummary>>();
            foreach (var r in ps.Runners) after[r.id] = ps.SummariesOf(r.id);
            string me = ps.Profile.id;
            var mine = after.TryGetValue(me, out var m) ? m : new List<SessionSummary>();
            var mineBefore = mine.FindAll(x => x.file != rec.summary.file);
            var before = new Dictionary<string, List<SessionSummary>>(after) { [me] = mineBefore };

            // family journey
            var j0 = Game.JourneyNow(before); var j1 = Game.JourneyNow(after);
            var (stops, finished) = Game.JourneyStep(j0, j1);
            string unit(float v, Game.Journey j) => j.elevation ? Jogging.Core.Units.FmtElev(v) : Jogging.Core.Units.FmtDist(v * 1000f);
            if (finished)
            {
                text.Append("\n🧭 ").Append(Jogging.Core.Loc.F("Familienreise geschafft: {0}!", Jogging.Core.Loc.T(j0.journey.name)));
                said.Add(Jogging.Core.Loc.F("Eure Familienreise ist geschafft: {0}.", Jogging.Core.Loc.T(j0.journey.name)));
            }
            else if (stops.Count > 0)
            {
                text.Append("\n🧭 ").Append(Jogging.Core.Loc.F("Familienreise: {0} erreicht", string.Join(", ", stops.ConvertAll(Jogging.Core.Loc.T))));
                said.Add(Jogging.Core.Loc.F("Familienreise: {0} erreicht.", Jogging.Core.Loc.T(stops[stops.Count - 1])));
            }
            else if (j1.done > j0.done)
                text.Append("\n🧭 ").Append(Jogging.Core.Loc.F("Familienreise: noch {0} bis {1}", unit(j1.next.at - j1.done, j1.journey), Jogging.Core.Loc.T(j1.next.name)));

            // album
            var cards = Game.NewCards(mineBefore, rec.summary);
            if (cards.Count > 0)
            {
                text.Append("\n📒 ").Append(Jogging.Core.Loc.F("Neu im Album: {0}", string.Join(", ", cards.ConvertAll(Jogging.Core.Loc.T))));
                said.Add(Jogging.Core.Loc.F("Neu im Album: {0}.", string.Join(", ", cards.ConvertAll(Jogging.Core.Loc.T))));
            }

            // weekly quests
            var now = System.DateTime.Now;
            var q0 = Game.QuestProgress(mineBefore, now); var q1 = Game.QuestProgress(mine, now);
            for (int i = 0; i < q1.Count && i < q0.Count; i++)
                if (q1[i].done && !q0[i].done)
                {
                    text.Append("\n✅ ").Append(Jogging.Core.Loc.F("Wochenaufgabe geschafft: {0}", Jogging.Core.Loc.T(q1[i].q.text)));
                    said.Add(Jogging.Core.Loc.T("Wochenaufgabe geschafft."));
                }

            // experience and level
            int x0 = Game.Xp(mineBefore), x1 = Game.Xp(mine);
            int l0 = Game.Level(x0), l1 = Game.Level(x1);
            text.Append("\n⭐ ").Append(Jogging.Core.Loc.F("+{0} Punkte · Level {1}", x1 - x0, l1));
            if (l1 > l0)
            {
                var shirt = System.Array.Find(Game.Shirts, s => s.level == l1);
                text.Append(Jogging.Core.Loc.F(" – aufgestiegen!{0}", shirt.name != null ? Jogging.Core.Loc.F(" Neue Shirtfarbe: {0}", Jogging.Core.Loc.T(shirt.name)) : ""));
                said.Add(Jogging.Core.Loc.F("Glückwunsch, Level {0}!", l1));
            }
            return (text.ToString(), string.Join(" ", said));
        }

        private void SaveRoute()
        {
            var doc = RouteRuntime.Current;
            if (doc == null || !string.IsNullOrEmpty(doc.id)) return;
            doc.meta.name = Jogging.Core.Loc.F("Entdeckt am {0:dd.MM.} um {1:HH:mm}", System.DateTime.Now, System.DateTime.Now);
            var ps = ProfileService.Instance;
            if (ps != null) doc.meta.author = ps.Profile.playerName;
            Jogging.Route.RouteStore.Save(doc); // gives it an id: "Meine Strecken" and the same landscape again
            saveLabel.text = Jogging.Core.Loc.T("In „Meine Strecken“ gespeichert");
            RaceMessages.Post(doc.meta.name);
            saveRoute.interactable = false;
            Debug.Log($"[Jogging] Strecke gespeichert: „{doc.meta.name}“ (Seed {doc.generator.seed})");
        }

        private static void Place(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
        }

        private static IEnumerable<string> SightEntries()
        {
            foreach (var kv in Jogging.World.Wayside.Sights) yield return kv.Value > 1 ? $"{kv.Key}:{kv.Value}" : kv.Key;
        }
    }
}
