using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Jogging.Profile
{
    /// <summary>
    /// Stores each runner as <c>profiles/&lt;id&gt;.json</c> in <see cref="Application.persistentDataPath"/>.
    /// A single-runner <c>profile.json</c> from earlier versions becomes the first runner (the old
    /// file is kept as <c>profile.json.migrated</c>). Robust against missing/corrupt files.
    /// </summary>
    public class LocalProfileStore : IProfileStore
    {
        private readonly string root;

        /// <param name="root">Data folder; null = persistentDataPath (tests pass a temp folder).</param>
        public LocalProfileStore(string root = null) => this.root = root ?? Jogging.Core.DataPaths.Root;

        private string Dir => Path.Combine(root, "profiles");
        private string PathFor(string id) => Path.Combine(Dir, id + ".json");

        public List<ProfileData> LoadAll()
        {
            MigrateLegacy();
            var list = new List<ProfileData>();
            if (!Directory.Exists(Dir)) return list;
            // Every runner file, plus runners that only exist as a backup because a write was interrupted
            // (a .tmp is left too). A backup alone is an orphan of a deleted runner and stays ignored.
            var files = new HashSet<string>(Directory.GetFiles(Dir, "*.json"));
            foreach (var b in Directory.GetFiles(Dir, "*.json.bak"))
            {
                string f = b.Substring(0, b.Length - 4);
                if (Core.SafeFile.InterruptedWrite(f)) files.Add(f);
            }
            foreach (var f in files)
            {
                var d = Core.SafeFile.Read(f, ParseRunner);
                if (d == null) { Debug.LogWarning($"[Jogging] Läufer {Path.GetFileName(f)} nicht lesbar (auch keine Sicherung) – Datei bleibt liegen."); continue; }
                if (string.IsNullOrEmpty(d.id)) d.id = Path.GetFileNameWithoutExtension(f);
                list.Add(d);
            }
            list.Sort((a, b) => string.CompareOrdinal(a.created, b.created));
            return list;
        }

        public bool Save(ProfileData data)
        {
            try
            {
                if (string.IsNullOrEmpty(data.id)) data.id = "rn_" + Route.RouteStore.NewId().Substring(3); // runner, not route
                if (string.IsNullOrEmpty(data.created)) data.created = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                Directory.CreateDirectory(Dir);
                Core.SafeFile.Write(PathFor(data.id), JsonUtility.ToJson(data, true));
                return true;
            }
            catch (Exception e) { Debug.LogError($"[Jogging] Läufer konnte nicht gespeichert werden: {e.Message}"); return false; }
        }

        // A runner file must at least carry a name (JsonUtility turns garbage into defaults otherwise).
        private static ProfileData ParseRunner(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("{")) return null;
            var d = JsonUtility.FromJson<ProfileData>(json);
            return d != null && !string.IsNullOrEmpty(d.playerName) ? d : null;
        }

        public void Delete(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            try
            {
                Core.SafeFile.Delete(PathFor(id));
            }
            catch (Exception e) { Debug.LogError($"[Jogging] Läufer konnte nicht gelöscht werden: {e.Message}"); }
        }

        // Fixed id of the migrated runner: if renaming the old file failed last time, the runner is
        // already there and is not added a second time.
        private const string LegacyId = "rn_00000000000000000000000000";

        /// <summary>Old single profile.json → first runner (once). The old file is renamed only after a verified save.</summary>
        private void MigrateLegacy()
        {
            string legacy = Path.Combine(root, "profile.json");
            if (!File.Exists(legacy)) return;
            try
            {
                var d = JsonUtility.FromJson<ProfileData>(File.ReadAllText(legacy));
                if (Core.SafeFile.Read(PathFor(LegacyId), ParseRunner) == null)
                {
                    if (d == null || string.IsNullOrEmpty(d.playerName)) { Debug.LogWarning("[Jogging] Altes Profil nicht lesbar – bleibt liegen."); return; }
                    d.id = LegacyId; d.created = "";
                    if (!Save(d) || Core.SafeFile.Read(PathFor(LegacyId), ParseRunner) == null)
                    {
                        Debug.LogWarning("[Jogging] Altes Profil nicht übernommen (Speichern fehlgeschlagen) – nächster Versuch beim nächsten Start.");
                        return;
                    }
                }
                string moved = legacy + ".migrated";
                if (File.Exists(moved)) File.Delete(moved);
                File.Move(legacy, moved);
                Debug.Log($"[Jogging] Altes Profil übernommen als Läufer „{d?.playerName}“.");
            }
            catch (Exception e) { Debug.LogWarning($"[Jogging] Altes Profil nicht übernommen: {e.Message}"); }
        }
    }
}
