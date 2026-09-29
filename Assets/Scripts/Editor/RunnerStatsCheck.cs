using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Jogging.Profile;

namespace Jogging.EditorTools
{
    /// <summary>
    /// Self-check of runners and logbook in a temp folder: migration of an old single profile.json,
    /// several runners, session save/load round trip (with samples), index rebuild, crash-safe files
    /// (backup fallback, orphaned backups), the checkpoint of a run in progress, and the statistics
    /// (week totals, streak, weekly champion, pace).
    /// Menu: Jogging → Läufer → Logbuch prüfen. Headless: -executeMethod Jogging.EditorTools.RunnerStatsCheck.RunBatch
    /// </summary>
    public static class RunnerStatsCheck
    {
        [MenuItem("Jogging/Läufer/Logbuch prüfen")]
        public static void RunMenu() => EditorUtility.DisplayDialog("Jogging", Run() ? "Alle Prüfungen bestanden." : "Fehler – siehe Console.", "OK");

        public static void RunBatch() => EditorApplication.Exit(Run() ? 0 : 1);

        public static bool Run()
        {
            var fails = new List<string>();
            void Check(bool ok, string what) { if (!ok) fails.Add(what); }

            string root = Path.Combine(Path.GetTempPath(), "jogging-check-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                // Migration: old profile.json → first runner, old file kept.
                var legacy = new ProfileData { playerName = "Maik", figureModel = "Male_Adult_01", totalRuns = 7, totalDistanceMeters = 12345f };
                File.WriteAllText(Path.Combine(root, "profile.json"), JsonUtility.ToJson(legacy));
                var store = new LocalProfileStore(root);
                var all = store.LoadAll();
                Check(all.Count == 1 && all[0].playerName == "Maik" && all[0].totalRuns == 7 && all[0].figureModel == "Male_Adult_01", "Migration: Daten nicht übernommen");
                Check(all.Count == 1 && !string.IsNullOrEmpty(all[0].id) && !string.IsNullOrEmpty(all[0].created), "Migration: keine Id");
                Check(!File.Exists(Path.Combine(root, "profile.json")) && File.Exists(Path.Combine(root, "profile.json.migrated")), "Migration: alte Datei nicht umbenannt");
                Check(store.LoadAll().Count == 1, "Migration läuft doppelt");
                // Renaming the old file failed last time (it is still there): the runner is not added twice.
                File.Copy(Path.Combine(root, "profile.json.migrated"), Path.Combine(root, "profile.json"));
                Check(store.LoadAll().Count == 1 && !File.Exists(Path.Combine(root, "profile.json")), "Migration nach fehlgeschlagenem Umbenennen doppelt");

                // Second runner
                var anna = new ProfileData { playerName = "Anna", created = "2099-01-01T00:00:00Z" };
                store.Save(anna);
                all = store.LoadAll();
                Check(all.Count == 2 && all[1].playerName == "Anna", "Zweiter Läufer fehlt / Reihenfolge");
                string maik = all[0].id;

                // Crash-safe files: a corrupt runner file falls back to its backup.
                var annaFile = Path.Combine(root, "profiles", anna.id + ".json");
                anna.totalRuns = 4; store.Save(anna);                  // now there is a .bak with totalRuns 0
                File.WriteAllText(annaFile, "{ kaputt");               // crash mid-write
                var again = store.LoadAll().Find(r => r.id == anna.id);
                Check(again != null && again.playerName == "Anna", "kaputte Läufer-Datei: Sicherung nicht genutzt");
                File.Delete(annaFile);                                 // only the backup is left …
                Check(!store.LoadAll().Exists(r => r.id == anna.id), "verwaiste Sicherung eines gelöschten Läufers wieder aufgetaucht");
                File.WriteAllText(annaFile + ".tmp", "{");             // … because a write was interrupted
                Check(store.LoadAll().Exists(r => r.id == anna.id), "Läufer nur als Sicherung (Schreiben unterbrochen) nicht gefunden");
                File.Delete(annaFile + ".tmp");
                store.Save(anna);
                var tmpRunner = new ProfileData { playerName = "Weg" };
                store.Save(tmpRunner); store.Save(tmpRunner);          // has a .bak now
                store.Delete(tmpRunner.id);
                Check(!File.Exists(Path.Combine(root, "profiles", tmpRunner.id + ".json.bak")), "Läufer gelöscht, Sicherung bleibt liegen");
                var js = Jogging.Core.SafeFile.Read(Path.Combine(root, "nichts.json"), t => t);
                Check(js == null, "Lesen einer fehlenden Datei");
                Check(!File.Exists(annaFile + ".tmp"), "Temp-Datei bleibt liegen");

                // Sessions: Monday 2026-09-21 … fixed "now" Thursday 2026-09-24 12:00 local.
                var now = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Local);
                var sessions = new SessionStore(root);
                SessionRecord Rec(string runner, DateTime local, float m, float s)
                {
                    var r = new SessionRecord();
                    r.summary.runnerId = runner;
                    r.summary.start = local.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
                    r.summary.distanceM = m; r.summary.seconds = s;
                    for (int i = 0; i < 5; i++) r.samples.Add(new SessionSample { t = i, distM = i * 3f, kmh = 10.8f, incline = 2f });
                    return r;
                }
                sessions.Save(Rec(maik, now.AddDays(-1).AddHours(-3), 5000f, 1800f));   // Wed
                sessions.Save(Rec(maik, now.AddHours(-2), 3000f, 1000f));               // Thu (today)
                sessions.Save(Rec(maik, now.AddDays(-5), 8000f, 2900f));                // Sat last week
                sessions.Save(Rec(anna.id, now.AddDays(-2), 6000f, 2100f));             // Tue

                var ms = sessions.Summaries(maik);
                Check(ms.Count == 3, $"Maik: {ms.Count} statt 3 Läufe im Index");
                var full = ms.Count > 0 ? sessions.Load(maik, ms[0].file) : null;
                Check(full != null && full.samples.Count == 5 && Mathf.Approximately(full.samples[4].distM, 12f), "Lauf-Datei: Messpunkte fehlen");

                // Delete a run: file and index entry go.
                var extra = Rec(maik, now.AddDays(-3), 1234f, 500f);
                sessions.Save(extra);
                Check(sessions.Summaries(maik).Count == 4, "zusätzlicher Lauf fehlt");
                sessions.Delete(maik, extra.summary.file);
                Check(sessions.Summaries(maik).Count == 3 && !File.Exists(Path.Combine(root, "sessions", maik, extra.summary.file)), "Lauf nicht gelöscht");

                // Index rebuild
                File.Delete(Path.Combine(root, "sessions", maik, "index.json"));
                Check(sessions.Summaries(maik).Count == 3, "Index nicht neu aufgebaut");
                Check(File.Exists(Path.Combine(root, "sessions", maik, "index.json")), "neu aufgebauter Index nicht gespeichert");

                // A broken run file falls back to its backup; one without a backup is listed as broken
                // in the index, so the index still matches and is not rebuilt on every call.
                string runDir = Path.Combine(root, "sessions", maik);
                var first = sessions.Summaries(maik)[0];
                sessions.Save(sessions.Load(maik, first.file));      // second write → .bak
                File.WriteAllText(Path.Combine(runDir, first.file), "{ kaputt");
                var viaBak = sessions.Load(maik, first.file);
                Check(viaBak != null && viaBak.samples.Count == 5, "kaputte Lauf-Datei: Sicherung nicht genutzt");
                File.WriteAllText(Path.Combine(runDir, "20200101T000000Z.json"), "{ kaputt");
                File.Delete(Path.Combine(runDir, "index.json"));
                Check(sessions.Summaries(maik).Count == 3, "kaputte Lauf-Datei ohne Sicherung zählt als Lauf");
                var stamp = File.GetLastWriteTimeUtc(Path.Combine(runDir, "index.json"));
                System.Threading.Thread.Sleep(20);
                sessions.Summaries(maik);
                Check(File.GetLastWriteTimeUtc(Path.Combine(runDir, "index.json")) == stamp, "Index wird trotz kaputter Datei jedes Mal neu aufgebaut");
                File.Delete(Path.Combine(runDir, "20200101T000000Z.json"));
                Check(sessions.Summaries(maik).Count == 3, "Index nach Entfernen der kaputten Datei falsch");

                // Checkpoint of a run in progress: after a crash it becomes a normal run (once); too short → dropped.
                var cp = Rec(anna.id, now.AddDays(-4), 2500f, 900f);
                sessions.SaveCheckpoint(cp);
                Check(sessions.Summaries(anna.id).Count == 1, "Zwischenstand zählt schon als Lauf");
                Check(sessions.RunnersWithCheckpoint().Contains(anna.id), "Zwischenstand nicht gefunden");
                var got = sessions.RecoverCheckpoint(anna.id);
                Check(got != null && sessions.Summaries(anna.id).Count == 2, "Zwischenstand nicht als Lauf übernommen");
                Check(sessions.RunnersWithCheckpoint().Count == 0, "Zwischenstand bleibt liegen");
                sessions.SaveCheckpoint(cp);                                         // already saved (crash after saving)
                Check(sessions.RecoverCheckpoint(anna.id) == null && sessions.Summaries(anna.id).Count == 2, "Zwischenstand doppelt übernommen");
                sessions.SaveCheckpoint(Rec(anna.id, now.AddDays(-6), 50f, 12f));
                Check(sessions.RecoverCheckpoint(anna.id) == null && sessions.Summaries(anna.id).Count == 2 && sessions.RunnersWithCheckpoint().Count == 0,
                      "zu kurzer Zwischenstand übernommen / bleibt liegen");
                sessions.SaveCheckpoint(cp); sessions.DeleteCheckpoint(anna.id);     // "Verwerfen"
                Check(sessions.RunnersWithCheckpoint().Count == 0, "Zwischenstand nach Verwerfen nicht gelöscht");
                sessions.Delete(anna.id, got?.summary.file);                         // keep the statistics below as they were

                // Statistics
                var week = RunnerStats.Week(ms, now);
                Check(week.Runs == 2 && Mathf.Approximately(week.DistanceM, 8000f), $"Woche: {week.Runs} Läufe / {week.DistanceM} m statt 2 / 8000");
                Check(RunnerStats.All(ms).Runs == 3, "Gesamt falsch");
                Check(RunnerStats.StreakDays(ms, now) == 2, $"Serie {RunnerStats.StreakDays(ms, now)} statt 2");
                Check(RunnerStats.StreakDays(ms, now.AddDays(1)) == 2, "Serie von gestern zählt nicht mehr");
                Check(RunnerStats.StreakDays(ms, now.AddDays(2)) == 0, "Serie nach Pause nicht 0");
                Check(RunnerStats.WeekStart(now) == new DateTime(2026, 9, 21), "Wochenbeginn nicht Montag");
                var by = new Dictionary<string, List<SessionSummary>> { [maik] = ms, [anna.id] = sessions.Summaries(anna.id) };
                Check(RunnerStats.WeeklyChampion(by, now) == maik, "Wochen-Champion falsch");
                Check(RunnerStats.WeeklyChampion(by, now.AddDays(14)) == null, "Champion ohne Läufe");
                Check(RunnerStats.Last(ms)?.distanceM == 3000f, "Letzter Lauf falsch");
                Check(RunnerStats.Pace(5000f, 1500f) == "5:00 /km", $"Pace {RunnerStats.Pace(5000f, 1500f)}");
            }
            catch (Exception e) { fails.Add("Ausnahme: " + e); }
            finally { try { Directory.Delete(root, true); } catch { } }

            if (fails.Count == 0) Debug.Log("[RunnerCheck] Alle Prüfungen bestanden.");
            else Debug.LogError("[RunnerCheck] FAIL → " + string.Join("; ", fails));
            return fails.Count == 0;
        }
    }
}
