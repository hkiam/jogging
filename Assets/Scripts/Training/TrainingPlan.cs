using System;
using System.Collections.Generic;

namespace Jogging.Training
{
    /// <summary>
    /// A training plan: a sequence of workouts over several weeks ("5 km in 6 Wochen"). Plain data;
    /// the runner's progress (start date, completed sessions) lives in their profile. A session counts
    /// once its workout is completed; the plan suggests the next open session, whatever the date —
    /// the date only tells whether the runner is on schedule.
    /// </summary>
    [Serializable]
    public class TrainingPlan
    {
        public string id = "";
        public string name = "";
        public string description = "";
        public int weeks;
        public int perWeek;
        public List<PlanSession> sessions = new List<PlanSession>();
    }

    [Serializable]
    public class PlanSession
    {
        public int week;           // 1-based
        public WorkoutDoc workout;
    }

    /// <summary>Where a runner stands in a plan (pure logic, tested by PlanCheck).</summary>
    public static class PlanProgress
    {
        /// <summary>Index of the next open session, or -1 when all are done.</summary>
        public static int Next(TrainingPlan plan, ICollection<int> done)
        {
            for (int i = 0; i < plan.sessions.Count; i++) if (!done.Contains(i)) return i;
            return -1;
        }

        public static bool Finished(TrainingPlan plan, ICollection<int> done) => Next(plan, done) < 0;

        /// <summary>Plan week by the calendar (1-based, from the start date; clamped to the plan).</summary>
        public static int WeekByDate(TrainingPlan plan, DateTime startLocal, DateTime nowLocal)
        {
            int w = (int)Math.Floor((nowLocal.Date - startLocal.Date).TotalDays / 7.0) + 1;
            return Math.Max(1, Math.Min(plan.weeks, w));
        }

        /// <summary>Sessions behind schedule: those of the weeks before the current one still open.</summary>
        public static int Behind(TrainingPlan plan, ICollection<int> done, DateTime startLocal, DateTime nowLocal)
        {
            int week = (int)Math.Floor((nowLocal.Date - startLocal.Date).TotalDays / 7.0) + 1;
            int n = 0;
            for (int i = 0; i < plan.sessions.Count; i++)
                if (plan.sessions[i].week < week && !done.Contains(i)) n++;
            return n;
        }
    }

    /// <summary>Built-in plans (workouts generated: run/walk intervals, zone 2 runs, known workouts).</summary>
    public static class TrainingPlans
    {
        private const float Walk = 5.5f, Easy = 8f;

        private static WorkoutSegment Seg(string kind, float min, float kmh, int zone = 0, string label = "") =>
            new WorkoutSegment { kind = kind, durationS = min * 60f, speedKmh = kmh, hrZone = zone, label = label };

        // Warm-up walk, n × (run / walk), cool-down walk.
        private static WorkoutDoc RunWalk(string name, float runMin, float walkMin, int reps, float runKmh = Easy)
        {
            var d = new WorkoutDoc { name = name, description = Jogging.Core.Loc.F("{0} × {1} laufen / {2} gehen", reps, Clock(runMin), Clock(walkMin)) };
            d.segments.Add(Seg("warmup", 5f, Walk, 0, "Einlaufen (gehen)"));
            for (int i = 0; i < reps; i++)
            {
                d.segments.Add(Seg("run", runMin, runKmh));
                if (i < reps - 1 && walkMin > 0f) d.segments.Add(Seg("walk", walkMin, Walk));
            }
            d.segments.Add(Seg("cooldown", 5f, Walk, 0, "Auslaufen (gehen)"));
            return d;
        }

        private static WorkoutDoc Continuous(string name, float min, float kmh, int zone = 0)
        {
            var d = new WorkoutDoc { name = name, description = zone > 0 ? Jogging.Core.Loc.F("{0:0} min in Zone {1}", min, zone) : Jogging.Core.Loc.F("{0:0} min am Stück", min) };
            d.segments.Add(Seg("warmup", 5f, zone > 0 ? 0f : Walk, zone > 0 ? 1 : 0));
            d.segments.Add(Seg("run", min, zone > 0 ? 0f : kmh, zone));
            d.segments.Add(Seg("cooldown", 5f, zone > 0 ? 0f : Walk, zone > 0 ? 1 : 0));
            return d;
        }

        private static string Clock(float min) => min < 1f ? $"{min * 60f:0} s" : min % 1f == 0f ? $"{min:0} min" : $"{(int)min}:{(min % 1f) * 60f:00} min";

