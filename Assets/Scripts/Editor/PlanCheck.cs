using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Jogging.Training;

namespace Jogging.EditorTools
{
    /// <summary>
    /// Self-check of the training plans: every built-in plan has weeks × perWeek valid workouts that
    /// get longer (5 km plan ends with 5 km), next session / finished, week by date, behind schedule.
    /// Headless: -executeMethod Jogging.EditorTools.PlanCheck.RunBatch (part of Tools/check.sh)
    /// </summary>
    public static class PlanCheck
    {
        [MenuItem("Jogging/Lauf/Trainingspläne prüfen")]
        public static void RunMenu() => EditorUtility.DisplayDialog("Jogging", Run() ? "Alle Prüfungen bestanden." : "Fehler – siehe Console.", "OK");

        public static void RunBatch() => EditorApplication.Exit(Run() ? 0 : 1);

        public static bool Run()
        {
            var fails = new List<string>();
            void Ok(bool c, string what) { if (!c) fails.Add(what); }

            var ids = new HashSet<string>();
            foreach (var p in TrainingPlans.All())
            {
                Ok(ids.Add(p.id), $"Plan-Id doppelt: {p.id}");
                Ok(p.sessions.Count == p.weeks * p.perWeek, $"{p.name}: {p.sessions.Count} Einheiten statt {p.weeks * p.perWeek}");
                for (int i = 0; i < p.sessions.Count; i++)
                {
                    var wd = p.sessions[i].workout;
                    var back = WorkoutStore.FromJson(WorkoutStore.ToJson(wd));
                    Ok(back != null && back.segments.Count == wd.segments.Count, $"{p.name} #{i}: ungültiges Workout");
                    Ok(p.sessions[i].week == i / p.perWeek + 1, $"{p.name} #{i}: Woche {p.sessions[i].week}");
                    foreach (var g in wd.segments) Ok(g.speedKmh <= 16f && (!g.setIncline || g.inclinePercent <= 12f), $"{p.name} #{i}: Ziel außerhalb der Grenzen");
                }
                Ok(p.sessions[p.sessions.Count - 1].workout.Minutes > p.sessions[0].workout.Minutes, $"{p.name}: wird nicht länger");
            }
            var k5 = TrainingPlans.Find("plan:5k-6");
            float fin = 0f; foreach (var g in k5.sessions[k5.sessions.Count - 1].workout.segments) fin += g.distanceM;
            Ok(Mathf.Approximately(fin, 5000f), "5-km-Plan endet nicht mit 5 km");
            Ok(TrainingPlans.Find("plan:gibtsnicht") == null, "unbekannter Plan gefunden");

            // Progress
            var done = new List<int>();
            Ok(PlanProgress.Next(k5, done) == 0, "erste Einheit");
            done.AddRange(new[] { 0, 1, 3 });
            Ok(PlanProgress.Next(k5, done) == 2, "ausgelassene Einheit kommt als Nächstes");
            var start = new DateTime(2026, 9, 1);
            Ok(PlanProgress.WeekByDate(k5, start, start.AddDays(6)) == 1 && PlanProgress.WeekByDate(k5, start, start.AddDays(7)) == 2
               && PlanProgress.WeekByDate(k5, start, start.AddDays(100)) == 6, "Woche nach Datum");
            Ok(PlanProgress.Behind(k5, done, start, start.AddDays(3)) == 0, "Rückstand in Woche 1");
            Ok(PlanProgress.Behind(k5, done, start, start.AddDays(8)) == 1, $"Rückstand in Woche 2: {PlanProgress.Behind(k5, done, start, start.AddDays(8))}");
            var all = new List<int>(); for (int i = 0; i < k5.sessions.Count; i++) all.Add(i);
            Ok(PlanProgress.Finished(k5, all) && PlanProgress.Next(k5, all) == -1, "Plan fertig");

            if (fails.Count == 0) Debug.Log("[PlanCheck] Alle Prüfungen bestanden.");
            else Debug.LogError("[PlanCheck] FAIL → " + string.Join("; ", fails));
            return fails.Count == 0;
        }
    }
}
