namespace Jogging.Locomotion.Treadmill
{
    /// <summary>
    /// Bluetooth LE Fitness Machine Service (FTMS) identifiers. The Sportstech F37 and most
    /// modern treadmills expose FTMS, so parsing this standard covers them.
    /// </summary>
    public static class FtmsUuids
    {
        public const string FitnessMachineService = "00001826-0000-1000-8000-00805f9b34fb";
        public const string TreadmillData        = "00002acd-0000-1000-8000-00805f9b34fb"; // notify: speed/incline/…
        public const string FitnessMachineFeature = "00002acc-0000-1000-8000-00805f9b34fb";
        public const string FitnessMachineControlPoint = "00002ad9-0000-1000-8000-00805f9b34fb"; // set speed/incline (later)
        public const string FitnessMachineStatus  = "00002ada-0000-1000-8000-00805f9b34fb";
    }

    /// <summary>One decoded FTMS Treadmill Data notification.</summary>
    public struct TreadmillData
    {
        public float SpeedKmh;
        public bool HasIncline;
        public float InclinePercent;
        public bool HasDistance;
        public float TotalDistanceMeters;
        /// <summary>Heart rate the machine measures itself (hand grips or its own receiver); 0 = none.</summary>
        public int HeartRateBpm;
    }

    /// <summary>
    /// Decoder for the FTMS "Treadmill Data" characteristic (0x2ACD). Reads the fields in spec
    /// order according to the flags word and extracts instantaneous speed, inclination and total
    /// distance. Pure C#, so it is unit-testable and hardware-independent.
    /// </summary>
    public static class FtmsTreadmillParser
    {
        public static bool TryParse(byte[] data, out TreadmillData result)
        {
            result = default;
            if (data == null || data.Length < 2) return false;

            int i = 0;
            ushort flags = (ushort)(data[0] | (data[1] << 8));
            i = 2;

            bool moreData      = (flags & (1 << 0)) != 0; // 0 => instantaneous speed present
            bool avgSpeed      = (flags & (1 << 1)) != 0;
            bool totalDistance = (flags & (1 << 2)) != 0;
            bool inclination   = (flags & (1 << 3)) != 0;
            bool elevation     = (flags & (1 << 4)) != 0;
            bool instPace      = (flags & (1 << 5)) != 0;
            bool avgPace       = (flags & (1 << 6)) != 0;
            bool energy        = (flags & (1 << 7)) != 0;
            bool heartRate     = (flags & (1 << 8)) != 0;

            // Instantaneous Speed (uint16, 0.01 km/h) — present unless "more data".
            if (!moreData)
            {
                if (i + 2 > data.Length) return true; // partial but usable (flags only)
                result.SpeedKmh = ReadU16(data, ref i) * 0.01f;
            }

            if (avgSpeed) i += 2; // Average Speed uint16

            if (totalDistance)
            {
                if (i + 3 <= data.Length)
                {
                    result.TotalDistanceMeters = ReadU24(data, ref i);
                    result.HasDistance = true;
                }
                else return true;
            }

            if (inclination)
            {
                if (i + 4 <= data.Length)
                {
                    short inc = ReadS16(data, ref i);   // Inclination, 0.1 %
                    i += 2;                             // Ramp Angle Setting (skip)
                    result.InclinePercent = inc * 0.1f;
                    result.HasIncline = true;
                }
                else return true;
            }

            // Further fields in spec order, up to the heart rate (hand grips or a strap paired to the machine)
            if (elevation) i += 4;  // positive + negative elevation gain, uint16 each
            if (instPace) i += 1;   // uint8, 0.1 km/min
            if (avgPace) i += 1;
            if (energy) i += 5;     // total uint16, per hour uint16, per minute uint8
            if (heartRate && i < data.Length)
            {
                int bpm = data[i];
                if (bpm >= 30 && bpm <= 230) result.HeartRateBpm = bpm;
            }
            return true;
        }

        private static ushort ReadU16(byte[] d, ref int i)
        {
            ushort v = (ushort)(d[i] | (d[i + 1] << 8));
            i += 2; return v;
        }

        private static int ReadU24(byte[] d, ref int i)
        {
            int v = d[i] | (d[i + 1] << 8) | (d[i + 2] << 16);
            i += 3; return v;
        }

        private static short ReadS16(byte[] d, ref int i)
        {
            short v = (short)(d[i] | (d[i + 1] << 8));
            i += 2; return v;
        }
    }
}
