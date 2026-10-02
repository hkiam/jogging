using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Jogging.Core
{
    /// <summary>
    /// The app's language: German or English. Chosen by the operating system (German → Deutsch, anything
    /// else → English) until the runner picks one under Einstellungen → Sprache (AppSettings.language);
    /// -lang de|en on the command line for tests. Texts are written in German in the code and looked up in
    /// <see cref="EnglishTexts"/>: <c>Loc.T("Zurück")</c>, with values <c>Loc.F("Puls {0} · Zone {1}", bpm, z)</c>.
    /// A missing translation shows the German text (and is listed once with -diag). Numbers follow the
    /// language (0,8 km / 0.8 km); units stay metric — the treadmill runs in km/h.
    /// </summary>
    public static partial class Loc
    {
        public static bool En { get; private set; }
        public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("de-DE");

        /// <summary>"de" or "en" — the language that follows from the setting and the system.</summary>
        public static string Resolve(string setting) =>
            setting == "de" || setting == "en" ? setting : Application.systemLanguage == SystemLanguage.German ? "de" : "en";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Init()
        {
            string lang = null;
            var a = Args.All;
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-lang") lang = a[i + 1];
            En = Resolve(lang ?? AppSettings.Current.language) == "en";
            Culture = CultureInfo.GetCultureInfo(En ? "en-GB" : "de-DE");
            CultureInfo.CurrentCulture = CultureInfo.DefaultThreadCurrentCulture = Culture;
            CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentUICulture = Culture;
        }

        private static HashSet<string> missing, englishValues;

        /// <summary>The text in the app's language (German source text as the key).</summary>
        public static string T(string de)
        {
            if (!En || string.IsNullOrEmpty(de)) return de;
            if (EnglishTexts.TryGetValue(de, out var en)) return en;
            if (englishValues == null) englishValues = new HashSet<string>(EnglishTexts.Values);
            if (englishValues.Contains(de)) return de; // already English (translated before it got here)
            if (Args.Has("-diag") && HasWords(de) && (missing ??= new HashSet<string>()).Add(de)) Debug.Log("[Sprache] ohne Übersetzung: " + de);
            return de;
        }

        // worth translating: contains a word (not just numbers, units, symbols or a name-like single token)
        private static bool HasWords(string s)
        {
            int letters = 0;
            foreach (char c in s) if (char.IsLetter(c)) { if (++letters >= 3) return s.Contains(" ") || s.Length > 4; } else letters = 0;
            return false;
        }

        /// <summary>There is an English text for this German one (tests).</summary>
        public static bool HasEnglish(string de) => EnglishTexts.ContainsKey(de);

        /// <summary>A text with values: <c>F("{0} km diese Woche", km)</c>.</summary>
        public static string F(string de, params object[] args) => string.Format(Culture, T(de), args);
    }
}
