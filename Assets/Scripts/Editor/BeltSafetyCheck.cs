using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Jogging.Locomotion.Treadmill;

namespace Jogging.EditorTools
{
    /// <summary>
    /// Self-check of the belt safety layer and the control modes without hardware: never while the
    /// belt stands or the run is paused, range/step/interval limits, settle after speed changes,
    /// manual override (and our own echo not counting as one), speed only when allowed and ramped.
    /// Menu: Jogging → Lauf → Bandsicherheit prüfen. Headless: -executeMethod Jogging.EditorTools.BeltSafetyCheck.RunBatch
    /// </summary>
    public static class BeltSafetyCheck
    {
        [MenuItem("Jogging/Lauf/Bandsicherheit prüfen")]
        public static void RunMenu() => EditorUtility.DisplayDialog("Jogging", Run() ? "Alle Prüfungen bestanden." : "Fehler – siehe Console.", "OK");

        public static void RunBatch() => EditorApplication.Exit(Run() ? 0 : 1);

        private static BeltReading R(float kmh, float inc, bool running = true) => new BeltReading { Running = running, SpeedKmh = kmh, InclinePercent = inc };

        public static bool Run()
        {
            var fails = new List<string>();
            void Ok(bool c, string what) { if (!c) fails.Add(what); }

            // --- belt stands / run paused / range unknown → nothing
            var s = new BeltSafety();
            s.OnReading(R(0f, 0f, false), 0f);
            Ok(!s.TryCommand(10f, true, 5f, null, out _), "sendet bei stehendem Band");
            s.OnReading(R(8f, 0f), 0f);
            Ok(!s.TryCommand(10f, true, 5f, null, out _), "sendet ohne bekannten Steigungsbereich");
            s.SetInclineRange(0f, 10f);
            Ok(!s.TryCommand(10f, false, 5f, null, out _), "sendet bei pausiertem Lauf");

            // --- settle after (re)start, then one step per interval, speed echoed
            var a = new BeltSafety(); a.SetInclineRange(0f, 10f);
            a.OnReading(R(8f, 0f), 0f);
            Ok(!a.TryCommand(1f, true, 7f, null, out _), "sendet direkt nach dem Anlaufen");
            Ok(a.TryCommand(3.5f, true, 7f, null, out var c1) && Mathf.Approximately(c1.InclinePercent, 1f) && !c1.ChangesSpeed && Mathf.Approximately(c1.SpeedKmh, 8f),
               $"erster Schritt: {c1.InclinePercent} % / {c1.SpeedKmh} km/h");
            a.OnReading(R(8f, 1f), 4f); // our own value comes back → no override
            Ok(!a.InclineOverridden(4f), "eigener Befehl als manuelle Verstellung gewertet");
            Ok(!a.TryCommand(4.5f, true, 7f, null, out _), "Sendeabstand nicht eingehalten");
            Ok(a.TryCommand(6.6f, true, 7f, null, out var c2) && Mathf.Approximately(c2.InclinePercent, 2f), $"zweiter Schritt: {c2.InclinePercent} %");

            // --- clamps: belt range, app max, no decline, resolution
            var b = new BeltSafety(); b.SetInclineRange(0f, 10f);
            b.OnReading(R(8f, 10f), 0f);
            Ok(!b.TryCommand(5f, true, 20f, null, out _), "über den Bandbereich hinaus");
            var n = new BeltSafety(); n.SetInclineRange(-3f, 15f);
            n.OnReading(R(8f, 12f), 0f);
            Ok(!n.TryCommand(5f, true, 14f, null, out _), "über 12 % hinaus");
            n.OnReading(R(8f, 0f), 5f); // (manual change → override; new belt for the decline test)
            var d = new BeltSafety(); d.SetInclineRange(-3f, 15f);
            d.OnReading(R(8f, 0f), 0f);
            Ok(!d.TryCommand(5f, true, -3f, null, out _), "Gefälle ans Band gegeben");
            // --- per-treadmill caps: decline only when allowed, a lower max, the way back up from a decline
            var dc = new BeltSafety(); dc.SetInclineRange(-5f, 40f); dc.Limits.MinIncline = -3f; // "Gefälle bis −3 %"
            dc.OnReading(R(8f, 0f), 0f);
            Ok(dc.TryCommand(5f, true, -5f, null, out var dcc) && Mathf.Approximately(dcc.InclinePercent, -1f), $"Gefälle erlaubt: {dcc.InclinePercent} %");
            var dl = new BeltSafety(); dl.SetInclineRange(-5f, 40f); dl.Limits.MinIncline = -3f;
            dl.OnReading(R(8f, -3f), 0f);
            Ok(!dl.TryCommand(10f, true, -5f, null, out _), "unter die eigene Gefälle-Grenze");
            Ok(dl.TryLevelOut(10f, -3f, out var up) && Mathf.Approximately(up.InclinePercent, -2f), $"nach dem Lauf aus dem Gefälle zurück: {up.InclinePercent} %");
            var own = new BeltSafety(); own.SetInclineRange(0f, 40f); own.Limits.MaxIncline = 8f;
            own.OnReading(R(8f, 8f), 0f);
            Ok(!own.TryCommand(5f, true, 20f, null, out _), "über die eigene Steigungsgrenze (8 %)");
            var hi = new BeltSafety(); hi.SetInclineRange(0f, 40f); hi.Limits.MaxIncline = 25f;
            hi.OnReading(R(8f, 14f), 0f);
            Ok(hi.TryCommand(5f, true, 20f, null, out var hic) && Mathf.Approximately(hic.InclinePercent, 15f), $"Steigungstrainer über 12 %: {hic.InclinePercent} %");

            // 2-% belt (F37): even values only, 2 % per step, level-out 4 → 2 → 0
            var two = new BeltSafety(); two.SetInclineRange(0f, 15f); two.Limits.InclineResolution = 2f; two.Limits.MaxInclineStep = 2f;
            two.OnReading(R(6f, 0f), 0f);
            Ok(two.TryCommand(5f, true, 3f, null, out var tw) && Mathf.Approximately(tw.InclinePercent, 2f), $"2-%-Band: erster Schritt {tw.InclinePercent} %");
            var two2 = new BeltSafety(); two2.SetInclineRange(0f, 15f); two2.Limits.InclineResolution = 2f; two2.Limits.MaxInclineStep = 2f;
            two2.OnReading(R(6f, 4f), 0f);
            Ok(two2.TryLevelOut(10f, 4f, out var tl) && Mathf.Approximately(tl.InclinePercent, 2f), $"2-%-Band: zurück {tl.InclinePercent} %");

            var q = new BeltSafety(); q.SetInclineRange(0f, 10f);
            q.OnReading(R(8f, 3f), 0f);
            Ok(!q.TryCommand(5f, true, 3.4f, null, out _), "sendet 3 % immer wieder (Rundung)");
            var none = new BeltSafety(); none.SetInclineRange(0f, 0f);
            none.OnReading(R(8f, 0f), 0f);
            Ok(!none.TryCommand(5f, true, 5f, null, out _), "Steigung an ein Band ohne Steigung");

            // --- manual incline change → back off 30 s
            var m = new BeltSafety(); m.SetInclineRange(0f, 10f);
            m.OnReading(R(8f, 0f), 0f);
            m.OnReading(R(8f, 5f), 10f);
            Ok(m.InclineOverridden(10f), "manuelle Steigung nicht erkannt");
            Ok(!m.TryCommand(20f, true, 1f, null, out _) && m.State.Contains("manuell"), "sendet trotz manueller Verstellung");
            Ok(m.TryCommand(41f, true, 1f, null, out var cm) && Mathf.Approximately(cm.InclinePercent, 4f), "nach 30 s nicht weiter");

            // --- manual speed change → settle, speed never changed without permission
            var v = new BeltSafety(); v.SetInclineRange(0f, 10f);
            v.OnReading(R(8f, 0f), 0f);
            v.OnReading(R(9f, 0f), 10f);
            Ok(!v.TryCommand(11f, true, 3f, 12f, out _), "sendet direkt nach Tempoänderung");
            Ok(v.TryCommand(13.5f, true, 3f, 12f, out var cv) && Mathf.Approximately(cv.SpeedKmh, 9f) && !cv.ChangesSpeed, "Tempo ohne Freigabe geändert");

            // --- speed allowed: ramped, capped, runner's change overrides
            var sp = new BeltSafety(new BeltLimits { AllowSpeed = true }); sp.SetInclineRange(0f, 10f); sp.SetSpeedRange(1f, 20f);
            sp.OnReading(R(8f, 0f), 0f);
            Ok(sp.TryCommand(3.5f, true, null, 20f, out var cs) && cs.ChangesSpeed && Mathf.Approximately(cs.SpeedKmh, 8.5f), $"Temporampe: {cs.SpeedKmh}");
            var cap = new BeltSafety(new BeltLimits { AllowSpeed = true }); cap.SetInclineRange(0f, 10f);
            cap.OnReading(R(16f, 0f), 0f);
            Ok(!cap.TryCommand(5f, true, null, 20f, out _), "über 16 km/h hinaus");
            sp.OnReading(R(8.5f, 0f), 4f);   // our value
            Ok(!sp.SpeedOverridden(4f), "eigenes Tempo als Verstellung gewertet");
            sp.OnReading(R(7f, 0f), 12f);    // runner slows down (after the belt's own start-up)
            Ok(sp.SpeedOverridden(12f), "manuelles Tempo nicht erkannt");
            Ok(!sp.TryCommand(16f, true, null, 20f, out _), "Tempo trotz manueller Verstellung");
            // …but the belt accelerating right after its start is not the runner
            var su = new BeltSafety(new BeltLimits { AllowSpeed = true }); su.SetInclineRange(0f, 10f);
            su.OnReading(R(1f, 0f), 0f); su.OnReading(R(2.5f, 0f), 1f); su.OnReading(R(4f, 0f), 2f);
            Ok(!su.SpeedOverridden(2f), "Anlaufen des Bands als Verstellung gewertet");

            // --- belt stops mid-run → nothing, restart → settle again
            var st = new BeltSafety(); st.SetInclineRange(0f, 10f);
            st.OnReading(R(8f, 0f), 0f);
            st.OnReading(R(0f, 0f, false), 5f);
            Ok(!st.TryCommand(6f, true, 5f, null, out _), "sendet nach Bandstopp");
            st.OnReading(R(3f, 0f), 20f);
            Ok(!st.TryCommand(21f, true, 5f, null, out _), "sendet sofort nach Wiederanlauf");
            Ok(st.TryCommand(23.5f, true, 5f, null, out _), "nach Wiederanlauf nicht weiter");

            // --- level out after the run: only down, only what the app set, stepwise, manual wins
            var lv = new BeltSafety(); lv.SetInclineRange(0f, 10f);
            lv.OnReading(R(8f, 5f), 0f);
            Ok(!lv.TryLevelOut(5f, null, out _), "senkt eine Steigung, die der Läufer gesetzt hat");
            Ok(!lv.TryLevelOut(5f, 3f, out _), "senkt, obwohl das Band nicht mehr auf dem App-Wert steht");
            Ok(lv.TryLevelOut(5f, 5f, out var l1) && Mathf.Approximately(l1.InclinePercent, 4f) && !l1.ChangesSpeed && Mathf.Approximately(l1.SpeedKmh, 8f), $"erster Schritt zurück: {l1.InclinePercent} %");
            lv.OnReading(R(8f, 4f), 6f); // our own step comes back
            Ok(!lv.InclineOverridden(6f), "eigener Schritt als Verstellung gewertet");
            Ok(!lv.TryLevelOut(6.5f, 4f, out _), "Sendeabstand beim Zurückfahren");
            Ok(lv.TryLevelOut(8.5f, 4f, out var l2) && Mathf.Approximately(l2.InclinePercent, 3f), "zweiter Schritt zurück");
            lv.OnReading(R(8f, 6f), 9f);  // runner raises it by hand
            Ok(!lv.TryLevelOut(15f, 3f, out _), "senkt nach manueller Verstellung");
            var flat = new BeltSafety(); flat.SetInclineRange(0f, 10f);
            flat.OnReading(R(8f, 0f), 0f);
            Ok(!flat.TryLevelOut(10f, 0f, out _), "sendet bei 0 %");
            var stop = new BeltSafety(); stop.SetInclineRange(0f, 10f);
            stop.OnReading(R(0f, 5f, false), 0f);
            Ok(!stop.TryLevelOut(10f, 5f, out _), "sendet bei stehendem Band");
            var neg = new BeltSafety(); neg.SetInclineRange(-3f, 10f);
            neg.OnReading(R(8f, -2f), 0f);
            Ok(!neg.TryLevelOut(10f, -2f, out _), "hebt ein Gefälle an");

            // --- when is a run over (level out) vs. paused (keep)
            Ok(BeltControl.RunIsOver(null), "ohne Session nicht vorbei");
            var run = new Jogging.Core.SessionStateMachine();
            Ok(BeltControl.RunIsOver(run), "vor dem Start nicht vorbei");
            run.Tick(0.1f, false, true, false, true, Jogging.Core.BeltSignal.Running);
            Ok(!BeltControl.RunIsOver(run), "laufender Lauf gilt als vorbei");
            run.PauseByUser(); run.Tick(0.1f, false, true, false, true, Jogging.Core.BeltSignal.Running);
            Ok(!BeltControl.RunIsOver(run), "Pause gilt als vorbei");
            run.Finish();
            Ok(BeltControl.RunIsOver(run), "beendeter Lauf nicht vorbei");

            // --- control modes
            BeltControl.Mode = ControlMode.Treadmill;
            Ok(!BeltControl.Targets(5f, out _, out _), "Modus Band erzeugt Ziele");
            BeltControl.Mode = ControlMode.Route;
            Ok(BeltControl.Targets(4f, out var ri, out var rs) && ri == 4f && rs == null, "Modus Strecke: Steigung");
            Ok(BeltControl.Targets(-3f, out var rn, out _) && rn == 0f, "Modus Strecke: Gefälle");
            BeltControl.Mode = ControlMode.Workout; BeltControl.Workout = null;
            Ok(!BeltControl.Targets(4f, out _, out _), "Workout ohne Workout");
            BeltControl.Workout = (out float? i, out float? v) => { i = 3f; v = 11f; return true; };
            Ok(BeltControl.Targets(0f, out var wi, out var wv) && wi == 3f && wv == 11f, "Workout-Ziele");
            // Speed only reaches the belt if allowed: the safety layer drops it otherwise.
            var ws = new BeltSafety(new BeltLimits { AllowSpeed = false }); ws.SetInclineRange(0f, 10f);
            ws.OnReading(R(8f, 3f), 0f);
            Ok(!ws.TryCommand(5f, true, 3f, 11f, out _), "Tempo ohne Freigabe gesendet");
            BeltControl.Workout = null;
            BeltControl.Mode = ControlMode.Treadmill;

            if (fails.Count == 0) Debug.Log("[BeltCheck] Alle Prüfungen bestanden.");
            else Debug.LogError("[BeltCheck] FAIL → " + string.Join("; ", fails));
            return fails.Count == 0;
        }
    }
}
