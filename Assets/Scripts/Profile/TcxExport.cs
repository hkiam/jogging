using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Jogging.Profile
{
    /// <summary>
    /// Export a run as TCX (Garmin Training Center XML) — the format Strava, Garmin Connect and others
    /// import for indoor runs: one lap with time, distance, calories and heart rate, and a trackpoint
    /// per second (time, distance, heart rate, speed). No GPS (treadmill, virtual landscape).
    /// Plain .NET (tested by StatsCheck).
    /// </summary>
    public static class TcxExport
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string ToTcx(SessionRecord rec)
        {
            var s = rec.summary;
            var start = RunnerStats.ParseUtc(s.start);
            string Iso(DateTime t) => t.ToString("yyyy-MM-ddTHH:mm:ssZ", Inv);
            string F(double v, string fmt = "0.0") => v.ToString(fmt, Inv);

            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
            sb.Append("<TrainingCenterDatabase xmlns=\"http://www.garmin.com/xmlschemas/TrainingCenterDatabase/v2\" ")
              .Append("xmlns:ns3=\"http://www.garmin.com/xmlschemas/ActivityExtension/v2\">\n");
            sb.Append("  <Activities>\n    <Activity Sport=\"Running\">\n");
            sb.Append($"      <Id>{Iso(start)}</Id>\n");
            sb.Append($"      <Lap StartTime=\"{Iso(start)}\">\n");
            sb.Append($"        <TotalTimeSeconds>{F(s.seconds)}</TotalTimeSeconds>\n");
            sb.Append($"        <DistanceMeters>{F(s.distanceM)}</DistanceMeters>\n");
            sb.Append($"        <MaximumSpeed>{F(s.maxKmh / 3.6, "0.00")}</MaximumSpeed>\n");
            sb.Append($"        <Calories>{Math.Max(0, (int)Math.Round(s.kcal))}</Calories>\n");
            if (s.avgHr > 0)
            {
                sb.Append($"        <AverageHeartRateBpm><Value>{s.avgHr}</Value></AverageHeartRateBpm>\n");
                sb.Append($"        <MaximumHeartRateBpm><Value>{s.maxHr}</Value></MaximumHeartRateBpm>\n");
            }
            sb.Append("        <Intensity>Active</Intensity>\n        <TriggerMethod>Manual</TriggerMethod>\n        <Track>\n");
            foreach (var p in rec.samples)
            {
                sb.Append("          <Trackpoint>");
                sb.Append($"<Time>{Iso(start.AddSeconds(p.t))}</Time>");
                sb.Append($"<DistanceMeters>{F(p.distM)}</DistanceMeters>");
                if (p.hr > 0) sb.Append($"<HeartRateBpm><Value>{p.hr}</Value></HeartRateBpm>");
                sb.Append($"<Extensions><ns3:TPX><ns3:Speed>{F(p.kmh / 3.6, "0.00")}</ns3:Speed></ns3:TPX></Extensions>");
                sb.Append("</Trackpoint>\n");
            }
            sb.Append("        </Track>\n      </Lap>\n");
            string note = string.IsNullOrEmpty(s.workoutName) ? s.routeName : $"{s.workoutName} · {s.routeName}";
            sb.Append($"      <Notes>{Escape(note)} (Jogging, {(s.source == "belt" ? "Laufband" : "Tastatur")})</Notes>\n");
            sb.Append("    </Activity>\n  </Activities>\n</TrainingCenterDatabase>\n");
            return sb.ToString();
        }

        /// <summary>Write the TCX to the Downloads folder (else the app data folder); returns the path.</summary>
        public static string SaveToDownloads(SessionRecord rec, string runnerName)
        {
            string dir = Jogging.Core.DataPaths.Downloads;
            if (!Directory.Exists(dir)) dir = Jogging.Core.DataPaths.Root;
            var t = RunnerStats.ParseUtc(rec.summary.start).ToLocalTime();
            string safe = new string((runnerName ?? "Lauf").Where(char.IsLetterOrDigit).ToArray());
            string name = $"Jogging-{safe}-{t:yyyy-MM-dd-HHmmss}";
            string path = Path.Combine(dir, name + ".tcx");
            for (int n = 2; File.Exists(path); n++) path = Path.Combine(dir, $"{name}-{n}.tcx"); // never overwrite
            File.WriteAllText(path, ToTcx(rec), new UTF8Encoding(false));
            return path;
        }

        /// <summary>Text for an XML element: markup escaped, characters XML doesn't allow (control chars, lone surrogates) dropped.</summary>
        private static string Escape(string s)
        {
            var sb = new StringBuilder();
            s ??= "";
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { sb.Append(c).Append(s[++i]); continue; }
                bool ok = c == '\t' || c == '\n' || c == '\r' || (c >= 0x20 && c <= 0xD7FF) || (c >= 0xE000 && c <= 0xFFFD);
                if (!ok) continue;
                sb.Append(c == '&' ? "&amp;" : c == '<' ? "&lt;" : c == '>' ? "&gt;" : c.ToString());
            }
            return sb.ToString();
        }
    }
}
