using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;
using Jogging.Core;

namespace Jogging.EditorTools
{
    /// <summary>
    /// Self-check of the data backup: create, inspect, restore (with safety backup), no path escapes
    /// (also "profiles/../"), parts missing in a backup are kept, broken backups are rejected and a
    /// failure while swapping brings the previous state back. Part of Tools/check.sh.
    /// </summary>
    public static class BackupCheck
    {
        public static void RunBatch() => EditorApplication.Exit(Run() ? 0 : 1);

        public static bool Run()
        {
            var fails = new List<string>();
            void Ok(bool c, string what) { if (!c) fails.Add(what); }
            string root = Path.Combine(Path.GetTempPath(), "jogging-backup-" + Guid.NewGuid().ToString("N"));
            string data = Path.Combine(root, "data"), out1 = Path.Combine(root, "out"), safe = Path.Combine(root, "safe");
            try
            {
                Directory.CreateDirectory(Path.Combine(data, "profiles"));
                Directory.CreateDirectory(Path.Combine(data, "sessions", "r1"));
                File.WriteAllText(Path.Combine(data, "profiles", "r1.json"), "{\"playerName\":\"A\"}");
                File.WriteAllText(Path.Combine(data, "sessions", "r1", "20260101T000000Z.json"), "{}");
                File.WriteAllText(Path.Combine(data, "sessions", "r1", "index.json"), "{}");
                File.WriteAllText(Path.Combine(data, "settings.json"), "{\"x\":1}");
                File.WriteAllText(Path.Combine(data, "fremd.txt"), "bleibt");

                string zip = DataBackup.Create(data, out1);
                var info = DataBackup.Inspect(zip);
                Ok(info.HasValue && info.Value.runners == 1 && info.Value.runs == 1, $"Inhalt: {info}");
                Ok(DataBackup.Find(out1).Count == 1, "Sicherung nicht gefunden");

                // change the data, then restore
                File.WriteAllText(Path.Combine(data, "profiles", "r1.json"), "{\"playerName\":\"B\"}");
                File.WriteAllText(Path.Combine(data, "profiles", "r2.json"), "{\"playerName\":\"neu\"}");
                string safety = DataBackup.Restore(zip, data, safe);
                Ok(File.ReadAllText(Path.Combine(data, "profiles", "r1.json")).Contains("\"A\""), "Profil nicht wiederhergestellt");
                Ok(!File.Exists(Path.Combine(data, "profiles", "r2.json")), "neueres Profil nicht entfernt");
                Ok(File.Exists(Path.Combine(data, "sessions", "r1", "20260101T000000Z.json")), "Lauf fehlt");
                Ok(File.Exists(Path.Combine(data, "fremd.txt")), "fremde Datei gelöscht");
                Ok(DataBackup.Inspect(safety)?.runners == 2, "Sicherheitskopie vor dem Wiederherstellen fehlt");

                // a ZIP that tries to write outside the data folder, and a ZIP that is no backup
                string evil = Path.Combine(root, "Jogging-Sicherung-evil.zip");
                using (var z = ZipFile.Open(evil, ZipArchiveMode.Create))
                {
                    using (var w = new StreamWriter(z.CreateEntry("profiles/r1.json").Open())) w.Write("{\"playerName\":\"E\"}");
                    using (var w = new StreamWriter(z.CreateEntry("../ausbruch.txt").Open())) w.Write("x");
                }
                DataBackup.Restore(evil, data, safe);
                Ok(!File.Exists(Path.Combine(root, "ausbruch.txt")), "Pfad außerhalb des Datenordners geschrieben");
                string none = Path.Combine(root, "Jogging-Sicherung-leer.zip");
                using (var z = ZipFile.Open(none, ZipArchiveMode.Create)) { using var w = new StreamWriter(z.CreateEntry("x.txt").Open()); w.Write("x"); }
                bool threw = false; try { DataBackup.Restore(none, data, safe); } catch (InvalidDataException) { threw = true; }
                Ok(threw && File.Exists(Path.Combine(data, "profiles", "r1.json")), "Nicht-Sicherung wiederhergestellt");

                // "profiles/../x" is normalized before the part check (no writing next to the parts)
                string sneaky = Path.Combine(root, "Jogging-Sicherung-sneaky.zip");
                using (var z = ZipFile.Open(sneaky, ZipArchiveMode.Create))
                {
                    using (var w = new StreamWriter(z.CreateEntry("profiles/r1.json").Open())) w.Write("{\"playerName\":\"S\"}");
                    using (var w = new StreamWriter(z.CreateEntry("profiles/../Sicherungen/x.txt").Open())) w.Write("x");
                    using (var w = new StreamWriter(z.CreateEntry("profiles/../fremd.txt").Open())) w.Write("überschrieben");
                }
                DataBackup.Restore(sneaky, data, safe);
                Ok(!File.Exists(Path.Combine(data, "Sicherungen", "x.txt")), "profiles/../ nicht normalisiert");
                Ok(File.ReadAllText(Path.Combine(data, "fremd.txt")) == "bleibt", "fremde Datei über profiles/../ überschrieben");
                Ok(DataBackup.IsPart("profiles/a.json") && !DataBackup.IsPart("profilesX/a.json") && !DataBackup.IsPart("Sicherungen/x"), "IsPart");

                // Parts the backup doesn't hold stay (routes, workouts); runners and logbook are replaced together.
                Directory.CreateDirectory(Path.Combine(data, "routes"));
                File.WriteAllText(Path.Combine(data, "routes", "rt_x.jogroute"), "{}");
                Directory.CreateDirectory(Path.Combine(data, "sessions", "r9"));
                File.WriteAllText(Path.Combine(data, "sessions", "r9", "20260102T000000Z.json"), "{}");
                string onlyProfiles = Path.Combine(root, "Jogging-Sicherung-nurlaeufer.zip");
                using (var z = ZipFile.Open(onlyProfiles, ZipArchiveMode.Create))
                    using (var w = new StreamWriter(z.CreateEntry("profiles/r5.json").Open())) w.Write("{\"playerName\":\"P\"}");
                DataBackup.Restore(onlyProfiles, data, safe);
                Ok(File.Exists(Path.Combine(data, "routes", "rt_x.jogroute")), "Strecken gelöscht, obwohl nicht in der Sicherung");
                Ok(File.Exists(Path.Combine(data, "profiles", "r5.json")) && !File.Exists(Path.Combine(data, "profiles", "r1.json")), "Läufer nicht ersetzt");
                Ok(!Directory.Exists(Path.Combine(data, "sessions")), "Logbuch nicht mit den Läufern ersetzt");
                Ok(!Directory.Exists(Path.Combine(data, ".wiederherstellen")) && !Directory.Exists(Path.Combine(data, ".vorher")), "Zwischenordner bleiben liegen");

                // A broken backup (truncated JSON) is rejected before anything is touched.
                string broken = Path.Combine(root, "Jogging-Sicherung-kaputt.zip");
                using (var z = ZipFile.Open(broken, ZipArchiveMode.Create))
                {
                    using (var w = new StreamWriter(z.CreateEntry("profiles/r7.json").Open())) w.Write("{\"playerName\":\"K\"}");
                    using (var w = new StreamWriter(z.CreateEntry("sessions/r7/1.json").Open())) w.Write("{\"summary\":{\"dist");
                }
                threw = false; try { DataBackup.Restore(broken, data, safe); } catch (InvalidDataException) { threw = true; }
                Ok(threw && File.Exists(Path.Combine(data, "profiles", "r5.json")) && !File.Exists(Path.Combine(data, "profiles", "r7.json")), "kaputte Sicherung teilweise übernommen");

                // A failure in the middle of the swap brings the previous state back.
                string before = File.ReadAllText(Path.Combine(data, "profiles", "r5.json"));
                DataBackup.BeforeSwap = part => { if (part == "settings.json") throw new IOException("Test: Platte voll"); };
                threw = false;
                try { DataBackup.Restore(zip, data, safe); } catch (IOException) { threw = true; }
                finally { DataBackup.BeforeSwap = null; }
                Ok(threw, "Fehler beim Tauschen nicht gemeldet");
                Ok(File.Exists(Path.Combine(data, "profiles", "r5.json")) && File.ReadAllText(Path.Combine(data, "profiles", "r5.json")) == before
                   && !File.Exists(Path.Combine(data, "profiles", "r1.json")), "nach Fehler: alte Läufer nicht zurück");
                Ok(File.Exists(Path.Combine(data, "routes", "rt_x.jogroute")) && File.ReadAllText(Path.Combine(data, "settings.json")) == "{\"x\":1}", "nach Fehler: alte Daten nicht zurück");
            }
            catch (Exception e) { fails.Add("Ausnahme: " + e); }
            finally { try { Directory.Delete(root, true); } catch { } }
            if (fails.Count == 0) Debug.Log("[BackupCheck] Alle Prüfungen bestanden.");
            else Debug.LogError("[BackupCheck] FAIL → " + string.Join("; ", fails));
            return fails.Count == 0;
        }
    }
}
