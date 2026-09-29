using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Jogging.Training
{
    /// <summary>
    /// A workout: HOW I run (the route says WHERE). Plain data, JsonUtility-friendly, so it can be a
    /// built-in (<see cref="WorkoutPresets"/>) or a <c>.jogworkout</c> file (<see cref="WorkoutStore"/>).
    /// </summary>
    [Serializable]
    public class WorkoutDoc
    {
        public const int CurrentSchema = 1;
        public int schemaVersion = CurrentSchema;
        public string id = "";
        public string name = "Workout";
        public string description = "";
        public List<WorkoutSegment> segments = new List<WorkoutSegment>();

        /// <summary>File it was loaded from (own workouts); null for built-ins. Not saved in the file.</summary>
        [NonSerialized] public string path;

        /// <summary>Set when started as a session of a training plan (not saved in the file).</summary>
        [NonSerialized] public string planId;
        [NonSerialized] public int planIndex = -1;

        public bool BuiltIn => id != null && id.StartsWith("builtin:");

        /// <summary>A deep copy (for editing without touching the original until saved).</summary>
        public WorkoutDoc Clone()
        {
            var c = JsonUtility.FromJson<WorkoutDoc>(JsonUtility.ToJson(this));
            c.path = path; c.planId = planId; c.planIndex = planIndex;
            return c;
        }

        /// <summary>Estimated distance (m): time segments at their target speed (else 8 km/h).</summary>
        public float EstimatedMeters
        {
            get
            {
                float m = 0f;
                foreach (var g in segments) m += g.ByDistance ? g.distanceM : g.durationS * (g.speedKmh > 0f ? g.speedKmh : 8f) / 3.6f;
                return m;
            }
        }

        /// <summary>Estimated minutes (distance segments at their target pace, else 8 km/h).</summary>
        public float Minutes
        {
            get
            {
                float s = 0f;
                foreach (var g in segments) s += g.EstimatedSeconds;
                return s / 60f;
            }
        }
    }

    /// <summary>One block of a workout, ended by time or by distance.</summary>
    [Serializable]
    public class WorkoutSegment
    {
        /// <summary>warmup | run | walk | fast | recovery | climb | cooldown (colour and default label).</summary>
        public string kind = "run";
        public string label = "";
        public float durationS;         // > 0: ends after this running time
        public float distanceM;         // > 0 (and no duration): ends after this distance
        public float speedKmh;          // target speed (a hint for now); 0 = free
        public bool setIncline;         // true: the belt goes to inclinePercent (else the route's grade)
        public float inclinePercent;
        public int hrZone;              // target heart rate zone 1…5 (a hint); 0 = none

        public bool ByDistance => durationS <= 0f && distanceM > 0f;

        public float EstimatedSeconds => ByDistance ? distanceM / ((speedKmh > 0f ? speedKmh : 8f) / 3.6f) : Mathf.Max(0f, durationS);

        public string Title => !string.IsNullOrEmpty(label) ? Jogging.Core.Loc.T(label) : KindName(kind);

        public static string KindName(string k)
        {
            switch (k)
            {
                case "warmup": return Jogging.Core.Loc.T("Aufwärmen");
                case "fast": return Jogging.Core.Loc.T("Schnell");
                case "recovery": return Jogging.Core.Loc.T("Erholung");
                case "climb": return Jogging.Core.Loc.T("Anstieg");
                case "cooldown": return Jogging.Core.Loc.T("Auslaufen");
                case "walk": return Jogging.Core.Loc.T("Gehen");
                default: return Jogging.Core.Loc.T("Laufen");
            }
        }

        public static Color KindColor(string k)
        {
            switch (k)
            {
                case "warmup": case "cooldown": return new Color(0.45f, 0.62f, 0.85f);
                case "fast": return new Color(0.92f, 0.45f, 0.35f);
                case "recovery": return new Color(0.45f, 0.75f, 0.55f);
                case "walk": return new Color(0.55f, 0.72f, 0.62f);
                case "climb": return new Color(0.88f, 0.68f, 0.30f);
                default: return new Color(0.62f, 0.66f, 0.74f);
            }
        }
    }

    /// <summary>Built-in workouts (ids start with "builtin:").</summary>
    public static class WorkoutPresets
    {
        private static WorkoutSegment T(string kind, float min, float kmh, float incline = float.NaN) =>
            new WorkoutSegment { kind = kind, durationS = min * 60f, speedKmh = kmh, setIncline = !float.IsNaN(incline), inclinePercent = float.IsNaN(incline) ? 0f : incline };

        private static WorkoutSegment D(string kind, float m, float kmh) => new WorkoutSegment { kind = kind, distanceM = m, speedKmh = kmh };

        public static List<WorkoutDoc> All()
        {
            var list = new List<WorkoutDoc>();

            var iv = new WorkoutDoc { id = "builtin:intervalle-30", name = "Intervalle 30 min", description = "4 × 3 min schnell, 2 min Erholung" };
            iv.segments.Add(T("warmup", 5f, 7f));
            for (int i = 0; i < 4; i++) { iv.segments.Add(T("fast", 3f, 11f)); iv.segments.Add(T("recovery", 2f, 7.5f)); }
            iv.segments.Add(T("cooldown", 5f, 6.5f));
            list.Add(iv);

            var py = new WorkoutDoc { id = "builtin:pyramide-23", name = "Pyramide 23 min", description = "1-2-3-2-1 min schnell" };
            py.segments.Add(T("warmup", 5f, 7f));
            foreach (float m in new[] { 1f, 2f, 3f, 2f, 1f }) { py.segments.Add(T("fast", m, 9f + m * 0.6f)); py.segments.Add(T("recovery", 1f, 7.5f)); }
            py.segments.RemoveAt(py.segments.Count - 1);
            py.segments.Add(T("cooldown", 5f, 6.5f));
            list.Add(py);

            var hill = new WorkoutDoc { id = "builtin:huegel-25", name = "Hügel 25 min", description = "Steigung am Band: 3 · 5 · 7 %" };
            hill.segments.Add(T("warmup", 5f, 7f, 0f));
            foreach (float g in new[] { 3f, 5f, 7f }) { hill.segments.Add(T("climb", 3f, 7f, g)); hill.segments.Add(T("recovery", 2f, 7.5f, 1f)); }
            hill.segments.Add(T("cooldown", 5f, 6.5f, 0f));
            list.Add(hill);

            var tempo = new WorkoutDoc { id = "builtin:tempo-25", name = "Tempodauerlauf 25 min", description = "15 min gleichmäßig zügig" };
            tempo.segments.Add(T("warmup", 5f, 7f));
            tempo.segments.Add(T("run", 15f, 10f));
            tempo.segments.Add(T("cooldown", 5f, 6.5f));
            list.Add(tempo);

            var z2 = new WorkoutDoc { id = "builtin:zone2-30", name = "Zone 2 · 30 min", description = "Grundlage nach Puls (Pulsgurt)" };
            z2.segments.Add(new WorkoutSegment { kind = "warmup", durationS = 300f, hrZone = 1 });
            z2.segments.Add(new WorkoutSegment { kind = "run", label = "Grundlage", durationS = 1200f, hrZone = 2 });
            z2.segments.Add(new WorkoutSegment { kind = "cooldown", durationS = 300f, hrZone = 1 });
            list.Add(z2);

            var easy = new WorkoutDoc { id = "builtin:locker-5k", name = "5 km locker", description = "nach Distanz" };
            easy.segments.Add(D("warmup", 500f, 7f));
            easy.segments.Add(D("run", 4000f, 8.5f));
            easy.segments.Add(D("cooldown", 500f, 6.5f));
            list.Add(easy);

            return list;
        }
    }

    /// <summary>
    /// Own workouts as <c>.jogworkout</c> JSON files (same shape as <see cref="WorkoutDoc"/>) in
    /// <c>workouts/</c> of the app data folder; broken files are skipped.
    /// </summary>
    public static class WorkoutStore
    {
        public const string Extension = ".jogworkout";

        public static string Folder
        {
            get
            {
                string f = Path.Combine(Jogging.Core.DataPaths.Root, "workouts");
                try { Directory.CreateDirectory(f); } catch { }
                return f;
            }
        }

        /// <summary>Built-ins first, then own files (by name).</summary>
        public static List<WorkoutDoc> LoadAll()
        {
            var list = WorkoutPresets.All();
            var own = new List<WorkoutDoc>();
            try
            {
                foreach (var file in Directory.GetFiles(Folder, "*" + Extension))
                {
                    var d = Jogging.Core.SafeFile.Read(file, FromJson); // broken file → its backup
                    if (d == null) { Debug.LogWarning($"[Jogging] Workout {Path.GetFileName(file)} ungültig"); continue; }
                    if (string.IsNullOrEmpty(d.id)) d.id = "file:" + Path.GetFileNameWithoutExtension(file);
                    d.path = file;
                    own.Add(d);
                }
            }
            catch (Exception e) { Debug.LogWarning($"[Jogging] Workouts nicht lesbar: {e.Message}"); }
            own.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.CurrentCultureIgnoreCase));
            list.AddRange(own);
            return list;
        }

        public static string ToJson(WorkoutDoc d) => JsonUtility.ToJson(d, true);

        /// <summary>Save an own workout (new ones get an id and a file); built-ins are never overwritten.</summary>
        public static void Save(WorkoutDoc d)
        {
            if (d.BuiltIn) { d.id = ""; d.path = null; } // saving a built-in = saving a copy
            if (string.IsNullOrEmpty(d.id) || d.id.StartsWith("file:")) d.id = "wk_" + Jogging.Route.RouteStore.NewId().Substring(3);
            if (string.IsNullOrEmpty(d.path)) d.path = Path.Combine(Folder, d.id + Extension);
            Jogging.Core.SafeFile.Write(d.path, ToJson(d));
        }

        public static void Delete(WorkoutDoc d)
        {
            if (d == null || d.BuiltIn || string.IsNullOrEmpty(d.path)) return;
            try
            {
                Jogging.Core.SafeFile.Delete(d.path);
            }
            catch (Exception e) { Debug.LogWarning($"[Jogging] Workout nicht gelöscht: {e.Message}"); }
        }

        /// <summary>Parse and validate (at least one segment that ends by time or distance); null if unusable.</summary>
        public static WorkoutDoc FromJson(string json)
        {
            try
            {
                var d = JsonUtility.FromJson<WorkoutDoc>(json);
                if (d == null || d.segments == null) return null;
                d.segments.RemoveAll(s => s == null || (s.durationS <= 0f && s.distanceM <= 0f));
                return d.segments.Count > 0 ? d : null;
            }
            catch { return null; }
        }
    }
}
