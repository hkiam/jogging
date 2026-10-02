using System.Collections.Generic;
using UnityEngine;

namespace Jogging.Route
{
    /// <summary>
    /// Climb segments ("Bergwertungen") of a route, found in its elevation profile (so GPX routes have them
    /// too): every climb with at least <see cref="MinGainM"/> m up, <see cref="MinLengthM"/> m long and an
    /// average grade of <see cref="MinGrade"/> or more. Short dips inside a climb (less than
    /// <see cref="MaxDipM"/> m down) don't end it. Plain C#, deterministic (tested by GameCheck).
    /// A segment is identified by route id + revision + its index.
    /// </summary>
    public static class ClimbSegments
    {
        public const float MinGainM = 12f, MinLengthM = 150f, MinGrade = 0.025f, MaxDipM = 3f;
        /// <summary>A flat stretch this long (less than 1 m up) ends a climb at its top.</summary>
        public const float MaxFlatM = 150f;

        public struct Climb
        {
            public float startM, endM, gainM;
            public float LengthM => endM - startM;
            public float Grade => gainM / Mathf.Max(1f, LengthM);
        }

        public static List<Climb> Find(float[] h, float step)
        {
            var list = new List<Climb>();
            if (h == null || h.Length < 3 || step <= 0f) return list;
            int i = 0, n = h.Length;
            while (i < n - 1)
            {
                // the bottom of a climb: the next point goes up
                while (i < n - 1 && h[i + 1] <= h[i]) i++;
                int start = i, top = i, rose = i;
                float peak = h[i], riseRef = h[i];
                int j = i;
                while (j < n - 1)
                {
                    j++;
                    if (h[j] > peak) { peak = h[j]; top = j; }
                    else if (peak - h[j] > MaxDipM) break;              // a real descent: the climb ended at its top
                    if (h[j] >= riseRef + 1f) { riseRef = h[j]; rose = j; }
                    else if ((j - rose) * step > MaxFlatM) break;      // a plateau: ended too
                }
                if (top > rose) { top = rose; peak = h[rose]; }          // not the tiny rise along the plateau
                float gain = peak - h[start], len = (top - start) * step;
                if (gain >= MinGainM && len >= MinLengthM && gain / len >= MinGrade)
                    list.Add(new Climb { startM = start * step, endM = top * step, gainM = gain });
                i = Mathf.Max(top, start + 1);
            }
            return list;
        }

        public static List<Climb> Of(RouteDoc d) =>
            d?.profile?.heightsM != null ? Find(d.profile.heightsM, d.profile.stepM > 0f ? d.profile.stepM : RouteGenerator.ProfileStep) : new List<Climb>();

        /// <summary>"Anstieg 2 · 600 m · ↑35 m" (for lists and announcements).</summary>
        public static string Name(int index) => Jogging.Core.Loc.F("Anstieg {0}", index + 1);

        public static string Describe(Climb c) =>
            $"{Jogging.Core.Units.FmtDist(c.LengthM)} · ↑{Jogging.Core.Units.FmtElev(c.gainM)} · {c.Grade * 100f:0.0} %";
    }
}
