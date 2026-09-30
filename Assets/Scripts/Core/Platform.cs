using UnityEngine;

namespace Jogging.Core
{
    /// <summary>
    /// What the device can do. The Mac app talks to the treadmill through the Swift BLE bridge and
    /// speaks with macOS "say"; on iPad and Android neither exists (yet): the run is driven by touch.
    /// </summary>
    public static class Platform
    {
        /// <summary>iPad / Android device (also in the simulator / emulator).</summary>
        public static bool IsMobile => Application.isMobilePlatform;

        /// <summary>
        /// Under 4 GB RAM (e.g. a Fire HD 10 with 3 GB, where textures and terrain share the memory):
        /// half-resolution textures, a smaller terrain grid, graphics "low" — else Android kills the app.
        /// -lowmem forces it (tests).
        /// </summary>
        public static bool LowMemory => Args.Has("-lowmem") || (SystemInfo.systemMemorySize > 0 && SystemInfo.systemMemorySize < 4000);

#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        /// <summary>The Swift bridge (Bluetooth to belt and strap), macOS speech, QR reading and shell commands are available.</summary>
        public const bool HasMacBridge = true;
#else
        public const bool HasMacBridge = false;
#endif

#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX || UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        /// <summary>
        /// A bridge helper app speaks Bluetooth LE for the app over UDP: Tools/MacBleBridge (macOS) or
        /// Tools/WinBleBridge (Windows, which also speaks the announcements).
        /// </summary>
        public const bool HasBleBridge = true;
#else
        public const bool HasBleBridge = false;
#endif

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        /// <summary>Windows: the bridge also speaks (Windows has no "say").</summary>
        public const bool SpeechViaBridge = true;
#else
        public const bool SpeechViaBridge = false;
#endif
    }
}
