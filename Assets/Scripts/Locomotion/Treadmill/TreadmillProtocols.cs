namespace Jogging.Locomotion.Treadmill
{
    /// <summary>
    /// KingSmith WalkingPad (older models with their own protocol; newer ones speak FTMS): service FE00,
    /// FE01 notify, FE02 write. The pad answers the stats query (<see cref="AskStats"/>, sent about once
    /// a second by the Bluetooth plugin) with
    ///   F8 A2 state speed mode time(3) dist(3) steps(3) … checksum FD
    /// speed in 0.1 km/h, time in s, distance in 10 m, 24-bit values big-endian, checksum = sum of the
    /// bytes between the header byte and the checksum. Layout as in the open-source ph4-walkingpad.
    /// Read only: the app sends the pad nothing but this query. Not yet tried on a real pad.
    /// </summary>
    public static class WalkingPadParser
    {
        public static readonly byte[] AskStats = { 0xF7, 0xA2, 0x00, 0x00, 0xA2, 0xFD };
        public const byte StateIdle = 0, StateRunning = 1, StateStandby = 5, StateStarting = 9;

        public static bool IsFrame(byte[] d)
        {
            if (d == null || d.Length < 15 || d[0] != 0xF8 || d[1] != 0xA2 || d[d.Length - 1] != 0xFD) return false;
            int sum = 0;
            for (int i = 1; i < d.Length - 2; i++) sum += d[i];
            return (sum & 0xFF) == d[d.Length - 2];
        }

        public static bool TryParse(byte[] d, out TreadmillData result, out BeltState state)
        {
            result = default; state = BeltState.Unknown;
            if (!IsFrame(d)) return false;
            byte st = d[2];
            state = st == StateRunning ? BeltState.Running : st == StateStarting ? BeltState.Countdown : BeltState.Stopped;
            result.SpeedKmh = state == BeltState.Running ? d[3] * 0.1f : 0f;
            if (result.SpeedKmh > 20f) return false; // implausible: not what we think it is
            result.HasDistance = true;
            result.TotalDistanceMeters = ((d[8] << 16) | (d[9] << 8) | d[10]) * 10f;
            return true;
        }
    }

    /// <summary>
    /// Running Speed and Cadence (service 1814, measurement 2A53): foot pods (e.g. Stryd, Polar Stride,
    /// Milestone) and some treadmills. With a foot pod any treadmill, even one without Bluetooth, can
    /// drive the run. Flags: bit 0 stride length, bit 1 total distance present; speed uint16 in 1/256 m/s,
    /// cadence uint8, [stride uint16 cm], [total distance uint32 in 0.1 m]. No incline, read only.
    /// </summary>
    public static class RscParser
    {
        public static bool TryParse(byte[] d, out TreadmillData result, out BeltState state)
        {
            result = default; state = BeltState.Unknown;
            if (d == null || d.Length < 4) return false;
            byte flags = d[0];
            float mps = (d[1] | (d[2] << 8)) / 256f;
            if (mps > 8f) return false;
            result.SpeedKmh = mps * 3.6f;
            int i = 4;
            if ((flags & 0x01) != 0) i += 2;
            if ((flags & 0x02) != 0 && i + 4 <= d.Length)
            {
                uint dist = (uint)(d[i] | (d[i + 1] << 8) | (d[i + 2] << 16) | (d[i + 3] << 24));
                result.HasDistance = true; result.TotalDistanceMeters = dist * 0.1f;
            }
            state = result.SpeedKmh > 0.3f ? BeltState.Running : BeltState.Stopped;
            return true;
        }
    }

    /// <summary>
    /// Changyow iConsole+ consoles (Reebok, Taurus, Finnlo, FlowFitness, Maxxus 9.1 …): the plugin sends
    /// only the status query F0 A2 addr D3 sum; the answer is a 19-byte F0 B2 (or B0) frame, sum of all
    /// bytes before it as checksum, every data byte stored as value + 1 (so 0 never appears):
    /// [4] s, [5] min, [6..7] distance in 10 m (hundreds, rest), [10..11] heart rate, [12..13] speed in
    /// 0.1 km/h, [14] incline %. Layout from the open-source qdomyos-zwift. Read only, not yet tried.
    /// </summary>
    public static class IConsoleParser
    {
        public const int Length = 19;

        public static bool IsFrame(byte[] d)
        {
            if (d == null || d.Length != Length || d[0] != 0xF0 || (d[1] != 0xB2 && d[1] != 0xB0)) return false;
            int sum = 0;
            for (int i = 0; i < d.Length - 1; i++) sum += d[i];
            return (sum & 0xFF) == d[d.Length - 1];
        }

        private static int V(byte b) => b > 0 ? b - 1 : 0;

        public static bool TryParse(byte[] d, out TreadmillData result, out BeltState state)
        {
            result = default; state = BeltState.Unknown;
            if (!IsFrame(d)) return false;
            result.SpeedKmh = (V(d[12]) * 100 + V(d[13])) / 10f;
            if (result.SpeedKmh > 25f) return false;
            result.HasIncline = true; result.InclinePercent = V(d[14]);
            result.HasDistance = true; result.TotalDistanceMeters = (V(d[6]) * 100 + V(d[7])) * 10f;
            int hr = V(d[10]) * 100 + V(d[11]);
            if (hr >= 30 && hr <= 230) result.HeartRateBpm = hr;
            state = result.SpeedKmh > 0.05f ? BeltState.Running : BeltState.Stopped;
            return true;
        }
    }

    /// <summary>Joins iConsole notifications (a frame may come in pieces) into whole 19-byte frames.</summary>
    public class IConsoleAssembler
    {
        private readonly System.Collections.Generic.List<byte> buf = new System.Collections.Generic.List<byte>();

        public System.Collections.Generic.IEnumerable<byte[]> Add(byte[] piece)
        {
            buf.AddRange(piece);
            while (true)
            {
                int start = buf.IndexOf(0xF0);
                if (start < 0) { buf.Clear(); yield break; }
                if (start > 0) buf.RemoveRange(0, start);
                if (buf.Count < IConsoleParser.Length) yield break;
                var f = buf.GetRange(0, IConsoleParser.Length).ToArray();
                if (IConsoleParser.IsFrame(f)) { buf.RemoveRange(0, IConsoleParser.Length); yield return f; }
                else buf.RemoveAt(0); // not a frame start: look for the next F0
                if (buf.Count > 256) buf.Clear();
            }
        }
    }

    /// <summary>
    /// LifeSpan under-desk treadmills: request/response on FFF0. The plugin asks A1 op 00 00 00 in turn
    /// for speed (0x82), state (0x91) and distance (0x85) and puts the op in front of the answer
    /// (A1 AA b2 b3 b4 00): speed = b2 + b3/100 in mph; state 03 running, 05 paused, else stopped.
    /// Only queries — the app sends a LifeSpan nothing else. Read only, not yet tried.
    /// </summary>
    public static class LifeSpanParser
    {
        private static float lastKmh; private static BeltState lastState = BeltState.Unknown;

        public static bool TryParse(byte[] d, out TreadmillData result, out BeltState state)
        {
            result = default; state = lastState;
            if (d == null || d.Length < 5 || d[1] != 0xA1 || d[2] != 0xAA) return false;
            switch (d[0])
            {
                case 0x82: lastKmh = (d[3] + d[4] / 100f) * 1.609344f; if (lastKmh > 25f) lastKmh = 0f; break;
                case 0x91: lastState = d[3] == 3 ? BeltState.Running : d[3] == 5 ? BeltState.Paused : BeltState.Stopped; break;
                default: return false; // distance: scaling not confirmed yet — the run measures its own
            }
            state = lastState;
            result.SpeedKmh = lastState == BeltState.Running ? lastKmh : 0f;
            return true;
        }
    }

    /// <summary>One entry point for every kind of treadmill data (locomotion, heart rate from the grips).</summary>
    public static class TreadmillFrames
    {
        public static bool TryDecode(string protocol, byte[] bytes, out TreadmillData d, out BeltState state)
        {
            state = BeltState.Unknown;
            // FitShow and WalkingPad frames identify themselves (framing + checksum)
            if (FitShowParser.IsFitShowFrame(bytes))
            {
                bool ok = FitShowParser.TryParse(bytes, out d, out var status); // false e.g. for a SYS_INFO reply
                if (ok) state = FitShowParser.ToBeltState(status);
                return ok;
            }
            if (protocol == "WalkingPad" || WalkingPadParser.IsFrame(bytes)) return WalkingPadParser.TryParse(bytes, out d, out state);
            if (protocol == "RSC") return RscParser.TryParse(bytes, out d, out state);
            if (protocol == "iConsole" || IConsoleParser.IsFrame(bytes)) return IConsoleParser.TryParse(bytes, out d, out state);
            if (protocol == "LifeSpan") return LifeSpanParser.TryParse(bytes, out d, out state);
            if (!FtmsTreadmillParser.TryParse(bytes, out d)) return false;
            state = d.SpeedKmh > 0.05f ? BeltState.Running : BeltState.Stopped; // FTMS: no status here
            return true;
        }
    }
}
