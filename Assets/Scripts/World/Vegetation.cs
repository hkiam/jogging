using System.Collections.Generic;
using UnityEngine;
using Jogging.Route;

namespace Jogging.World
{
    /// <summary>
    /// Vegetation plan along the route: the course is split into sections (≈ 0.5–1.3 km) of a kind —
    /// mixed forest, pines, birches, clearing or meadow — drawn from the route's vegetation type,
    /// clearing share and seed (deterministic). <see cref="At"/> returns the blended mix at an arc
    /// length (sections fade into each other over ~80 m); scatterers scale their counts and pick
    /// pine vs broadleaf by it.
    /// </summary>
    public static class Vegetation
    {
        public struct Mix
        {
            public float trees;       // tree density factor
            public float pine;        // share of pines among trees
            public float undergrowth; // bush/plant density factor
        }

        private enum Kind { Forest, Pines, Birches, Clearing, Meadow }
        private static readonly Mix[] Mixes =
        {
            new Mix { trees = 1.00f, pine = 0.45f, undergrowth = 0.8f }, // Forest (mixed)
            new Mix { trees = 1.05f, pine = 0.92f, undergrowth = 0.5f }, // Pines
            new Mix { trees = 0.85f, pine = 0.06f, undergrowth = 0.9f }, // Birches
            new Mix { trees = 0.10f, pine = 0.50f, undergrowth = 1.1f }, // Clearing
            new Mix { trees = 0.18f, pine = 0.25f, undergrowth = 1.3f }, // Meadow (with a few groves)
        };

        private static string key;
        private static readonly List<float> starts = new List<float>();
        private static readonly List<Kind> kinds = new List<Kind>();
        private static float length, density = 0.7f;
        private static bool loop;

        private const float Blend = 80f;

        public static Mix At(float s)
        {
            var doc = RouteRuntime.Current;
            if (doc == null) return Mixes[0];
            Ensure(doc);
            if (loop) s = Mathf.Repeat(s, length);
            int i = Index(s);
            Mix m = Mixes[(int)kinds[i]];
            // Fade into the next / from the previous section.
            float end = i + 1 < starts.Count ? starts[i + 1] : (loop ? length : float.MaxValue);
            float next = end - s, prev = s - starts[i];
            if (next < Blend * 0.5f) m = Lerp(m, Mixes[(int)kinds[(i + 1) % kinds.Count]], 0.5f - next / Blend);
            else if (prev < Blend * 0.5f && (i > 0 || loop)) m = Lerp(m, Mixes[(int)kinds[(i - 1 + kinds.Count) % kinds.Count]], 0.5f - prev / Blend);
            float d = 0.3f + 1.1f * density;
            m.trees *= d;
            m.undergrowth *= 0.6f + 0.6f * density;
            return m;
        }

        /// <summary>
        /// Workshop tree-density factor at s on one side (lateral sign: &lt; 0 left, &gt; 0 right);
        /// edits fade in/out over 30 m so a clearing doesn't start with a hard line.
        /// </summary>
        public static float EditFactor(float s, float lateral)
        {
            var doc = RouteRuntime.Current;
            if (doc == null || doc.edits == null || doc.edits.Count == 0) return 1f;
            float f = 1f;
            bool loopRoute = doc.@params.loop && !doc.@params.endless;
            float L = RouteGenerator.LengthM(doc.@params);
            if (loopRoute) s = Mathf.Repeat(s, L);
            foreach (var e in doc.edits)
            {
                if (e.type != "trees") continue;
                if (e.side == "left" && lateral > 0f || e.side == "right" && lateral < 0f) continue;
                float edge = Mathf.Min(s - e.fromM, e.toM - s);
                if (edge < -15f) continue;
                float w = Mathf.Clamp01((edge + 15f) / 30f);
                f *= Mathf.Lerp(1f, e.factor, w);
            }
            return f;
        }

        private static Mix Lerp(Mix a, Mix b, float t) => new Mix
        {
            trees = Mathf.Lerp(a.trees, b.trees, t), pine = Mathf.Lerp(a.pine, b.pine, t),
            undergrowth = Mathf.Lerp(a.undergrowth, b.undergrowth, t),
        };

        private static int Index(float s)
        {
            int lo = 0, hi = starts.Count - 1;
            while (lo < hi) { int mid = (lo + hi + 1) / 2; if (starts[mid] <= s) lo = mid; else hi = mid - 1; }
            return lo;
        }

        private static void Ensure(RouteDoc doc)
        {
            var p = doc.@params;
            string k = doc.generator.seed + "|" + p.vegetation + "|" + p.clearings + "|" + p.density + "|" + p.lengthKm + "|" + p.loop + "|" + p.endless;
            if (k == key) return;
            key = k;
            loop = p.loop && !p.endless;
            length = RouteGenerator.LengthM(p);
            density = Mathf.Clamp01(p.density);
            starts.Clear(); kinds.Clear();

            // Weights per vegetation type (+ clearings share).
            float c = Mathf.Clamp01(p.clearings) * 0.5f;
            float[] w;
            switch (p.vegetation)
            {
                case "conifer": w = new[] { 0.15f, 0.70f, 0.02f, c, 0.03f }; break;
                case "birch":   w = new[] { 0.15f, 0.03f, 0.65f, c, 0.07f }; break;
                case "meadow":  w = new[] { 0.05f, 0.03f, 0.15f, c, 0.60f }; break;
                default:        w = new[] { 0.45f, 0.15f, 0.15f, c, 0.10f }; break; // mixed
            }
            var rng = new System.Random(doc.generator.seed * 31 + 7);
            float s0 = loop ? 0f : -400f, s1 = loop ? length : length + 400f;
            Kind last = (Kind)(-1);
            for (float s = s0; s < s1;)
            {
                Kind kind;
                int guard = 0;
                do { kind = Pick(w, rng); } while (kind == last && guard++ < 4); // vary neighbours
                starts.Add(s); kinds.Add(kind); last = kind;
                s += Mathf.Lerp(500f, 1300f, (float)rng.NextDouble());
            }
            // Start in something wooded (the first view matters), unless it is a meadow route.
            if (p.vegetation != "meadow" && (kinds[0] == Kind.Clearing || kinds[0] == Kind.Meadow))
                kinds[0] = p.vegetation == "conifer" ? Kind.Pines : p.vegetation == "birch" ? Kind.Birches : Kind.Forest;
        }

        private static Kind Pick(float[] w, System.Random rng)
        {
            float sum = 0f; foreach (var x in w) sum += x;
            float r = (float)rng.NextDouble() * sum;
            for (int i = 0; i < w.Length; i++) { r -= w[i]; if (r <= 0f) return (Kind)i; }
            return Kind.Forest;
        }
    }
}
