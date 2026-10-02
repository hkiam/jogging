using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Jogging.EditorTools
{
    /// <summary>
    /// Runs every self-check in one go (one Unity start) and prints a summary.
    /// Menu: Jogging → Alle Prüfungen. Headless: Tools/check.sh (-executeMethod Jogging.EditorTools.AllChecks.RunBatch)
    /// </summary>
    public static class AllChecks
    {
        private static readonly (string name, System.Func<bool> run)[] Checks =
        {
            ("Streckengenerator", RouteGeneratorCheck.Run),
            ("Strecken teilen", RouteShareCheck.Run),
            ("Läufer & Logbuch", RunnerStatsCheck.Run),
            ("Session", SessionCheck.Run),
            ("Bandsicherheit", BeltSafetyCheck.Run),
            ("Band-Simulator", BeltEmulatorCheck.Run),
            ("Workouts", WorkoutCheck.Run),
            ("Trainingspläne", PlanCheck.Run),
            ("Puls", HeartRateCheck.Run),
            ("Laufband-Protokolle", TreadmillProtocolsCheck.Run),
            ("Statistik", StatsCheck.Run),
            ("Datensicherung", BackupCheck.Run),
            ("Spiel", GameCheck.Run),
            ("Erosion (bitgleich)", ErosionCheck.Run),
        };

        [MenuItem("Jogging/Alle Prüfungen")]
        public static void RunMenu() => EditorUtility.DisplayDialog("Jogging", Run() ? "Alle Prüfungen bestanden." : "Fehler – siehe Console.", "OK");

        public static void RunBatch() => EditorApplication.Exit(Run() ? 0 : 1);

        public static bool Run()
        {
            var failed = new List<string>();
            foreach (var (name, run) in Checks)
            {
                bool ok;
                try { ok = run(); }
                catch (System.Exception e) { Debug.LogError($"[AllChecks] {name}: Ausnahme {e}"); ok = false; }
                Debug.Log($"[AllChecks] {(ok ? "OK  " : "FAIL")} {name}");
                if (!ok) failed.Add(name);
            }
            Debug.Log(failed.Count == 0 ? $"[AllChecks] Alle {Checks.Length} Prüfungen bestanden."
                                        : $"[AllChecks] {failed.Count} von {Checks.Length} fehlgeschlagen: {string.Join(", ", failed)}");
            return failed.Count == 0;
        }
    }
}
