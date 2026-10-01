using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using UnityEngine;

namespace Jogging.Route
{
    /// <summary>
    /// A real route from a GPX file (Strava, Garmin, Komoot, …) becomes a route of the app: its length,
    /// its elevation profile and whether it is a loop are taken over exactly (heights smoothed – GPS
    /// heights are noisy); how winding it is, roughly (the course itself is generated: a real track
    /// crosses itself or turns back, which the landscape can't); the season from the recording's date.
    /// The landscape and mood stay free to choose. Plain .NET (tested by RouteGeneratorCheck).
    /// </summary>
    public static class GpxImport
    {
        public const string Extension = ".gpx";
        public const float StepM = 20f;        // the stored profile: one height every 20 m (decimetres)
        public const float MinKm = 0.5f, MaxKm = 100f;
        private const float SmoothM = 60f;     // ± metres of the moving average over the heights

        private struct Pt { public double lat, lon; public float ele; public bool hasEle; }

        /// <summary>The route from a GPX file; null + error text if it can't be used.</summary>
        public static RouteDoc Load(string path, out string error)
        {
            try { return FromXml(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path), out error); }
            catch (Exception e) { error = e.Message; return null; }
        }

        public static RouteDoc FromXml(string xml, string fileName, out string error)
        {
            error = null;
            var pts = new List<Pt>();
            string name = "", time = "";
            var rte = new List<Pt>();
            try
            {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true };
                using var r = XmlReader.Create(new StringReader(xml), settings);
                Pt cur = default; bool inPt = false, isRte = false; int depth = 0;
                r.Read();
                while (!r.EOF) // ReadElementContentAsString already moves on: no extra Read() after it
                {
                    if (r.NodeType == XmlNodeType.Element)
                    {
                        string tag = r.LocalName;
                        if (tag == "wpt") { r.Skip(); continue; } // single waypoints: not part of the route (nor their names)
                        if (tag == "trkpt" || tag == "rtept")
                        {
                            cur = new Pt { lat = Num(r.GetAttribute("lat")), lon = Num(r.GetAttribute("lon")) };
                            isRte = tag == "rtept";
                            inPt = !r.IsEmptyElement; depth = r.Depth;
                            if (!inPt) (isRte ? rte : pts).Add(cur);
                        }
                        else if (inPt && tag == "ele" && !r.IsEmptyElement) { cur.ele = (float)Num(r.ReadElementContentAsString()); cur.hasEle = !float.IsNaN(cur.ele); if (!cur.hasEle) cur.ele = 0f; continue; }
                        else if (inPt && tag == "time" && time == "" && !r.IsEmptyElement) { time = r.ReadElementContentAsString(); continue; }
                        else if (!inPt && tag == "name" && name == "" && !r.IsEmptyElement) { name = r.ReadElementContentAsString().Trim(); continue; }
                    }
                    else if (r.NodeType == XmlNodeType.EndElement && inPt && r.Depth == depth && (r.LocalName == "trkpt" || r.LocalName == "rtept"))
                    {
                        (isRte ? rte : pts).Add(cur);
                        inPt = false;
                    }
                    r.Read();
                }
            }
            catch (Exception e) { error = Jogging.Core.Loc.F("Keine gültige GPX-Datei ({0})", e.Message); return null; }
            if (pts.Count < 2) pts = rte; // a planned route (Komoot & co.) instead of a recorded track
            pts.RemoveAll(p => double.IsNaN(p.lat) || double.IsNaN(p.lon) || Math.Abs(p.lat) > 90 || Math.Abs(p.lon) > 180);
            if (pts.Count < 2) { error = Jogging.Core.Loc.T("Die GPX-Datei enthält keine Strecke."); return null; }

