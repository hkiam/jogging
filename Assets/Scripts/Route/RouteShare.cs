using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace Jogging.Route
{
    /// <summary>
    /// Sharing routes without a server:
    ///   • Share code: "JOG1:" + base64url(gzip(JSON)) of the route WITHOUT the generated profile
    ///     (the receiver regenerates it from parameters + seed) plus a checksum of the profile, so the
    ///     receiver can tell whether its generator produced the same course. ≈ 1 KB of text.
    ///   • Files: export a .jogroute to ~/Downloads; import finds .jogroute files in Downloads and
    ///     on the Desktop.
    /// Imported routes keep their id and revision (the same route on every machine; best times stay
    /// per revision); an older or equal revision than the local one is not imported again. Local
    /// content is never lost: a local route that a newer revision replaces is kept as "… (lokal)",
    /// and the same revision with other content comes in as a copy.
    /// </summary>
    public static class RouteShare
    {
        private const string Prefix = "JOG1:";
        public const int MaxCodeChars = 16 * 1024;      // a real code is ≈ 1–4 KB
        public const int MaxJsonChars = 256 * 1024;     // decompressed (gzip bombs)
        private const long MaxFileBytes = 8 * 1024 * 1024; // .jogroute with the profile of a 100 km route ≈ 1 MB

        [Serializable]
        private class Packet
        {
            public RouteDoc doc;
            public string profileCheck; // generator version + ascent/descent/length checksum
        }

        public static string Checksum(RouteDoc d)
        {
            var h = d.profile?.heightsM ?? new float[0];
            double sum = 0; for (int i = 0; i < h.Length; i += 7) sum += h[i] * (1 + (i % 13));
            return $"{d.generator.version}|{h.Length}|{d.profile.ascentM:0.0}|{sum:0.0}";
        }

        // ------------------------------------------------------------------ share code

        public static string ToCode(RouteDoc d)
        {
            var copy = RouteStore.FromJson(RouteStore.ToJson(d));
            string check = Checksum(copy);
            copy.profile = new RouteProfile(); // regenerated on import
            string json = JsonUtility.ToJson(new Packet { doc = copy, profileCheck = check });
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal, true))
            {
                var b = Encoding.UTF8.GetBytes(json);
                gz.Write(b, 0, b.Length);
            }
            return Prefix + Convert.ToBase64String(ms.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>Parse a share code (whitespace/line breaks tolerated). match = the regenerated profile equals the sender's.</summary>
        public static RouteDoc FromCode(string code, out bool match, out string error)
        {
            match = false; error = null;
            if (string.IsNullOrWhiteSpace(code)) { error = Jogging.Core.Loc.T("Kein Code in der Zwischenablage."); return null; }
            if (code.Length > MaxCodeChars) { error = Jogging.Core.Loc.T("Das ist kein Strecken-Code (zu lang)."); return null; }
            code = code.Trim().Replace("\n", "").Replace("\r", "").Replace(" ", "");
            int at = code.IndexOf(Prefix, StringComparison.Ordinal);
            if (at < 0) { error = Jogging.Core.Loc.T("Das ist kein Strecken-Code (JOG1:…)."); return null; }
            try
            {
                string b64 = code.Substring(at + Prefix.Length).Replace('-', '+').Replace('_', '/');
                b64 += new string('=', (4 - b64.Length % 4) % 4);
                var bytes = Convert.FromBase64String(b64);
                using var gz = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
                using var r = new StreamReader(gz, Encoding.UTF8);
                var buf = new char[MaxJsonChars + 1];
                int len = 0, n;
                while (len < buf.Length && (n = r.Read(buf, len, buf.Length - len)) > 0) len += n;
                if (len > MaxJsonChars) { error = Jogging.Core.Loc.T("Code ist zu groß."); return null; }
                var packet = JsonUtility.FromJson<Packet>(new string(buf, 0, len));
                if (packet == null || packet.doc == null || packet.doc.@params == null) { error = Jogging.Core.Loc.T("Code ist unvollständig."); return null; }
                var doc = RouteStore.FromJson(JsonUtility.ToJson(packet.doc)); // schema checks/migration
                if (doc == null) { error = Jogging.Core.Loc.T("Code stammt aus einer neueren App-Version."); return null; }
                RouteGenerator.Generate(doc);
                match = Checksum(doc) == packet.profileCheck;
                return doc;
            }
            catch (Exception e)
            {
                error = Jogging.Core.Loc.F("Code nicht lesbar ({0}).", e.GetType().Name);
                return null;
            }
        }

        // ------------------------------------------------------------------ files

        public static string DownloadsFolder => Core.DataPaths.Downloads;
        private static string DesktopFolder => Core.DataPaths.Desktop;

        /// <summary>Write the route as "&lt;Name&gt;.jogroute" to Downloads; returns the path.</summary>
        public static string ExportFile(RouteDoc d)
        {
            Directory.CreateDirectory(DownloadsFolder);
            string name = SafeName(d.meta.name);
            string path = Path.Combine(DownloadsFolder, name + RouteStore.Extension);
            for (int i = 2; File.Exists(path) && !SameRoute(path, d); i++)
                path = Path.Combine(DownloadsFolder, $"{name} ({i}){RouteStore.Extension}");
            File.WriteAllText(path, RouteStore.ToJson(d));
            return path;
        }

        private static bool SameRoute(string path, RouteDoc d)
        {
            var other = RouteStore.Load(path);
            return other != null && other.id == d.id;
        }

        private static string SafeName(string n)
        {
            var sb = new StringBuilder();
            foreach (char c in string.IsNullOrWhiteSpace(n) ? "Strecke" : n.Trim())
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 || c == '/' || c == ':' ? '_' : c);
            return sb.ToString();
        }

        public class Candidate { public RouteDoc doc; public string file; public string status; public bool importable; public string error; }

        /// <summary>
        /// .jogroute and .gpx files in Downloads and on the Desktop, with their state against the local routes
        /// (a GPX file that can't be used comes with <c>error</c> and no doc).
        /// </summary>
        public static List<Candidate> FindFiles()
        {
            var local = RouteStore.LoadAll();
            var list = new List<Candidate>();
            foreach (var dir in new[] { DownloadsFolder, DesktopFolder })
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir, "*" + RouteStore.Extension))
                {
                    if (new FileInfo(f).Length > MaxFileBytes) continue;
                    var d = RouteStore.Load(f);
                    if (d == null) continue;
                    list.Add(new Candidate { doc = d, file = f, status = StateOf(d, local, out bool ok), importable = ok });
                }
                foreach (var f in Directory.GetFiles(dir, "*" + GpxImport.Extension))
                {
                    if (new FileInfo(f).Length > MaxFileBytes) { list.Add(new Candidate { file = f, error = Jogging.Core.Loc.T("Datei zu groß") }); continue; }
                    var d = GpxImport.Load(f, out string err);
                    if (d == null) { list.Add(new Candidate { file = f, error = err }); continue; }
                    list.Add(new Candidate { doc = d, file = f, status = "GPX · " + StateOf(d, local, out bool ok), importable = ok });
                }
            }
            return list;
        }

        /// <summary>
        /// How an incoming route relates to the local ones ("neu", "neuere Version", "schon vorhanden",
        /// "gleiche Version, anderer Inhalt", "ältere Version"); importable = <see cref="Import"/> adds something.
        /// </summary>
        public static string StateOf(RouteDoc d, List<RouteDoc> local, out bool importable)
        {
            var l = LocalOf(d, local);
            if (l == null) { importable = true; return "neu"; }
            if (d.revision > l.revision) { importable = true; return Jogging.Core.Loc.F("neuere Version (Rev. {0} statt {1})", d.revision, l.revision); }
            if (d.revision == l.revision && !SameContent(d, l)) { importable = true; return Jogging.Core.Loc.T("gleiche Version, anderer Inhalt – kommt als Kopie"); }
            importable = false;
            return d.revision == l.revision ? Jogging.Core.Loc.T("schon vorhanden") : Jogging.Core.Loc.F("ältere Version (Rev. {0}, hier Rev. {1})", d.revision, l.revision);
        }

        private static RouteDoc LocalOf(RouteDoc d, List<RouteDoc> local)
        {
            if (string.IsNullOrEmpty(d.id)) return null;
            foreach (var l in local) if (l.id == d.id) return l;
            return null;
        }

        /// <summary>
        /// Same recipe (parameters + seed — the course; unlike <see cref="Checksum"/> independent of the
        /// generator version), same workshop edits and name.
        /// </summary>
        public static bool SameContent(RouteDoc a, RouteDoc b) => Recipe(a) == Recipe(b);

        private static string Recipe(RouteDoc d) =>
            JsonUtility.ToJson(new RecipeOf { p = d.@params, seed = d.generator.seed, edits = d.edits, name = d.meta.name });

        [Serializable] private class RecipeOf { public RouteParams p; public int seed; public List<RouteEdit> edits; public string name; }

        /// <summary>
        /// Store an incoming route locally (keeps id and revision; a missing id gets a new one).
        /// A local route it replaces is kept as "&lt;Name&gt; (lokal)" if its content differs; the same
        /// revision with other content is stored as a copy (new id, name "… (importiert)").
        /// Does nothing if <see cref="StateOf"/> says it is not importable.
        /// </summary>
        public static void Import(RouteDoc d)
        {
            if (d.profile == null || d.profile.heightsM == null || d.profile.heightsM.Length < 2) RouteGenerator.Generate(d);
            var local = RouteStore.LoadAll();
            var l = LocalOf(d, local);
            StateOf(d, local, out bool importable);
            if (!importable) return;
            if (l != null && d.revision > l.revision && !SameContent(d, l))
            {
                var keep = RouteStore.FromJson(RouteStore.ToJson(l));
                keep.id = ""; keep.meta.created = ""; keep.meta.name = l.meta.name + Jogging.Core.Loc.T(" (lokal)");
                RouteStore.Save(keep);
            }
            else if (l != null && d.revision == l.revision)
            {
                d.id = ""; d.revision = 1; d.meta.created = ""; d.meta.name += Jogging.Core.Loc.T(" (importiert)");
            }
            RouteStore.Save(d);
        }
    }
}
