using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Jogging.Route
{
    /// <summary>
    /// Local persistence of routes: one <c>.jogroute</c> JSON file per route in
    /// <c>Application.persistentDataPath/routes/</c>. Import/export are plain file copies.
    /// </summary>
    public static class RouteStore
    {
        public const string Extension = ".jogroute";

        /// <summary>Tests only: another data folder instead of <see cref="Core.DataPaths.Root"/>.</summary>
        public static string RootOverride;

        public static string Folder
        {
            get
            {
                string f = Path.Combine(RootOverride ?? Jogging.Core.DataPaths.Root, "routes");
                Directory.CreateDirectory(f);
                return f;
            }
        }

        /// <summary>Route ids as <see cref="NewId"/> makes them ("rt_" + 26 Crockford base32 chars).</summary>
        public static bool IsValidId(string id) =>
            id != null && System.Text.RegularExpressions.Regex.IsMatch(id, "^rt_[0-9A-HJKMNP-TV-Z]{26}$");

        /// <summary>The route's file; throws for an id that isn't one of ours (it would be a path).</summary>
        public static string PathFor(RouteDoc doc)
        {
            if (!IsValidId(doc.id)) throw new ArgumentException("ungültige Strecken-Id");
            string folder = Path.GetFullPath(Folder) + Path.DirectorySeparatorChar;
            string p = Path.GetFullPath(Path.Combine(folder, doc.id + Extension));
            if (!p.StartsWith(folder, StringComparison.Ordinal)) throw new ArgumentException("Pfad außerhalb des Strecken-Ordners");
            return p;
        }

        /// <summary>Save (assigns id/timestamps if missing). Returns the file path.</summary>
        public static string Save(RouteDoc doc)
        {
            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            if (!IsValidId(doc.id)) doc.id = NewId();
            if (string.IsNullOrEmpty(doc.meta.created)) doc.meta.created = now;
            doc.meta.updated = now;
            string path = PathFor(doc);
            Core.SafeFile.Write(path, ToJson(doc));
            return path;
        }

        public static string ToJson(RouteDoc doc) => JsonUtility.ToJson(doc, true);

        /// <summary>
        /// Parse a document; null if it isn't one (or from a newer, unknown schema). Also for routes
        /// from others (share code, QR, file): a foreign id gets a new one, and sizes are clamped to
        /// what the generator and the workshop can handle.
        /// </summary>
        public static RouteDoc FromJson(string json)
        {
            try
            {
                var doc = JsonUtility.FromJson<RouteDoc>(json);
                if (doc == null || doc.@params == null) return null;
                if (doc.schemaVersion > RouteDoc.CurrentSchema)
                {
                    Debug.LogWarning($"[Jogging] Strecke hat neueres Format (v{doc.schemaVersion}) – bitte App aktualisieren.");
                    return null;
                }
                // (migrations from older schema versions go here)
                doc.schemaVersion = RouteDoc.CurrentSchema;
                Sanitize(doc);
                return doc;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Jogging] Strecke nicht lesbar: " + e.Message);
                return null;
            }
        }

        public const int MaxEdits = 500, MaxClimbs = 20;

        private static void Sanitize(RouteDoc doc)
        {
            if (!string.IsNullOrEmpty(doc.id) && !IsValidId(doc.id)) doc.id = NewId();
            doc.revision = Math.Clamp(doc.revision, 1, RouteDoc.MaxRevision);
            doc.meta ??= new RouteMeta();
            doc.generator ??= new RouteGeneratorInfo();
            doc.generator.seed = Math.Clamp(doc.generator.seed, -1_000_000_000, 1_000_000_000);
            var p = doc.@params;
            p.lengthKm = float.IsNaN(p.lengthKm) ? 5f : Math.Clamp(p.lengthKm, 0.5f, 100f);
            p.climbs ??= new List<RouteClimb>();
            p.gpxHeightsDm ??= new int[0];
            if (p.gpxHeightsDm.Length > 20001) Array.Resize(ref p.gpxHeightsDm, 20001); // 100 km at 5 m
            p.gpxStepM = float.IsNaN(p.gpxStepM) ? 20f : Math.Clamp(p.gpxStepM, 5f, 100f);
            if (p.source == "gpx" && p.gpxHeightsDm.Length >= 2) p.lengthKm = Math.Clamp((p.gpxHeightsDm.Length - 1) * p.gpxStepM / 1000f, 0.5f, 100f);
            if (p.climbs.Count > MaxClimbs) p.climbs.RemoveRange(MaxClimbs, p.climbs.Count - MaxClimbs);
            doc.edits ??= new List<RouteEdit>();
            if (doc.edits.Count > MaxEdits) doc.edits.RemoveRange(MaxEdits, doc.edits.Count - MaxEdits);
            doc.profile ??= new RouteProfile();
        }

        /// <summary>A route file (or its backup if the file is broken); null if neither is usable.</summary>
        public static RouteDoc Load(string path) => Core.SafeFile.Read(path, FromJson);

        /// <summary>All saved routes, newest first.</summary>
        public static List<RouteDoc> LoadAll()
        {
            var list = new List<RouteDoc>();
            foreach (var f in Directory.GetFiles(Folder, "*" + Extension))
            {
                var d = Load(f);
                if (d != null) list.Add(d);
            }
            list.Sort((a, b) => string.CompareOrdinal(b.meta.updated, a.meta.updated));
            return list;
        }

        public static void Delete(RouteDoc doc)
        {
            if (!IsValidId(doc.id)) return;
            Core.SafeFile.Delete(PathFor(doc));
        }

        /// <summary>Time-sortable unique id (ULID-style: 48-bit ms timestamp + 80 random bits, Crockford base32).</summary>
        public static string NewId()
        {
            const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
            long ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var bytes = new byte[10];
            new System.Random(Guid.NewGuid().GetHashCode()).NextBytes(bytes);
            var c = new char[26];
            for (int i = 9; i >= 0; i--) { c[i] = alphabet[(int)(ms & 31)]; ms >>= 5; }
            ulong hi = 0; for (int i = 0; i < 5; i++) hi = (hi << 8) | bytes[i];
            ulong lo = 0; for (int i = 5; i < 10; i++) lo = (lo << 8) | bytes[i];
            for (int i = 17; i >= 10; i--) { c[i] = alphabet[(int)(hi & 31)]; hi >>= 5; }
            for (int i = 25; i >= 18; i--) { c[i] = alphabet[(int)(lo & 31)]; lo >>= 5; }
            return "rt_" + new string(c);
        }
    }
}
