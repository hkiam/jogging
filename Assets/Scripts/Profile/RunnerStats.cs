using System;
using System.Collections.Generic;
using System.Globalization;

namespace Jogging.Profile
{
    /// <summary>
    /// Statistics derived from the logbook (plain C#, no Unity): totals per period, last run,
    /// streak of running days and the family's weekly champion. Weeks start Monday, local time.
    /// </summary>
    public static class RunnerStats
    {
        public struct Totals
        {
            public int Runs;
            public float DistanceM;
            public float Seconds;
        }

        public static DateTime ParseUtc(string iso) =>
            DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) ? t : DateTime.MinValue;

        /// <summary>Monday 00:00 (local) of the week containing <paramref name="local"/>.</summary>
        public static DateTime WeekStart(DateTime local)
        {
            int back = ((int)local.DayOfWeek + 6) % 7; // Monday = 0
            return local.Date.AddDays(-back);
        }

        /// <summary>Totals of the runs started in [fromLocal, toLocal).</summary>
        public static Totals Sum(IEnumerable<SessionSummary> runs, DateTime fromLocal, DateTime toLocal)
        {
            var t = new Totals();
            foreach (var r in runs)
            {
                var start = ParseUtc(r.start).ToLocalTime();
                if (start < fromLocal || start >= toLocal) continue;
                t.Runs++; t.DistanceM += r.distanceM; t.Seconds += r.seconds;
            }
            return t;
        }

        public static Totals Week(IEnumerable<SessionSummary> runs, DateTime nowLocal)
        {
            var ws = WeekStart(nowLocal);
            return Sum(runs, ws, ws.AddDays(7));
        }

        /// <summary>
        /// Totals of the last <paramref name="weeks"/> weeks (Monday–Sunday, local), oldest first; the
        /// last entry is the current week. Runs outside the window are ignored.
        /// </summary>
        public static Totals[] Weekly(IEnumerable<SessionSummary> runs, DateTime nowLocal, int weeks)
        {
            var result = new Totals[Math.Max(0, weeks)];
            if (weeks <= 0) return result;
            var first = WeekStart(nowLocal).AddDays(-7 * (weeks - 1));
            foreach (var r in runs)
            {
                var start = ParseUtc(r.start).ToLocalTime();
                if (start < first) continue;
                int i = (int)((start.Date - first).TotalDays / 7);
                if (i < 0 || i >= weeks) continue;
                result[i].Runs++; result[i].DistanceM += r.distanceM; result[i].Seconds += r.seconds;
            }
            return result;
        }

        /// <summary>ISO calendar week (Monday start, week 1 contains the first Thursday).</summary>
        public static int IsoWeek(DateTime d)
        {
            var thursday = d.Date.AddDays(3 - ((int)d.DayOfWeek + 6) % 7);
            return (thursday.DayOfYear - 1) / 7 + 1;
        }

        public static Totals All(IEnumerable<SessionSummary> runs) => Sum(runs, DateTime.MinValue, DateTime.MaxValue);

        public static SessionSummary Last(IReadOnlyList<SessionSummary> runs)
        {
            SessionSummary best = null;
            foreach (var r in runs) if (best == null || string.CompareOrdinal(r.start, best.start) > 0) best = r;
            return best;
        }

        /// <summary>Consecutive days with a run, ending today (or yesterday: the streak is still alive).</summary>
        public static int StreakDays(IEnumerable<SessionSummary> runs, DateTime nowLocal)
        {
            var days = new HashSet<DateTime>();
            foreach (var r in runs) days.Add(ParseUtc(r.start).ToLocalTime().Date);
            var d = nowLocal.Date;
            if (!days.Contains(d)) d = d.AddDays(-1);
            int n = 0;
            while (days.Contains(d)) { n++; d = d.AddDays(-1); }
            return n;
        }

        /// <summary>Runner id with the most distance this week (null if nobody ran).</summary>
        public static string WeeklyChampion(IDictionary<string, List<SessionSummary>> byRunner, DateTime nowLocal)
        {
            string champ = null; float best = 0f;
            foreach (var kv in byRunner)
            {
                float km = Week(kv.Value, nowLocal).DistanceM;
                if (km > best) { best = km; champ = kv.Key; }
            }
            return champ;
        }

