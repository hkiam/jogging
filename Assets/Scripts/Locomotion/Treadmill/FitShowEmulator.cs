using System;
using System.Collections.Generic;

namespace Jogging.Locomotion.Treadmill
{
    /// <summary>
    /// A simulated Sportstech F37 speaking FitShow byte for byte (as verified on the real belt, see
    /// <see cref="FitShowParser"/>): answers the SYS_STATUS poll, the SYS_INFO range queries and
    /// executes SYS_CONTROL/TARGET_OR_RUN. The belt moves like a real one — speed ~1 km/h per second,
    /// incline ~1 % per 1.5 s — and, like the real one, a control command while stopped STARTS it:
    /// the emulator does that too but counts it as a <see cref="Violations">violation</see>, so tests
    /// can prove the app never does it. The runner's buttons are the Press… methods.
    /// Plain C# (tested by BeltEmulatorCheck; used by MacBleBridgeTransport with -beltsim).
    /// </summary>
    public class FitShowEmulator
    {
        public const byte MinSpeed = 10, MaxSpeed = 200;   // 0.1 km/h (F37: 1.0…20.0)
        public const sbyte MinIncline = 0, MaxIncline = 15; // % (F37: 0…15)
        private const float CountdownS = 3f;

        public enum Status { Stopped, Countdown, Running, Safety }

        public Status State { get; private set; } = Status.Stopped;
        public float SpeedKmh { get; private set; }
        public float InclinePercent { get; private set; }
        public float TargetSpeedKmh { get; private set; } = 4f;
        public int TargetIncline { get; private set; }
        public float DistanceM { get; private set; }
        public float TimeS { get; private set; }

        /// <summary>Control commands that arrived while the belt was not running (the real F37 would start).</summary>
        public int Violations { get; private set; }

        /// <summary>Every command the belt received (for tests / the log).</summary>
        public readonly List<string> Log = new List<string>();

        private float countdown;

        // ------------------------------------------------------------------ the runner's buttons

        /// <summary>Start (3 s countdown, then 4 km/h or the last speed) or stop.</summary>
        public void PressStartStop()
        {
            if (State == Status.Safety) return;
            if (State == Status.Stopped) { State = Status.Countdown; countdown = CountdownS; if (TargetSpeedKmh < 1f) TargetSpeedKmh = 4f; }
            else { State = Status.Stopped; }
        }

        public void PressSpeed(float deltaKmh)
        {
            if (State != Status.Running) return;
            TargetSpeedKmh = Clamp(TargetSpeedKmh + deltaKmh, MinSpeed / 10f, MaxSpeed / 10f);
        }

        public void PressIncline(int delta)
        {
            if (State != Status.Running) return;
            TargetIncline = Math.Max(MinIncline, Math.Min(MaxIncline, TargetIncline + delta));
        }

        /// <summary>Pull / put back the safety clip (pulled: stops at once).</summary>
        public void ToggleSafetyClip()
        {
            if (State == Status.Safety) State = Status.Stopped;
            else { State = Status.Safety; SpeedKmh = 0f; }
        }

        // ------------------------------------------------------------------ physics

        public void Tick(float dt)
        {
            switch (State)
            {
                case Status.Countdown:
                    countdown -= dt;
                    if (countdown <= 0f) State = Status.Running;
                    break;
                case Status.Running:
                    SpeedKmh = Toward(SpeedKmh, TargetSpeedKmh, 1f * dt);           // ~1 km/h per second
                    InclinePercent = Toward(InclinePercent, TargetIncline, dt / 1.5f); // ~1 % per 1.5 s
                    DistanceM += SpeedKmh / 3.6f * dt;
                    TimeS += dt;
                    break;
                default:
                    SpeedKmh = Toward(SpeedKmh, 0f, 3f * dt);
                    break;
            }
        }

        // ------------------------------------------------------------------ protocol

        /// <summary>Reply to a frame written to FFF2 (null = no reply).</summary>
        public byte[] Handle(byte[] f)
        {
            if (!FitShowParser.IsFitShowFrame(f)) return null;
            byte cmd = f[1];
            if (cmd == FitShowParser.CmdSysStatus) return StatusFrame();
            if (cmd == FitShowParser.CmdSysInfo && f.Length >= 4)
            {
                if (f[2] == FitShowParser.InfoSpeed) return FitShowParser.Frame(FitShowParser.CmdSysInfo, FitShowParser.InfoSpeed, MaxSpeed, MinSpeed);
                if (f[2] == FitShowParser.InfoIncline) return FitShowParser.Frame(FitShowParser.CmdSysInfo, FitShowParser.InfoIncline, (byte)MaxIncline, (byte)MinIncline);
                return null;
            }
            if (cmd == FitShowParser.CmdSysControl && f.Length >= 7 && f[2] == FitShowParser.ControlTargetOrRun)
            {
                byte spd = f[3]; sbyte inc = unchecked((sbyte)f[4]);
                Log.Add($"SYS_CONTROL Tempo {spd / 10f:0.0} km/h, Steigung {inc} % (Band {State})");
                if (State != Status.Running)
                {
                    Violations++; // the real F37 would START here
                    if (State == Status.Stopped) { State = Status.Countdown; countdown = CountdownS; }
                }
                TargetSpeedKmh = Clamp(spd / 10f, MinSpeed / 10f, MaxSpeed / 10f);
                TargetIncline = Math.Max(MinIncline, Math.Min(MaxIncline, (int)inc));
                return null;
            }
            return null;
        }

        /// <summary>The SYS_STATUS reply the real F37 sends (idle frames are only the status byte).</summary>
        public byte[] StatusFrame()
        {
            switch (State)
            {
                case Status.Running:
                    int t = (int)TimeS, d = (int)DistanceM;
                    return FitShowParser.Frame(FitShowParser.CmdSysStatus, FitShowParser.StatusRunning,
                        // like the real F37: the speed it heads for (the belt itself only gets there at ~1 km/h per s)
                        (byte)Math.Round(TargetSpeedKmh * 10f), unchecked((byte)(sbyte)Math.Round(InclinePercent)),
                        (byte)(t & 0xFF), (byte)(t >> 8), (byte)(d & 0xFF), (byte)(d >> 8), 0, 0, 0, 0);
                case Status.Countdown: return FitShowParser.Frame(FitShowParser.CmdSysStatus, FitShowParser.StatusStart);
                case Status.Safety: return FitShowParser.Frame(FitShowParser.CmdSysStatus, FitShowParser.StatusSafety);
                default: return FitShowParser.Frame(FitShowParser.CmdSysStatus, FitShowParser.StatusIdle);
            }
        }

        private static float Toward(float v, float target, float step) =>
            Math.Abs(target - v) <= step ? target : v + Math.Sign(target - v) * step;

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
