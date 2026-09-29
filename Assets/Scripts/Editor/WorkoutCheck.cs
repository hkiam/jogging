using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Jogging.Training;

namespace Jogging.EditorTools
{
    /// <summary>
    /// Self-check of the workout engine: built-in durations, segments by time and by distance with
    /// carry-over, current/next/remaining, completion exactly once, JSON round trip and validation.
    /// Menu: Jogging → Lauf → Workouts prüfen. Headless: -executeMethod Jogging.EditorTools.WorkoutCheck.RunBatch
    /// </summary>
    public static class WorkoutCheck
    {
        [MenuItem("Jogging/Lauf/Workouts prüfen")]
        public static void RunMenu() => EditorUtility.DisplayDialog("Jogging", Run() ? "Alle Prüfungen bestanden." : "Fehler – siehe Console.", "OK");

        public static void RunBatch() => EditorApplication.Exit(Run() ? 0 : 1);

        public static bool Run()
        {
            var fails = new List<string>();
            void Ok(bool c, string what) { if (!c) fails.Add(what); }

            var all = WorkoutPresets.All();
            WorkoutDoc Find(string id) => all.Find(w => w.id == id);
            float Minutes(string id) { float s = 0f; foreach (var g in Find(id).segments) s += g.durationS; return s / 60f; }
            Ok(Mathf.Approximately(Minutes("builtin:intervalle-30"), 30f), $"Intervalle {Minutes("builtin:intervalle-30")} min");
            Ok(Mathf.Approximately(Minutes("builtin:pyramide-23"), 23f), $"Pyramide {Minutes("builtin:pyramide-23")} min");
            Ok(Mathf.Approximately(Minutes("builtin:huegel-25"), 25f), $"Hügel {Minutes("builtin:huegel-25")} min");
            Ok(Mathf.Approximately(Minutes("builtin:tempo-25"), 25f), $"Tempo {Minutes("builtin:tempo-25")} min");
            float km = 0f; foreach (var g in Find("builtin:locker-5k").segments) km += g.distanceM;
            Ok(Mathf.Approximately(km, 5000f), $"5 km locker: {km} m");
            foreach (var w in all) foreach (var g in w.segments)
                Ok(!g.setIncline || (g.inclinePercent >= 0f && g.inclinePercent <= 12f), $"{w.name}: Steigung {g.inclinePercent} % außerhalb 0–12");

            // By time, with carry-over across segment ends.
            var iv = new WorkoutRunner(Find("builtin:intervalle-30"));
            var started = new List<string>(); int done = 0;
            iv.SegmentStarted += s => started.Add(s.kind);
            iv.Completed += () => done++;
            iv.Start();
            Ok(started.Count == 1 && started[0] == "warmup", "erster Abschnitt nicht gemeldet");
            iv.Advance(299f, 700f);
            Ok(iv.Index == 0 && Mathf.Approximately(iv.Remaining, 1f), $"Aufwärmen: Index {iv.Index}, Rest {iv.Remaining}");
            Ok(iv.Next != null && iv.Next.kind == "fast", "Nächstes nicht schnell");
            iv.Advance(3f, 5f);
            Ok(iv.Index == 1 && Mathf.Approximately(iv.SegmentSeconds, 2f), $"Übertrag: Index {iv.Index}, {iv.SegmentSeconds} s");
            iv.Advance(60f * 25f, 0f); // far beyond the end in one step
            Ok(iv.Done && done == 1 && iv.Current == null, $"Ende: Done {iv.Done}, {done}× gemeldet");
            Ok(started.Count == 10, $"{started.Count} Abschnitte gemeldet statt 10");
            iv.Advance(10f, 10f);
            Ok(done == 1, "Ende doppelt gemeldet");

            // By distance.
            var ez = new WorkoutRunner(Find("builtin:locker-5k"));
            ez.Start();
            ez.Advance(100f, 300f);
            Ok(ez.Index == 0 && Mathf.Approximately(ez.Remaining, 200f) && Mathf.Approximately(ez.SegmentProgress, 0.6f), $"Distanz: Rest {ez.Remaining} m");
            ez.Advance(60f, 250f);
            Ok(ez.Index == 1 && Mathf.Approximately(ez.SegmentMeters, 50f), $"Distanz-Übertrag {ez.SegmentMeters} m");
            ez.Advance(0f, 4500f);
            Ok(ez.Done, "5 km nicht beendet");

            // Copy for editing: independent of the original; built-ins are recognised.
            var orig = Find("builtin:intervalle-30");
            var copy = orig.Clone();
            copy.segments[0].durationS = 1f; copy.name = "x";
            Ok(orig.segments[0].durationS == 300f && orig.name != "x", "Kopie ändert das Original");
            Ok(orig.BuiltIn && !new WorkoutDoc { id = "wk_1" }.BuiltIn, "eingebaut/eigen");

            // Spoken texts
            Ok(Jogging.Core.Units.RegionOf("de_DE") == "DE" && Jogging.Core.Units.RegionOf("en_US@rg=dezzzz") == "DE" && Jogging.Core.Units.RegionOf("de-AT") == "AT"
               && Jogging.Core.Units.RegionOf("en") == "" && Jogging.Core.Units.Resolve("") == (Jogging.Core.Units.SystemRegion == "US" ? "imperial" : Jogging.Core.Units.Resolve("")),
               "Region aus der Locale-Kennung (de_DE, en_US@rg=dezzzz, de-AT)");
            Ok(Speech.Km(5000f) == "5 Kilometer" && Speech.Km(5200f) == "5,2 Kilometer", $"Ansage km: {Speech.Km(5200f)}");
            Ok(Speech.Kmh(11f) == "11 Kilometer pro Stunde" && Speech.Kmh(7.5f) == "7 Komma 5 Kilometer pro Stunde", $"Ansage km/h: {Speech.Kmh(7.5f)}");
            Ok(Speech.Duration(180f) == "3 Minuten" && Speech.Duration(342f) == "5 Minuten 42" && Speech.Duration(45f) == "45 Sekunden" && Speech.Duration(60f) == "1 Minute", $"Ansage Zeit: {Speech.Duration(342f)}");
            Ok(Speech.Pace(5000f, 1710f) == "5 Minuten 42 pro Kilometer", $"Ansage Pace: {Speech.Pace(5000f, 1710f)}");
            Ok(Speech.Segment(Find("builtin:intervalle-30").segments[1]) == "Schnell, 11 Kilometer pro Stunde, 3 Minuten.", $"Ansage Abschnitt: {Speech.Segment(Find("builtin:intervalle-30").segments[1])}");

            // JSON round trip + validation.
            var src = Find("builtin:huegel-25");
            var back = WorkoutStore.FromJson(WorkoutStore.ToJson(src));
            Ok(back != null && back.segments.Count == src.segments.Count && back.segments[1].setIncline && Mathf.Approximately(back.segments[1].inclinePercent, 3f), "JSON-Round-Trip");
            Ok(WorkoutStore.FromJson("{ \"name\": \"leer\", \"segments\": [ { \"kind\": \"run\" } ] }") == null, "Workout ohne Dauer akzeptiert");
            Ok(WorkoutStore.FromJson("kein json") == null, "kaputtes JSON akzeptiert");

            if (fails.Count == 0) Debug.Log("[WorkoutCheck] Alle Prüfungen bestanden.");
            else Debug.LogError("[WorkoutCheck] FAIL → " + string.Join("; ", fails));
            return fails.Count == 0;
        }
    }
}
