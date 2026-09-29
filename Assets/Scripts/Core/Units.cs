using System.Globalization;
using UnityEngine;

namespace Jogging.Core
{
    /// <summary>
    /// Units for display and input: metric (km/h, km, m, kg) or imperial (mph, mi, ft, lb). Everything
    /// inside stays metric — the treadmill, the logbook, routes and workouts work in km/h and metres;
    /// only what the runner reads, types and hears is converted. The choice: Einstellungen → Einheiten
    /// (AppSettings.units), until then from the operating system's region (imperial in the USA, Liberia
    /// and Myanmar, metric elsewhere); -units metric|imperial for tests.
    /// </summary>
    public static class Units
    {
        public const float KmPerMile = 1.609344f, FeetPerMetre = 3.28084f, PoundsPerKg = 2.20462f;

        public static bool Imperial { get; private set; }

        private static string systemRegion = "";
        private static bool nativeAsked;

        /// <summary>"metric" or "imperial" — what follows from the setting and the system region.</summary>
        public static string Resolve(string setting) =>
            setting == "metric" || setting == "imperial" ? setting
            : systemRegion == "US" || systemRegion == "LR" || systemRegion == "MM" ? "imperial" : "metric";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void CaptureRegion()
        {
            // The player's .NET culture doesn't know the system region (on macOS it says en-US for a German
            // Mac), so ask the system: CoreFoundation on Mac/iPad, java.util.Locale on Android. The culture
            // (captured before Loc sets the app's own) is only the fallback.
            try { var n = CultureInfo.CurrentCulture.Name; systemRegion = n.Length >= 5 ? n.Substring(n.Length - 2).ToUpperInvariant() : ""; }
            catch { systemRegion = ""; }
        }

        /// <summary>The region the system is set to ("DE", "US", …), "" if unknown.</summary>
        public static string SystemRegion => systemRegion;

#if (UNITY_STANDALONE_OSX || UNITY_IOS) && !UNITY_EDITOR
#if UNITY_IOS
        private const string CF = "__Internal";
#else
        private const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
#endif
        [System.Runtime.InteropServices.DllImport(CF)] private static extern System.IntPtr CFLocaleCopyCurrent();
        [System.Runtime.InteropServices.DllImport(CF)] private static extern System.IntPtr CFLocaleGetIdentifier(System.IntPtr locale);
        [System.Runtime.InteropServices.DllImport(CF)] [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I1)] private static extern bool CFStringGetCString(System.IntPtr s, byte[] buffer, long size, uint encoding);
        [System.Runtime.InteropServices.DllImport(CF)] private static extern void CFRelease(System.IntPtr cf);
#endif

        private static string NativeRegion()
        {
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                using (var locale = new AndroidJavaClass("java.util.Locale"))
                using (var def = locale.CallStatic<AndroidJavaObject>("getDefault"))
                    return (def.Call<string>("getCountry") ?? "").ToUpperInvariant();
#elif (UNITY_STANDALONE_OSX || UNITY_IOS) && !UNITY_EDITOR
                var loc = CFLocaleCopyCurrent();
                if (loc == System.IntPtr.Zero) return "";
                var buf = new byte[128];
                bool ok = CFStringGetCString(CFLocaleGetIdentifier(loc), buf, buf.Length, 0x08000100);
                CFRelease(loc);
                return ok ? RegionOf(System.Text.Encoding.UTF8.GetString(buf).TrimEnd('\0')) : "";
#else
                return "";
#endif
            }
            catch { return ""; }
        }

        /// <summary>Region of a locale identifier: "de_DE" → DE, "en_US@rg=dezzzz" → DE (own region setting).</summary>
        public static string RegionOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            int rg = id.IndexOf("rg=", System.StringComparison.Ordinal);
            if (rg >= 0 && id.Length >= rg + 5) return id.Substring(rg + 3, 2).ToUpperInvariant();
            int at = id.IndexOf('@'); if (at >= 0) id = id.Substring(0, at);
            int us = id.LastIndexOfAny(new[] { '_', '-' });
            return us >= 0 && id.Length - us - 1 == 2 ? id.Substring(us + 1).ToUpperInvariant() : "";
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Init()
        {
            if (!nativeAsked) { nativeAsked = true; var r = NativeRegion(); if (r.Length == 2) systemRegion = r; } // the player is up: JNI / CoreFoundation
            string u = null;
            var a = Args.All;
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-units") u = a[i + 1];
            Imperial = Resolve(u ?? AppSettings.Current.units) == "imperial";
        }

        // ---- values in the display unit
        public static float Speed(float kmh) => Imperial ? kmh / KmPerMile : kmh;
        public static float SpeedToKmh(float display) => Imperial ? display * KmPerMile : display;
        public static float Dist(float metres) => Imperial ? metres / 1000f / KmPerMile : metres / 1000f; // km or mi
        public static float Elev(float metres) => Imperial ? metres * FeetPerMetre : metres;               // m or ft
        public static float Weight(float kg) => Imperial ? kg * PoundsPerKg : kg;
        public static float WeightToKg(float display) => Imperial ? display / PoundsPerKg : display;

        /// <summary>A distance in km rounded to <paramref name="step"/> in the display unit (0.5 km or 0.5 mi).</summary>
        public static float RoundKm(float km, float step)
        {
            float d = Imperial ? km / KmPerMile : km;
            d = Mathf.Round(d / step) * step;
            return Imperial ? d * KmPerMile : d;
        }

        // ---- unit names
        public static string SpeedUnit => Imperial ? "mph" : "km/h";
        public static string DistUnit => Imperial ? "mi" : "km";
        public static string ElevUnit => Imperial ? "ft" : "m";
        public static string WeightUnit => Imperial ? "lb" : "kg";
        public static string PaceUnit => Imperial ? "/mi" : "/km";

        // ---- formatted with the unit, in the app's number format ("8,0 km/h", "5.0 mph")
        public static string FmtSpeed(float kmh, string format = "0.0") => Speed(kmh).ToString(format, Loc.Culture) + " " + SpeedUnit;
        public static string FmtDist(float metres, string format = "0.0") => Dist(metres).ToString(format, Loc.Culture) + " " + DistUnit;
        public static string FmtElev(float metres, string format = "0") => Elev(metres).ToString(format, Loc.Culture) + " " + ElevUnit;
        public static string FmtWeight(float kg, string format = "0") => Weight(kg).ToString(format, Loc.Culture) + " " + WeightUnit;

        /// <summary>Short distances (gaps, what is left of a segment): "240 m" / "790 ft".</summary>
        public static string FmtShort(float metres) => Elev(metres).ToString("0", Loc.Culture) + " " + ElevUnit;

        /// <summary>A distance that may be short or long: "640 m" / "1,25 km", "0.32 mi" / "450 ft".</summary>
        public static string FmtDistAuto(float metres) =>
            (Imperial ? metres < 160.9f : metres < 1000f) ? FmtShort(metres) : FmtDist(metres, "0.00");

        /// <summary>Pace from metres and seconds: "5:42 /km" or "9:10 /mi".</summary>
        public static string FmtPace(float metres, float seconds)
        {
            float units = Dist(metres);
            if (units < 0.01f) return "–";
            int s = Mathf.RoundToInt(seconds / units);
            return $"{s / 60}:{s % 60:00} {PaceUnit}";
        }
    }
}
