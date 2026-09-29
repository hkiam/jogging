using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Jogging.Route;

namespace Jogging.EditorTools
{
    /// <summary>
    /// Self-check of the route generator for every preset and a few seeds: deterministic (two runs
    /// identical), length as requested, loops closed (position, heading, height), no self-crossing,
    /// grade within the limit, and a .jogroute save/load round trip (with workshop edits).
    /// Menu: Jogging → Strecken → Generator prüfen. Headless: -executeMethod Jogging.EditorTools.RouteGeneratorCheck.RunBatch
    /// </summary>
    public static class RouteGeneratorCheck
    {
        [MenuItem("Jogging/Strecken/Generator prüfen")]
        public static void RunMenu() => EditorUtility.DisplayDialog("Jogging", Run() ? "Alle Prüfungen bestanden." : "Fehler – siehe Console.", "OK");

        public static void RunBatch() => EditorApplication.Exit(Run() ? 0 : 1);

        public static bool Run()
        {
            bool ok = true;
            foreach (var preset in RoutePresets.All)
            foreach (int seed in new[] { 1, 42, 9001 })
            {
                var a = RoutePresets.Create(preset, seed);
                var b = RoutePresets.Create(preset, seed);
                var pa = RouteGenerator.Path(a.@params, seed);
                var pb = RouteGenerator.Path(b.@params, seed);
                var fails = new List<string>();
                if (seed == 1) Debug.Log($"[RouteGeneratorCheck] {preset,-10} {a.@params.lengthKm,4:0.0} km  ↑{a.profile.ascentM,4:0} m  max {a.profile.maxGradePercent,4:0.0} %  {(a.@params.loop ? "Runde" : a.@params.endless ? "endlos" : "A→B")}");

                // Deterministic
                if (pa.Count != pb.Count) fails.Add("Pfad nicht deterministisch (Länge)");
                else for (int i = 0; i < pa.Count; i += 97) if (pa[i] != pb[i]) { fails.Add("Pfad nicht deterministisch"); break; }
                var ha = a.profile.heightsM; var hb = b.profile.heightsM;
                if (ha.Length != hb.Length) fails.Add("Profil nicht deterministisch (Länge)");
                else for (int i = 0; i < ha.Length; i++) if (ha[i] != hb[i]) { fails.Add("Profil nicht deterministisch"); break; }

                // Length
                float L = RouteGenerator.LengthM(a.@params);
                bool loop = a.@params.loop && !a.@params.endless;
                float pathLen = 0f;
                for (int i = 1; i < pa.Count; i++) pathLen += (pa[i] - pa[i - 1]).magnitude;
                if (loop) pathLen += (pa[0] - pa[pa.Count - 1]).magnitude;
                if (Mathf.Abs(pathLen - L) > L * 0.01f) fails.Add($"Länge {pathLen:0} m statt {L:0} m");

                // Loop closure (position gap ≈ one step, heading continuous, height back at 0)
                if (loop)
                {
                    float gap = (pa[0] - pa[pa.Count - 1]).magnitude;
                    if (gap > 2f) fails.Add($"Rundkurs offen ({gap:0.0} m)");
                    Vector2 tEnd = (pa[0] - pa[pa.Count - 1]).normalized, tStart = (pa[1] - pa[0]).normalized;
                    if (Vector2.Angle(tEnd, tStart) > 8f) fails.Add($"Knick am Start ({Vector2.Angle(tEnd, tStart):0}°)");
                    if (Mathf.Abs(ha[ha.Length - 1] - ha[0]) > 0.5f) fails.Add($"Höhe am Ziel {ha[ha.Length - 1]:0.0} m ≠ Start");
                }
                if (pa[0] != Vector2.zero || (pa[1] - pa[0]).normalized.y < 0.99f) fails.Add("Start nicht im Ursprung Richtung +Z");

                // Grade limit
                float maxG = a.@params.maxGrade * 100f + 0.05f;
                if (a.profile.maxGradePercent > maxG) fails.Add($"Steigung {a.profile.maxGradePercent:0.0} % > {maxG:0.0} %");

                // Min radius of curvature (≥ 25 m) and self-crossing
                float minR = MinRadius(pa, loop);
                if (minR < 25f) fails.Add($"Kurve zu eng (r = {minR:0} m)");
                if (SelfCrosses(pa, loop)) fails.Add("Strecke kreuzt sich selbst");

                // File round trip (incl. workshop edits), and edits survive a parameter change
                a.edits.Add(new RouteEdit { type = "trees", fromM = 100f, toM = 300f, side = "left", factor = 0.05f });
                a.edits.Add(new RouteEdit { type = "spectators", atM = 500f, count = 12 });
                var back = RouteStore.FromJson(RouteStore.ToJson(a));
                if (back == null || back.profile.heightsM.Length != ha.Length || back.@params.climbs.Count != a.@params.climbs.Count
                    || back.edits.Count != 2 || back.edits[0].side != "left" || back.edits[1].atM != 500f)
                    fails.Add(".jogroute Round-Trip fehlerhaft");
                if (back != null)
                {
                    back.@params.lengthKm += 1f; RouteGenerator.Generate(back);
                    if (back.edits.Count != 2) fails.Add("Anpassungen gehen bei Parameter-Änderung verloren");
                }

                // Share code round trip: same course (profile regenerated identically), edits, id.
                a.id = RouteStore.NewId();
                string code = RouteShare.ToCode(a);
                var shared = RouteShare.FromCode(code, out bool match, out string err);
                if (shared == null) fails.Add("Teilen-Code nicht lesbar: " + err);
                else
                {
                    if (!match) fails.Add("Teilen-Code: Profil weicht ab");
                    if (shared.id != a.id || shared.edits.Count != a.edits.Count || shared.profile.heightsM.Length != ha.Length) fails.Add("Teilen-Code: Inhalt fehlt");
                    else for (int i = 0; i < ha.Length; i += 11) if (Mathf.Abs(shared.profile.heightsM[i] - ha[i]) > 1e-4f) { fails.Add("Teilen-Code: Profil anders"); break; }
                }
                if (code.Length > 4000) fails.Add($"Teilen-Code zu lang ({code.Length})");

                string info = $"{a.meta.name,-18} seed {seed,5}: code {code.Length} Zeichen, {L / 1000f:0.0} km {(loop ? "Rundkurs" : "Strecke")}, ↑{a.profile.ascentM:0} m ↓{a.profile.descentM:0} m, max {a.profile.maxGradePercent:0.0} %, rMin {minR:0} m";
                if (fails.Count == 0) Debug.Log("[RouteCheck] OK   " + info);
                else { ok = false; Debug.LogError("[RouteCheck] FAIL " + info + " → " + string.Join("; ", fails)); }
            }
            Debug.Log(ok ? "[RouteCheck] Alle Prüfungen bestanden." : "[RouteCheck] Es gibt Fehler.");
            return ok;
        }

