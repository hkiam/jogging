using System.Linq;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Jogging.Profile;
using Jogging.Training;

namespace Jogging.UI
{
    /// <summary>
    /// Statistics of the active runner: totals per period, bests (longest, fastest 5 km), weekly
    /// challenges, the family's ranking this week, the 12-week history chart and the logbook (last runs → detail with speed and
    /// pulse over time). Plus the achievements page. Everything is computed from the logbook.
    /// </summary>
    public partial class StartMenuUI
    {
        private static readonly Color Gold = new Color(1f, 0.82f, 0.35f);

        private void ShowStats(int logPage = 0)
        {
            const float w = 1320f;
            var p = NewPage(w, 900f);
            var ps = ProfileService.Instance;
            if (ps == null) { ShowHome(); return; }
            var me = ps.Profile;
            var runs = ps.SummariesOf(me.id);
            var now = DateTime.Now;
            ps.CheckAchievements(null); // conditions on the logbook (e.g. streaks) may be met already

            UiControls.Label(p, Jogging.Core.Loc.F("Statistik · {0}", me.playerName), 34, 40f, -24f, 800f, 48f, UiTheme.TextPrimary, bold: true);
            UiControls.Button(p, Jogging.Core.Loc.T("Export (Strava, Garmin …)"), () => ShowExport(), w - 400f, -28f, 360f, 50f, null, 19);

            // ---- left: periods
            float x = 40f, y = -96f;
            UiControls.Label(p, "ZEITRAUM", 16, x, y, 150f, 24f, UiTheme.TextMuted, bold: true);
            UiControls.Label(p, "STRECKE", 16, x + 150f, y, 140f, 24f, UiTheme.TextMuted, TextAnchor.MiddleRight, bold: true);
            UiControls.Label(p, "ZEIT", 16, x + 300f, y, 140f, 24f, UiTheme.TextMuted, TextAnchor.MiddleRight, bold: true);
            UiControls.Label(p, "LÄUFE", 16, x + 450f, y, 110f, 24f, UiTheme.TextMuted, TextAnchor.MiddleRight, bold: true);
            y -= 30f;
            foreach (var (label, t) in RunnerStats.Periods(runs, now))
            {
                bool all = label == "Gesamt";
                // "Gesamt" uses the lifetime stats (they include runs from before the logbook).
                float dist = all ? Mathf.Max(t.DistanceM, me.totalDistanceMeters) : t.DistanceM;
                float secs = all ? Mathf.Max(t.Seconds, me.totalTimeSeconds) : t.Seconds;
                int n = all ? Mathf.Max(t.Runs, me.totalRuns) : t.Runs;
                UiControls.Label(p, label, 22, x, y, 150f, 34f, UiTheme.TextPrimary, bold: all);
                UiControls.Label(p, RunnerStats.Km(dist), 22, x + 150f, y, 140f, 34f, UiTheme.TextPrimary, TextAnchor.MiddleRight);
                UiControls.Label(p, n > 0 ? RunnerStats.Duration(secs) : "–", 22, x + 300f, y, 140f, 34f, UiTheme.TextPrimary, TextAnchor.MiddleRight);
                UiControls.Label(p, n.ToString(), 22, x + 450f, y, 110f, 34f, UiTheme.TextPrimary, TextAnchor.MiddleRight);
                y -= 36f;
            }

            // ---- left: bests
            y -= 18f;
            UiControls.Label(p, "BESTWERTE", 16, x, y, 300f, 24f, UiTheme.TextMuted, bold: true);
            y -= 30f;
            float longest = me.bestDistanceMeters, longestT = 0f, best5k = 0f;
            SessionSummary fastest = null;
            foreach (var r in runs)
            {
                longest = Mathf.Max(longest, r.distanceM);
                longestT = Mathf.Max(longestT, r.seconds);
                if (r.best5kS > 0f && (best5k <= 0f || r.best5kS < best5k)) { best5k = r.best5kS; fastest = r; }
            }
            Best(p, x, ref y, "Längster Lauf", longest > 0f ? RunnerStats.Km(longest) : "–");
            Best(p, x, ref y, "Längste Zeit", longestT > 0f ? RunnerStats.Duration(longestT) : "–");
            Best(p, x, ref y, "Schnellste 5 km", best5k > 0f ? $"{RunnerStats.Duration(best5k)}  ({RunnerStats.When(fastest.start, now)})" : "noch keine 5 km am Stück");
            Best(p, x, ref y, "Ø Tempo gesamt", RunnerStats.Pace(me.totalDistanceMeters, me.totalTimeSeconds));
            int streak = RunnerStats.StreakDays(runs, now);
            Best(p, x, ref y, Jogging.Core.Loc.T("Serie"), streak == 1 ? Jogging.Core.Loc.T("1 Tag") : Jogging.Core.Loc.F("{0} Tage", streak));

            // ---- left: weekly challenges
            y -= 18f;
            UiControls.Label(p, "WOCHENZIELE", 16, x, y, 300f, 24f, UiTheme.TextMuted, bold: true);
            y -= 32f;
            foreach (var c in Challenges.ThisWeek(runs, now, me))
            {
                UiControls.Label(p, c.Challenge.Title + (c.Done ? Jogging.Core.Loc.T("  – geschafft") : ""), 19, x, y, 330f, 28f, c.Done ? UiTheme.Success : UiTheme.TextPrimary);
                UiControls.Label(p, $"{Num(c.Value)} / {Num(c.Challenge.Target)} {Jogging.Core.Loc.T(c.Challenge.Unit)}", 17, x + 330f, y, 230f, 28f, UiTheme.TextMuted, TextAnchor.MiddleRight);
                Bar(p, x, y - 30f, 560f, 8f, c.Fraction, c.Done ? UiTheme.Success : UiTheme.Accent);
                y -= 48f;
            }

            // ---- right: family ranking
            float rx = 680f, ry = -96f;
            if (ps.Runners.Count > 1)
            {
                float fGoal = Challenges.FamilyGoalKm(ps.Runners.Count, Jogging.Core.AppSettings.Current.familyGoalKm);
                UiControls.Label(p, "FAMILIE DIESE WOCHE", 16, rx, ry, 400f, 24f, UiTheme.TextMuted, bold: true);
                // Family goal stepper (5 km steps; 0 = automatic 10 km per runner)
                Text goalLbl = null;
                goalLbl = UiControls.Label(p, Jogging.Core.Loc.F("Ziel {0}", Jogging.Core.Units.FmtDist(fGoal * 1000f, "0")), 16, rx + 330f, ry, 150f, 24f, UiTheme.TextMuted, TextAnchor.MiddleRight, bold: true);
                UiControls.Button(p, "-", () => { var st = Jogging.Core.AppSettings.Current; st.familyGoalKm = Mathf.Max(5f, Challenges.FamilyGoalKm(ps.Runners.Count, st.familyGoalKm) - 5f); Jogging.Core.AppSettings.Save(); ShowStats(logPage); }, rx + 490f, ry + 4f, 50f, 30f, null, 18);
                UiControls.Button(p, "+", () => { var st = Jogging.Core.AppSettings.Current; st.familyGoalKm = Mathf.Min(500f, Challenges.FamilyGoalKm(ps.Runners.Count, st.familyGoalKm) + 5f); Jogging.Core.AppSettings.Save(); ShowStats(logPage); }, rx + 548f, ry + 4f, 50f, 30f, null, 18);
                ry -= 30f;
                var byRunner = new Dictionary<string, List<SessionSummary>>();
                foreach (var r in ps.Runners) byRunner[r.id] = ps.SummariesOf(r.id);
                var rank = RunnerStats.WeeklyRanking(byRunner, now);
                if (rank.Count == 0) { UiControls.Label(p, "diese Woche ist noch niemand gelaufen", 19, rx, ry, 600f, 30f, UiTheme.TextMuted); ry -= 34f; }
                for (int i = 0; i < rank.Count && i < 5; i++)
                {
                    string name = "?";
                    foreach (var r in ps.Runners) if (r.id == rank[i].Key) name = r.playerName;
                    var col = i == 0 ? Gold : UiTheme.TextPrimary;
                    UiControls.Label(p, $"{i + 1}.", 22, rx, ry, 40f, 32f, col, bold: true);
                    UiControls.Label(p, name, 22, rx + 44f, ry, 360f, 32f, col, bold: rank[i].Key == me.id);
                    UiControls.Label(p, RunnerStats.Km(rank[i].Value), 22, rx + 400f, ry, 200f, 32f, col, TextAnchor.MiddleRight);
                    ry -= 34f;
                }
                ry -= 18f;
            }

            // ---- right: logbook
            UiControls.Label(p, "LOGBUCH", 16, rx, ry, 300f, 24f, UiTheme.TextMuted, bold: true);
            ry -= 30f;
            var recent = new List<SessionSummary>(runs);
            recent.Reverse(); // newest first
            int perPage = Mathf.Max(3, Mathf.FloorToInt((ry + 760f) / 62f));
            int pages = Mathf.Max(1, Mathf.CeilToInt(recent.Count / (float)perPage));
            logPage = Mathf.Clamp(logPage, 0, pages - 1);
            if (recent.Count == 0) UiControls.Label(p, "noch keine Läufe im Logbuch", 19, rx, ry, 600f, 30f, UiTheme.TextMuted);
            for (int k = logPage * perPage; k < Mathf.Min(recent.Count, (logPage + 1) * perPage); k++)
            {
                var r = recent[k];
                string what = Jogging.Core.Loc.T(!string.IsNullOrEmpty(r.workoutName) ? r.workoutName : r.routeName);
                string line = $"{RunnerStats.Km(r.distanceM)} · {RunnerStats.Duration(r.seconds)} · {RunnerStats.Pace(r.distanceM, r.seconds)}" + (r.avgHr > 0 ? $" · {Jogging.Core.Loc.T("Ø ")}{r.avgHr}" : "");
                var rs = r; int pg = logPage;
                var b = UiControls.Button(p, "", () => ShowRun(rs, pg), rx, ry, 600f, 56f, UiTheme.PanelSoft);
                UiControls.Label(b.transform, $"{When(r.start, now)}  ·  {Short(what, 26)}", 18, 16f, -4f, 570f, 26f, UiTheme.TextMuted);
                UiControls.Label(b.transform, line, 20, 16f, -28f, 570f, 26f, UiTheme.TextPrimary, bold: true);
                ry -= 62f;
            }
            if (pages > 1)
            {
                UiControls.Button(p, "◀", () => ShowStats(logPage - 1), rx, -800f, 70f, 52f);
                UiControls.Label(p, $"{logPage + 1} / {pages}", 20, rx + 80f, -800f, 110f, 52f, UiTheme.TextMuted, TextAnchor.MiddleCenter);
                UiControls.Button(p, "▶", () => ShowStats(logPage + 1), rx + 200f, -800f, 70f, 52f);
            }

            UiControls.Button(p, Jogging.Core.Loc.F("Erfolge  ({0} / {1})", me.unlockedAchievements.Count, AchievementCatalog.All.Length), ShowAchievements, 40f, -800f, 360f, 56f, new Color(0.55f, 0.42f, 0.85f));
            UiControls.Button(p, Jogging.Core.Loc.T("Verlauf (12 Wochen)"), () => ShowHistory(), 416f, -800f, 250f, 56f, UiTheme.Accent);
            UiControls.Button(p, Jogging.Core.Loc.T("Zurück"), () => ShowHome(), 1080f, -800f, 200f, 56f);
        }

