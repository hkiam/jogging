using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Jogging.Profile;
using Jogging.Route;
using Jogging.World;

namespace Jogging.UI
{
    /// <summary>
    /// Runner pages of the start menu:
    ///   • Who runs? — one card per family member (week and total km, a highlight for this week's champion), + runner.
    ///   • New runner — name + figure (previewed live on the player behind the menu).
    ///   • Runner home — Quick Run (dominant), routes, this week, last run, total, switch runner.
    /// Entry: no runner yet → new runner; a runner chosen this session or only one → their home;
    /// else → who runs?
    /// </summary>
    public partial class StartMenuUI
    {
        private const float RunnerDim = 0.55f;

        private void ShowEntry()
        {
            var ps = ProfileService.Instance;
            if (ps == null) { ShowHome(); return; }
            if (!ps.HasRunners) { ShowNewRunner(); return; }
            if (ProfileService.ChosenThisSession) { ShowHome(); return; }
            ShowRunners(); // the main dialog, also with a single runner: the app's settings are there
        }

        // Back from the settings: the main dialog (or the first runner page if there is none yet)
        private void ShowEntryAlways()
        {
            var ps = ProfileService.Instance;
            if (ps == null) { ShowHome(); return; }
            if (!ps.HasRunners) { ShowNewRunner(); return; }
            ShowRunners();
        }

        // ------------------------------------------------------------------ who runs?

        private void ShowRunners()
        {
            var ps = ProfileService.Instance;
            var runners = ps.Runners;
            int n = runners.Count + 1; // + "new runner" card
            int perRow = Mathf.Min(n, 5), rows = Mathf.CeilToInt(n / 5f);
            const float cw = 250f, ch = 290f, gap = 24f;
            float w = Mathf.Max(720f, perRow * cw + (perRow - 1) * gap + 80f);
            bool family = runners.Count > 1;
            float ph = 150f + rows * (ch + gap) + 20f + (family ? 90f : 0f) + 76f;
            var p = NewPage(w, ph);
            SetDim(RunnerDim);
            UiControls.Label(p, "Wer läuft?", 44, 0f, -24f, w, 64f, UiTheme.Accent, TextAnchor.MiddleCenter, bold: true);

            var now = DateTime.Now;
            var byRunner = new Dictionary<string, List<SessionSummary>>();
            foreach (var r in runners) byRunner[r.id] = ps.SummariesOf(r.id);
            string champ = runners.Count > 1 ? RunnerStats.WeeklyChampion(byRunner, now) : null;

            if (family) FamilyBar(p, byRunner, now, 40f, -110f - rows * (ch + gap) - 6f, w - 80f);
            // The app's settings (treadmill, strap, sound, display, backup) — for everybody, not one runner
            UiControls.Button(p, "Einstellungen", ShowSettingsPage, w - 40f - 260f, -(ph - 22f - 56f), 260f, 56f);

            float x0 = (w - (perRow * cw + (perRow - 1) * gap)) * 0.5f;
            for (int i = 0; i < n; i++)
            {
                float x = x0 + (i % 5) * (cw + gap), y = -110f - (i / 5) * (ch + gap);
                if (i == runners.Count)
                {
                    UiControls.Button(p, "＋\nLäufer", ShowNewRunner, x, y, cw, ch, null, 30);
                    continue;
                }
                var r = runners[i];
                var week = RunnerStats.Week(byRunner[r.id], now);
                string id = r.id;
                var card = UiControls.Button(p, "", () => { ps.Select(id); ShowHome(); }, x, y, cw, ch,
                    id == champ ? new Color(0.36f, 0.32f, 0.16f, 1f) : UiTheme.PanelSoft);
                var t = card.transform;
                if (id == champ)
                    UiControls.Label(t, "Nr. 1 diese Woche", 18, 0f, -14f, cw, 28f, new Color(1f, 0.85f, 0.35f), TextAnchor.MiddleCenter, bold: true);
                UiControls.Label(t, r.playerName, 34, 0f, -60f, cw, 50f, UiTheme.TextPrimary, TextAnchor.MiddleCenter, bold: true);
                UiControls.Label(t, FigureKind(r.figureModel), 18, 0f, -108f, cw, 26f, UiTheme.TextMuted, TextAnchor.MiddleCenter);
                UiControls.Label(t, RunnerStats.Km(week.DistanceM), 30, 0f, -160f, cw, 40f, UiTheme.TextPrimary, TextAnchor.MiddleCenter, bold: true);
                UiControls.Label(t, "diese Woche", 17, 0f, -198f, cw, 24f, UiTheme.TextMuted, TextAnchor.MiddleCenter);
                UiControls.Label(t, Jogging.Core.Loc.F("{0} gesamt · {1}", RunnerStats.Km(r.totalDistanceMeters), Runs(r.totalRuns)), 17, 0f, -240f, cw, 24f, UiTheme.TextMuted, TextAnchor.MiddleCenter);
            }
        }

