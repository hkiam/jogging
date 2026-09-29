using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Jogging.Core;
using SessionState = Jogging.Core.SessionState;

namespace Jogging.EditorTools
{
    /// <summary>
    /// Self-check of the run's state machine without hardware: keyboard countdown, pause/resume,
    /// belt start/stop/safety, connection loss (no fallback to the keyboard), auto-finish after a
    /// long belt stop, finish and abort.
    /// Menu: Jogging → Lauf → Session prüfen. Headless: -executeMethod Jogging.EditorTools.SessionCheck.RunBatch
    /// </summary>
    public static class SessionCheck
    {
        [MenuItem("Jogging/Lauf/Session prüfen")]
        public static void RunMenu() => EditorUtility.DisplayDialog("Jogging", Run() ? "Alle Prüfungen bestanden." : "Fehler – siehe Console.", "OK");

        public static void RunBatch() => EditorApplication.Exit(Run() ? 0 : 1);

        public static bool Run()
        {
            var fails = new List<string>();
            void Is(SessionStateMachine m, SessionState s, string what) { if (m.State != s) fails.Add($"{what}: {m.State} statt {s}"); }
            const float dt = 0.1f;

            // Keyboard: menu → loading → 3-2-1 → running → pause → resume → finish line.
            var k = new SessionStateMachine();
            k.Tick(dt, true, true, false, false, BeltSignal.None); Is(k, SessionState.Preparing, "Tastatur, Menü offen");
            k.Tick(dt, false, false, false, false, BeltSignal.None); Is(k, SessionState.Preparing, "Tastatur, Gelände lädt");
            k.PauseByUser(); Is(k, SessionState.Preparing, "Pause vor dem Start ignoriert");
            k.Tick(dt, false, true, false, false, BeltSignal.None); Is(k, SessionState.Countdown, "Tastatur, Countdown");
            for (int i = 0; i < 29; i++) k.Tick(dt, false, true, false, false, BeltSignal.None);
            Is(k, SessionState.Countdown, "Countdown noch nicht vorbei");
            k.Tick(dt, false, true, false, false, BeltSignal.None); k.Tick(dt, false, true, false, false, BeltSignal.None);
            Is(k, SessionState.Running, "Tastatur läuft nach 3 s");
            if (!k.Moves) fails.Add("Running bewegt nicht");
            k.PauseByUser(); Is(k, SessionState.Paused, "Tastatur Pause");
            k.Tick(dt, false, true, false, false, BeltSignal.None); Is(k, SessionState.Paused, "Pause bleibt");
            if (k.Moves) fails.Add("Pause bewegt");
            k.Resume(); k.Tick(dt, false, true, false, false, BeltSignal.None); Is(k, SessionState.Running, "Tastatur weiter");
            k.Tick(dt, false, true, true, false, BeltSignal.None); Is(k, SessionState.Finished, "Ziellinie");

            // Belt: waits for the belt, its countdown, running, stop → pause → running again.
            var b = new SessionStateMachine();
            b.Tick(dt, false, true, false, true, BeltSignal.Stopped); Is(b, SessionState.Preparing, "Band steht vor dem Start");
            if (!b.BeltRun) fails.Add("Bandlauf nicht erkannt");
            b.Tick(dt, false, true, false, true, BeltSignal.Countdown); Is(b, SessionState.Countdown, "Band-Countdown");
            b.Tick(dt, false, true, false, true, BeltSignal.Running); Is(b, SessionState.Running, "Band läuft");
            b.Tick(dt, false, true, false, true, BeltSignal.Stopped); Is(b, SessionState.Paused, "Band gestoppt");
            if (b.Reason != PauseReason.BeltStopped) fails.Add($"Grund {b.Reason} statt BeltStopped");
            b.Tick(dt, false, true, false, true, BeltSignal.Running); Is(b, SessionState.Running, "Band läuft wieder");
            b.Tick(dt, false, true, false, true, BeltSignal.Safety);
            if (b.Reason != PauseReason.BeltSafety) fails.Add("Sicherheitsclip nicht erkannt");
            b.Tick(dt, false, true, false, true, BeltSignal.Running);

            // Connection lost: pause (no keyboard fallback), resume when data returns.
            b.Tick(dt, false, true, false, false, BeltSignal.None); Is(b, SessionState.Paused, "Verbindung weg");
            if (b.Reason != PauseReason.ConnectionLost) fails.Add($"Grund {b.Reason} statt ConnectionLost");
            b.Tick(dt, false, true, false, false, BeltSignal.None); Is(b, SessionState.Paused, "bleibt ohne Verbindung pausiert");
            b.Tick(dt, false, true, false, true, BeltSignal.Running); Is(b, SessionState.Running, "Verbindung wieder da");

            // User pause on a belt run: stays paused while the belt runs, resumes on Resume().
            b.PauseByUser(); b.Tick(dt, false, true, false, true, BeltSignal.Running); Is(b, SessionState.Paused, "Band: Pause per Knopf");
            b.Resume(); b.Tick(dt, false, true, false, true, BeltSignal.Running); Is(b, SessionState.Running, "Band: weiter per Knopf");

            // Auto-finish after a long belt stop.
            int finished = 0; b.FinishRequested += () => finished++;
            b.Tick(dt, false, true, false, true, BeltSignal.Stopped);
            for (int i = 0; i < (int)(SessionStateMachine.AutoFinishAfter / 1f) + 2; i++) b.Tick(1f, false, true, false, true, BeltSignal.Stopped);
            Is(b, SessionState.Finished, "Auto-Ende nach Bandstopp");
            if (finished != 1) fails.Add($"FinishRequested {finished}× statt 1×");

            // Belt start aborted during its countdown → back to waiting.
            var c = new SessionStateMachine();
            c.Tick(dt, false, true, false, true, BeltSignal.Countdown);
            c.Tick(dt, false, true, false, true, BeltSignal.Stopped); Is(c, SessionState.Preparing, "Band-Start abgebrochen");

            // Belt connects during the keyboard countdown → waits for the belt.
            var d = new SessionStateMachine();
            d.Tick(dt, false, true, false, false, BeltSignal.None);
            d.Tick(dt, false, true, false, true, BeltSignal.Stopped); Is(d, SessionState.Preparing, "Band kommt im Countdown dazu");

            // Abort / finish by the runner.
            var a = new SessionStateMachine();
            a.Tick(dt, false, true, false, true, BeltSignal.Running); a.Abort(); Is(a, SessionState.Aborted, "Verwerfen");
            a.Tick(dt, false, true, true, true, BeltSignal.Running); Is(a, SessionState.Aborted, "Verworfen bleibt verworfen");
            var f = new SessionStateMachine(); int ff = 0; f.FinishRequested += () => ff++;
            f.Tick(dt, false, true, false, true, BeltSignal.Running); f.PauseByUser(); f.Finish(); f.Finish();
            Is(f, SessionState.Finished, "Beenden & speichern");
            if (ff != 1) fails.Add("Beenden doppelt");

            if (fails.Count == 0) Debug.Log("[SessionCheck] Alle Prüfungen bestanden.");
            else Debug.LogError("[SessionCheck] FAIL → " + string.Join("; ", fails));
            return fails.Count == 0;
        }
    }
}
