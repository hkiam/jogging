using System;
using System.Collections.Generic;

namespace Jogging.Profile
{
    /// <summary>What an achievement condition can look at: lifetime stats, the whole logbook and the run just recorded.</summary>
    public class AchievementContext
    {
        public ProfileData Profile;
        public IReadOnlyList<SessionSummary> Runs = Array.Empty<SessionSummary>();
        public SessionSummary Last; // null when checking outside a run
        public DateTime NowLocal = DateTime.Now;
    }

    /// <summary>
    /// Central list of achievements and the logic that unlocks them. Data-driven: a new achievement is
    /// one entry (id, title, how to get it, condition). Ids never change — unlocked ids are stored.
    /// </summary>
    public static class AchievementCatalog
    {
        public readonly struct Achievement
        {
            public readonly string Id, Title, Hint, Group;
            public readonly Func<AchievementContext, bool> Condition;

            public Achievement(string group, string id, string title, string hint, Func<AchievementContext, bool> condition)
            {
                Group = group; Id = id; Title = title; Hint = hint; Condition = condition;
            }
        }

        private static float LongestRun(AchievementContext c)
        {
            float m = c.Profile.bestDistanceMeters;
            foreach (var r in c.Runs) if (r.distanceM > m) m = r.distanceM;
            return m;
        }

        private static float LongestTime(AchievementContext c)
        {
            float s = 0f;
            foreach (var r in c.Runs) if (r.seconds > s) s = r.seconds;
            return s;
        }

        private static float DogKm(AchievementContext c)
        {
            float m = 0f;
            foreach (var r in c.Runs) if (!string.IsNullOrEmpty(r.dog)) m += r.distanceM;
            return m / 1000f;
        }

        private static int Count(AchievementContext c, Func<SessionSummary, bool> f)
        {
            int n = 0;
            foreach (var r in c.Runs) if (f(r)) n++;
            return n;
        }

        private static float MaxZoneSeconds(AchievementContext c, int zone)
        {
            float m = 0f;
            foreach (var r in c.Runs)
                if (r.hrZoneSeconds != null && r.hrZoneSeconds.Count >= zone && r.hrZoneSeconds[zone - 1] > m) m = r.hrZoneSeconds[zone - 1];
            return m;
        }

        public static readonly Achievement[] All =
        {
            new("Läufe",   "first-run",  "Erster Lauf",      "einen Lauf beenden",                   c => c.Profile.totalRuns >= 1),
            new("Läufe",   "run-5",      "5 Läufe",          "5 Läufe",                              c => c.Profile.totalRuns >= 5),
            new("Läufe",   "run-25",     "25 Läufe",         "25 Läufe",                             c => c.Profile.totalRuns >= 25),
            new("Läufe",   "run-100",    "100 Läufe",        "100 Läufe",                            c => c.Profile.totalRuns >= 100),
            new("Strecke", "dist-1k",    "1 km gesamt",      "insgesamt 1 km",                       c => c.Profile.totalDistanceMeters >= 1000f),
            new("Strecke", "dist-10k",   "10 km gesamt",     "insgesamt 10 km",                      c => c.Profile.totalDistanceMeters >= 10000f),
            new("Strecke", "dist-50k",   "50 km gesamt",     "insgesamt 50 km",                      c => c.Profile.totalDistanceMeters >= 50000f),
            new("Strecke", "dist-100k",  "100 km gesamt",    "insgesamt 100 km",                     c => c.Profile.totalDistanceMeters >= 100000f),
            new("Strecke", "dist-500k",  "500 km gesamt",    "insgesamt 500 km",                     c => c.Profile.totalDistanceMeters >= 500000f),
            new("Am Stück", "single-2k", "2 km am Stück",    "2 km in einem Lauf",                   c => LongestRun(c) >= 2000f),
            new("Am Stück", "single-5k", "Erste 5 km",       "5 km in einem Lauf",                   c => LongestRun(c) >= 5000f),
            new("Am Stück", "single-10k","Erste 10 km",      "10 km in einem Lauf",                  c => LongestRun(c) >= 10000f),
            new("Am Stück", "time-30",   "30 Minuten",       "30 min in einem Lauf",                 c => LongestTime(c) >= 1800f),
            new("Am Stück", "time-60",   "60 Minuten",       "60 min in einem Lauf",                 c => LongestTime(c) >= 3600f),
            new("Berge",   "climb-100",  "100 Höhenmeter",   "insgesamt 100 m bergauf",              c => c.Profile.totalElevationMeters >= 100f),
            new("Berge",   "climb-1000", "1000 Höhenmeter",  "insgesamt 1000 m bergauf",             c => c.Profile.totalElevationMeters >= 1000f),
            new("Serie",   "streak-3",   "3 Tage in Folge",  "an 3 Tagen hintereinander laufen",     c => RunnerStats.StreakDays(c.Runs, c.NowLocal) >= 3),
            new("Serie",   "streak-7",   "7 Tage in Folge",  "an 7 Tagen hintereinander laufen",     c => RunnerStats.StreakDays(c.Runs, c.NowLocal) >= 7),
            new("Training","workout-1",  "Erstes Workout",   "ein Workout vollständig laufen",       c => Count(c, r => r.workoutCompleted) >= 1),
            new("Training","workout-10", "10 Workouts",      "10 Workouts vollständig",              c => Count(c, r => r.workoutCompleted) >= 10),
            new("Training","belt-1",     "Aufs Band!",       "ein Lauf auf dem Laufband",            c => Count(c, r => r.source == "belt") >= 1),
            new("Training","zone2-20",   "Grundlage",        "20 min in Zone 2 in einem Lauf",       c => MaxZoneSeconds(c, 2) >= 1200f),
            new("Hund",    "dog-1",      "Auf vier Pfoten",  "ein Lauf mit deinem Hund",             c => Count(c, r => !string.IsNullOrEmpty(r.dog)) >= 1),
            new("Hund",    "dog-50",     "Treue Pfoten",     "50 km mit deinem Hund",                c => DogKm(c) >= 50f),
            new("Training","plan-1",     "Planerfüller",     "einen Trainingsplan ganz durchlaufen", c => c.Profile.plansFinished.Count >= 1),
            new("Training","route-1",    "Streckenbauer",    "eine eigene Strecke komplett laufen",  c => Count(c, r => r.completed && !string.IsNullOrEmpty(r.routeId)) >= 1),
        };

        /// <summary>
        /// Adds any newly satisfied achievements to the profile and returns their titles
        /// (for a toast/notification). Already-unlocked ones are skipped.
        /// </summary>
        public static List<string> CheckNew(AchievementContext ctx)
        {
            var newly = new List<string>();
            foreach (var a in All)
            {
                if (ctx.Profile.unlockedAchievements.Contains(a.Id)) continue;
                bool ok;
                try { ok = a.Condition(ctx); } catch { ok = false; }
                if (ok)
                {
                    ctx.Profile.unlockedAchievements.Add(a.Id);
                    newly.Add(Jogging.Core.Loc.T(a.Title));
                }
            }
            return newly;
        }

        /// <summary>Lifetime-only check (no logbook), e.g. for old callers.</summary>
        public static List<string> CheckNew(ProfileData profile) => CheckNew(new AchievementContext { Profile = profile });
    }
}
