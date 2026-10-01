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

            // Incline by heart rate: patient, one step at a time, waits for the pulse, stays within limits
            var pi = new Jogging.Training.PulseIncline();
            var C = Jogging.Training.PulseIncline.Change.None;
            Ok(pi.Step(60f, 4, 2, 5f, 0f, 12f) == C, "Pulssteigung: nicht im Aufwärmen");
            Ok(pi.Step(100f, 4, 2, 5f, 0f, 12f) == C && pi.Step(129f, 4, 2, 5f, 0f, 12f) == C, "Pulssteigung: wartet 30 s ab");
            Ok(pi.Step(131f, 4, 2, 5f, 0f, 12f) == Jogging.Training.PulseIncline.Change.Down && Mathf.Approximately(pi.Offset, -1f), "Pulssteigung: 1 % runter bei Zone 4 (Ziel 2)");
            Ok(pi.Step(170f, 4, 2, 4f, 0f, 12f) == C, "Pulssteigung: wartet, bis der Puls antwortet (60 s)");
            Ok(pi.Step(192f, 4, 2, 4f, 0f, 12f) == Jogging.Training.PulseIncline.Change.Down && Mathf.Approximately(pi.Offset, -2f), "Pulssteigung: dann der nächste Schritt");
            Ok(pi.Step(260f, 2, 2, 3f, 0f, 12f) == C && Mathf.Approximately(pi.Offset, -2f), "Pulssteigung: in der Zielzone bleibt es so");
            Ok(pi.Step(300f, 0, 2, 3f, 0f, 12f) == C && pi.Step(500f, 0, 2, 3f, 0f, 12f) == C, "Pulssteigung: ohne Puls nichts");
            var pf = new Jogging.Training.PulseIncline();
            pf.Step(100f, 4, 2, 0f, 0f, 12f);
            Ok(pf.Step(131f, 4, 2, 0f, 0f, 12f) == Jogging.Training.PulseIncline.Change.AtLimit && pf.Offset == 0f, "Pulssteigung: flach geht es nicht weiter runter");
            var pu = new Jogging.Training.PulseIncline();
            float t = 100f; pu.Step(t, 1, 3, 2f, 0f, 12f);
            for (int i = 0; i < 10; i++) { t += 61f; pu.Step(t, 1, 3, 2f + pu.Offset, 0f, 12f); }
            Ok(Mathf.Approximately(pu.Offset, Jogging.Training.PulseIncline.MaxOffset), $"Pulssteigung: höchstens +{Jogging.Training.PulseIncline.MaxOffset} % ({pu.Offset})");
            var p2 = new Jogging.Training.PulseIncline();
            p2.Step(100f, 4, 2, 6f, 0f, 12f);
            Ok(p2.Step(131f, 4, 2, 6f, 0f, 12f, 2f) == Jogging.Training.PulseIncline.Change.Down && Mathf.Approximately(p2.Offset, -2f), "Pulssteigung: Bandschritt 2 % (F37)");

            // the belt target: route grade + offset, within the belt's range
            var mode0 = Jogging.Locomotion.Treadmill.BeltControl.Mode;
            Jogging.Locomotion.Treadmill.BeltControl.Mode = Jogging.Locomotion.Treadmill.ControlMode.Route;
            Jogging.Locomotion.Treadmill.BeltControl.PulseOffset = -2f;
            Jogging.Locomotion.Treadmill.BeltControl.Targets(5f, out float? inc1, out _);
            Jogging.Locomotion.Treadmill.BeltControl.Targets(1f, out float? inc2, out _);
            Jogging.Locomotion.Treadmill.BeltControl.PulseOffset = 0f;
            Jogging.Locomotion.Treadmill.BeltControl.Targets(5f, out float? inc3, out _);
            Jogging.Locomotion.Treadmill.BeltControl.Mode = mode0;
            Ok(inc1 == 3f && inc2 == 0f && inc3 == 5f, $"Pulssteigung am Band: {inc1} / {inc2} / {inc3} statt 3 / 0 / 5");

            if (fails.Count == 0) Debug.Log("[HeartRateCheck] Alle Prüfungen bestanden.");
            else Debug.LogError("[HeartRateCheck] FAIL → " + string.Join("; ", fails));
            return fails.Count == 0;
        }
    }
}
