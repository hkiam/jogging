using System;
using System.Collections.Generic;

namespace Jogging.Profile
{
    /// <summary>
    /// A previous run as a ghost: from its 1 Hz samples, where it was after t seconds of running and
    /// when it passed a distance — linear between samples. Plain C# (tested by StatsCheck).
    /// </summary>
    public class GhostTrack
    {
        private readonly List<SessionSample> s;
        public SessionSummary Summary { get; }

        public GhostTrack(SessionRecord rec)
        {
            Summary = rec.summary;
            s = rec.samples ?? new List<SessionSample>();
        }

        public bool Valid => s.Count >= 2;
        public float Duration => Valid ? s[s.Count - 1].t : 0f;
        public float Length => Valid ? s[s.Count - 1].distM - s[0].distM : 0f;

        /// <summary>Metres run after <paramref name="t"/> seconds (holds at the end).</summary>
        public float DistanceAt(float t)
        {
            if (!Valid) return 0f;
            float d0 = s[0].distM;
            if (t <= s[0].t) return 0f;
            if (t >= s[s.Count - 1].t) return s[s.Count - 1].distM - d0;
            int lo = 0, hi = s.Count - 1;
            while (hi - lo > 1) { int m = (lo + hi) / 2; if (s[m].t <= t) lo = m; else hi = m; }
            float f = (t - s[lo].t) / Math.Max(1e-4f, s[hi].t - s[lo].t);
            return s[lo].distM + (s[hi].distM - s[lo].distM) * f - d0;
        }

        /// <summary>Speed (km/h) at <paramref name="t"/> (0 after the end).</summary>
        public float KmhAt(float t)
        {
            if (!Valid || t >= s[s.Count - 1].t) return 0f;
            int lo = 0, hi = s.Count - 1;
            while (hi - lo > 1) { int m = (lo + hi) / 2; if (s[m].t <= t) lo = m; else hi = m; }
            return s[lo].kmh;
        }

        /// <summary>Seconds the ghost needed for <paramref name="meters"/>; NaN if it never got that far.</summary>
        public float TimeAt(float meters)
        {
            if (!Valid) return float.NaN;
            float d0 = s[0].distM, target = meters + d0;
            if (meters <= 0f) return s[0].t;
            if (target > s[s.Count - 1].distM) return float.NaN;
            int lo = 0, hi = s.Count - 1;
            while (hi - lo > 1) { int m = (lo + hi) / 2; if (s[m].distM <= target) lo = m; else hi = m; }
            float seg = s[hi].distM - s[lo].distM;
            float f = seg > 1e-4f ? (target - s[lo].distM) / seg : 0f;
            return s[lo].t + (s[hi].t - s[lo].t) * f;
        }

        /// <summary>
        /// The ghost for a route revision: the fastest complete run on it, else the latest run on it.
        /// Null if the runner never ran it (or only without samples).
        /// </summary>
        public static SessionSummary Pick(IEnumerable<SessionSummary> runs, string routeId, int revision)
        {
            if (string.IsNullOrEmpty(routeId)) return null;
            SessionSummary best = null, latest = null;
            foreach (var r in runs)
            {
                if (r.routeId != routeId || r.revision != revision || !string.IsNullOrEmpty(r.workoutId)) continue;
                if (r.completed && (best == null || r.seconds < best.seconds)) best = r;
                if (latest == null || string.CompareOrdinal(r.start, latest.start) > 0) latest = r;
            }
            return best ?? latest;
        }
    }
}
