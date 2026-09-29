using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Jogging.Locomotion.Treadmill;

namespace Jogging.EditorTools
{
    /// <summary>
    /// Self-check of the simulated F37: its frames decode with the real parser exactly like the F37's
    /// (idle, countdown, running, safety, ranges), the belt ramps like a real one, and a control
    /// command while stopped is counted as a violation. Then the safety layer against the emulator in
    /// a closed loop: the incline follows a target in 1 % steps, a ramping belt is not mistaken for a
    /// manual change, a real manual change backs the app off, and leveling out never starts the belt.
    /// Headless: -executeMethod Jogging.EditorTools.BeltEmulatorCheck.RunBatch
    /// </summary>
    public static class BeltEmulatorCheck
    {
        [MenuItem("Jogging/Lauf/Band-Simulator prüfen")]
        public static void RunMenu() => EditorUtility.DisplayDialog("Jogging", Run() ? "Alle Prüfungen bestanden." : "Fehler – siehe Console.", "OK");

        public static void RunBatch() => EditorApplication.Exit(Run() ? 0 : 1);

        private static BeltReading Read(FitShowEmulator e)
        {
            var f = e.StatusFrame();
            bool run = FitShowParser.TryGetRunningSpeedByte(f, out var spd, out var inc);
            return new BeltReading { Running = run, SpeedKmh = spd / 10f, InclinePercent = inc };
        }