        /// <summary>
        /// The family challenge as one bar: everybody's kilometres this week together towards the
        /// family goal, each runner's share in their colour (same order as the statistics).
        /// </summary>
        private static void FamilyBar(Transform p, Dictionary<string, List<SessionSummary>> byRunner, DateTime now, float x, float y, float width)
        {
            var ps = ProfileService.Instance;
            var (total, per) = Challenges.Family(byRunner, now);
            float goal = Challenges.FamilyGoalKm(ps.Runners.Count, Jogging.Core.AppSettings.Current.familyGoalKm);
            bool done = total >= goal;
            UiControls.Label(p, Jogging.Core.Loc.F("FAMILIEN-CHALLENGE: ZUSAMMEN {0:0} KM DIESE WOCHE", goal), 16, x, y, width - 200f, 26f, done ? UiTheme.Success : UiTheme.TextMuted, bold: true);
            UiControls.Label(p, Jogging.Core.Units.Dist(total * 1000f).ToString("0.0", Jogging.Core.Loc.Culture) + " / " + Jogging.Core.Units.FmtDist(goal * 1000f, "0") + (done ? Jogging.Core.Loc.T("  geschafft!") : ""), 18, x + width - 300f, y, 300f, 26f,
                done ? UiTheme.Success : UiTheme.TextPrimary, TextAnchor.MiddleRight, bold: true);
            var bg = UiTheme.Panel(p, UiTheme.ButtonBg);
            UiControls.Place(bg.GetComponent<RectTransform>(), x, y - 34f, width, 22f);
            float fx = 0f;
            for (int k = 0; k < per.Count; k++)
            {
                float f = Mathf.Min(per[k].Value / goal, 1f - fx);
                if (f <= 0f) continue;
                int idx = 0; for (int i = 0; i < ps.Runners.Count; i++) if (ps.Runners[i].id == per[k].Key) idx = i;
                var seg = new GameObject("share", typeof(Image));
                seg.transform.SetParent(bg.transform, false);
                seg.GetComponent<Image>().color = RunnerColors[idx % RunnerColors.Length];
                var rt = seg.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(fx, 0f); rt.anchorMax = new Vector2(fx + f, 1f);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                fx += f;
            }
        }

        private static string FigureKind(string model) =>
            Jogging.Core.Loc.T(!string.IsNullOrEmpty(model) && model.Contains("Female") ? "Läuferin" : "Läufer");

        // ------------------------------------------------------------------ new runner

        private int figureIndex;

