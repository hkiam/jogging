using System.Collections.Generic;
using System.Diagnostics;

namespace Jogging.World
{
    /// <summary>
    /// Diagnostics (-hitch): main-thread time of our own per-frame work, by name — so a slow frame
    /// can be pinned on a script (see <see cref="FrameStats"/>). Costs nothing when off.
    /// </summary>
    public static class FrameWork
    {
        public static bool On;
        private static readonly Dictionary<string, double> ms = new Dictionary<string, double>();

        public static long Start() => On ? Stopwatch.GetTimestamp() : 0;

        public static void Stop(string name, long start)
        {
            if (!On) return;
            double d = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            ms.TryGetValue(name, out double v);
            ms[name] = v + d;
        }

        /// <summary>The frame's entries over 2 ms, largest first ("Bäume 31 · Gras 4"), then cleared.</summary>
        public static string TakeFrame()
        {
            if (ms.Count == 0) return "";
            var list = new List<KeyValuePair<string, double>>(ms);
            ms.Clear();
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            var sb = new System.Text.StringBuilder();
            foreach (var kv in list)
            {
                if (kv.Value < 2.0) break;
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(kv.Key).Append(' ').Append(kv.Value.ToString("0"));
            }
            return sb.ToString();
        }
    }
}
