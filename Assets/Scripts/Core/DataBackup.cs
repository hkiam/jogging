using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Jogging.Core
{
    /// <summary>
    /// Backup of all the app's data (runners, logbook, routes, workouts, settings) as one ZIP file,
    /// and restoring it. Restoring first saves the current state as a backup of its own, so it can
    /// always be undone; only paths inside the data folder are extracted (no "../" tricks), and a
    /// failed restore leaves the previous data in place.
    /// Plain .NET (tested by BackupCheck).
    /// </summary>
    public static class DataBackup
    {
        public static readonly string[] Parts = { "profiles", "sessions", "routes", "workouts", "settings.json" };
        public const string Prefix = "Jogging-Sicherung-";

        /// <summary>ZIP of <paramref name="dataRoot"/>'s parts into <paramref name="targetDir"/>; returns the file.</summary>
        public static string Create(string dataRoot, string targetDir, string label = "")
        {
            Directory.CreateDirectory(targetDir);
            string name = Prefix + DateTime.Now.ToString("yyyy-MM-dd-HHmmss") + (label != "" ? "-" + label : "") + ".zip";
            string path = Path.Combine(targetDir, name);
            for (int n = 2; File.Exists(path); n++) path = Path.Combine(targetDir, name.Replace(".zip", $"-{n}.zip")); // same second
            string tmp = path + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);
            using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
            {
                foreach (var part in Parts)
                {
                    string p = Path.Combine(dataRoot, part);
                    if (File.Exists(p)) zip.CreateEntryFromFile(p, part);
                    else if (Directory.Exists(p))
                        foreach (var f in Directory.GetFiles(p, "*", SearchOption.AllDirectories))
                        {
                            if (f.EndsWith(".tmp")) continue;
                            zip.CreateEntryFromFile(f, Path.GetRelativePath(dataRoot, f).Replace('\\', '/'));
                        }
                }
            }
            File.Move(tmp, path);
            return path;
        }

        /// <summary>Backups found in these folders, newest first.</summary>
        public static List<string> Find(params string[] dirs)
        {
            var list = new List<string>();
            foreach (var d in dirs)
                if (Directory.Exists(d)) list.AddRange(Directory.GetFiles(d, Prefix + "*.zip"));
            list.Sort((a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
            return list;
        }

        /// <summary>What a backup holds (runners, runs), without extracting; null if it is no backup.</summary>
        public static (int runners, int runs)? Inspect(string zipPath)
        {
            try
            {
                using var zip = ZipFile.OpenRead(zipPath);
                int runners = 0, runs = 0;
                foreach (var e in zip.Entries)
                {
                    if (e.FullName.StartsWith("profiles/") && e.FullName.EndsWith(".json")) runners++;
                    else if (e.FullName.StartsWith("sessions/") && e.FullName.EndsWith(".json") && !e.FullName.EndsWith("index.json")) runs++;
                }
                return runners > 0 ? (runners, runs) : ((int, int)?)null;
            }
            catch { return null; }
        }

        /// <summary>Test hook: called with each part just before it is swapped in (a test may throw here).</summary>
        public static Action<string> BeforeSwap;

        /// <summary>
        /// Replace the data with the backup. The current state is saved first to
        /// <paramref name="safetyDir"/>; returns that safety backup's path.
        /// The backup is extracted into a staging folder inside the data folder and checked there;
        /// only then are the parts swapped in by renaming (same volume). Runners and logbook are one
        /// unit and always replaced together; routes, workouts and settings only if the backup holds
        /// them (a backup without routes keeps the local routes). On any error during the swap the
        /// previous state comes back (renamed back, else extracted from the safety backup) and the
        /// error is rethrown.
        /// </summary>
        public static string Restore(string zipPath, string dataRoot, string safetyDir)
        {
            if (Inspect(zipPath) == null) throw new InvalidDataException("keine Jogging-Sicherung");
            string safety = Create(dataRoot, safetyDir, "vor-Wiederherstellen");
            string staging = Path.Combine(dataRoot, ".wiederherstellen");
            string old = Path.Combine(dataRoot, ".vorher");
            foreach (var d in new[] { staging, old }) if (Directory.Exists(d)) Directory.Delete(d, true);
            try
            {
                Extract(zipPath, staging);
                Verify(staging);
            }
            catch
            {
                try { Directory.Delete(staging, true); } catch { }
                throw; // nothing touched yet
            }

            var swap = new List<string>();
            foreach (var part in Parts)
                if (part == "profiles" || part == "sessions" || Exists(Path.Combine(staging, part))) swap.Add(part);
            var moved = new List<string>(); // parts whose current version went to "old"
            var placed = new List<string>(); // parts taken from the backup
            try
            {
                Directory.CreateDirectory(old);
                foreach (var part in swap)
                {
                    BeforeSwap?.Invoke(part);
                    string cur = Path.Combine(dataRoot, part);
                    if (Exists(cur)) { Move(cur, Path.Combine(old, part)); moved.Add(part); }
                    string incoming = Path.Combine(staging, part);
                    if (Exists(incoming)) { Move(incoming, cur); placed.Add(part); }
                }
            }
            catch
            {
                try
                {
                    foreach (var part in placed) Delete(Path.Combine(dataRoot, part));
                    foreach (var part in moved) Move(Path.Combine(old, part), Path.Combine(dataRoot, part));
                }
                catch
                {
                    // renaming back failed as well → the safety backup has the previous state
                    foreach (var part in Parts) Delete(Path.Combine(dataRoot, part));
                    Extract(safety, dataRoot);
                }
                try { Directory.Delete(staging, true); } catch { }
                try { Directory.Delete(old, true); } catch { }
                throw;
            }
            try { Directory.Delete(staging, true); } catch { }
            try { Directory.Delete(old, true); } catch { }
            return safety;
        }

        // Known parts of the ZIP into targetDir; nothing outside it and nothing but the parts.
        private static void Extract(string zipPath, string targetDir)
        {
            string root = Path.GetFullPath(targetDir) + Path.DirectorySeparatorChar;
            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var e in zip.Entries)
            {
                if (string.IsNullOrEmpty(e.Name)) continue; // folder entry
                string target = Path.GetFullPath(Path.Combine(targetDir, e.FullName));
                if (!target.StartsWith(root, StringComparison.Ordinal)) continue; // outside the folder
                string rel = target.Substring(root.Length).Replace('\\', '/'); // normalized: "profiles/../x" → "x"
                if (!IsPart(rel)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                e.ExtractToFile(target, true);
                if (new FileInfo(target).Length != e.Length) throw new InvalidDataException(Loc.F("Sicherung beschädigt: {0}", rel));
            }
        }

        public static bool IsPart(string relPath)
        {
            foreach (var part in Parts) if (relPath == part || relPath.StartsWith(part + "/", StringComparison.Ordinal)) return true;
            return false;
        }

        // The extracted data must contain runners, and every JSON file must at least look like JSON.
        private static void Verify(string staging)
        {
            string profiles = Path.Combine(staging, "profiles");
            if (!Directory.Exists(profiles) || Directory.GetFiles(profiles, "*.json").Length == 0)
                throw new InvalidDataException("Sicherung enthält keine Läufer");
            foreach (var f in Directory.GetFiles(staging, "*.json", SearchOption.AllDirectories))
            {
                string t = File.ReadAllText(f).Trim();
                if (!t.StartsWith("{") || !t.EndsWith("}")) throw new InvalidDataException(Loc.F("Sicherung beschädigt: {0}", Path.GetFileName(f)));
            }
        }

        private static bool Exists(string p) => File.Exists(p) || Directory.Exists(p);

        private static void Move(string from, string to)
        {
            if (Directory.Exists(from)) Directory.Move(from, to);
            else File.Move(from, to);
        }

        private static void Delete(string p)
        {
            if (File.Exists(p)) File.Delete(p);
            else if (Directory.Exists(p)) Directory.Delete(p, true);
        }
    }
}
