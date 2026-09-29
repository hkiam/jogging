using System;

namespace Jogging.Training
{
    /// <summary>
    /// Decoder for the standard BLE Heart Rate Measurement (0x2A37): flags bit 0 = 16-bit value,
    /// bits 1–2 = sensor contact (supported + detected). Plain C#, tested by HeartRateCheck.
    /// </summary>
    public static class HeartRateParser
    {
        /// <summary>False for malformed data, and for a strap that reports "no skin contact".</summary>
        public static bool TryParse(byte[] d, out int bpm)
        {
            bpm = 0;
            if (d == null || d.Length < 2) return false;
            byte flags = d[0];
            bool wide = (flags & 0x01) != 0;
            bool contactSupported = (flags & 0x04) != 0, contact = (flags & 0x02) != 0;
            if (contactSupported && !contact) return false;
            if (wide) { if (d.Length < 3) return false; bpm = d[1] | (d[2] << 8); }
            else bpm = d[1];
            return bpm >= 25 && bpm <= 250;
        }

        /// <summary>The strap says it has no skin contact (dry electrodes, loose strap).</summary>
        public static bool NoContact(byte[] d) => d != null && d.Length >= 1 && (d[0] & 0x04) != 0 && (d[0] & 0x02) == 0;
    }

    /// <summary>Five zones as share of the runner's max heart rate: &lt;60 · 60–70 · 70–80 · 80–90 · ≥90 %.</summary>
    public static class HeartRateZones
    {
        public const int DefaultMax = 190;
        private static readonly float[] Lower = { 0f, 0.6f, 0.7f, 0.8f, 0.9f };

        public static int Max(int profileMax) => profileMax >= 120 && profileMax <= 230 ? profileMax : DefaultMax;

        /// <summary>Estimate from the age (Tanaka: 208 − 0.7 × age); 0 if the birth year is unknown.</summary>
        public static int FromAge(int birthYear, int currentYear)
        {
            int age = currentYear - birthYear;
            return birthYear > 1900 && age >= 5 && age <= 100 ? (int)Math.Round(208 - 0.7 * age) : 0;
        }

        /// <summary>The runner's max heart rate: set by hand, else from the age, else the default.</summary>
        public static int ForRunner(int maxHeartRate, int birthYear, int currentYear)
        {
            if (maxHeartRate >= 120 && maxHeartRate <= 230) return maxHeartRate;
            int est = FromAge(birthYear, currentYear);
            return est > 0 ? Max(est) : DefaultMax;
        }

        /// <summary>Zone 1…5 of a heart rate (0 = no heart rate).</summary>
        public static int Zone(int bpm, int max)
        {
            if (bpm <= 0) return 0;
            float p = bpm / (float)Max(max);
            for (int z = 5; z >= 2; z--) if (p >= Lower[z - 1]) return z;
            return 1;
        }

        /// <summary>Heart rate range [lo, hi) of zone 1…5.</summary>
        public static (int lo, int hi) Range(int zone, int max)
        {
            int m = Max(max);
            zone = Math.Max(1, Math.Min(5, zone));
            int lo = zone == 1 ? 0 : (int)Math.Round(Lower[zone - 1] * m);
            int hi = zone == 5 ? 250 : (int)Math.Round(Lower[zone] * m);
            return (lo, hi);
        }

        public static string Name(int zone)
        {
            switch (zone)
            {
                case 1: return Jogging.Core.Loc.T("sehr leicht");
                case 2: return Jogging.Core.Loc.T("Grundlage");
                case 3: return Jogging.Core.Loc.T("aerob");
                case 4: return Jogging.Core.Loc.T("Schwelle");
                case 5: return Jogging.Core.Loc.T("maximal");
                default: return "";
            }
        }
    }

    /// <summary>
    /// Energy estimate from speed and incline (ACSM equations: walking below 8 km/h, running above),
    /// 5 kcal per litre O₂. Treadmill running without air resistance — a rough, honest estimate.
    /// </summary>
    public static class Calories
    {
        /// <summary>kcal for <paramref name="seconds"/> at this speed (km/h) and grade (%, uphill only).</summary>
        public static double For(float kmh, float gradePercent, float seconds, float weightKg)
        {
            if (weightKg <= 0f || seconds <= 0f || kmh <= 0.5f) return 0.0;
            double v = kmh * 1000.0 / 60.0;                  // m/min
            double g = Math.Max(0.0, gradePercent / 100.0);
            double vo2 = kmh < 8f ? 0.1 * v + 1.8 * v * g + 3.5 // ml/kg/min
                                  : 0.2 * v + 0.9 * v * g + 3.5;
            return vo2 * weightKg / 1000.0 * 5.0 * seconds / 60.0;
        }
    }

    /// <summary>Average, maximum and time per zone of a run's heart rate.</summary>
    public class HeartRateStats
    {
        public readonly float[] ZoneSeconds = new float[5];
        private double sum, seconds;
        public int MaxBpm { get; private set; }

        public int AvgBpm => seconds > 0 ? (int)Math.Round(sum / seconds) : 0;
        public bool HasData => seconds > 0;

        public void Add(int bpm, float dt, int maxHr)
        {
            if (bpm <= 0 || dt <= 0f) return;
            sum += bpm * (double)dt; seconds += dt;
            if (bpm > MaxBpm) MaxBpm = bpm;
            ZoneSeconds[HeartRateZones.Zone(bpm, maxHr) - 1] += dt;
        }
    }
}
