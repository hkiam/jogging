using System;
using System.Collections.Generic;
using UnityEngine;

namespace Jogging.Route
{
    /// <summary>
    /// Turns route parameters + seed into a course, deterministically (only System.Random with
    /// the route's seed; no time, no frame order):
    ///   • <see cref="Path"/>: centre line in metres on the ground plane, one point per metre,
    ///     starting at the origin heading +Z. Open courses progress along +Z (heading &lt; 90°),
    ///     loops are star-shaped closed curves — neither can cross itself.
    ///   • <see cref="Profile"/>: height (m, relative to the start) every 2 m, made of the requested
    ///     climbs + rolling terrain, then limited to a runnable grade and grade change; loops end at
    ///     the start height.
    /// </summary>
    public static class RouteGenerator
    {
        public const string Version = "1.0";
        public const float PathStep = 1f;
        public const float ProfileStep = 2f;

        /// <summary>Endless free runs are generated this long (open course; the trail continues straight after).</summary>
        public const float EndlessLengthKm = 42f;

        public static float LengthM(RouteParams p) => (p.endless ? EndlessLengthKm : Mathf.Max(0.5f, p.lengthKm)) * 1000f;

        // ------------------------------------------------------------------ path

        public static List<Vector2> Path(RouteParams p, int seed)
        {
            float L = LengthM(p);
            var rng = new System.Random(seed * 7919 + 17);
            return p.loop && !p.endless ? LoopPath(L, Mathf.Clamp01(p.curviness), rng) : OpenPath(L, Mathf.Clamp01(p.curviness), rng);
        }

        private static List<Vector2> OpenPath(float L, float curviness, System.Random rng)
        {
            // Heading = slow sine waves; |heading| ≤ 0.95 rad (< 90°) → always progresses along +Z.
            float a = 0.95f * curviness;
            float[] amp = { 0.58f * a, 0.29f * a, 0.13f * a };
            float[] wl = { 170f, 61f, 27f };
            float[] ph = { R(rng) * 6.283f, R(rng) * 6.283f, R(rng) * 6.283f };
            const float straight = 40f;

            int n = Mathf.CeilToInt(L / PathStep) + 1;
            var pts = new List<Vector2>(n) { Vector2.zero };
            for (int i = 1; i < n; i++)
            {
                float s = (i - 0.5f) * PathStep, h = 0f;
                if (s > straight)
                {
                    float u = s - straight, fade = Mathf.Clamp01(u / 60f);
                    for (int k = 0; k < 3; k++) h += amp[k] * Mathf.Sin(u / wl[k] + ph[k]);
                    h *= fade;
                }
                pts.Add(pts[i - 1] + new Vector2(Mathf.Sin(h), Mathf.Cos(h)) * PathStep);
            }
            return pts;
        }