        private void ShowNewRunner()
        {
            var ps = ProfileService.Instance;
            bool first = ps == null || !ps.HasRunners;
            var player = FindFirstObjectByType<RealPlayerFigure>();
            var choices = player != null ? player.Choices : new GameObject[0];
            string before = player != null ? player.CurrentName : "";

            var p = NewPage(720f, 430f);
            SetDim(RunnerDim);
            UiControls.Label(p, first ? "Leg deinen ersten Läufer an" : "Neuer Läufer", 36, 0f, -24f, 720f, 56f, UiTheme.Accent, TextAnchor.MiddleCenter, bold: true);
            UiControls.Label(p, "Name", 20, 60f, -100f, 200f, 40f, UiTheme.TextMuted);
            var name = UiControls.TextField(p, "", 200f, -98f, 460f);
            if (name.placeholder is Text ph) ph.text = Jogging.Core.Loc.T("Name");

            UiControls.Label(p, "Figur", 20, 60f, -170f, 200f, 40f, UiTheme.TextMuted);
            var label = UiControls.Label(p, "", 22, 270f, -166f, 320f, 48f, UiTheme.TextPrimary, TextAnchor.MiddleCenter, bold: true);
            figureIndex = Mathf.Clamp(figureIndex, 0, Mathf.Max(0, choices.Length - 1));
            Action refresh = () =>
            {
                if (choices.Length == 0) { label.text = "–"; return; }
                string m = choices[figureIndex] != null ? choices[figureIndex].name : "";
                label.text = $"{FigureKind(m)} {figureIndex + 1} / {choices.Length}";
                player.Show(m); // live preview behind the menu
            };
            UiControls.Button(p, "◀", () => { if (choices.Length > 0) { figureIndex = (figureIndex + choices.Length - 1) % choices.Length; refresh(); } }, 200f, -166f, 60f, 48f);
            UiControls.Button(p, "▶", () => { if (choices.Length > 0) { figureIndex = (figureIndex + 1) % choices.Length; refresh(); } }, 600f, -166f, 60f, 48f);
            refresh();

            UiControls.Button(p, "Anlegen", () =>
            {
                string fig = choices.Length > 0 && choices[figureIndex] != null ? choices[figureIndex].name : "";
                ps.Create(name.text, fig);
                ShowHome();
            }, first ? 210f : 60f, -300f, 300f, 60f, UiTheme.Success, 24);
            if (!first)
                UiControls.Button(p, Jogging.Core.Loc.T("Abbrechen"), () => { if (player != null) player.Show(before); ShowRunners(); }, 380f, -300f, 280f, 60f);
            else // before the first runner the main dialog is this page: the app's settings from here too
                UiControls.Button(p, "Einstellungen", ShowSettingsPage, 720f - 40f - 200f, -370f, 200f, 44f, null, 19);
        }

        // ------------------------------------------------------------------ runner home

