using System.Collections.Generic;
using UnityEngine;
using Jogging.Route;

namespace Jogging.World
{
    /// <summary>
    /// What the trail is made of, section by section, matching its surroundings: asphalt through the
    /// village stretch, gravel or earth in mixed forest, forest floor (needles, roots) among pines, a
    /// meadow path or a gravel field track in open country. Sections are at least ~200 m; long stretches
    /// change once. Each surface has its width (asphalt 3.4 m … forest path 2.5 m); both blend over 12 m.
    /// Planned per route (deterministic), read by <see cref="TrailRibbon"/> (weights, width),
    /// <see cref="TrailGrassClearer"/> (width) and the wayside (no mud on asphalt).
    /// </summary>
    public static class TrailSurface
    {
        public enum Kind { Asphalt = 0, Gravel = 1, Earth = 2, Forest = 3, Meadow = 4 }
        public const int Count = 5;
        private static readonly float[] Widths = { 3.4f, 3.2f, 2.8f, 2.5f, 2.6f }; // room for a few runners side by side
        private const float Blend = 12f;

        private static readonly List<float> starts = new List<float>();
        private static readonly List<Kind> kinds = new List<Kind>();
        private static float length;
        private static bool loop;

        public static void Plan(RouteDoc doc, TrailPath path, List<Wayside.Feature> features)
        {
            starts.Clear(); kinds.Clear();
            if (doc == null || path == null) return;
            length = path.Length; loop = path.Loop;
            float end = doc.@params.endless ? Mathf.Min(length, 30000f) : length;
            var rnd = new System.Random(doc.generator.seed * 13 + 71);
            float R() => (float)rnd.NextDouble();
            float villageEnd = -1f;
            foreach (var f in features) if (f.kind == Wayside.Kind.Village) villageEnd = f.s + f.length + 120f;

            int Context(float s) { var m = Vegetation.At(s); return m.trees < 0.3f ? 0 : m.pine > 0.55f ? 1 : 2; } // open, pines, forest
            Kind Choose(int c, Kind avoid)
            {
                for (int t = 0; t < 4; t++)
                {
                    Kind k = c == 0 ? (R() < 0.55f ? Kind.Meadow : Kind.Gravel) : c == 1 ? (R() < 0.6f ? Kind.Forest : Kind.Gravel) : (R() < 0.55f ? Kind.Earth : Kind.Gravel);
                    if (k != avoid) return k;
                }
                return c == 0 ? Kind.Meadow : c == 1 ? Kind.Forest : Kind.Earth;
            }
            float s0 = 0f;
            if (villageEnd > 0f) { starts.Add(0f); kinds.Add(Kind.Asphalt); s0 = villageEnd; }
            while (s0 < end)
            {
                int c = Context(s0 + 30f);
                float run = 0f;
                while (s0 + run < end && (Context(s0 + run + 30f) == c || run < 200f)) run += 50f;
                Kind prev = kinds.Count > 0 ? kinds[kinds.Count - 1] : Kind.Asphalt;
                if (run > 1200f)
                {
                    float cut = run * (0.35f + R() * 0.3f);
                    Add(s0, Choose(c, prev)); Add(s0 + cut, Choose(c, kinds[kinds.Count - 1]));
                }
                else Add(s0, Choose(c, prev));
                s0 += run;
            }
            if (starts.Count == 0) Add(0f, Kind.Gravel);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < starts.Count; i++) sb.Append($"{starts[i]:0} m {kinds[i]}, ");
            Debug.Log($"[Weg] Beläge: {sb}");
        }

        private static void Add(float s, Kind k)
        {
            if (kinds.Count > 0 && kinds[kinds.Count - 1] == k) return; // same as before: one section
            starts.Add(s); kinds.Add(k);
        }

        private static int Index(float s)
        {
            if (loop && length > 0f) s = Mathf.Repeat(s, length);
            int lo = 0, hi = starts.Count - 1;
            while (lo < hi) { int mid = (lo + hi + 1) / 2; if (starts[mid] <= s) lo = mid; else hi = mid - 1; }
            return lo;
        }

        /// <summary>The surface at s (the dominant one where two meet).</summary>
        public static Kind At(float s) => starts.Count == 0 ? Kind.Gravel : kinds[Index(s)];

        /// <summary>Weights of the five surfaces at s (sum 1), blended over 12 m at section ends.</summary>
        public static void Weights(float s, float[] w)
        {
            for (int k = 0; k < Count; k++) w[k] = 0f;
            if (starts.Count == 0) { w[(int)Kind.Gravel] = 1f; return; }
            float sl = loop && length > 0f ? Mathf.Repeat(s, length) : s;
            int i = Index(sl);
            float t = 1f;
            // towards the next section
            if (i + 1 < starts.Count || loop)
            {
                float next = i + 1 < starts.Count ? starts[i + 1] : length;
                float d = next - sl;
                if (d < Blend * 0.5f) { float b = 0.5f - d / Blend; w[(int)kinds[(i + 1) % kinds.Count]] += b; t -= b; }
            }
            // from the previous one
            if (i > 0 || loop)
            {
                float d = sl - starts[i];
                if (d < Blend * 0.5f && (i > 0 || starts.Count > 1)) { float b = 0.5f - d / Blend; w[(int)kinds[(i - 1 + kinds.Count) % kinds.Count]] += b; t -= b; }
            }
            w[(int)kinds[i]] += t;
        }

        private static readonly float[] tmp = new float[Count];

        /// <summary>Half the trail width at s (blended between surfaces).</summary>
        public static float HalfWidth(float s)
        {
            Weights(s, tmp);
            float wdt = 0f;
            for (int k = 0; k < Count; k++) wdt += tmp[k] * Widths[k];
            return wdt * 0.5f;
        }
    }
}
