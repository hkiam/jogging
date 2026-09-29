using System.Runtime.InteropServices;
using UnityEngine;

namespace Jogging.Core
{
    /// <summary>
    /// Runs a shell command on macOS through libc's system() — System.Diagnostics.Process does not work
    /// in IL2CPP players on macOS. Arguments must be quoted with <see cref="Quote"/>; end the command
    /// with " &amp;" to not wait for it.
    /// </summary>
    public static class Shell
    {
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        [DllImport("libc", EntryPoint = "system")]
        private static extern int NativeSystem(string command);
#endif

        /// <summary>Exit status (0 = ok), -1 when not available on this platform.</summary>
        public static int Run(string command)
        {
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            try { return NativeSystem(command); }
            catch (System.Exception e) { Debug.LogWarning("[Jogging] Befehl nicht möglich: " + e.Message); return -1; }
#else
            return -1;
#endif
        }

        /// <summary>A single shell argument in single quotes ('…'\''…').</summary>
        public static string Quote(string s) => "'" + (s ?? "").Replace("'", "'\\''") + "'";
    }
}