        private static void Best(Transform p, float x, ref float y, string label, string value)
        {
            UiControls.Label(p, label, 20, x, y, 240f, 30f, UiTheme.TextMuted);
            UiControls.Label(p, value, 21, x + 240f, y, 320f, 30f, UiTheme.TextPrimary, TextAnchor.MiddleRight, bold: true);
            y -= 32f;
        }

        private static void Bar(Transform p, float x, float y, float w, float h, float f, Color c)
        {
            var bg = UiTheme.Panel(p, UiTheme.ButtonBg);
            UiControls.Place(bg.GetComponent<RectTransform>(), x, y, w, h);
            if (f <= 0f) return;
            var fill = UiTheme.Panel(bg.transform, c).GetComponent<RectTransform>();
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(Mathf.Clamp01(f), 1f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
        }

        private static string Num(float v) => (v >= 10f || Mathf.Approximately(v, Mathf.Round(v)) ? v.ToString("0", Jogging.Core.Loc.Culture) : v.ToString("0.0", Jogging.Core.Loc.Culture));

        private static string When(string iso, DateTime now)
        {
            var t = RunnerStats.ParseUtc(iso).ToLocalTime();
            return $"{RunnerStats.When(iso, now)} {t:HH:mm}";
        }

        // ------------------------------------------------------------------ one run

        private void ShowRun(SessionSummary s, int backPage)
        {
            const float w = 1320f;
            var p = NewPage(w, 900f);
            var ps = ProfileService.Instance;
            var rec = ps != null ? ps.Sessions.Load(s.runnerId, s.file) : null;
            var t = RunnerStats.ParseUtc(s.start).ToLocalTime();
            string what = !string.IsNullOrEmpty(s.workoutName) ? $"{Jogging.Core.Loc.T(s.workoutName)} · {Jogging.Core.Loc.T(s.routeName)}" : Jogging.Core.Loc.T(s.routeName);
            UiControls.Label(p, t.ToString(Jogging.Core.Loc.En ? "dddd, d MMMM yyyy · HH:mm" : "dddd, dd.MM.yyyy · HH:mm", Jogging.Core.Loc.Culture), 30, 40f, -24f, 900f, 44f, UiTheme.TextPrimary, bold: true);
            UiControls.Label(p, what + (s.source == "belt" ? Jogging.Core.Loc.T(" · Laufband") : "") + (s.workoutCompleted ? Jogging.Core.Loc.T(" · vollständig") : ""), 20, 40f, -70f, 1200f, 30f, UiTheme.TextMuted);
            string seen = Jogging.World.Wayside.SightsText(s.sights ?? new List<string>()), ways = Jogging.World.Wayside.SurfacesText(s.surfaceM);
            if (seen != "" || ways != "")
                UiControls.Label(p, string.Join("   ·   ", new[] { seen != "" ? Jogging.Core.Loc.F("Unterwegs: {0}", seen) : null, ways != "" ? Jogging.Core.Loc.F("Wege: {0}", ways) : null }.Where(x => x != null).ToArray()), 16, 660f, -24f, 620f, 44f, UiTheme.TextMuted);

            var fig = new List<(string, string)>
            {
                ("Distanz", RunnerStats.Km(s.distanceM)), ("Zeit", RunnerStats.Duration(s.seconds)),
                ("Ø Tempo", RunnerStats.Pace(s.distanceM, s.seconds)), ("Höhenmeter", "+" + Jogging.Core.Units.FmtElev(s.gainM)),
                ("Max. Tempo", Jogging.Core.Units.FmtSpeed(s.maxKmh)),
            };
            if (s.best5kS > 0f) fig.Add(("5 km", RunnerStats.Duration(s.best5kS)));
            if (s.kcal > 0f) fig.Add(("Energie", $"{s.kcal:0} kcal"));
            if (s.avgHr > 0) { fig.Add(("Ø Puls", $"{s.avgHr}")); fig.Add(("Max. Puls", $"{s.maxHr}")); }
            float fx = 40f, fw = Mathf.Min(155f, 1240f / fig.Count);
            foreach (var (k, v) in fig)
            {
                UiControls.Label(p, Jogging.Core.Loc.T(k).ToUpper(Jogging.Core.Loc.Culture), 15, fx, -114f, fw - 5f, 22f, UiTheme.TextMuted, bold: true);
                UiControls.Label(p, v, fw < 150f ? 23 : 26, fx, -138f, fw - 5f, 36f, UiTheme.TextPrimary, bold: true);
                fx += fw;
            }

            // Chart: speed (blue), pulse (red), incline (grey).
            var box = UiTheme.Panel(p, UiTheme.PanelSoft);
            UiControls.Place(box.GetComponent<RectTransform>(), 40f, -196f, 1240f, 440f);
            if (rec != null && rec.samples.Count > 1)
            {
                int n = rec.samples.Count;
                float[] kmh = new float[n], hr = new float[n], inc = new float[n];
                for (int i = 0; i < n; i++) { kmh[i] = rec.samples[i].kmh; hr[i] = rec.samples[i].hr; inc[i] = rec.samples[i].incline + 20f; }
                var go = new GameObject("Chart", typeof(RectTransform), typeof(CanvasRenderer), typeof(LineChart));
                go.transform.SetParent(box.transform, false);
                UiControls.Place(go.GetComponent<RectTransform>(), 20f, -20f, 1200f, 360f);
                var chart = go.GetComponent<LineChart>();
                chart.color = Color.white;
                chart.Add(inc, new Color(0.62f, 0.66f, 0.74f, 0.6f), 10f, 40f); // incline -10…+20 % in the lower area
                chart.Add(kmh, UiTheme.Accent, 0f, float.NaN);
                if (s.avgHr > 0) chart.Add(hr, UiTheme.Danger);
                UiControls.Label(box.transform, "Tempo", 17, 20f, -392f, 140f, 30f, UiTheme.Accent, bold: true);
                if (s.avgHr > 0) UiControls.Label(box.transform, "Puls", 17, 160f, -392f, 140f, 30f, UiTheme.Danger, bold: true);
                UiControls.Label(box.transform, "Steigung", 17, 300f, -392f, 160f, 30f, new Color(0.62f, 0.66f, 0.74f), bold: true);
                UiControls.Label(box.transform, $"0:00 … {RunnerStats.Duration(rec.samples[n - 1].t)}", 17, 900f, -392f, 320f, 30f, UiTheme.TextMuted, TextAnchor.MiddleRight);
            }
            else UiControls.Label(box.transform, "keine Messpunkte", 22, 20f, -200f, 1200f, 40f, UiTheme.TextMuted, TextAnchor.MiddleCenter);

            // Time per heart rate zone.
            if (s.hrZoneSeconds != null && s.hrZoneSeconds.Count == 5)
            {
                float total = 0f; foreach (var z in s.hrZoneSeconds) total += z;
                UiControls.Label(p, "ZEIT PRO PULSZONE", 16, 40f, -660f, 400f, 24f, UiTheme.TextMuted, bold: true);
                float zx = 40f;
                for (int z = 1; z <= 5; z++)
                {
                    float f = total > 0f ? s.hrZoneSeconds[z - 1] / total : 0f;
                    UiControls.Label(p, Jogging.Core.Loc.F("Zone {0}  {1}", z, RunnerStats.Duration(s.hrZoneSeconds[z - 1])), 17, zx, -688f, 240f, 26f, HeartRateMonitor.ZoneColor(z), bold: true);
                    Bar(p, zx, -718f, 230f, 10f, f, HeartRateMonitor.ZoneColor(z));
                    zx += 248f;
                }
            }

            // Delete this run (second click confirms): logbook and lifetime stats.
            Text del = null;
            bool armed = false;
            del = UiControls.Button(p, "Lauf löschen", () =>
            {
                if (!armed) { armed = true; del.text = Jogging.Core.Loc.T("Wirklich löschen?"); return; }
                ps.DeleteRun(s);
                ShowStats(backPage);
            }, 40f, -800f, 300f, 56f, UiTheme.Danger, 20).GetComponentInChildren<Text>();

            // Export for Strava, Garmin Connect & co. (TCX to Downloads).
            if (rec != null)
            {
                Text exp = null;
                exp = UiControls.Button(p, "Export (TCX)", () =>
                {
                    try
                    {
                        string path = TcxExport.SaveTo(ProfileService.ExportFolder, rec, ps.Profile.playerName, false);
                        exp.text = Jogging.Core.Loc.F("In „{0}“ gespeichert", ExportFolderName());
                        Debug.Log($"[Jogging] Lauf exportiert: {path}");
                        Application.OpenURL("file://" + System.IO.Path.GetDirectoryName(path));
                    }
                    catch (Exception e) { exp.text = Jogging.Core.Loc.T("Export fehlgeschlagen"); Debug.LogWarning($"[Jogging] Export: {e.Message}"); }
                }, 360f, -800f, 360f, 56f, UiTheme.Accent, 20).GetComponentInChildren<Text>();
            }

            UiControls.Button(p, Jogging.Core.Loc.T("Zurück"), () => ShowStats(backPage), 1080f, -800f, 200f, 56f);
        }

        // ------------------------------------------------------------------ achievements

        private void ShowAchievements()
        {
            const float w = 1320f;
            var p = NewPage(w, 900f);
            var me = ProfileService.Instance.Profile;
            UiControls.Label(p, Jogging.Core.Loc.F("Erfolge · {0}   {1} / {2}", me.playerName, me.unlockedAchievements.Count, AchievementCatalog.All.Length), 34, 40f, -24f, 1000f, 48f, UiTheme.TextPrimary, bold: true);
            const int cols = 5;
            const float tw = 240f, th = 118f, gap = 10f;
            int i = 0;
            foreach (var a in AchievementCatalog.All)
            {
                bool got = me.unlockedAchievements.Contains(a.Id);
                float x = 40f + (i % cols) * (tw + gap), y = -92f - (i / cols) * (th + gap);
                var tile = UiTheme.Panel(p, got ? new Color(0.30f, 0.27f, 0.14f, 1f) : UiTheme.PanelSoft);
                UiControls.Place(tile.GetComponent<RectTransform>(), x, y, tw, th);
                UiControls.Label(tile.transform, Jogging.Core.Loc.T(a.Group).ToUpper(Jogging.Core.Loc.Culture), 14, 14f, -8f, tw - 28f, 20f, got ? Gold : UiTheme.TextMuted, bold: true);
                UiControls.Label(tile.transform, a.Title, 22, 14f, -30f, tw - 28f, 34f, got ? UiTheme.TextPrimary : UiTheme.TextMuted, bold: true);
                var hint = UiControls.Label(tile.transform, a.Hint, 16, 14f, -66f, tw - 28f, 44f, UiTheme.TextMuted);
                hint.horizontalOverflow = HorizontalWrapMode.Wrap;
                i++;
            }
            UiControls.Button(p, Jogging.Core.Loc.T("Zurück"), () => ShowStats(), 1080f, -820f, 200f, 56f);
        }
    }
}
