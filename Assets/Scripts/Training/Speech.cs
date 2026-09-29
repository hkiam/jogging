using System;
using System.Globalization;

namespace Jogging.Training
{
    /// <summary>
    /// Spoken texts for the announcements, German or English (<see cref="Jogging.Core.Loc"/>) (plain C#, tested by WorkoutCheck): numbers the way
    /// they are said ("5,2 Kilometer", "5 Minuten 42", "11 Komma 5 Kilometer pro Stunde").
    /// </summary>
    public static class Speech
    {
        private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
        private static bool En => Jogging.Core.Loc.En;

        private static bool Mi => Jogging.Core.Units.Imperial;

        /// <summary>A distance in the display unit: "5,2 Kilometer", "3.2 miles".</summary>
        public static string Km(float meters)
        {
            float d = Jogging.Core.Units.Dist(meters);
            bool whole = Math.Abs(d - Math.Round(d)) < 0.05f, one = whole && Math.Round(d) == 1;
            string n = whole ? $"{Math.Round(d):0}" : d.ToString("0.0", En ? CultureInfo.InvariantCulture : De);
            if (En) return n + (Mi ? (one ? " mile" : " miles") : (one ? " kilometre" : " kilometres"));
            return n + (Mi ? (one ? " Meile" : " Meilen") : " Kilometer");
        }

        /// <summary>A speed in the display unit: "11 Komma 5 Kilometer pro Stunde", "7 point 5 miles per hour".</summary>
        public static string Kmh(float kmh)
        {
            float v = Jogging.Core.Units.Speed(kmh);
            bool whole = Math.Abs(v - Math.Round(v)) < 0.05f;
            if (En) return (whole ? $"{Math.Round(v):0}" : v.ToString("0.0", CultureInfo.InvariantCulture).Replace(".", " point ")) + (Mi ? " miles per hour" : " kilometres per hour");
            return (whole ? $"{Math.Round(v):0}" : v.ToString("0.0", De).Replace(",", " Komma ")) + (Mi ? " Meilen pro Stunde" : " Kilometer pro Stunde");
        }

        public static string Duration(float seconds)
        {
            int s = (int)Math.Round(seconds), h = s / 3600, m = s / 60 % 60, sec = s % 60;
            if (En)
            {
                string Hr(int n) => n == 1 ? "1 hour" : $"{n} hours";
                string Mn(int n) => n == 1 ? "1 minute" : $"{n} minutes";
                if (h > 0) return $"{Hr(h)} {Mn(m)}";
                if (m == 0) return $"{sec} seconds";
                return sec == 0 ? Mn(m) : $"{Mn(m)} {sec}";
            }
            if (h > 0) return $"{h} Stunde{(h == 1 ? "" : "n")} {m} Minute{(m == 1 ? "" : "n")}";
            if (m == 0) return $"{sec} Sekunden";
            return sec == 0 ? $"{m} Minute{(m == 1 ? "" : "n")}" : $"{m} Minute{(m == 1 ? "" : "n")} {sec}";
        }

        /// <summary>Pace per kilometre (or mile), e.g. "5 Minuten 42 pro Kilometer" / "9 minutes 10 per mile".</summary>
        public static string Pace(float meters, float seconds) =>
            meters < 10f ? "" : Duration(seconds / Jogging.Core.Units.Dist(meters))
                + (En ? (Mi ? " per mile" : " per kilometre") : (Mi ? " pro Meile" : " pro Kilometer"));

        public static string Segment(WorkoutSegment s)
        {
            string t = Jogging.Core.Loc.T(s.Title);
            string amount = s.ByDistance ? Km(s.distanceM) : Duration(s.durationS);
            string target = s.hrZone > 0 ? $", {(En ? "zone" : "Zone")} {s.hrZone}" : s.speedKmh > 0f ? ", " + Kmh(s.speedKmh) : "";
            if (s.setIncline) target += En ? $", incline {s.inclinePercent:0} percent" : $", Steigung {s.inclinePercent:0} Prozent";
            return $"{t}{target}, {amount}.";
        }

        public static string Split(int km, float splitSeconds, float totalSeconds) =>
            En ? (Mi ? $"Mile {km}. {Duration(splitSeconds)} for this mile." : $"Kilometre {km}. {Duration(splitSeconds)} for this kilometre.")
               : (Mi ? $"Meile {km}. {Duration(splitSeconds)} für diese Meile." : $"Kilometer {km}. {Duration(splitSeconds)} für diesen Kilometer.");
    }
}