        private static List<Vector2> LoopPath(float L, float curviness, System.Random rng)
        {
            // r(θ) = 1 + Σ b_k sin(kθ + φ_k): star-shaped (r > 0 for every θ) → a simple closed curve.
            float[] b = { 0.16f * curviness, 0.08f * curviness, 0.045f * curviness, 0.025f * curviness };
            float[] ph = new float[b.Length];
            for (int k = 0; k < b.Length; k++) ph[k] = R(rng) * 6.283f;
            bool ccw = rng.NextDouble() < 0.5;

            const int N = 8192;
            var raw = new Vector2[N + 1];
            for (int i = 0; i <= N; i++)
            {
                float t = i * 6.2831853f / N, r = 1f;
                for (int k = 0; k < b.Length; k++) r += b[k] * Mathf.Sin((k + 2) * t + ph[k]);
                float th = ccw ? t : -t;
                raw[i] = new Vector2(Mathf.Cos(th), Mathf.Sin(th)) * r;
            }
            // Scale to the requested length.
            float per = 0f;
            for (int i = 0; i < N; i++) per += (raw[i + 1] - raw[i]).magnitude;
            float scale = L / per;
            for (int i = 0; i <= N; i++) raw[i] *= scale;

            // Resample every PathStep metres by arc length.
            int n = Mathf.RoundToInt(L / PathStep);
            var pts = new List<Vector2>(n);
            float acc = 0f; int seg = 0;
            for (int j = 0; j < n; j++)
            {
                float target = j * (L / n);
                while (seg < N - 1 && acc + (raw[seg + 1] - raw[seg]).magnitude < target) { acc += (raw[seg + 1] - raw[seg]).magnitude; seg++; }
                float len = (raw[seg + 1] - raw[seg]).magnitude;
                pts.Add(Vector2.Lerp(raw[seg], raw[seg + 1], len > 0f ? (target - acc) / len : 0f));
            }

            // Small periodic wiggle along the normal (an integer number of waves → still closed).
            int waves = Mathf.Max(1, Mathf.RoundToInt(L / 95f));
            float wig = 3.5f * curviness, wph = R(rng) * 6.283f;
            var outPts = new List<Vector2>(n);
            for (int j = 0; j < n; j++)
            {
                Vector2 d = pts[(j + 1) % n] - pts[(j - 1 + n) % n];
                Vector2 nrm = new Vector2(d.y, -d.x).normalized;
                outPts.Add(pts[j] + nrm * (wig * Mathf.Sin(j * 6.2831853f * waves / n + wph)));
            }

            // Start at the origin, heading +Z.
            Vector2 p0 = outPts[0], t0 = (outPts[1] - outPts[0]).normalized;
            float ang = Mathf.Atan2(t0.x, t0.y); // angle from +Z
            float cs = Mathf.Cos(ang), sn = Mathf.Sin(ang);
            for (int j = 0; j < n; j++)
            {
                Vector2 q = outPts[j] - p0;
                outPts[j] = new Vector2(q.x * cs - q.y * sn, q.x * sn + q.y * cs);
            }
            return outPts; // closed: point n would be point 0
        }

        // ------------------------------------------------------------------ profile

        public static float[] Profile(RouteParams p, int seed)
        {
            float L = LengthM(p);
            bool loop = p.loop && !p.endless;
            var rng = new System.Random(seed * 104729 + 3);
            int n = Mathf.FloorToInt(L / ProfileStep) + 1;
            float maxG = Mathf.Clamp(p.maxGrade, 0.02f, 0.15f);

            // 1) Designed elevation: climbs as smooth steps + rolling waves.
            var climbs = new List<RouteClimb>(p.climbs ?? new List<RouteClimb>());
            if (p.endless) climbs = EndlessClimbs(L, p, rng);
            if (loop) AddLoopCompensation(climbs, L, maxG);

            float[] wl = { 620f, 240f, 95f };
            float[] ph = { R(rng) * 6.283f, R(rng) * 6.283f, R(rng) * 6.283f };
            if (loop) for (int k = 0; k < 3; k++) wl[k] = L / Mathf.Max(1, Mathf.Round(L / wl[k])); // periodic
            float A = Mathf.Clamp01(p.rolling) * 7f; // rolling stays secondary to the explicit climbs
            float flat = Mathf.Max(0f, p.flatStartEndM);

            var e = new float[n];
            for (int i = 0; i < n; i++)
            {
                float s = i * ProfileStep;
                float sr = loop ? s : Mathf.Clamp(s, flat, Mathf.Max(flat, L - flat)); // flat run-in / run-out
                float h = A * (0.6f * Mathf.Sin(6.2831853f * sr / wl[0] + ph[0])
                             + 0.3f * Mathf.Sin(6.2831853f * sr / wl[1] + ph[1])
                             + 0.1f * Mathf.Sin(6.2831853f * sr / wl[2] + ph[2]));
                foreach (var c in climbs)
                {
                    float g = Mathf.Clamp(Mathf.Abs(c.grade), 0.02f, maxG);
                    float len = Mathf.Max(40f, Mathf.Abs(c.heightM) / (0.75f * g)); // mean grade ≈ 0.75 × peak
                    float u = (s - (c.atKm * 1000f - len * 0.5f)) / len;
                    h += c.heightM * Smoother(Mathf.Clamp01(u));
                }
                e[i] = h;
            }
            if (loop) // rolling may not start exactly at 0; the loop must end at the start height
            {
                float e0 = e[0], e1 = e[n - 1];
                for (int i = 0; i < n; i++) e[i] -= e0 + (e1 - e0) * i / (n - 1f);
            }
            else { float e0 = e[0]; for (int i = 0; i < n; i++) e[i] -= e0; }

            // 2) Runnable: grade ≤ maxG, grade change ≤ ramp per metre (treadmill can follow).
            const float ramp = 0.002f, lookahead = 50f;
            int la = Mathf.RoundToInt(lookahead / ProfileStep);
            var prof = new float[n];
            float grade = 0f;
            for (int i = 1; i < n; i++)
            {
                float want = (e[Mathf.Min(n - 1, i - 1 + la)] - prof[i - 1]) / lookahead;
                grade = Mathf.Clamp(Mathf.Clamp(want, grade - ramp * ProfileStep, grade + ramp * ProfileStep), -maxG, maxG);
                prof[i] = prof[i - 1] + grade * ProfileStep;
            }
            if (loop) // close exactly; spreads a small residual over the whole loop
            {
                float r = prof[n - 1];
                for (int i = 0; i < n; i++) prof[i] -= r * i / (n - 1f);
            }
            return prof;
        }

