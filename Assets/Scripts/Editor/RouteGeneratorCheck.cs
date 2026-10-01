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
            ok &= GpxChecks();
            Debug.Log(ok ? "[RouteCheck] Alle Prüfungen bestanden." : "[RouteCheck] Es gibt Fehler.");
            return ok;
        }

        // GPX import: a loop around a hill, an out-and-back route plan without heights, broken files
        private static bool GpxChecks()
        {
            var fails = new List<string>();
            string Gpx(string body) => "<?xml version=\"1.0\"?><gpx version=\"1.1\" xmlns=\"http://www.topografix.com/GPX/1/1\">" +
                                       "<wpt lat=\"50\" lon=\"8\"><name>Parkplatz</name></wpt>" + body + "</gpx>";
            string F(double v) => v.ToString("0.0000000", System.Globalization.CultureInfo.InvariantCulture);

            // 1) a loop of r = 500 m (3.14 km) with one 30 m hill, recorded in July
            var sb = new System.Text.StringBuilder("<trk><name>Hausrunde</name><trkseg>");
            for (int i = 0; i <= 600; i++)
            {
                double t = i / 600.0 * 2 * System.Math.PI;
                double lat = 50 + 500 * System.Math.Sin(t) / 111320.0, lon = 8 + 500 * (1 - System.Math.Cos(t)) / (111320.0 * System.Math.Cos(50 * System.Math.PI / 180));
                double ele = 200 + 30 * System.Math.Pow(System.Math.Sin(t / 2), 2) + (i % 2 == 0 ? 1.5 : -1.5); // GPS noise
                sb.Append($"<trkpt lat=\"{F(lat)}\" lon=\"{F(lon)}\"><ele>{ele:0.0}</ele><time>2026-07-14T07:{i / 60 % 60:00}:{i % 60:00}Z</time></trkpt>");
            }
            sb.Append("</trkseg></trk>");
            string xml = Gpx(sb.ToString());
            var d = GpxImport.FromXml(xml, "hausrunde", out string err);
            if (d == null) fails.Add("GPX Runde nicht gelesen: " + err);
            else
            {
                var p = d.@params;
                if (d.meta.name != "Hausrunde") fails.Add($"GPX: Name „{d.meta.name}“");
                if (!p.loop) fails.Add("GPX: Runde nicht als Rundkurs erkannt");
                if (Mathf.Abs(p.lengthKm - 3.1416f) > 0.05f) fails.Add($"GPX: Länge {p.lengthKm:0.000} km statt 3,142");
                if (p.season != "summer") fails.Add($"GPX: Jahreszeit {p.season} statt summer");
                float top = 0f; foreach (var h in d.profile.heightsM) top = Mathf.Max(top, h);
                if (Mathf.Abs(top - 30f) > 4f) fails.Add($"GPX: Gipfel {top:0.0} m statt 30 m");
                if (d.profile.ascentM > 36f || d.profile.ascentM < 24f) fails.Add($"GPX: ↑{d.profile.ascentM:0.0} m statt ≈ 30 m (Rauschen nicht geglättet?)");
                if (Mathf.Abs(d.profile.heightsM[d.profile.heightsM.Length - 1]) > 0.5f) fails.Add("GPX: Runde endet nicht auf Starthöhe");
                if (p.curviness < 0.15f || p.curviness > 0.6f) fails.Add($"GPX: Kurvigkeit {p.curviness:0.00}");
                if (!RouteStore.IsValidId(d.id)) fails.Add($"GPX: ungültige Id {d.id}");
                var again = GpxImport.FromXml(xml, "hausrunde", out _);
                if (again == null || again.id != d.id || again.generator.seed != d.generator.seed) fails.Add("GPX: zweimal dieselbe Datei → andere Strecke");
                // stored, shared and loaded again: the same profile
                var back = RouteStore.FromJson(RouteStore.ToJson(d));
                RouteGenerator.Generate(back);
                if (back.profile.heightsM.Length != d.profile.heightsM.Length || Mathf.Abs(back.profile.ascentM - d.profile.ascentM) > 0.01f) fails.Add("GPX: nach Speichern anderes Profil");
                string code = RouteShare.ToCode(d);
                var shared = RouteShare.FromCode(code, out bool match, out string cerr);
                if (shared == null || !match) fails.Add("GPX: Teilen-Code " + (cerr ?? "Profil weicht ab"));
                Debug.Log($"[RouteCheck] GPX Runde: {p.lengthKm:0.00} km, ↑{d.profile.ascentM:0} m, Kurvigkeit {p.curviness:0.00}, Code {code.Length} Zeichen");
            }

            // 2) a planned out-and-back route (rtept, no heights): open, flat, a turning point is no curve
            sb.Clear(); sb.Append("<rte><name>Hin und zurück</name>");
            for (int i = 0; i <= 100; i++) { double s = i <= 50 ? i * 20 : (100 - i) * 20; sb.Append($"<rtept lat=\"{F(50 + s / 111320.0)}\" lon=\"{F(8 + (i <= 50 ? 0.00001 : 0))}\"/>"); }
            sb.Append("</rte>");
            d = GpxImport.FromXml(Gpx(sb.ToString()), "hin", out err);
            if (d == null) fails.Add("GPX Hin und zurück nicht gelesen: " + err);
            else
            {
                if (Mathf.Abs(d.@params.lengthKm - 2f) > 0.03f) fails.Add($"GPX hin/zurück: {d.@params.lengthKm:0.00} km statt 2");
                if (d.profile.ascentM > 0.5f) fails.Add("GPX ohne Höhen: nicht flach");
                if (d.@params.curviness > 0.1f) fails.Add($"GPX hin/zurück: Kurvigkeit {d.@params.curviness:0.00} (Wende als Kurve gezählt)");
                if (!d.@params.loop) fails.Add("GPX hin/zurück: endet am Start → Rundkurs erwartet");
            }

            // 3) unusable files say why
            if (GpxImport.FromXml("kein xml <", "x", out err) != null || string.IsNullOrEmpty(err)) fails.Add("GPX: kaputte Datei angenommen");
            if (GpxImport.FromXml(Gpx("<trk><trkseg><trkpt lat=\"50\" lon=\"8\"/><trkpt lat=\"50.0001\" lon=\"8\"/></trkseg></trk>"), "x", out err) != null) fails.Add("GPX: 11 m lange Strecke angenommen");
            if (GpxImport.FromXml("<!DOCTYPE x [<!ENTITY a \"b\">]><gpx/>", "x", out err) != null) fails.Add("GPX: DTD angenommen");

            foreach (var f in fails) Debug.LogError("[RouteCheck] FAIL " + f);
            if (fails.Count == 0) Debug.Log("[RouteCheck] OK   GPX-Import");
            return fails.Count == 0;
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