        private static TrainingPlan Plan(string id, string name, string desc, int weeks, int perWeek, Func<int, int, WorkoutDoc> session)
        {
            var p = new TrainingPlan { id = id, name = name, description = desc, weeks = weeks, perWeek = perWeek };
            for (int w = 1; w <= weeks; w++)
                for (int s = 1; s <= perWeek; s++)
                {
                    var wd = session(w, s);
                    wd.id = $"{id}#{p.sessions.Count}";
                    p.sessions.Add(new PlanSession { week = w, workout = wd });
                }
            return p;
        }

        public static List<TrainingPlan> All()
        {
            var list = new List<TrainingPlan>();

            // From walking to 5 km at a stretch: run/walk intervals growing week by week.
            list.Add(Plan("plan:5k-6", "5 km in 6 Wochen", "vom Gehen zu 5 km am Stück · 3 × pro Woche", 6, 3, (w, s) =>
            {
                switch (w)
                {
                    case 1: return RunWalk(Jogging.Core.Loc.F("Woche 1 · Lauf {0}", s), 1f, 1.5f, 8);
                    case 2: return RunWalk(Jogging.Core.Loc.F("Woche 2 · Lauf {0}", s), 2f, 1.5f, 6);
                    case 3: return RunWalk(Jogging.Core.Loc.F("Woche 3 · Lauf {0}", s), 3f, 1.5f, 5);
                    case 4: return RunWalk(Jogging.Core.Loc.F("Woche 4 · Lauf {0}", s), 5f, 2f, 3);
                    case 5: return s < 3 ? RunWalk(Jogging.Core.Loc.F("Woche 5 · Lauf {0}", s), 8f, 2f, 3) : RunWalk(Jogging.Core.Loc.T("Woche 5 · Lauf 3"), 12f, 2f, 2);
                    default:
                        if (s == 1) return RunWalk(Jogging.Core.Loc.T("Woche 6 · Lauf 1"), 15f, 2f, 2);
                        if (s == 2) return Continuous(Jogging.Core.Loc.T("Woche 6 · 25 min am Stück"), 25f, Easy);
                        var fin = new WorkoutDoc { name = "Woche 6 · 5 km!", description = "5 km am Stück" };
                        fin.segments.Add(Seg("warmup", 5f, Walk, 0, "Einlaufen (gehen)"));
                        fin.segments.Add(new WorkoutSegment { kind = "run", label = "5 km", distanceM = 5000f, speedKmh = Easy });
                        fin.segments.Add(Seg("cooldown", 5f, Walk, 0, "Auslaufen (gehen)"));
                        return fin;
                }
            }));

            // Aerobic base by heart rate: zone 2 runs getting longer (chest strap recommended).
            list.Add(Plan("plan:base-4", "Grundlage in 4 Wochen", "Zone-2-Läufe von 25 auf 45 min · 3 × pro Woche · mit Pulsgurt", 4, 3, (w, s) =>
                Continuous(Jogging.Core.Loc.F("Woche {0} · Grundlage {1}", w, s), 20f + 5f * w + (s == 3 ? 5f : 0f), 0f, 2)));

            // Faster: intervals, an easy run and a tempo run each week, a little more every week.
            list.Add(Plan("plan:faster-6", "Schneller werden in 6 Wochen", "Intervalle, locker, Tempo · 3 × pro Woche", 6, 3, (w, s) =>
            {
                if (s == 2) return Continuous(Jogging.Core.Loc.F("Woche {0} · locker", w), 25f + 2f * w, 8.5f);
                if (s == 1)
                {
                    int reps = 3 + (w + 1) / 2;
                    var d = new WorkoutDoc { name = Jogging.Core.Loc.F("Woche {0} · Intervalle", w), description = Jogging.Core.Loc.F("{0} × 3 min schnell", reps) };
                    d.segments.Add(Seg("warmup", 8f, 7f));
                    for (int i = 0; i < reps; i++) { d.segments.Add(Seg("fast", 3f, 10.5f + 0.2f * w)); if (i < reps - 1) d.segments.Add(Seg("recovery", 2f, 7.5f)); }
                    d.segments.Add(Seg("cooldown", 5f, 6.5f));
                    return d;
                }
                var t = new WorkoutDoc { name = Jogging.Core.Loc.F("Woche {0} · Tempo", w), description = Jogging.Core.Loc.F("{0} min zügig", 10 + 2 * w) };
                t.segments.Add(Seg("warmup", 8f, 7f));
                t.segments.Add(Seg("run", 10f + 2f * w, 9.5f + 0.15f * w, 0, "Tempo"));
                t.segments.Add(Seg("cooldown", 5f, 6.5f));
                return t;
            }));

            return list;
        }

        public static TrainingPlan Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var p in All()) if (p.id == id) return p;
            return null;
        }
    }
}
