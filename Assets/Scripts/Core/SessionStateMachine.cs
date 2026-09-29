namespace Jogging.Core
{
    public enum SessionState { Preparing, Countdown, Running, Paused, Finished, Aborted }

    /// <summary>Why a run is paused (drives the pause dialog's text and how it resumes).</summary>
    public enum PauseReason { None, User, BeltStopped, BeltSafety, BeltError, ConnectionLost }

    /// <summary>What the belt reports, reduced to what the session needs (no Unity/BLE types).</summary>
    public enum BeltSignal { None, Stopped, Countdown, Running, Safety, Error }

    /// <summary>
    /// The run's state as a plain, testable machine. It is fed once per frame with what the world and
    /// the treadmill report (<see cref="Tick"/>) and with the runner's commands (pause, resume,
    /// finish, abort). Rules:
    ///   • Preparing until the menu is closed and the ground is ready; then a keyboard run counts
    ///     down 3-2-1, a treadmill run waits for the belt (its own countdown, then running).
    ///   • A run becomes a treadmill run as soon as a belt delivers data and stays one: losing the
    ///     connection pauses it (it never silently falls back to the keyboard).
    ///   • A belt stop, missing safety key or belt error pauses; the belt running again resumes.
    ///     A belt stopped for <see cref="AutoFinishAfter"/> seconds finishes the run.
    ///   • The runner can pause at any time; resuming a treadmill run needs the belt running.
    /// Only <see cref="SessionState.Running"/> moves the world and the clock.
    /// </summary>
    public class SessionStateMachine
    {
        public const float CountdownSeconds = 3f;
        public const float AutoFinishAfter = 120f;

        public SessionState State { get; private set; } = SessionState.Preparing;
        public PauseReason Reason { get; private set; }
        public bool BeltRun { get; private set; }

        /// <summary>Countdown seconds left (keyboard runs), for "3 · 2 · 1".</summary>
        public float CountdownLeft { get; private set; }

        /// <summary>Seconds until a belt-stopped run ends by itself (0 when not applicable).</summary>
        public float AutoFinishLeft { get; private set; }

        /// <summary>True once the run was running at least once (a pause before the start is no pause).</summary>
        public bool Started { get; private set; }

        public bool Moves => State == SessionState.Running;

        /// <summary>Raised when the machine decides the run is over (finish line, auto-finish or Finish()).</summary>
        public event System.Action FinishRequested;

        private bool userPaused;
        private float stoppedFor;

        /// <param name="menuOpen">start menu or workshop is open (nothing counts)</param>
        /// <param name="terrainReady">ground under the runner is final</param>
        /// <param name="trackFinished">finish line reached (or finished from outside)</param>
        /// <param name="beltActive">a treadmill delivers fresh data</param>
        /// <param name="belt">what that treadmill reports</param>
        public void Tick(float dt, bool menuOpen, bool terrainReady, bool trackFinished, bool beltActive, BeltSignal belt)
        {
            if (State == SessionState.Finished || State == SessionState.Aborted) return;
            if (trackFinished) { State = SessionState.Finished; return; }
            if (beltActive) BeltRun = true;
            if (!beltActive) belt = BeltSignal.None;
            AutoFinishLeft = 0f;

            if (menuOpen || !terrainReady)
            {
                if (!Started) State = SessionState.Preparing;
                return;
            }

            switch (State)
            {
                case SessionState.Preparing:
                    if (BeltRun)
                    {
                        if (belt == BeltSignal.Countdown) State = SessionState.Countdown;
                        else if (belt == BeltSignal.Running) Run();
                    }
                    else { State = SessionState.Countdown; CountdownLeft = CountdownSeconds; }
                    break;

                case SessionState.Countdown:
                    if (BeltRun)
                    {
                        if (belt == BeltSignal.Running) Run();
                        else if (belt != BeltSignal.Countdown) State = SessionState.Preparing; // start aborted on the belt
                    }
                    else
                    {
                        CountdownLeft -= dt;
                        if (CountdownLeft <= 0f) Run();
                    }
                    break;

                case SessionState.Running:
                    if (userPaused) { Pause(PauseReason.User); break; }
                    if (BeltRun)
                    {
                        var r = BeltPause(belt, beltActive);
                        if (r != PauseReason.None) Pause(r);
                    }
                    break;

                case SessionState.Paused:
                    if (BeltRun)
                    {
                        var r = BeltPause(belt, beltActive);
                        if (userPaused) Reason = PauseReason.User;
                        else if (r == PauseReason.None) { Run(); break; }
                        else Reason = r;
                        if (r == PauseReason.BeltStopped)
                        {
                            stoppedFor += dt;
                            AutoFinishLeft = UnityEngine.Mathf.Max(0f, AutoFinishAfter - stoppedFor);
                            if (stoppedFor >= AutoFinishAfter) Finish();
                        }
                        else stoppedFor = 0f;
                    }
                    else if (!userPaused) Run();
                    break;
            }
        }

        /// <summary>The runner pauses (from the HUD or Esc). Ignored before the start.</summary>
        public void PauseByUser()
        {
            if (State == SessionState.Running || State == SessionState.Paused) { userPaused = true; Pause(PauseReason.User); }
        }

        /// <summary>The runner resumes. A treadmill run continues once the belt runs again.</summary>
        public void Resume() => userPaused = false;

        /// <summary>End the run and keep it (recorded, finish screen).</summary>
        public void Finish()
        {
            if (State == SessionState.Finished || State == SessionState.Aborted) return;
            State = SessionState.Finished;
            FinishRequested?.Invoke();
        }

        /// <summary>End the run without keeping it.</summary>
        public void Abort()
        {
            if (State == SessionState.Finished) return;
            State = SessionState.Aborted;
        }

        private void Run()
        {
            State = SessionState.Running; Reason = PauseReason.None;
            Started = true; stoppedFor = 0f; CountdownLeft = 0f;
        }

        private void Pause(PauseReason r) { State = SessionState.Paused; Reason = r; }

        private static PauseReason BeltPause(BeltSignal belt, bool active)
        {
            if (!active) return PauseReason.ConnectionLost;
            switch (belt)
            {
                case BeltSignal.Running: return PauseReason.None;
                case BeltSignal.Safety: return PauseReason.BeltSafety;
                case BeltSignal.Error: return PauseReason.BeltError;
                default: return PauseReason.BeltStopped;
            }
        }
    }
}