            // Distance along the track (metres), duplicates dropped
            var dist = new List<double> { 0 };
            var keep = new List<Pt> { pts[0] };
            for (int i = 1; i < pts.Count; i++)
            {
                double d = Metres(keep[keep.Count - 1], pts[i]);
                if (d < 0.5) continue;
                keep.Add(pts[i]); dist.Add(dist[dist.Count - 1] + d);
            }
            double L = dist[dist.Count - 1];
            if (L < MinKm * 1000) { error = Jogging.Core.Loc.F("Die Strecke ist zu kurz ({0:0} m).", L); return null; }
            if (L > MaxKm * 1000) { error = Jogging.Core.Loc.F("Die Strecke ist zu lang ({0:0} km, höchstens {1:0}).", L / 1000, MaxKm); return null; }

            // Heights every StepM, smoothed, relative to the start
            int withEle = 0; foreach (var p in keep) if (p.hasEle) withEle++;
            bool hasEle = withEle >= keep.Count / 2;
            int n = Mathf.Max(2, (int)Math.Round(L / StepM) + 1);
            double step = L / (n - 1);
            var raw = new float[n];
            for (int i = 0, j = 0; i < n; i++)
            {
                double s = i * step;
                while (j < dist.Count - 2 && dist[j + 1] < s) j++;
                double seg = dist[j + 1] - dist[j], u = seg > 0 ? Math.Clamp((s - dist[j]) / seg, 0, 1) : 0;
                raw[i] = hasEle ? (float)(Ele(keep, j) + (Ele(keep, j + 1) - Ele(keep, j)) * u) : 0f;
            }
            int half = Mathf.Max(1, Mathf.RoundToInt(SmoothM / (float)step));
            var heights = new int[n];
            float h0 = 0f;
            for (int i = 0; i < n; i++)
            {
                double sum = 0; int c = 0;
                for (int k = Math.Max(0, i - half); k <= Math.Min(n - 1, i + half); k++) { sum += raw[k]; c++; }
                float h = (float)(sum / c);
                if (i == 0) h0 = h;
                heights[i] = Mathf.RoundToInt((h - h0) * 10f);
            }

            // Loop: ends near where it started
            bool loop = Metres(keep[0], keep[keep.Count - 1]) < Math.Max(100.0, 0.02 * L);

            // How winding: turning per km on the track resampled every 20 m
            double turn = 0; double? lastHeading = null;
            for (int i = 0, j = 0; i + 1 < n; i++)
            {
                var a = At(keep, dist, i * step, ref j);
                int j2 = j; var b = At(keep, dist, (i + 1) * step, ref j2);
                double hd = Math.Atan2((b.lon - a.lon) * Math.Cos(a.lat * Math.PI / 180), b.lat - a.lat);
                if (lastHeading.HasValue)
                {
                    double dh = hd - lastHeading.Value;
                    while (dh > Math.PI) dh -= 2 * Math.PI;
                    while (dh < -Math.PI) dh += 2 * Math.PI;
                    if (Math.Abs(dh) < 2.6) turn += Math.Abs(dh); // a turning point (out and back) is not a curve
                }
                lastHeading = hd;
            }
            float degPerKm = (float)(turn * 180 / Math.PI / (L / 1000));

            // Steepest stretch (over 50 m) – the runnable limit of the route
            float maxGrade = 0.03f;
            int w = Mathf.Max(1, Mathf.RoundToInt(50f / (float)step));
            for (int i = 0; i + w < n; i++) maxGrade = Mathf.Max(maxGrade, Mathf.Abs(heights[i + w] - heights[i]) / 10f / (float)(w * step));