        /// <summary>Fill in the generated profile + its stats.</summary>
        public static void Generate(RouteDoc doc)
        {
            doc.generator.version = Version;
            var h = Profile(doc.@params, doc.generator.seed);
            doc.profile.stepM = ProfileStep;
            doc.profile.heightsM = h;
            float up = 0f, down = 0f, mg = 0f;
            for (int i = 1; i < h.Length; i++)
            {
                float d = h[i] - h[i - 1];
                if (d > 0) up += d; else down -= d;
                mg = Mathf.Max(mg, Mathf.Abs(d) / ProfileStep * 100f);
            }
            doc.profile.ascentM = up; doc.profile.descentM = down; doc.profile.maxGradePercent = mg;
        }

        // Free run: a climb up or down every ~1.5–3 km, balanced so the height stays within ± 60 m.
        private static List<RouteClimb> EndlessClimbs(float L, RouteParams p, System.Random rng)
        {
            var list = new List<RouteClimb>();
            float at = 1.2f, level = 0f;
            while (at * 1000f < L - 500f)
            {
                float h = Mathf.Lerp(12f, 40f, R(rng)) * Mathf.Lerp(0.5f, 1.2f, Mathf.Clamp01(p.rolling + 0.3f));
                float sign = level > 40f ? -1f : level < -40f ? 1f : (rng.NextDouble() < 0.55 ? 1f : -1f);
                level += sign * h;
                list.Add(new RouteClimb { atKm = at, heightM = sign * h, grade = Mathf.Lerp(0.04f, p.maxGrade * 0.85f, R(rng)) });
                at += Mathf.Lerp(1.5f, 3f, R(rng));
            }
            return list;
        }

        // A loop must come back down: every climb becomes a hill — the matching descent (or climb,
        // for a descent) goes halfway to the next climb. Never right at the start/finish (keep 300 m +
        // half the ramp); a descent that would fall there moves to just before the finish.
        private static void AddLoopCompensation(List<RouteClimb> climbs, float L, float maxG)
        {
            if (climbs.Count == 0) return;
            var sorted = new List<RouteClimb>(climbs);
            sorted.Sort((x, y) => x.atKm.CompareTo(y.atKm));
            for (int i = 0; i < sorted.Count; i++)
            {
                var c = sorted[i];
                float a = c.atKm * 1000f, b = i + 1 < sorted.Count ? sorted[i + 1].atKm * 1000f : sorted[0].atKm * 1000f + L;
                float g = Mathf.Min(Mathf.Max(c.grade, 0.04f), maxG * 0.8f);
                float half = Mathf.Abs(c.heightM) / (0.75f * g) * 0.5f, m = Mathf.Min(300f + half, L * 0.3f);
                float mid = (a + b) * 0.5f;
                if (mid >= L) mid -= L;
                if (mid < m) mid = mid < m * 0.5f && b > L ? L - m : m;   // near the start → just before the finish
                mid = Mathf.Clamp(mid, m, L - m);
                climbs.Add(new RouteClimb { atKm = mid / 1000f, heightM = -c.heightM, grade = g });
            }
        }

        private static float Smoother(float x) => x * x * x * (x * (x * 6f - 15f) + 10f);
        private static float R(System.Random r) => (float)r.NextDouble();
    }
}
