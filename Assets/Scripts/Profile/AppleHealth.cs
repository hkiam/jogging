using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Jogging.Profile
{
    /// <summary>
    /// Runs into Apple Health (iPad/iPhone; Plugins/iOS/JoggingHealth.swift): one indoor running workout per
    /// run with distance, energy, heart rate and elevation gain. Only writes. Each run has a fixed id (runner +
    /// start), so sending it again replaces it in Health. Elsewhere (Mac, Windows, Android) not available.
    /// </summary>
    public static class AppleHealth
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern bool jog_health_available();
        [DllImport("__Internal")] private static extern void jog_health_authorize();
        [DllImport("__Internal")] private static extern int jog_health_status();
        [DllImport("__Internal")] private static extern void jog_health_save(string json);
        [DllImport("__Internal")] private static extern int jog_health_state();
        [DllImport("__Internal")] private static extern string jog_health_error();
        public static bool Available => jog_health_available();
        public static void Authorize() => jog_health_authorize();
        /// <summary>0 not asked yet, 1 allowed, 2 not allowed.</summary>
        public static int Status => jog_health_status();
        /// <summary>Last save: 0 pending, 1 saved, −1 failed (<see cref="LastError"/>).</summary>
        public static int SaveState => jog_health_state();
        public static string LastError => jog_health_error() ?? "";
        private static void Send(string json) => jog_health_save(json);
#else
        public static bool Available => false;
        public static void Authorize() { }
        public static int Status => 0;
        public static int SaveState => 0;
        public static string LastError => "";
        private static void Send(string json) { }
#endif

        [Serializable]
        private class Run
        {
            public string id;
            public double start, seconds, distanceM, kcal, gainM;
            public List<double> t = new List<double>(), dist = new List<double>();
            public List<int> hr = new List<int>();
        }

        /// <summary>The run as Health gets it (JSON; tested by StatsCheck).</summary>
        public static string ToJson(SessionRecord rec)
        {
            var s = rec.summary;
            var start = RunnerStats.ParseUtc(s.start);
            var run = new Run
            {
                id = $"jogging-{s.runnerId}-{s.start}",
                start = (start - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds,
                seconds = s.seconds, distanceM = s.distanceM, kcal = s.kcal, gainM = s.gainM,
            };
            foreach (var p in rec.samples) { run.t.Add(p.t); run.dist.Add(p.distM); run.hr.Add(p.hr); }
            return JsonUtility.ToJson(run);
        }

        /// <summary>Send one run to Health (allowed before; otherwise Health just doesn't take it).</summary>
        public static void Save(SessionRecord rec)
        {
            if (!Available || rec == null) return;
            Send(ToJson(rec));
            Debug.Log($"[Jogging] Lauf an Apple Health: {rec.summary.distanceM:0} m, {rec.samples.Count} Messpunkte");
        }
    }
}