        private static float MinRadius(List<Vector2> p, bool loop)
        {
            float min = float.MaxValue;
            int n = p.Count, span = 10; // over 10 m chords
            for (int i = loop ? 0 : span; i < (loop ? n : n - span); i++)
            {
                Vector2 a = p[(i - span + n) % n], b = p[i], c = p[(i + span) % n];
                float area2 = Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));
                if (area2 < 1e-4f) continue;
                float r = (b - a).magnitude * (c - b).magnitude * (c - a).magnitude / (2f * area2);
                min = Mathf.Min(min, r);
            }
            return min;
        }

        // Coarse check (every 5 m) with a grid, skipping neighbours along the course.
        private static bool SelfCrosses(List<Vector2> p, bool loop)
        {
            var grid = new Dictionary<(int, int), List<int>>();
            const float cell = 20f;
            for (int i = 0; i < p.Count; i += 5)
            {
                var k = ((int)Mathf.Floor(p[i].x / cell), (int)Mathf.Floor(p[i].y / cell));
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    if (grid.TryGetValue((k.Item1 + dx, k.Item2 + dy), out var l))
                        foreach (int j in l)
                        {
                            int along = Mathf.Abs(i - j);
                            if (loop) along = Mathf.Min(along, p.Count - along);
                            if (along > 60 && (p[i] - p[j]).magnitude < 8f) return true;
                        }
                if (!grid.TryGetValue(k, out var own)) grid[k] = own = new List<int>();
                own.Add(i);
            }
            return false;
        }
    }
}
