using System.Runtime.InteropServices;
using UnityEngine;

namespace Jogging.UI
{
    /// <summary>
    /// Spoken announcements on iPad and Android: the system voice in German through a small native
    /// plugin (Plugins/iOS/JoggingSpeech.swift, Plugins/Android/JoggingSpeech.java). The Mac speaks
    /// via "say" in <see cref="Announcer"/>. A new announcement replaces the one being spoken.
    /// </summary>
    public static class NativeSpeech
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void jog_say(string text, string language);
        [DllImport("__Internal")] private static extern void jog_say_stop();
        public const bool Available = true;
#elif UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidJavaObject plugin;
        public const bool Available = true;
#else
        public const bool Available = false;
#endif

        public static void Say(string text)
        {
#if UNITY_IOS && !UNITY_EDITOR
            jog_say(text, Jogging.Core.Loc.En ? "en-GB" : "de-DE");
#elif UNITY_ANDROID && !UNITY_EDITOR
            if (plugin == null)
            {
                using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var cls = new AndroidJavaClass("local.jogging.speech.JoggingSpeech"))
                    plugin = cls.CallStatic<AndroidJavaObject>("start", activity);
            }
            plugin?.Call("say", text, Jogging.Core.Loc.En ? "en" : "de");
            var err = plugin?.Call<string>("error");
            if (!string.IsNullOrEmpty(err)) Debug.LogWarning("[Jogging] Ansage: " + err);
#endif
        }

        public static void Stop()
        {
#if UNITY_IOS && !UNITY_EDITOR
            jog_say_stop();
#elif UNITY_ANDROID && !UNITY_EDITOR
            plugin?.Call("stop");
#endif
        }
    }
}
