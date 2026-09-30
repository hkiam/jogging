using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Jogging.Core
{
    /// <summary>
    /// While the app runs, the device neither locks nor goes to sleep: on the treadmill nobody touches
    /// the screen for an hour. iPad/Android: the screen stays on (Screen.sleepTimeout). macOS: a power
    /// assertion that keeps the display awake (so no screen saver or lock either) — the same one video
    /// players take. It is taken in-process through IOKit (no child process) and ends with the app.
    /// </summary>
    public static class KeepAwake
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [DllImport("kernel32.dll")] private static extern uint SetThreadExecutionState(uint flags);
        private const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x1, ES_DISPLAY_REQUIRED = 0x2;
#endif
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
        private const string IOKit = "/System/Library/Frameworks/IOKit.framework/IOKit";
        private const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        [DllImport(IOKit)] private static extern int IOPMAssertionCreateWithName(IntPtr type, uint level, IntPtr name, out uint id);
        [DllImport(CF)] private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string s, uint encoding);
        private const uint Utf8 = 0x08000100, LevelOn = 255;
        private static bool taken;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            // Windows: display and system stay on while the app runs (released when the process ends)
            try { SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED); }
            catch (Exception e) { Debug.LogWarning("[Jogging] Ruhezustand kann nicht verhindert werden: " + e.Message); }
#endif
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
            if (taken) return;
            try
            {
                var type = CFStringCreateWithCString(IntPtr.Zero, "PreventUserIdleDisplaySleep", Utf8);
                var name = CFStringCreateWithCString(IntPtr.Zero, "Jogging: Lauf am Laufband", Utf8);
                taken = IOPMAssertionCreateWithName(type, LevelOn, name, out _) == 0; // CF strings are kept for the app's life
                if (!taken) Debug.LogWarning("[Jogging] Ruhezustand kann nicht verhindert werden");
            }
            catch (Exception e) { Debug.LogWarning("[Jogging] Ruhezustand kann nicht verhindert werden: " + e.Message); }
#endif
        }
    }
}
