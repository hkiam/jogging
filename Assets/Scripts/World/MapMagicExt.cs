using System;
using System.Reflection;
using UnityEngine;

namespace Jogging.World
{
    /// <summary>
    /// The app's view of its changes inside MapMagic 2 (docs/Setup.md). MapMagic comes from the Asset Store and
    /// isn't part of the public repository; the changes (spare-tile prewarming, a thread pool, cooperative
    /// stopping, timing for -hitch/-diag …) may or may not be applied. Everything here is found by reflection:
    /// with the changes it works as before, with an unmodified MapMagic the app still builds and runs – only
    /// without these extras (a fallback is noted where there is one).
    /// </summary>
    public static class MapMagicExt
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        private static Type tileDiag, coroutines, threads;
        private static bool looked;

        private static Type Find(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }

        private static void Look()
        {
            if (looked) return;
            looked = true;
            tileDiag = Find("Den.Tools.TileDiag");
            coroutines = Find("Den.Tools.Tasks.CoroutineManager");
            threads = Find("Den.Tools.Tasks.ThreadManager");
            if (tileDiag == null) Debug.Log("[Jogging] MapMagic ohne die Anpassungen der App (docs/Setup.md) – läuft, aber ohne Vorbauen der Kacheln und ohne Zeitmessung");
        }

        /// <summary>True if MapMagic carries the app's changes.</summary>
        public static bool Patched { get { Look(); return tileDiag != null; } }

        private static object Get(Type t, string name, object target = null)
        {
            if (t == null) return null;
            var f = t.GetField(name, Any);
            if (f != null) return f.GetValue(target);
            var p = t.GetProperty(name, Any);
            return p != null ? p.GetValue(target) : null;
        }

        private static void Set(Type t, string name, object value)
        {
            if (t == null) return;
            var f = t.GetField(name, Any);
            if (f != null) { f.SetValue(null, value); return; }
            var p = t.GetProperty(name, Any);
            if (p != null && p.CanWrite) p.SetValue(null, value);
        }

        private static double Num(object o) => o is IConvertible c ? c.ToDouble(null) : 0.0;

        /// <summary>Spare tiles may be built in quiet frames (only after the start terrain is ready).</summary>
        public static bool PrewarmAllowed { set { Look(); Set(tileDiag, "PrewarmAllowed", value); } }

        /// <summary>Time the tile grid needed to deploy (ms), 0 without the changes.</summary>
        public static double DeployMs { get { Look(); return Num(Get(tileDiag, "DeployMs")); } }

        /// <summary>One line of tile-grid and MapMagic main-thread timing for -hitch, "" without the changes.</summary>
        public static string HitchLine()
        {
            Look();
            if (tileDiag == null) return "";
            string s = $"MapMagic {Num(Get(coroutines, "FrameMs")):0} ms (längster Schritt {Num(Get(coroutines, "SlowestStepMs")):0} ms {Get(coroutines, "SlowestStep")}) · " +
                       $"Raster: Deploy {Num(Get(tileDiag, "DeployMs")):0}, Dists {Num(Get(tileDiag, "DistsMs")):0}, Move {Num(Get(tileDiag, "MoveMs")):0}, " +
                       $"Weld {Num(Get(tileDiag, "WeldMs")):0}, StartGen {Num(Get(tileDiag, "StartGenMs")):0}, LOD {Num(Get(tileDiag, "LodMs")):0} [{Get(tileDiag, "Sections")}]";
            tileDiag.GetMethod("Reset", Any)?.Invoke(null, null);
            return s;
        }

        /// <summary>Drop MapMagic's queued generator work (scene change). Without the changes: nothing to do.</summary>
        public static void ClearQueue() { Look(); threads?.GetMethod("ClearQueue", Any, null, Type.EmptyTypes, null)?.Invoke(null, null); }

        /// <summary>Stop generating before the terrains are freed (the method is private in the original).</summary>
        public static void StopGenerate(object mapMagic) =>
            mapMagic?.GetType().GetMethod("StopGenerate", Any, null, Type.EmptyTypes, null)?.Invoke(mapMagic, null);

        /// <summary>Main-thread time a generator node took (ms), 0 without the changes.</summary>
        public static double MainTime(object generator) => generator == null ? 0.0 : Num(Get(generator.GetType(), "mainTime", generator));
    }
}
