using System;

namespace Jogging.Locomotion.Treadmill
{
    /// <summary>
    /// Abstraction over the BLE link to a treadmill: <see cref="MacBleBridgeTransport"/> (the Swift
    /// bridge on macOS; with -beltsim a simulated F37). Keeping this an interface means the rest of
    /// the game never depends on how Bluetooth is reached.
    /// </summary>
    public interface ITreadmillTransport
    {
        /// <summary>Raw FTMS Treadmill Data (0x2ACD) notification bytes.</summary>
        event Action<byte[]> TreadmillDataReceived;

        /// <summary>Fired when the connection state changes.</summary>
        event Action<bool> ConnectionChanged;

        bool IsConnected { get; }

        /// <summary>What the connected device speaks: "FitShow", "FTMS", "WalkingPad", "RSC" (foot pod) or "".</summary>
        string Protocol { get; }

        void Connect();
        void Disconnect();

        /// <summary>Write to the FTMS Control Point (0x2AD9) — e.g. set target speed/incline.</summary>
        void WriteControlPoint(byte[] data);
    }
}
