using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Jogging.Training;

namespace Jogging.EditorTools
{
    /// <summary>
    /// Self-check of heart rate: Heart Rate Measurement decoding (8/16 bit, sensor contact, RR
    /// intervals), zones and their ranges, the run's statistics, the heart rate workout.
    /// Menu: Jogging → Lauf → Puls prüfen. Headless: -executeMethod Jogging.EditorTools.HeartRateCheck.RunBatch
    /// </summary>
    public static class HeartRateCheck
    {
        [MenuItem("Jogging/Lauf/Puls prüfen")]
        public static void RunMenu() => EditorUtility.DisplayDialog("Jogging", Run() ? "Alle Prüfungen bestanden." : "Fehler – siehe Console.", "OK");

        public static void RunBatch() => EditorApplication.Exit(Run() ? 0 : 1);

        public static bool Run()
        {
            var fails = new List<string>();
            void Ok(bool c, string what) { if (!c) fails.Add(what); }

            // Parser
            Ok(HeartRateParser.TryParse(new byte[] { 0x00, 72 }, out int a) && a == 72, "8 Bit");
            Ok(HeartRateParser.TryParse(new byte[] { 0x01, 0x2C, 0x00 }, out int b) && b == 44, "16 Bit");
            Ok(HeartRateParser.TryParse(new byte[] { 0x16, 150, 0x10, 0x03 }, out int c) && c == 150, "mit Kontakt + RR");
            Ok(!HeartRateParser.TryParse(new byte[] { 0x04, 150 }, out _), "ohne Hautkontakt akzeptiert");
            Ok(!HeartRateParser.TryParse(new byte[] { 0x00, 0 }, out _), "Puls 0 akzeptiert");
            Ok(!HeartRateParser.TryParse(new byte[] { 0x01, 0x2C }, out _), "16 Bit zu kurz akzeptiert");
            Ok(!HeartRateParser.TryParse(null, out _), "null akzeptiert");

            // Zones (max 200): <120 · 120–140 · 140–160 · 160–180 · ≥180
            Ok(HeartRateZones.Zone(119, 200) == 1 && HeartRateZones.Zone(120, 200) == 2 && HeartRateZones.Zone(159, 200) == 3
               && HeartRateZones.Zone(160, 200) == 4 && HeartRateZones.Zone(180, 200) == 5 && HeartRateZones.Zone(0, 200) == 0, "Zonengrenzen");
            Ok(HeartRateZones.Range(2, 200) == (120, 140), $"Zone 2 = {HeartRateZones.Range(2, 200)}");
            Ok(HeartRateZones.Max(0) == HeartRateZones.DefaultMax && HeartRateZones.Max(500) == HeartRateZones.DefaultMax && HeartRateZones.Max(185) == 185, "Max. Puls Standard");

            // Stats
            var st = new HeartRateStats();
            st.Add(130, 60f, 200); st.Add(170, 60f, 200); st.Add(0, 60f, 200);
            Ok(st.AvgBpm == 150 && st.MaxBpm == 170, $"Ø {st.AvgBpm} / max {st.MaxBpm}");
            Ok(Mathf.Approximately(st.ZoneSeconds[1], 60f) && Mathf.Approximately(st.ZoneSeconds[3], 60f), "Zeit pro Zone");
            Ok(!new HeartRateStats().HasData, "leere Statistik hat Daten");

            // Heart rate workout
            var z2 = WorkoutPresets.All().Find(w => w.id == "builtin:zone2-30");
            Ok(z2 != null && Mathf.Approximately(z2.Minutes, 30f) && z2.segments[1].hrZone == 2, "Zone-2-Workout");
            var back = WorkoutStore.FromJson(WorkoutStore.ToJson(z2));
            Ok(back != null && back.segments[1].hrZone == 2, "Zielzone im JSON");

            // Pulse coach: patient, not repeating, quiet in the first minute and without pulse
            var pc = new Jogging.Training.PulseCoach();
            var H = Jogging.Training.PulseCoach.Hint.None;
            Ok(pc.Step(30f, 4, 2) == H, "Pulscoach: in der ersten Minute still");
            Ok(pc.Step(61f, 4, 2) == H && pc.Step(75f, 4, 2) == H, "Pulscoach: wartet 20 s ab");
            Ok(pc.Step(82f, 4, 2) == Jogging.Training.PulseCoach.Hint.Slower, "Pulscoach: „ruhiger“ nach 20 s in Zone 4 (Ziel 2)");
            Ok(pc.Step(100f, 4, 2) == H, "Pulscoach: wiederholt nicht gleich");
            Ok(pc.Step(143f, 4, 2) == Jogging.Training.PulseCoach.Hint.Slower, "Pulscoach: nach 60 s wieder");
            Ok(pc.Step(150f, 2, 2) == Jogging.Training.PulseCoach.Hint.Good && pc.Step(151f, 2, 2) == H, "Pulscoach: „gut so“ einmal");
            Ok(pc.Step(160f, 0, 2) == H && pc.Step(260f, 0, 2) == H, "Pulscoach: ohne Puls still");
            Ok(pc.Step(300f, 1, 0) == H, "Pulscoach: ohne Ziel still");
            var c2 = new Jogging.Training.PulseCoach();
            c2.Step(100f, 1, 3);
            Ok(c2.Step(121f, 1, 3) == Jogging.Training.PulseCoach.Hint.Faster, "Pulscoach: „schneller“ unter der Zielzone");

            if (fails.Count == 0) Debug.Log("[HeartRateCheck] Alle Prüfungen bestanden.");
            else Debug.LogError("[HeartRateCheck] FAIL → " + string.Join("; ", fails));
            return fails.Count == 0;
        }
    }
}