        public static bool Run()
        {
            var fails = new List<string>();
            void Ok(bool c, string what) { if (!c) fails.Add(what); }

            // Frames like the real F37
            var e = new FitShowEmulator();
            var idle = e.StatusFrame();
            Ok(FitShowParser.IsFitShowFrame(idle) && idle.Length == 5 && FitShowParser.TryParse(idle, out _, out var st0) && st0 == FitShowParser.StatusIdle, "Ruhe-Frame wie am F37 (02 51 00 51 03)");
            Ok(FitShowParser.TryParseRange(e.Handle(FitShowParser.QueryInclineRange()), out var k, out var mx, out var mn) && k == FitShowParser.InfoIncline && mx == 15 && mn == 0, "Steigungsbereich 0…15");
            Ok(FitShowParser.TryParseRange(e.Handle(FitShowParser.QuerySpeedRange()), out _, out var smx, out var smn) && smx == 200 && smn == 10, "Tempobereich 1…20");
            e.PressStartStop();
            Ok(FitShowParser.ToBeltState(e.StatusFrame()[2]) == BeltState.Countdown, "Countdown nach Start");
            for (int i = 0; i < 40; i++) e.Tick(0.1f); // 3 s countdown + 1 s ramp
            Ok(e.State == FitShowEmulator.Status.Running, "läuft nach Countdown");
            Ok(FitShowParser.TryParse(e.StatusFrame(), out var d, out var st) && st == FitShowParser.StatusRunning && d.SpeedKmh > 0.5f && d.HasIncline, "Lauf-Frame dekodiert");
            e.ToggleSafetyClip();
            Ok(FitShowParser.ToBeltState(e.StatusFrame()[2]) == BeltState.Safety, "Sicherheitsclip");
            e.ToggleSafetyClip();

            // A command while stopped = the real F37 would start → violation
            var v = new FitShowEmulator();
            v.Handle(FitShowParser.SetSpeedAndIncline(40, 3));
            Ok(v.Violations == 1 && v.State == FitShowEmulator.Status.Countdown, "Startbefehl bei stehendem Band nicht erkannt");

            // Closed loop: safety layer + emulator, incline target 5 %
            var b = new FitShowEmulator();
            var s = new BeltSafety(new BeltLimits { SendInterval = 2f });
            s.OnReading(Read(b), 0f);
            foreach (var r in new[] { FitShowParser.QueryInclineRange() }) { FitShowParser.TryParseRange(b.Handle(r), out _, out var hi, out var lo); s.SetInclineRange(lo, hi); }
            float t = 0f; int sent = 0;
            void Step(float dt, bool running, float? inc, float? spd)
            {
                b.Tick(dt); t += dt;
                if (Mathf.Repeat(t, 1f) < dt) // the 1 s poll
                {
                    var rd = Read(b);
                    s.OnReading(rd, t);
                    if (s.TryCommand(t, running, inc, spd, out var c))
                    {
                        sent++;
                        b.Handle(FitShowParser.SetSpeedAndIncline((byte)Mathf.RoundToInt(c.SpeedKmh * 10f), (sbyte)Mathf.RoundToInt(c.InclinePercent)));
                    }
                }
            }
            for (int i = 0; i < 100; i++) Step(0.1f, true, 5f, null); // belt stopped for 10 s
            Ok(sent == 0 && b.Violations == 0, $"Befehle bei stehendem Band: {sent}, Verstöße {b.Violations}");
            b.PressStartStop();
            for (int i = 0; i < 400; i++) Step(0.1f, true, 5f, null); // 40 s running
            Ok(Mathf.Approximately(b.InclinePercent, 5f), $"Steigung folgt nicht: {b.InclinePercent:0.0} % statt 5 %");
            Ok(!s.InclineOverridden(t), "eigene Steigungsrampe als Verstellung gewertet");
            Ok(Mathf.Approximately(b.SpeedKmh, 4f), $"Tempo verändert ohne Freigabe: {b.SpeedKmh:0.0}");

            // Runner raises the incline → back off
            b.PressIncline(+3);
            for (int i = 0; i < 60; i++) Step(0.1f, true, 5f, null);
            Ok(s.InclineOverridden(t), "manuelle Verstellung nicht erkannt");
            Ok(Mathf.Approximately(b.InclinePercent, 8f), $"App hat gegen die Hand gearbeitet: {b.InclinePercent:0.0} %");

            // Speed allowed: ramps up; the belt's own ramp is not a manual change
            var sp = new FitShowEmulator(); var ss = new BeltSafety(new BeltLimits { SendInterval = 2f, AllowSpeed = true });
            ss.SetInclineRange(0f, 15f); ss.SetSpeedRange(1f, 20f);
            sp.PressStartStop();
            float t2 = 0f;
            for (int i = 0; i < 600; i++)
            {
                sp.Tick(0.1f); t2 += 0.1f;
                if (Mathf.Repeat(t2, 1f) < 0.1f)
                {
                    var rd = Read(sp); ss.OnReading(rd, t2);
                    if (ss.TryCommand(t2, true, 0f, 8f, out var c))
                        sp.Handle(FitShowParser.SetSpeedAndIncline((byte)Mathf.RoundToInt(c.SpeedKmh * 10f), (sbyte)Mathf.RoundToInt(c.InclinePercent)));
                }
            }
            Ok(Mathf.Abs(sp.SpeedKmh - 8f) < 0.05f, $"Tempo erreicht Ziel nicht: {sp.SpeedKmh:0.0} km/h");
            Ok(!ss.SpeedOverridden(t2), "Tempo-Rampe des Bands als Verstellung gewertet");
            Ok(sp.Violations == 0, "Verstoß beim Tempo");

            // Level out after the run: down to 0, belt keeps running, never a start
            var lv = new FitShowEmulator(); var ls = new BeltSafety(new BeltLimits { SendInterval = 2f });
            ls.SetInclineRange(0f, 15f);
            lv.PressStartStop();
            float t3 = 0f; float? app = null;
            for (int i = 0; i < 300; i++) // 30 s: raise to 6 %
            {
                lv.Tick(0.1f); t3 += 0.1f;
                if (Mathf.Repeat(t3, 1f) < 0.1f) { ls.OnReading(Read(lv), t3); if (ls.TryCommand(t3, true, 6f, null, out var c)) { lv.Handle(FitShowParser.SetSpeedAndIncline((byte)Mathf.RoundToInt(c.SpeedKmh * 10f), (sbyte)c.InclinePercent)); app = c.InclinePercent; } }
            }
            Ok(Mathf.Approximately(lv.InclinePercent, 6f), $"Vorbereitung: {lv.InclinePercent:0.0} %");
            for (int i = 0; i < 300; i++) // run over: level out
            {
                lv.Tick(0.1f); t3 += 0.1f;
                if (Mathf.Repeat(t3, 1f) < 0.1f) { ls.OnReading(Read(lv), t3); if (ls.TryLevelOut(t3, app, out var c)) { lv.Handle(FitShowParser.SetSpeedAndIncline((byte)Mathf.RoundToInt(c.SpeedKmh * 10f), (sbyte)c.InclinePercent)); app = c.InclinePercent; } }
            }
            Ok(Mathf.Approximately(lv.InclinePercent, 0f) && lv.State == FitShowEmulator.Status.Running, $"nicht zurück auf 0: {lv.InclinePercent:0.0} %");
            lv.PressStartStop(); // runner stops the belt
            for (int i = 0; i < 100; i++) { lv.Tick(0.1f); t3 += 0.1f; if (Mathf.Repeat(t3, 1f) < 0.1f) { ls.OnReading(Read(lv), t3); if (ls.TryLevelOut(t3, 6f, out _)) fails.Add("Zurückfahren bei stehendem Band"); } }
            Ok(lv.Violations == 0, "Verstoß beim Zurückfahren");

            if (fails.Count == 0) Debug.Log("[BeltEmuCheck] Alle Prüfungen bestanden.");
            else Debug.LogError("[BeltEmuCheck] FAIL → " + string.Join("; ", fails));
            return fails.Count == 0;
        }
    }
}
