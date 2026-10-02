using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Jogging.Profile
{
    /// <summary>Key figures of one run (what lists and statistics need; no time series).</summary>
    [Serializable]
    public class SessionSummary
    {
        public string file = "";        // session file name (without folder)
        public string runnerId = "";
        public string start = "";       // ISO 8601 UTC
        public string end = "";
        public string routeId = "";     // empty = unsaved route (e.g. Quick Run)
        public int revision;
        public string routeName = "";
        public float distanceM;
        public float seconds;
        public float gainM;
        public float avgKmh;
        public float maxKmh;
        public bool completed;          // whole route run (finite routes only)
        public string source = "keys";  // "belt" | "keys"
        public string workoutId = "";   // empty = free run
        public string workoutName = "";
        public bool workoutCompleted;
        public string planId = "";      // run as a session of this training plan
        public int planIndex = -1;
        public float kcal;              // 0 = no weight set
        public float best5kS;           // fastest 5 km within this run (s); 0 = shorter run
        public int avgHr;               // 0 = no heart rate
        public int maxHr;
        public List<float> hrZoneSeconds = new List<float>(); // seconds in zone 1…5 (empty without heart rate)
        public List<string> sights = new List<string>();     // passed on the way: "Kapelle", "Brücke:2" … (German keys)
        public List<float> surfaceM = new List<float>();     // metres on asphalt, gravel, earth, forest floor, meadow path
        public List<float> climbS = new List<float>();       // climb segments of the route (Route/ClimbSegments): seconds, 0 = not run through
        public int overtakes;                                // fellow runners overtaken
    }

    /// <summary>One sample per second of running time.</summary>
    [Serializable]
    public struct SessionSample
    {
        public float t, distM, kmh, incline;
        public int hr; // 0 = no heart rate
    }

    /// <summary>A finished run: summary + time series.</summary>
    [Serializable]
    public class SessionRecord
    {
        public int schemaVersion = 1;
        public SessionSummary summary = new SessionSummary();
        public List<SessionSample> samples = new List<SessionSample>();
    }

    [Serializable]
    internal class SessionIndex
    {
        public List<SessionSummary> sessions = new List<SessionSummary>();
        public List<string> broken = new List<string>(); // run files that couldn't be read (so the index still matches the folder)
    }

    /// <summary>
    /// The logbook: <c>sessions/&lt;runnerId&gt;/&lt;start&gt;.json</c> per run plus an
    /// <c>index.json</c> of summaries per runner, so menus never parse the time series. A missing or
    /// broken index is rebuilt from the session files. A run in progress is checkpointed to
    /// <c>inprogress.checkpoint</c> (not a *.json, so never part of the logbook until recovered).
    /// </summary>
    public class SessionStore
    {
        /// <summary>Shorter runs are not recorded (accidental starts).</summary>
        public const float MinSeconds = 30f, MinMeters = 1f;
        public const string CheckpointFile = "inprogress.checkpoint";

        private readonly string root;

        public SessionStore(string root = null) => this.root = root ?? Jogging.Core.DataPaths.Root;

        private string Dir(string runnerId) => Path.Combine(root, "sessions", runnerId);
        private string IndexPath(string runnerId) => Path.Combine(Dir(runnerId), "index.json");

        public void Save(SessionRecord rec)
        {
            var s = rec.summary;
            if (string.IsNullOrEmpty(s.runnerId)) return;
            try
            {
                Directory.CreateDirectory(Dir(s.runnerId));
                if (string.IsNullOrEmpty(s.file))
                    s.file = (s.start.Length > 0 ? s.start : DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"))
                             .Replace(":", "").Replace("-", "") + ".json";
                var idx = LoadIndex(s.runnerId); // before the new file (else it wouldn't match the folder)
                Core.SafeFile.Write(Path.Combine(Dir(s.runnerId), s.file), JsonUtility.ToJson(rec));
                idx.broken.Remove(s.file);
                idx.sessions.RemoveAll(x => x.file == s.file);
                idx.sessions.Add(s);
                Core.SafeFile.Write(IndexPath(s.runnerId), JsonUtility.ToJson(idx, true));
            }
            catch (Exception e) { Debug.LogError($"[Jogging] Lauf konnte nicht gespeichert werden: {e.Message}"); }
        }

        /// <summary>All run summaries of a runner, oldest first.</summary>
        public List<SessionSummary> Summaries(string runnerId)
        {
            if (string.IsNullOrEmpty(runnerId)) return new List<SessionSummary>();
            var list = LoadIndex(runnerId).sessions;
            list.Sort((a, b) => string.CompareOrdinal(a.start, b.start));
            return list;
        }

        /// <summary>Remove one run (file, backup and index entry).</summary>
        public void Delete(string runnerId, string file)
        {
            if (string.IsNullOrEmpty(runnerId) || string.IsNullOrEmpty(file)) return;
            try
            {
                var idx = LoadIndex(runnerId);
                Core.SafeFile.Delete(Path.Combine(Dir(runnerId), file));
                idx.sessions.RemoveAll(x => x.file == file);
                idx.broken.Remove(file);
                Core.SafeFile.Write(IndexPath(runnerId), JsonUtility.ToJson(idx, true));
            }
            catch (Exception e) { Debug.LogError($"[Jogging] Lauf konnte nicht gelöscht werden: {e.Message}"); }
        }

        /// <summary>Remove a runner's whole logbook.</summary>
        public void DeleteAll(string runnerId)
        {
            if (string.IsNullOrEmpty(runnerId)) return;
            try { if (Directory.Exists(Dir(runnerId))) Directory.Delete(Dir(runnerId), true); }
            catch (Exception e) { Debug.LogError($"[Jogging] Logbuch konnte nicht gelöscht werden: {e.Message}"); }
        }

        /// <summary>Full record (with samples), or null.</summary>
        public SessionRecord Load(string runnerId, string file)
        {
            if (string.IsNullOrEmpty(runnerId) || string.IsNullOrEmpty(file)) return null;
            try { return Core.SafeFile.Read(Path.Combine(Dir(runnerId), file), ParseRecord); }
            catch { return null; }
        }

        private static SessionRecord ParseRecord(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("{")) return null;
            var r = JsonUtility.FromJson<SessionRecord>(json);
            return r?.summary != null ? r : null;
        }

        // ---- checkpoint of the run in progress (a crash then costs at most the last minute)

        private string CheckpointPath(string runnerId) => Path.Combine(Dir(runnerId), CheckpointFile);

        public void SaveCheckpoint(SessionRecord rec)
        {
            string id = rec.summary.runnerId;
            if (string.IsNullOrEmpty(id)) return;
            try
            {
                Directory.CreateDirectory(Dir(id));
                Core.SafeFile.Write(CheckpointPath(id), JsonUtility.ToJson(rec));
            }
            catch (Exception e) { Debug.LogWarning($"[Jogging] Zwischenstand nicht gespeichert: {e.Message}"); }
        }

        public void DeleteCheckpoint(string runnerId)
        {
            if (string.IsNullOrEmpty(runnerId)) return;
            try { Core.SafeFile.Delete(CheckpointPath(runnerId)); }
            catch (Exception e) { Debug.LogWarning($"[Jogging] Zwischenstand nicht gelöscht: {e.Message}"); }
        }

        /// <summary>Runners (folder names) that have a checkpoint left over.</summary>
        public List<string> RunnersWithCheckpoint()
        {
            var list = new List<string>();
            string dir = Path.Combine(root, "sessions");
            if (!Directory.Exists(dir)) return list;
            foreach (var d in Directory.GetDirectories(dir))
            {
                string p = Path.Combine(d, CheckpointFile);
                if (File.Exists(p) || File.Exists(Core.SafeFile.BackupOf(p))) list.Add(Path.GetFileName(d));
            }
            return list;
        }

        /// <summary>
        /// A checkpoint left by a crash becomes a normal run in the logbook (if it is long enough and not
        /// already there); the checkpoint is removed either way. Returns the recovered run, or null.
        /// </summary>
        public SessionRecord RecoverCheckpoint(string runnerId)
        {
            if (string.IsNullOrEmpty(runnerId)) return null;
            var rec = Core.SafeFile.Read(CheckpointPath(runnerId), ParseRecord);
            DeleteCheckpoint(runnerId);
            if (rec == null) return null;
            var s = rec.summary;
            s.runnerId = runnerId; s.file = "";
            if (s.distanceM < MinMeters || s.seconds < MinSeconds) return null;
            foreach (var x in Summaries(runnerId)) if (x.start == s.start) return null; // saved before the crash
            Save(rec);
            Debug.Log($"[Jogging] Abgebrochener Lauf übernommen: {s.distanceM:0} m, {s.seconds:0} s vom {s.start}");
            return rec;
        }

        private SessionIndex LoadIndex(string runnerId)
        {
            // The run files are the truth; the index is only a cache of their summaries. No fallback to
            // the index backup (it is one run behind): rebuild when missing, unreadable or not matching
            // the run files by name (e.g. a crash between saving a run and updating the index), and
            // save the rebuilt index (unreadable files are listed in it, so it matches next time).
            if (!Directory.Exists(Dir(runnerId))) return new SessionIndex();
            var files = RunFiles(runnerId);
            try
            {
                if (File.Exists(IndexPath(runnerId)))
                {
                    var idx = JsonUtility.FromJson<SessionIndex>(File.ReadAllText(IndexPath(runnerId)));
                    if (idx != null && idx.sessions != null)
                    {
                        idx.broken ??= new List<string>(); // index from an older version
                        var known = new HashSet<string>(idx.broken);
                        foreach (var x in idx.sessions) known.Add(x.file);
                        if (known.SetEquals(files) && known.Count == idx.sessions.Count + idx.broken.Count) return idx;
                    }
                }
            }
            catch { /* rebuilt below */ }
            if (files.Count > 0) Debug.Log("[Jogging] Lauf-Index wird aus den Lauf-Dateien neu aufgebaut.");
            var rebuilt = Rebuild(runnerId, files);
            try { Core.SafeFile.Write(IndexPath(runnerId), JsonUtility.ToJson(rebuilt, true)); }
            catch (Exception e) { Debug.LogWarning($"[Jogging] Lauf-Index nicht gespeichert: {e.Message}"); }
            return rebuilt;
        }

        // Run file names (".bak"/".tmp" files and the checkpoint don't match *.json).
        private HashSet<string> RunFiles(string runnerId)
        {
            var set = new HashSet<string>();
            foreach (var f in Directory.GetFiles(Dir(runnerId), "*.json"))
                if (Path.GetFileName(f) != "index.json") set.Add(Path.GetFileName(f));
            return set;
        }

        private SessionIndex Rebuild(string runnerId, HashSet<string> files)
        {
            var idx = new SessionIndex();
            foreach (var name in files)
            {
                SessionRecord r = null;
                try { r = Core.SafeFile.Read(Path.Combine(Dir(runnerId), name), ParseRecord); }
                catch { /* broken */ }
                if (r?.summary == null) { idx.broken.Add(name); continue; }
                r.summary.file = name;
                idx.sessions.Add(r.summary);
            }
            return idx;
        }
    }
}
