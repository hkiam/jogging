using UnityEngine;

namespace Jogging.Locomotion.Treadmill
{
    /// <summary>
    /// Builds FTMS Fitness Machine Control Point (0x2AD9) command payloads. Used to take control
    /// of the treadmill and set target speed / inclination so the route can drive the belt.
    /// </summary>
    public static class FtmsControl
    {
        // Op codes (FTMS spec).
        public const byte OpRequestControl = 0x00;
        public const byte OpReset          = 0x01;
        public const byte OpSetTargetSpeed = 0x02; // uint16, 0.01 km/h
        public const byte OpSetTargetIncl  = 0x03; // sint16, 0.1 %
        public const byte OpStartResume    = 0x07;
        public const byte OpStopPause      = 0x08; // 0x01 = stop, 0x02 = pause

        public static byte[] RequestControl() => new[] { OpRequestControl };
        public static byte[] Reset() => new[] { OpReset };
        public static byte[] Start() => new[] { OpStartResume };
        public static byte[] Stop() => new byte[] { OpStopPause, 0x01 };

        public static byte[] SetTargetSpeed(float kmh)
        {
            ushort v = (ushort)Mathf.Clamp(kmh * 100f, 0f, 65535f);
            return new[] { OpSetTargetSpeed, (byte)(v & 0xFF), (byte)(v >> 8) };
        }

        public static byte[] SetTargetInclination(float percent)
        {
            short v = (short)Mathf.Clamp(percent * 10f, short.MinValue, short.MaxValue);
            return new[] { OpSetTargetIncl, (byte)(v & 0xFF), (byte)(v >> 8) };
        }
    }
}
