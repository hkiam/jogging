using System.Text;
using UnityEditor;
using UnityEngine;
using Jogging.Profile;

namespace Jogging.EditorTools
{
    /// <summary>
    /// Editor helpers for the runners: print everyone's lifetime stats, open the data folder
    /// (profiles/, sessions/, settings.json). Reads through the same stores the game uses.
    /// </summary>
    public static class ProfileEditorTools
    {
        [MenuItem("Jogging/Läufer/Statistik zeigen")]
        public static void ShowStats()
        {
            var runners = new LocalProfileStore().LoadAll();
            var sessions = new SessionStore();
            var sb = new StringBuilder();
            if (runners.Count == 0) sb.Append("Noch kein Läufer angelegt.");
            foreach (var p in runners)
                sb.Append($"{p.playerName}: {p.totalRuns} Läufe, {p.totalDistanceMeters / 1000f:0.00} km, " +
                          $"{p.totalTimeSeconds / 60f:0} min, ↑{p.totalElevationMeters:0} m, " +
                          $"Erfolge {p.unlockedAchievements.Count}/{AchievementCatalog.All.Length}, " +
                          $"Logbuch {sessions.Summaries(p.id).Count}\n");
            Debug.Log("[Jogging] " + sb.ToString().Replace("\n", " | "));
            EditorUtility.DisplayDialog("Jogging – Läufer", sb.ToString(), "OK");
        }

        [MenuItem("Jogging/Läufer/Datenordner öffnen")]
        public static void OpenFolder() => EditorUtility.RevealInFinder(Application.persistentDataPath + "/");
    }
}
