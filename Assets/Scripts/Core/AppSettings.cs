using System;
using System.IO;
using UnityEngine;

namespace Jogging.Core
{
    /// <summary>
    /// App-wide settings, independent of the runner (<c>settings.json</c>): the last active runner and
    /// the treadmill setup (connect on start, route incline to the belt). Loaded once, saved on change.
    /// </summary>
    /// <summary>
    /// One treadmill: what it reported about itself (speed and incline range, if it tells) and the
    /// runner's caps. Defaults are the app's old fixed limits: incline 0–12 %, speed up to 16 km/h.
    /// </summary>
    [Serializable]
    public class TreadmillProfile
    {
        public string id = "", name = "", protocol = "";
        public bool speedKnown, inclineKnown;
        public float speedMin, speedMax, inclineMin, inclineMax; // as reported by the belt
        public float maxIncline = 12f;  // the app never sets more than this
        public bool decline;            // downhill below 0 % (only if the belt can)
        public float maxDecline = 3f;   // … down to −maxDecline %
        public float maxSpeed = 16f;    // workouts never set more than this (and only if allowed at all)
        public float inclineStep;       // % per step the belt takes (0 = not known: 1 %); learned when it ignores smaller steps (F37: 2 %)
        public float rampKmhPerS;       // how fast the belt speeds up / runs down (0 = automatic: FitShow 1 km/h per s, others report their real speed; −1 = off)

        public bool CanDecline => inclineKnown && inclineMin < 0f;

        public string RangeText()
        {
            string s = speedKnown ? Loc.F("Tempo {0}–{1}", Units.Speed(speedMin).ToString("0.#", Loc.Culture), Units.FmtSpeed(speedMax, "0.#")) : Loc.T("Tempo: meldet das Band nicht");
            string i = !inclineKnown ? Loc.T("Steigung: meldet das Band nicht") : inclineMax <= inclineMin ? Loc.T("keine Steigung") : Loc.F("Steigung {0:0.#}–{1:0.#} %", inclineMin, inclineMax);
            return s + " · " + i + (inclineStep > 1f ? Loc.F(" in {0:0}-%-Schritten", inclineStep) : "");
        }
    }

    [Serializable]
    public class AppSettings
    {
        public string lastRunnerId = "";
        public string units = "";         // "metric", "imperial" or "" = from the system region (Einstellungen → Einheiten)
        public string language = "";      // "de", "en" or "" = the operating system's (Einstellungen → Sprache)
        public bool treadmillAutoConnect = true;
        [Tooltip("Route grade → belt incline (FitShow; only while the belt runs, never starts it).")]
        public bool beltIncline = true;
        public bool heartRateAutoConnect = true;
        public bool announcements = true; // spoken announcements during the run
        public bool bigHud;
        public bool ambience = true;
        public string graphics = "high";  // minimal | low | medium | high — used once chosen in Geräte → Grafik
        public bool graphicsChosen;       // false: the device's default (DefaultGraphics)

        /// <summary>Graphics level for this device until the runner picks one: weak tablets minimal, tablets medium.</summary>
        public static string DefaultGraphics => Platform.LowMemory ? "minimal" : Application.isMobilePlatform ? "medium" : "high";
        public float familyGoalKm = 0f;   // family challenge per week (0 = automatic: 10 km per runner)      // synthesized ambient sound (wind, steps, birds, rain, stream)               // treadmill view: large numbers at the bottom
        [Tooltip("Workouts set the belt speed too (ramped, capped). Off by default — opt-in.")]
        public bool beltSpeed = false;
        // The family's treadmill (remembered on first connect); empty = the first one found.
        public string treadmillId = "";
        public string treadmillName = "";
        /// <summary>Every treadmill seen, with its reported range and the runner's caps (Geräte → Laufband einstellen).</summary>
        public System.Collections.Generic.List<TreadmillProfile> treadmills = new System.Collections.Generic.List<TreadmillProfile>();

        /// <summary>The profile of a treadmill (created with safe defaults when first seen; "" = unknown device).</summary>
        public static TreadmillProfile Treadmill(string id)
        {
            var list = Current.treadmills;
            foreach (var t in list) if (t.id == (id ?? "")) return t;
            var n = new TreadmillProfile { id = id ?? "" };
            if (!string.IsNullOrEmpty(id)) list.Add(n);
            return n;
        }

        private static AppSettings current;
        private static string FilePath => Path.Combine(DataPaths.Root, "settings.json");

        public static AppSettings Current
        {
            get
            {
                if (current != null) return current;
                current = SafeFile.Read(FilePath, JsonUtility.FromJson<AppSettings>);
                return current ??= new AppSettings();
            }
        }

        /// <summary>Read the file again (after restoring a backup).</summary>
        public static void Reload() => current = null;

        public static void Save()
        {
            try { SafeFile.Write(FilePath, JsonUtility.ToJson(Current, true)); }
            catch (Exception e) { Debug.LogWarning($"[Jogging] Einstellungen nicht gespeichert: {e.Message}"); }
        }
    }
}
