namespace Jogging.Locomotion.Treadmill
{
    /// <summary>
    /// Decoder for the proprietary FitShow BLE protocol used by many Sportstech treadmills
    /// (incl. the F37, which advertises as "FS-xxxxxx" and exposes NO FTMS service).
    ///
    /// Transport: service FFF0, FFF1 = notify (belt → app), FFF2 = write (app → belt). The belt
    /// only answers polls, so the transport sends <see cref="StatusPoll"/> about once a second.
    ///
    /// Frame: 0x02, cmd, payload…, XOR-checksum of cmd+payload, 0x03.
    /// SYS_STATUS reply (cmd 0x51), verified live on the F37:
    ///   02 51 03 28 02 42 00 3B 00 17 00 00 00 00 00 16 03
    ///         │  │  │  └─┬─┘ └─┬─┘ └─┬─┘ └──┬──┘      └ checksum
    ///         │  │  │  time s dist m kcal    steps
    ///         │  │  └ incline %   (signed)
    ///         │  └ speed 0.1 km/h (0x28 = 4.0 km/h)
    ///         └ status: 0x00 idle/stopped, 0x03 running (others: start countdown / stopping)
    /// Idle reply is just 02 51 00 51 03. Reference: qdomyos-zwift fitshowtreadmill.cpp.
    /// </summary>
    public static class FitShowParser
    {
        public const byte Stx = 0x02, Etx = 0x03;
        public const byte CmdSysInfo = 0x50, CmdSysStatus = 0x51;
        public const byte StatusIdle = 0x00, StatusRunning = 0x03;

        public const byte CmdSysControl = 0x53;
        public const byte InfoSpeed = 0x02, InfoIncline = 0x03;
        public const byte ControlTargetOrRun = 0x02;

        /// <summary>SYS_STATUS poll written to FFF2.</summary>
        public static readonly byte[] StatusPoll = { Stx, CmdSysStatus, CmdSysStatus, Etx };

        /// <summary>Wrap a command payload: STX, payload…, XOR(payload), ETX.</summary>
        public static byte[] Frame(params byte[] payload)
        {
            var f = new byte[payload.Length + 3];
            f[0] = Stx;
            byte x = 0;
            for (int i = 0; i < payload.Length; i++) { f[i + 1] = payload[i]; x ^= payload[i]; }
            f[f.Length - 2] = x;
            f[f.Length - 1] = Etx;
            return f;
        }

        /// <summary>Read-only query: supported speed range (reply 50 02 max min …, 0.1 km/h).</summary>
        public static byte[] QuerySpeedRange() => Frame(CmdSysInfo, InfoSpeed);
        /// <summary>Read-only query: supported incline range (reply 50 03 max min …).</summary>
        public static byte[] QueryInclineRange() => Frame(CmdSysInfo, InfoIncline);

        /// <summary>
        /// Set target speed AND incline in one command (FitShow has no incline-only command).
        /// DANGER: "TARGET_OR_RUN" may start a stopped belt — only send while the belt reports
        /// RUNNING, and always pass the belt's own current speed byte so the speed doesn't change.
        /// </summary>
        public static byte[] SetSpeedAndIncline(byte speedTenthsKmh, sbyte inclinePercent) =>
            Frame(CmdSysControl, ControlTargetOrRun, speedTenthsKmh, unchecked((byte)inclinePercent));

        /// <summary>Parse a SYS_INFO range reply (speed or incline). Returns false otherwise.</summary>
        public static bool TryParseRange(byte[] d, out byte kind, out int max, out int min)
        {
            kind = 0; max = min = 0;
            if (!IsFitShowFrame(d) || d[1] != CmdSysInfo || d.Length < 5) return false;
            kind = d[2];
            if (kind != InfoSpeed && kind != InfoIncline) return false;
            if (d.Length < 7) return true; // e.g. "incline not supported" → max = min = 0
            max = kind == InfoIncline ? (sbyte)d[3] : d[3];
            min = kind == InfoIncline ? (sbyte)d[4] : d[4];
            return true;
        }

        /// <summary>Raw speed byte (0.1 km/h) of a RUNNING status frame, for echoing back.</summary>
        public static bool TryGetRunningSpeedByte(byte[] d, out byte speed, out sbyte incline)
        {
            speed = 0; incline = 0;
            if (!IsFitShowFrame(d) || d[1] != CmdSysStatus || d.Length < 11 || d[2] != StatusRunning) return false;
            speed = d[3];
            incline = (sbyte)d[4];
            return true;
        }

        // SYS_STATUS values (qdomyos-zwift fitshowtreadmill.h)
        public const byte StatusEnd = 0x01, StatusStart = 0x02, StatusStop = 0x04,
                          StatusError = 0x05, StatusSafety = 0x06, StatusPaused = 0x0A;

        public static BeltState ToBeltState(byte status)
        {
            switch (status)
            {
                case StatusRunning: return BeltState.Running;
                case StatusStart: return BeltState.Countdown;
                case StatusPaused: return BeltState.Paused;
                case StatusSafety: return BeltState.Safety;
                case StatusError: return BeltState.Error;
                default: return BeltState.Stopped; // idle, end, stopping
            }
        }

        /// <summary>True if this looks like a well-formed FitShow frame (framing + checksum).</summary>
        public static bool IsFitShowFrame(byte[] d)
        {
            if (d == null || d.Length < 4 || d[0] != Stx || d[d.Length - 1] != Etx) return false;
            byte x = 0;
            for (int i = 1; i < d.Length - 2; i++) x ^= d[i];
            return x == d[d.Length - 2];
        }

        /// <summary>Decode a SYS_STATUS reply. Idle frames decode to speed 0.</summary>
        public static bool TryParse(byte[] d, out TreadmillData result, out byte status)
        {
            result = default;
            status = 0;
            if (!IsFitShowFrame(d) || d[1] != CmdSysStatus) return false;

            status = d[2];
            if (status != StatusRunning || d.Length < 11)
            {
                result.SpeedKmh = 0f; // idle / countdown / stopping: belt not delivering distance
                return true;
            }

            result.SpeedKmh = d[3] * 0.1f;
            result.HasIncline = true;
            result.InclinePercent = (sbyte)d[4];
            result.HasDistance = true;
            result.TotalDistanceMeters = d[7] | (d[8] << 8);
            // after calories (9–10) and steps (11–12): the hand-grip pulse, 0 without hands on the grips
            // (qdomyos-zwift reads it there; on the F37 still to be confirmed with hands on the grips)
            if (d.Length >= 16 && d[13] >= 40 && d[13] <= 220) result.HeartRateBpm = d[13];
            return true;
        }
    }
}
