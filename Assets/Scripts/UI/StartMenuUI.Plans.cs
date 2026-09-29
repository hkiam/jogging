using System;
using UnityEngine;
using UnityEngine.UI;
using Jogging.Profile;
using Jogging.Training;

namespace Jogging.UI
{
    /// <summary>
    /// Training plans: the active plan of the runner (progress grid: one row per week, one cell per
    /// session; next session with "Starten"; on schedule or behind; end the plan) and the list of
    /// plans to start. A session is a workout run on the current landscape (any route works).
    /// </summary>
    public partial class StartMenuUI
    {
        private void ShowPlans()
        {
            const float w = 1320f;
            var p = NewPage(w, 900f);
            var ps = ProfileService.Instance;
            if (ps == null) { ShowHome(); return; }
            var me = ps.Profile;
            var active = ps.ActivePlan;
            var now = DateTime.Now;
            UiControls.Label(p, "Trainingspläne", 34, 40f, -24f, 600f, 48f, UiTheme.TextPrimary, bold: true);

            float y = -96f;
            if (active != null)
            {
                var start = RunnerStats.ParseUtc(me.planStarted).ToLocalTime();
                int week = PlanProgress.WeekByDate(active, start, now);
                int behind = PlanProgress.Behind(active, me.planDone, start, now);
                int next = PlanProgress.Next(active, me.planDone);

                var box = UiTheme.Panel(p, UiTheme.PanelSoft);
                UiControls.Place(box.GetComponent<RectTransform>(), 40f, y, 1240f, 470f);
                var t = box.transform;
                UiControls.Label(t, active.name, 28, 20f, -12f, 700f, 40f, UiTheme.TextPrimary, bold: true);
                UiControls.Label(t, Jogging.Core.Loc.F("Woche {0} von {1}  ·  {2} von {3} Einheiten", week, active.weeks, me.planDone.Count, active.sessions.Count) +
                    (behind > 0 ? Jogging.Core.Loc.F("  ·  {0} {1} im Rückstand", behind, Jogging.Core.Loc.T(behind == 1 ? "Einheit" : "Einheiten")) : Jogging.Core.Loc.T("  ·  im Plan")),
                    19, 20f, -54f, 1000f, 30f, behind > 0 ? new Color(1f, 0.78f, 0.35f) : UiTheme.Success);

                // Grid: week rows × session cells
                float gy = -100f, cellW = 150f, cellH = 38f;
                for (int wk = 1; wk <= active.weeks; wk++)
                {
                    UiControls.Label(t, Jogging.Core.Loc.F("Woche {0}", wk), 17, 20f, gy, 110f, cellH, wk == week ? UiTheme.TextPrimary : UiTheme.TextMuted, bold: wk == week);
                    int col = 0;
                    for (int i = 0; i < active.sessions.Count; i++)
                    {
                        if (active.sessions[i].week != wk) continue;
                        bool done = me.planDone.Contains(i), isNext = i == next;
                        var cell = UiTheme.Panel(t, done ? UiTheme.Success : isNext ? UiTheme.Accent : UiTheme.ButtonBg);
                        UiControls.Place(cell.GetComponent<RectTransform>(), 130f + col * (cellW + 8f), gy - 2f, cellW, cellH - 4f);
                        UiControls.Label(cell.transform, done ? "geschafft" : isNext ? "als Nächstes" : $"{active.sessions[i].workout.Minutes:0} min",
                            15, 0f, 0f, cellW, cellH - 4f, UiTheme.TextPrimary, TextAnchor.MiddleCenter, bold: done || isNext);
                        col++;
                    }
                    gy -= cellH + 4f;
                }

                if (next >= 0)
                {
                    var wd = active.sessions[next].workout;
                    UiControls.Label(t, Jogging.Core.Loc.F("Nächste Einheit: {0}", Jogging.Core.Loc.T(wd.name)), 22, 660f, -100f, 560f, 32f, UiTheme.TextPrimary, bold: true);
                    var desc = UiControls.Label(t, $"~{wd.Minutes:0} min · {Jogging.Core.Loc.T(wd.description)}", 18, 660f, -136f, 560f, 52f, UiTheme.TextMuted);
                    desc.horizontalOverflow = HorizontalWrapMode.Wrap;
                    Profile(t, wd, 660f, -196f, 540f, 60f);
                    UiControls.Button(t, Jogging.Core.Loc.T("▶  Einheit starten"), () => StartPlanSession(active, next), 660f, -280f, 300f, 60f, UiTheme.Success, 24);
                }

                Text stop = null; bool armed = false;
                stop = UiControls.Button(t, "Plan beenden", () =>
                {
                    if (!armed) { armed = true; stop.text = Jogging.Core.Loc.T("Wirklich beenden? (Fortschritt geht verloren)"); return; }
                    ps.StopPlan();
                    ShowPlans();
                }, 660f, -400f, 560f, 48f, UiTheme.Danger, 18).GetComponentInChildren<Text>();
                y -= 490f;
            }

            UiControls.Label(p, active != null ? "ANDERE PLÄNE" : "PLAN WÄHLEN", 16, 40f, y, 400f, 24f, UiTheme.TextMuted, bold: true);
            y -= 30f;
            foreach (var plan in TrainingPlans.All())
            {
                if (active != null && plan.id == active.id) continue;
                var row = UiTheme.Panel(p, UiTheme.PanelSoft);
                UiControls.Place(row.GetComponent<RectTransform>(), 40f, y, 1240f, 76f);
                bool finished = me.plansFinished.Contains(plan.id);
                UiControls.Label(row.transform, Jogging.Core.Loc.T(plan.name) + (finished ? Jogging.Core.Loc.T("   (schon geschafft)") : ""), 23, 20f, -6f, 800f, 34f, UiTheme.TextPrimary, bold: true);
                UiControls.Label(row.transform, Jogging.Core.Loc.F("{0} Wochen · {1} Einheiten · {2}", plan.weeks, plan.sessions.Count, Jogging.Core.Loc.T(plan.description)), 17, 20f, -42f, 900f, 28f, UiTheme.TextMuted);
                var pl = plan;
                Text lbl = null; bool confirm = false;
                lbl = UiControls.Button(row.transform, "Plan starten", () =>
                {
                    if (active != null && !confirm) { confirm = true; lbl.text = Jogging.Core.Loc.T("Aktuellen ersetzen?"); return; }
                    ps.StartPlan(pl.id);
                    ShowPlans();
                }, 1000f, -12f, 220f, 52f, UiTheme.Accent, 19).GetComponentInChildren<Text>();
                y -= 86f;
            }

            UiControls.Button(p, Jogging.Core.Loc.T("Workouts"), () => ShowWorkouts(), 820f, -800f, 240f, 56f);
            UiControls.Button(p, Jogging.Core.Loc.T("Zurück"), () => ShowHome(), 1080f, -800f, 200f, 56f);
        }

        private void StartPlanSession(TrainingPlan plan, int index)
        {
            var wd = plan.sessions[index].workout.Clone();
            wd.planId = plan.id; wd.planIndex = index;
            StartWorkout(wd);
        }
    }
}