        private void ShowHome()
        {
            const float w = 860f;
            var p = NewPage(w, 940f);
            SetDim(RunnerDim);
            var ps = ProfileService.Instance;
            var me = ps != null ? ps.Profile : new ProfileData();
            var runs = ps != null ? ps.SummariesOf(me.id) : new List<SessionSummary>();
            var now = DateTime.Now;

            UiControls.Label(p, me.playerName, 48, 50f, -24f, 420f, 64f, UiTheme.TextPrimary, bold: true);
            int streak = Game.Streak(runs, now).days; // rest days allowed (up to 2 in a row)
            if (streak >= 2)
                UiControls.Label(p, Jogging.Core.Loc.F("Serie {0} Tage", streak), 22, w - 560f, -24f, 300f, 64f, new Color(1f, 0.7f, 0.3f), TextAnchor.MiddleRight, bold: true);
            UiControls.Button(p, Jogging.Core.Loc.T("Profil"), () => ShowProfile(), w - 230f, -34f, 180f, 46f);

            // Quick Run: the one obvious action.
            UiControls.Button(p, "▶   QUICK RUN", QuickRun, 50f, -104f, w - 100f, 120f, UiTheme.Success, 44);
            UiControls.Label(p, "jedes Mal eine neue Landschaft – einfach loslaufen", 18, 50f, -228f, w - 100f, 28f, UiTheme.TextMuted, TextAnchor.MiddleCenter);

            // Routes
            var cur = RouteRuntime.Current;
            bool resume = cur != null && !cur.@params.endless;
            int saved = RouteStore.LoadAll().Count;
            float bw = resume ? (w - 100f - 2 * 16f) / 3f : (w - 100f - 16f) / 2f;
            UiControls.Button(p, Jogging.Core.Loc.T("＋  Neue Strecke"), () => ShowEditor(null), 50f, -272f, bw, 60f, UiTheme.Accent);
            UiControls.Button(p, saved > 0 ? Jogging.Core.Loc.F("Meine Strecken ({0})", saved) : Jogging.Core.Loc.T("Meine Strecken"), () => ShowList(0), 50f + bw + 16f, -272f, bw, 60f);
            if (resume)
                UiControls.Button(p, $"▶  {Short(Jogging.Core.Loc.T(cur.meta.name), 18)}", () => Run(cur), 50f + 2 * (bw + 16f), -272f, bw, 60f);

            // Training plan: its next session right here; else plans and workouts side by side.
            var plan = ps != null ? ps.ActivePlan : null;
            int nextSession = plan != null ? Training.PlanProgress.Next(plan, me.planDone) : -1;
            float half = (w - 100f - 16f) / 2f;
            if (plan != null && nextSession >= 0)
            {
                var nwd = plan.sessions[nextSession].workout;
                UiControls.Button(p, $"▶  {Jogging.Core.Loc.T(plan.name)}: {Jogging.Core.Loc.T(nwd.name)}", () => StartPlanSession(plan, nextSession), 50f, -344f, w - 100f - 216f, 60f, UiTheme.Success, 20);
                UiControls.Button(p, Jogging.Core.Loc.T("Plan"), () => ShowPlans(), w - 250f, -344f, 200f, 60f, new Color(0.55f, 0.42f, 0.85f), 22);
            }
            else
            {
                UiControls.Button(p, Jogging.Core.Loc.T("Trainingspläne"), () => ShowPlans(), 50f, -344f, half, 60f, new Color(0.55f, 0.42f, 0.85f), 22);
                UiControls.Button(p, Jogging.Core.Loc.T("Workouts"), () => ShowWorkouts(), 66f + half, -344f, half, 60f, new Color(0.55f, 0.42f, 0.85f), 22);
            }

            // This week / last run / total
            var week = RunnerStats.Week(runs, now);
            Section(p, "DIESE WOCHE", -436f,
                week.Runs > 0 ? $"{RunnerStats.Km(week.DistanceM)}   ·   {RunnerStats.Duration(week.Seconds)}   ·   {Runs(week.Runs)}"
                              : "noch kein Lauf diese Woche");
            var last = RunnerStats.Last(runs);
            Section(p, "LETZTER LAUF", -542f,
                last != null ? $"{RunnerStats.Km(last.distanceM)} · {RunnerStats.Duration(last.seconds)} · {RunnerStats.Pace(last.distanceM, last.seconds)}{(last.avgHr > 0 ? $" · {Jogging.Core.Loc.T("Ø ")}{last.avgHr} bpm" : "")}{(string.IsNullOrEmpty(last.workoutName) ? "" : " · " + Jogging.Core.Loc.T(last.workoutName))} · {RunnerStats.When(last.start, now)}"
                             : "noch keiner – leg los!");
            Section(p, "GESAMT", -648f,
                $"{RunnerStats.Km(me.totalDistanceMeters)}   ·   {Runs(me.totalRuns)}   ·   ↑{Jogging.Core.Units.FmtElev(me.totalElevationMeters)}   ·   {me.unlockedAchievements.Count} {Jogging.Core.Loc.T(me.unlockedAchievements.Count == 1 ? "Erfolg" : "Erfolge")}");

            // Weekly challenges, compact: done count next to "this week".
            var goals = Challenges.ThisWeek(runs, now, me);
            int done = 0; foreach (var g in goals) if (g.Done) done++;
            UiControls.Label(p, Jogging.Core.Loc.F("Wochenziele {0} / {1}", done, goals.Count), 17, w - 350f, -436f, 300f, 26f, done == goals.Count ? UiTheme.Success : UiTheme.TextMuted, TextAnchor.MiddleRight, bold: true);
            float gx = w - 350f;
            foreach (var g in goals) { Bar(p, gx, -466f, 92f, 8f, g.Fraction, g.Done ? UiTheme.Success : UiTheme.Accent); gx += 104f; }
            if (ps != null && ps.Runners.Count > 1)
            {
                var all = new Dictionary<string, List<SessionSummary>>();
                foreach (var r in ps.Runners) all[r.id] = ps.SummariesOf(r.id);
                var (famKm, _) = Challenges.Family(all, now);
                float famGoal = Challenges.FamilyGoalKm(ps.Runners.Count, Jogging.Core.AppSettings.Current.familyGoalKm);
                UiControls.Label(p, Jogging.Core.Loc.F("Familie {0} / {1}", Jogging.Core.Units.Dist(famKm * 1000f).ToString("0", Jogging.Core.Loc.Culture), Jogging.Core.Units.FmtDist(famGoal * 1000f, "0")), 17, w - 350f, -480f, 300f, 26f, famKm >= famGoal ? UiTheme.Success : UiTheme.TextMuted, TextAnchor.MiddleRight, bold: true);
                Bar(p, w - 350f, -508f, 300f, 8f, Mathf.Clamp01(famKm / famGoal), famKm >= famGoal ? UiTheme.Success : new Color(1f, 0.82f, 0.35f));
            }

            // the app's settings are on the main dialog ("Läufer wechseln"), not here
            // the playful side in one line (Abenteuer page)
            if (ps != null)
            {
                var fam = new Dictionary<string, List<SessionSummary>>();
                foreach (var r in ps.Runners) fam[r.id] = ps.SummariesOf(r.id);
                var j = Game.JourneyNow(fam);
                int quests = Game.QuestProgress(runs, now).Count(q => q.done);
                int cards = Game.Album(runs).Keys.Count(k => System.Array.Exists(Game.Cards, c => c.key == k));
                string left = j.journey.elevation ? Jogging.Core.Units.FmtElev(j.next.at - j.done) : Jogging.Core.Units.FmtDist((j.next.at - j.done) * 1000f);
                Section(p, "ABENTEUER", -754f, Jogging.Core.Loc.F("Level {0}  ·  {1} bis {2}  ·  Aufgaben {3}/3  ·  Album {4}/{5}",
                    Game.Level(Game.Xp(runs)), left, Jogging.Core.Loc.T(j.next.name), quests, cards, Game.Cards.Length));
            }

            float bw3 = (w - 100f - 2 * 16f) / 3f;
            UiControls.Button(p, "Läufer wechseln", ShowRunners, 50f, -858f, bw3, 52f);
            UiControls.Button(p, Jogging.Core.Loc.T("Abenteuer"), () => ShowAdventure(), 50f + bw3 + 16f, -858f, bw3, 52f, new Color(0.85f, 0.62f, 0.15f));
            UiControls.Button(p, Jogging.Core.Loc.T("Statistik & Erfolge"), () => ShowStats(), 50f + 2 * (bw3 + 16f), -858f, bw3, 52f, new Color(0.55f, 0.42f, 0.85f));
        }

        /// <summary>
        /// The landscape behind the menu is a fresh free run (built while you chose): run it right away.
        /// Only if it was run already (or is a saved route) a new one is generated (scene reload).
        /// </summary>
        private void QuickRun()
        {
            Training.WorkoutRuntime.Selected = null;
            var cur = RouteRuntime.Current;
            bool fresh = RoutePresets.IsUnsavedFreeRun(cur) && (RunSessionUI.Session == null || !RunSessionUI.Session.Started);
            Run(fresh ? cur : RoutePresets.NewFreeRun());
        }

        private void Section(Transform p, string caption, float y, string value)
        {
            UiControls.Label(p, caption, 17, 50f, y, 400f, 26f, UiTheme.TextMuted, bold: true);
            UiControls.Label(p, value, 28, 50f, y - 30f, 760f, 44f, UiTheme.TextPrimary);
        }

        private static string Runs(int n) => n == 1 ? Jogging.Core.Loc.T("1 Lauf") : Jogging.Core.Loc.F("{0} Läufe", n);

        private static string Short(string s, int n) => s.Length > n ? s.Substring(0, n) + "…" : s;
    }
}
