using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Jogging.Profile;

namespace Jogging.UI
{
    /// <summary>
    /// History chart: the last 12 weeks as bars — kilometres, time or runs — one bar per runner and
    /// week (the active runner bright, the family muted), the active runner's weekly goal as a line,
    /// calendar weeks below, and a short summary (average of the last 4 weeks, best week).
    /// </summary>
    public partial class StartMenuUI
    {
        private const int HistoryWeeks = 12;
        private static string historyMetric = "km"; // km | min | runs (kept for the session)

        private static readonly Color[] RunnerColors =
        {
            new Color(0.30f, 0.66f, 0.98f), new Color(0.95f, 0.60f, 0.30f), new Color(0.45f, 0.80f, 0.50f),
            new Color(0.80f, 0.50f, 0.90f), new Color(0.95f, 0.80f, 0.35f), new Color(0.40f, 0.80f, 0.80f),
        };

        private void ShowHistory()
        {
            const float w = 1320f;
            var p = NewPage(w, 900f);
            var ps = ProfileService.Instance;
            if (ps == null) { ShowHome(); return; }
            var me = ps.Profile;
            var now = DateTime.Now;

            UiControls.Label(p, "Verlauf", 34, 40f, -24f, 400f, 48f, UiTheme.TextPrimary, bold: true);
            Cycle(p, "Zeigen", new[] { "km", "min", "runs" }, new[] { "Kilometer pro Woche", "Zeit pro Woche", "Läufe pro Woche" },
                historyMetric, v => { historyMetric = v; ShowHistory(); }, 640f, -28f, 640f);

            // Data: active runner first, then the family (runners who ran in the window).
            var series = new List<(ProfileData runner, RunnerStats.Totals[] weeks)>();
            series.Add((me, RunnerStats.Weekly(ps.SummariesOf(me.id), now, HistoryWeeks)));
            foreach (var r in ps.Runners)
            {
                if (r.id == me.id) continue;
                var wk = RunnerStats.Weekly(ps.SummariesOf(r.id), now, HistoryWeeks);
                foreach (var t in wk) if (t.Runs > 0) { series.Add((r, wk)); break; }
            }
            float Value(RunnerStats.Totals t) => historyMetric == "km" ? t.DistanceM / 1000f : historyMetric == "min" ? t.Seconds / 60f : t.Runs;
            string Unit = historyMetric == "km" ? "km" : historyMetric == "min" ? "min" : Jogging.Core.Loc.T("Läufe");

            float goal = historyMetric == "km" ? (me.weekGoalKm > 0f ? me.weekGoalKm : Challenges.DefaultKm)
                       : historyMetric == "min" ? (me.weekGoalMinutes > 0 ? me.weekGoalMinutes : Challenges.DefaultMinutes)
                       : (me.weekGoalRuns > 0 ? me.weekGoalRuns : Challenges.DefaultRuns);
            float max = goal;
            foreach (var s in series) foreach (var t in s.weeks) max = Mathf.Max(max, Value(t));
            float step = NiceStep(max * 1.1f / 4f);              // round grid steps (1, 2, 2.5, 5 × 10ⁿ)
            int lines = Mathf.Max(1, Mathf.CeilToInt(max * 1.1f / step));
            float top = step * lines;

            // Chart area
            const float cx = 110f, cy = -110f, cw = 1170f, ch = 520f;
            var box = UiTheme.Panel(p, UiTheme.PanelSoft);
            UiControls.Place(box.GetComponent<RectTransform>(), 40f, cy + 20f, 1240f, ch + 90f);
            for (int g = 0; g <= lines; g++)
            {
                float gy = cy - ch + ch * g / lines;
                Rect(p, cx, gy, cw, 1f, new Color(1f, 1f, 1f, 0.08f));
                UiControls.Label(p, Num(step * g), 16, 44f, gy + 12f, 60f, 24f, UiTheme.TextMuted, TextAnchor.MiddleRight);
            }

            float slot = cw / HistoryWeeks, groupW = slot * 0.72f, barW = groupW / series.Count;
            var ws = RunnerStats.WeekStart(now);
            for (int i = 0; i < HistoryWeeks; i++)
            {
                float gx = cx + i * slot + (slot - groupW) * 0.5f;
                for (int k = 0; k < series.Count; k++)
                {
                    float v = Value(series[k].weeks[i]);
                    if (v <= 0f) continue;
                    float h = Mathf.Max(3f, ch * v / top);
                    var col = RunnerColors[k % RunnerColors.Length];
                    if (k > 0) col = Color.Lerp(col, UiTheme.PanelSoft, 0.35f);
                    Rect(p, gx + k * barW + 1f, cy - ch + h, barW - 2f, h, col);
                    if (k == 0 && series.Count == 1) // value on top of the bar (single runner only: no clutter)
                        UiControls.Label(p, Num(v), 15, gx, cy - ch + h + 22f, groupW, 20f, UiTheme.TextPrimary, TextAnchor.MiddleCenter, bold: true);
                }
                var weekStart = ws.AddDays(-7 * (HistoryWeeks - 1 - i));
                bool current = i == HistoryWeeks - 1;
                UiControls.Label(p, current ? "diese" : Jogging.Core.Loc.F("KW {0}", RunnerStats.IsoWeek(weekStart)), 15, cx + i * slot, cy - ch - 6f, slot, 22f,
                    current ? UiTheme.TextPrimary : UiTheme.TextMuted, TextAnchor.MiddleCenter, bold: current);
            }

            // Goal line (active runner)
            float goalY = cy - ch + ch * goal / top;
            Rect(p, cx, goalY + 1f, cw, 2f, new Color(1f, 0.82f, 0.35f, 0.85f));
            UiControls.Label(p, Jogging.Core.Loc.F("Ziel {0} {1}", Num(goal), Unit), 15, cx + cw - 160f, goalY + 24f, 160f, 22f, new Color(1f, 0.82f, 0.35f), TextAnchor.MiddleRight, bold: true);

            // Legend
            float lx = 40f;
            for (int k = 0; k < series.Count; k++)
            {
                var col = RunnerColors[k % RunnerColors.Length];
                if (k > 0) col = Color.Lerp(col, UiTheme.PanelSoft, 0.35f);
                Rect(p, lx, -676f, 18f, 18f, col);
                UiControls.Label(p, series[k].runner.playerName, 18, lx + 26f, -670f, 200f, 30f, k == 0 ? UiTheme.TextPrimary : UiTheme.TextMuted, bold: k == 0);
                lx += 46f + Mathf.Min(200f, series[k].runner.playerName.Length * 11f);
            }

            // Summary for the active runner
            var mine = series[0].weeks;
            float last4 = 0f; for (int i = HistoryWeeks - 4; i < HistoryWeeks; i++) last4 += Value(mine[i]);
            int best = -1; for (int i = 0; i < HistoryWeeks; i++) if (Value(mine[i]) > 0f && (best < 0 || Value(mine[i]) > Value(mine[best]))) best = i;
            int reached = 0; for (int i = 0; i < HistoryWeeks; i++) if (Value(mine[i]) >= goal) reached++;
            string summary = best < 0 ? "In den letzten 12 Wochen noch kein Lauf."
                : Jogging.Core.Loc.F("Ø letzte 4 Wochen: {0} {1}   ·   beste Woche: KW {2} mit {3} {4}   ·   Ziel erreicht: {5} von 12 Wochen", Num(last4 / 4f), Unit, RunnerStats.IsoWeek(ws.AddDays(-7 * (HistoryWeeks - 1 - best))), Num(Value(mine[best])), Unit, reached);
            UiControls.Label(p, summary, 19, 40f, -720f, 1240f, 34f, UiTheme.TextMuted);

            UiControls.Button(p, Jogging.Core.Loc.T("Zurück"), () => ShowStats(), 1080f, -800f, 200f, 56f);
        }

        // 1, 2, 2.5, 5 × 10^n just above v (grid step); whole numbers for counts.
        private static float NiceStep(float v)
        {
            if (v <= 0f) return 1f;
            float e = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(v)));
            bool whole = historyMetric == "runs";
            foreach (var m in new[] { 1f, 2f, 2.5f, 5f, 10f })
                if (m * e >= v && !(whole && m * e < 1f) && !(whole && Mathf.Approximately(m, 2.5f))) return m * e;
            return 10f * e;
        }

        // A plain coloured rectangle, (x, y) = top-left in page coordinates.
        private static void Rect(Transform parent, float x, float y, float w, float h, Color c)
        {
            var go = new GameObject("bar", typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = c;
            go.GetComponent<Image>().raycastTarget = false;
            UiControls.Place(go.GetComponent<RectTransform>(), x, y, w, h);
        }
    }
}
