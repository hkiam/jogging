using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jogging.Route
{
    /// <summary>
    /// Reads shared routes from QR images (a photo or screenshot of the share page, or the
    /// "Strecke-….png" from Downloads). Recognition runs in the Bluetooth bridge
    /// (<c>JoggingBleBridge --qr &lt;image&gt;</c>, Apple Vision) — Unity itself can't; Bluetooth
    /// isn't used for it. Looks at the newest images in Downloads and on the Desktop, in the background.
    /// </summary>
    public static class QrReader
    {
        private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".heic" };
        private const int MaxImages = 15;

        /// <summary>The bridge executable (embedded in the app, else the installed one); null if missing.</summary>
        public static string Helper
        {
            get
            {
                const string exe = "JoggingBleScan.app/Contents/MacOS/JoggingBleBridge";
                string embedded = Path.Combine(Application.dataPath, "Resources", exe);
                if (File.Exists(embedded)) return embedded;
                string installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Applications", exe);
                return File.Exists(installed) ? installed : null;
            }
        }

        private const int TimeoutS = 5; // per image

        /// <summary>All QR texts in one image (empty if none or the helper is missing).</summary>
        public static List<string> Decode(string image) => Decode(image, Helper, Application.temporaryCachePath);

        // Thread-safe (no Unity API): helper and temp folder are passed in. The helper gets at most
        // TimeoutS seconds per image (perl's alarm — macOS has no "timeout").
        private static List<string> Decode(string image, string helper, string tempDir)
        {
            var result = new List<string>();
            if (helper == null || !File.Exists(image)) return result;
            string outFile = Path.Combine(tempDir, "qr-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                const string perl = "/usr/bin/perl";
                string limit = File.Exists(perl) ? $"{perl} -e 'alarm shift; exec @ARGV' {TimeoutS} " : "";
                Core.Shell.Run($"{limit}{Core.Shell.Quote(helper)} --qr {Core.Shell.Quote(image)} > {Core.Shell.Quote(outFile)} 2>/dev/null");
                if (File.Exists(outFile))
                    result.AddRange(File.ReadAllLines(outFile).Where(l => l.Trim().Length > 0));
            }
            catch (Exception e) { Debug.LogWarning("[Jogging] QR lesen: " + e.Message); }
            finally { try { File.Delete(outFile); } catch { } }
            return result;
        }

        /// <summary>
        /// Newest images in Downloads and on the Desktop (screenshots land there); exported
        /// "Strecke-….png" files first.
        /// </summary>
        public static List<string> RecentImages()
        {
            var dirs = new[] { Core.DataPaths.Downloads, Core.DataPaths.Desktop };
            var files = new List<FileInfo>();
            foreach (var d in dirs)
            {
                try
                {
                    if (!Directory.Exists(d)) continue;
                    files.AddRange(new DirectoryInfo(d).GetFiles().Where(f => Extensions.Contains(f.Extension.ToLowerInvariant())));
                }
                catch { /* folder not readable (privacy) → skip */ }
            }
            return files.OrderByDescending(f => f.Name.StartsWith("Strecke-", StringComparison.OrdinalIgnoreCase))
                        .ThenByDescending(f => f.LastWriteTimeUtc).Take(MaxImages).Select(f => f.FullName).ToList();
        }

        /// <summary>A running QR search (<see cref="Start"/>); the fields are valid once <see cref="Done"/>.</summary>
        public class Search
        {
            public volatile bool Done;
            public RouteDoc Doc;
            public bool Match;
            public string Info;
        }

        /// <summary>
        /// Look for a route QR code in the recent images on a background thread (the helper takes
        /// ~1 s per image). Stops at the first route that isn't already here identically (new or
        /// newer); if all found routes are known, the first of them is the result.
        /// </summary>
        public static Search Start(List<RouteDoc> local)
        {
            var s = new Search();
            string helper = Helper;
            if (helper == null) { s.Info = Jogging.Core.Loc.T("QR-Lesen braucht die Laufband-Bridge (fehlt in dieser Installation)."); s.Done = true; return s; }
            var images = RecentImages();
            if (images.Count == 0) { s.Info = Jogging.Core.Loc.T("Keine Bilder in Downloads oder auf dem Schreibtisch."); s.Done = true; return s; }
            string temp = Application.temporaryCachePath;
            var t = new System.Threading.Thread(() =>
            {
                try { Find(s, images, local, helper, temp); }
                catch (Exception e) { s.Doc = null; s.Info = Jogging.Core.Loc.F("QR lesen fehlgeschlagen: {0}", e.Message); }
                finally { s.Done = true; }
            }) { IsBackground = true, Name = "QR-Suche" };
            t.Start();
            return s;
        }

        private static void Find(Search s, List<string> images, List<RouteDoc> local, string helper, string temp)
        {
            foreach (var img in images)
                foreach (var text in Decode(img, helper, temp))
                {
                    var doc = RouteShare.FromCode(text, out bool match, out _);
                    if (doc == null) continue;
                    RouteShare.StateOf(doc, local, out bool importable);
                    if (s.Doc == null || importable) { s.Doc = doc; s.Match = match; s.Info = Jogging.Core.Loc.F("aus „{0}“", Path.GetFileName(img)); }
                    if (importable) return;
                }
            if (s.Doc == null) s.Info = Jogging.Core.Loc.F("In den {0} neuesten Bildern (Downloads, Schreibtisch) ist kein Strecken-QR-Code.", images.Count);
        }
    }
}
