using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Jogging.Locomotion.Treadmill
{
    /// <summary>
    /// Bluetooth on iPad and Android: a native plugin that does what the Mac's Swift bridge does
    /// (Plugins/iOS/JoggingBle.swift, Plugins/Android/JoggingBle.java) and speaks the same one-letter
    /// messages — 'D' frame, 'S' connected, 'P' protocol, 'N' device, 'R'/'T'/'M' heart rate, 'L' log;
    /// commands 'W' write, 'C' connect, 'X', 'H', 'Y'. So <see cref="MacBleBridgeTransport"/> and the
    /// safety layer above it stay the same; only UDP is replaced by <see cref="Send"/> / <see cref="Poll"/>.
    /// Messages are batched as [length lo, length hi, bytes…].
    /// </summary>
    public static class NativeBle
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void jog_ble_start();
        [DllImport("__Internal")] private static extern void jog_ble_send(byte[] data, int length);
        [DllImport("__Internal")] private static extern int jog_ble_poll(byte[] buffer, int capacity);
        private static readonly byte[] pollBuffer = new byte[64 * 1024];
#elif UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidJavaObject plugin;
#endif
        private static bool started;
        private static readonly List<byte[]> local = new List<byte[]>(); // messages from this side (permission denied)

        private static void Local(string logLine)
        {
            var m = System.Text.Encoding.UTF8.GetBytes("L" + logLine);
            lock (local) local.Add(m);
        }

        /// <summary>A native Bluetooth plugin exists on this platform (iPad / Android device).</summary>
#if (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
        public const bool Available = true;
#else
        public const bool Available = false;
#endif

        /// <summary>Start Bluetooth (asks for the Android permissions first; iOS asks by itself).</summary>
        public static void Start()
        {
            if (started || !Available) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!AndroidPermissions()) return; // asked; Start is called again on the next connect attempt
            using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var cls = new AndroidJavaClass("local.jogging.ble.JoggingBle"))
                plugin = cls.CallStatic<AndroidJavaObject>("start", activity);
            started = plugin != null;
#elif UNITY_IOS && !UNITY_EDITOR
            jog_ble_start();
            started = true;
#endif
        }

        public static void Send(byte[] msg)
        {
            if (!started) { Start(); if (!started) return; } // e.g. right after the permission dialog
#if UNITY_IOS && !UNITY_EDITOR
            jog_ble_send(msg, msg.Length);
#elif UNITY_ANDROID && !UNITY_EDITOR
            plugin.Call("send", (object)Array.ConvertAll(msg, b => unchecked((sbyte)b)));
#endif
        }

        /// <summary>All messages that arrived since the last call (main thread, once per frame).</summary>
        public static void Poll(Action<byte[]> each)
        {
            lock (local) { foreach (var m in local) each(m); local.Clear(); }
            if (!started) return;
            byte[] batch = null; int n = 0;
#if UNITY_IOS && !UNITY_EDITOR
            n = jog_ble_poll(pollBuffer, pollBuffer.Length);
            batch = pollBuffer;
#elif UNITY_ANDROID && !UNITY_EDITOR
            var s = plugin.Call<sbyte[]>("poll");
            if (s != null && s.Length > 0) { batch = (byte[])(Array)s; n = s.Length; }
#endif
            for (int i = 0; batch != null && i + 2 <= n;)
            {
                int len = batch[i] | (batch[i + 1] << 8);
                i += 2;
                if (len <= 0 || i + len > n) break;
                var m = new byte[len];
                Buffer.BlockCopy(batch, i, m, 0, len);
                i += len;
                each(m);
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // Android 12+: "nearby devices" (scan + connect); Android 11 and older (e.g. Fire OS 8): location.
        private static bool AndroidPermissions()
        {
            int sdk;
            using (var v = new AndroidJavaClass("android.os.Build$VERSION")) sdk = v.GetStatic<int>("SDK_INT");
            var needed = sdk >= 31
                ? new[] { "android.permission.BLUETOOTH_SCAN", "android.permission.BLUETOOTH_CONNECT" }
                : new[] { "android.permission.ACCESS_FINE_LOCATION" };
            var missing = new List<string>();
            foreach (var p in needed) if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(p)) missing.Add(p);
            if (missing.Count == 0) return true;
            // Unity re-sends connect every 3 s: ask at most once a minute (not again right after "deny")
            if (Time.realtimeSinceStartup < nextAsk) return false;
            nextAsk = Time.realtimeSinceStartup + 60f;
            var cb = new UnityEngine.Android.PermissionCallbacks();
            cb.PermissionDenied += _ => Local(Jogging.Core.Loc.T("ERROR Bluetooth nicht erlaubt (Einstellungen → Apps → Jogging → Berechtigungen)"));
            UnityEngine.Android.Permission.RequestUserPermissions(missing.ToArray(), cb);
            return false;
        }
        private static float nextAsk;
#endif
    }
}