        /// <summary>
        /// Fastest time (s) for <paramref name="meters"/> within one run, from its 1 Hz samples
        /// (two-pointer window, interpolated at the window start). 0 if the run is shorter.
        /// </summary>
        public static float BestSegmentSeconds(IReadOnlyList<SessionSample> s, float meters)
        {
            if (s == null || s.Count < 2 || s[s.Count - 1].distM - s[0].distM < meters) return 0f;
            float best = float.MaxValue;
            int i = 0;
            for (int j = 1; j < s.Count; j++)
            {
                // advance i while the window still covers the distance
                while (i + 1 < j && s[j].distM - s[i + 1].distM >= meters) i++;
                float d = s[j].distM - s[i].distM;
                if (d < meters) continue;
                // interpolate the start so exactly `meters` are covered
                float over = d - meters, segD = s[i + 1].distM - s[i].distM, segT = s[i + 1].t - s[i].t;
                float tStart = s[i].t + (segD > 0f ? Math.Min(1f, over / segD) * segT : 0f);
                best = Math.Min(best, s[j].t - tStart);
            }
            return best == float.MaxValue ? 0f : best;
        }

        /// <summary>Family ranking of the week: (runner id, metres), most first, only runners who ran.</summary>
        public static List<KeyValuePair<string, float>> WeeklyRanking(IDictionary<string, List<SessionSummary>> byRunner, DateTime nowLocal)
        {
            var list = new List<KeyValuePair<string, float>>();
            foreach (var kv in byRunner)
            {
                float m = Week(kv.Value, nowLocal).DistanceM;
                if (m > 0f) list.Add(new KeyValuePair<string, float>(kv.Key, m));
            }
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            return list;
        }

        /// <summary>Totals of today, this week, month, year and all time.</summary>
        public static (string label, Totals t)[] Periods(IEnumerable<SessionSummary> runs, DateTime nowLocal)
        {
            var d = nowLocal.Date; var ws = WeekStart(nowLocal);
            var m = new DateTime(d.Year, d.Month, 1); var y = new DateTime(d.Year, 1, 1);
            return new[]
            {
                ("Heute", Sum(runs, d, d.AddDays(1))),
                ("Woche", Sum(runs, ws, ws.AddDays(7))),
                ("Monat", Sum(runs, m, m.AddMonths(1))),
                ("Jahr", Sum(runs, y, y.AddYears(1))),
                ("Gesamt", All(runs)),
            };
        }

        // ---- formatting shared by the menus and the finish screen ----

        /// <summary>Distance in the display unit ("5,2 km" / "3.2 mi").</summary>
        public static string Km(float meters) => Jogging.Core.Units.FmtDist(meters, Jogging.Core.Units.Dist(meters) < 100f ? "0.0" : "0");

        public static string Duration(float seconds)
        {
            int s = (int)seconds, h = s / 3600, m = s / 60 % 60;
            return h > 0 ? $"{h}:{m:00} h" : $"{m}:{s % 60:00} min";
        }

        /// <summary>Pace "5:42 /km" from distance and time.</summary>
        public static string Pace(float meters, float seconds)
        {
            if (meters < 10f || seconds < 1f) return "–";
            return Jogging.Core.Units.FmtPace(meters, seconds);
        }

        /// <summary>"heute", "gestern", "vor 3 Tagen", else the date.</summary>
        public static string When(string isoUtc, DateTime nowLocal)
        {
            var t = ParseUtc(isoUtc).ToLocalTime();
            int days = (nowLocal.Date - t.Date).Days;
            if (days <= 0) return Jogging.Core.Loc.T("heute");
            if (days == 1) return Jogging.Core.Loc.T("gestern");
            if (days < 7) return Jogging.Core.Loc.F("vor {0} Tagen", days);
            return t.ToString("d", Jogging.Core.Loc.Culture);
        }
    }
}
