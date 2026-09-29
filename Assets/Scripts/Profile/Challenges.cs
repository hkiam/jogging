using System;
using System.Collections.Generic;

namespace Jogging.Profile
{
    /// <summary>
    /// Weekly challenges (Monday to Sunday, local time): data-driven goals computed from the logbook —
    /// no server, nothing to accept. Each shows a progress bar on the runner's home.
    /// </summary>
    public static class Challenges
    {
        public readonly struct Challenge
        {
            public readonly string Id, Title, Unit;
            public readonly float Target;
            public readonly Func<IEnumerable<SessionSummary>, float> Value;

            public Challenge(string id, string title, float target, string unit, Func<IEnumerable<SessionSummary>, float> value)
            {
                Id = id; Title = title; Target = target; Unit = unit; Value = value;
            }
        }

        public readonly struct Progress
        {
            public readonly Challenge Challenge;
            public readonly float Value;
            public float Fraction => Challenge.Target > 0f ? Math.Min(1f, Value / Challenge.Target) : 0f;
            public bool Done => Value >= Challenge.Target;
            public Progress(Challenge c, float v) { Challenge = c; Value = v; }
        }

        private static float Sum(IEnumerable<SessionSummary> runs, Func<SessionSummary, float> f)
        {
            float s = 0f;
            foreach (var r in runs) s += f(r);
            return s;
        }

        public const int DefaultRuns = 3, DefaultMinutes = 90;
        public const float DefaultKm = 15f;

        /// <summary>The weekly challenges with the runner's own targets (0 = default).</summary>
        public static Challenge[] Weekly(ProfileData p = null)
        {
            int runs = p != null && p.weekGoalRuns > 0 ? p.weekGoalRuns : DefaultRuns;
            float km = p != null && p.weekGoalKm > 0f ? p.weekGoalKm : DefaultKm;
            int min = p != null && p.weekGoalMinutes > 0 ? p.weekGoalMinutes : DefaultMinutes;
            return new[]
            {
                new Challenge("week-runs", (runs == 1 ? Jogging.Core.Loc.T("1 Lauf diese Woche") : Jogging.Core.Loc.F("{0} Läufe diese Woche", runs)), runs, Jogging.Core.Loc.T("Läufe"), rs => Sum(rs, r => 1f)),
                new Challenge("week-km",   Jogging.Core.Loc.F("{0} diese Woche", Jogging.Core.Units.FmtDist(km * 1000f, "0.#")), km, "km", rs => Sum(rs, r => r.distanceM) / 1000f),
                new Challenge("week-min",  Jogging.Core.Loc.F("{0} Minuten diese Woche", min), min, "min", rs => Sum(rs, r => r.seconds) / 60f),
            };
        }

        /// <summary>The family's weekly kilometres: 10 km per runner unless set, at least 5.</summary>
        public static float FamilyGoalKm(int runners, float setKm) => setKm > 0f ? setKm : System.Math.Max(5f, 10f * runners);

        /// <summary>The family challenge: everybody's kilometres this week together, and who contributed how much.</summary>
        public static (float totalKm, List<KeyValuePair<string, float>> perRunner) Family(IDictionary<string, List<SessionSummary>> byRunner, DateTime nowLocal)
        {
            float total = 0f;
            var list = new List<KeyValuePair<string, float>>();
            foreach (var kv in byRunner)
            {
                float km = RunnerStats.Week(kv.Value, nowLocal).DistanceM / 1000f;
                total += km;
                list.Add(new KeyValuePair<string, float>(kv.Key, km));
            }
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            return (total, list);
        }

        /// <summary>Progress of every weekly challenge for the runs of the week containing <paramref name="nowLocal"/>.</summary>
        public static List<Progress> ThisWeek(IEnumerable<SessionSummary> runs, DateTime nowLocal, ProfileData p = null)
        {
            var ws = RunnerStats.WeekStart(nowLocal);
            var week = new List<SessionSummary>();
            foreach (var r in runs)
            {
                var t = RunnerStats.ParseUtc(r.start).ToLocalTime();
                if (t >= ws && t < ws.AddDays(7)) week.Add(r);
            }
            var list = new List<Progress>();
            foreach (var c in Weekly(p)) list.Add(new Progress(c, c.Value(week)));
            return list;
        }
    }
}