            var doc = RoutePresets.Create("huegelig", 1);
            string hash = Hash(xml);
            doc.id = "rt_" + hash.Substring(0, 26);
            doc.revision = 1;
            doc.generator.seed = (int)(Crockford(hash.Substring(26, 6)) % 99999) + 1;
            doc.meta.name = Trim(name != "" ? name : fileName, 60);
            doc.meta.description = Jogging.Core.Loc.F("Aus GPX: {0}", fileName);
            doc.meta.tags = new List<string> { "gpx" };
            var prm = doc.@params;
            prm.source = "gpx";
            prm.gpxStepM = (float)step;
            prm.gpxHeightsDm = heights;
            prm.lengthKm = (float)(L / 1000);
            prm.loop = loop;
            prm.endless = false;
            prm.climbs = new List<RouteClimb>();
            prm.rolling = 0f;
            prm.curviness = Mathf.Clamp01(degPerKm / 700f);
            prm.maxGrade = Mathf.Clamp(maxGrade, 0.03f, 0.15f);
            int lo = 0, hi = 0;
            foreach (var h in heights) { lo = Math.Min(lo, h); hi = Math.Max(hi, h); }
            float range = (hi - lo) / 10f;
            prm.relief = Mathf.Clamp(0.25f + range / 400f, 0.25f, 0.9f); // the land around as hilly as the route
            if (DateTime.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var t))
                prm.season = t.Month >= 3 && t.Month <= 5 ? "spring" : t.Month <= 8 && t.Month >= 6 ? "summer" : t.Month >= 9 && t.Month <= 11 ? "autumn" : "winter";
            if (!hasEle) doc.meta.description += Jogging.Core.Loc.T(" (ohne Höhenangaben – flach)");
            RouteGenerator.Generate(doc);
            return doc;
        }

        /// <summary>The stored GPX profile (decimetres every step) as heights in metres at distance s.</summary>
        public static float HeightAt(RouteParams p, float s)
        {
            var h = p.gpxHeightsDm;
            float f = Mathf.Clamp(s / Mathf.Max(1f, p.gpxStepM), 0f, h.Length - 1);
            int i = Mathf.Min(h.Length - 2, Mathf.FloorToInt(f));
            return Mathf.Lerp(h[i], h[i + 1], f - i) / 10f;
        }

        public static bool IsGpx(RouteParams p) => p != null && p.source == "gpx" && p.gpxHeightsDm != null && p.gpxHeightsDm.Length >= 2;

        // ------------------------------------------------------------------ helpers

        private static double Num(string s) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;

        private static float Ele(List<Pt> pts, int i)
        {
            // a point without height takes the nearest one that has one
            for (int d = 0; d < pts.Count; d++)
            {
                if (i - d >= 0 && pts[i - d].hasEle) return pts[i - d].ele;
                if (i + d < pts.Count && pts[i + d].hasEle) return pts[i + d].ele;
            }
            return 0f;
        }

        private static Pt At(List<Pt> pts, List<double> dist, double s, ref int j)
        {
            while (j < dist.Count - 2 && dist[j + 1] < s) j++;
            double seg = dist[j + 1] - dist[j], u = seg > 0 ? Math.Clamp((s - dist[j]) / seg, 0, 1) : 0;
            return new Pt { lat = pts[j].lat + (pts[j + 1].lat - pts[j].lat) * u, lon = pts[j].lon + (pts[j + 1].lon - pts[j].lon) * u };
        }

        private static double Metres(Pt a, Pt b) // haversine
        {
            const double R = 6371000;
            double p1 = a.lat * Math.PI / 180, p2 = b.lat * Math.PI / 180;
            double dp = p2 - p1, dl = (b.lon - a.lon) * Math.PI / 180;
            double x = Math.Sin(dp / 2) * Math.Sin(dp / 2) + Math.Cos(p1) * Math.Cos(p2) * Math.Sin(dl / 2) * Math.Sin(dl / 2);
            return 2 * R * Math.Asin(Math.Min(1, Math.Sqrt(x)));
        }

        private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"; // as route ids (Crockford base 32)

        // the same file → the same id (importing it twice finds "schon vorhanden")
        private static string Hash(string text)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var b = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text));
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 32; i++) sb.Append(Alphabet[b[i] & 31]);
            return sb.ToString();
        }

        private static uint Crockford(string s)
        {
            uint v = 0;
            foreach (char c in s) v = v * 32 + (uint)Math.Max(0, Alphabet.IndexOf(c));
            return v;
        }

        private static string Trim(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 1) + "…";
    }
}
