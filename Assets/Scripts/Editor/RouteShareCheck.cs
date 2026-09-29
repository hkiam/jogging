using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEditor;
using UnityEngine;
using Jogging.Route;

namespace Jogging.EditorTools
{
    /// <summary>
    /// Self-check of routes coming from others (share code, QR, file) in a temp folder: foreign ids
    /// never become paths, oversized codes and gzip bombs are rejected, sizes are clamped, and
    /// importing never loses local content (newer revision keeps "… (lokal)", same revision with
    /// other content comes in as a copy).
    /// Menu: Jogging → Strecken → Teilen prüfen. Headless: -executeMethod Jogging.EditorTools.RouteShareCheck.RunBatch
    /// </summary>
    public static class RouteShareCheck
    {
        [MenuItem("Jogging/Strecken/Teilen prüfen")]
        public static void RunMenu() => EditorUtility.DisplayDialog("Jogging", Run() ? "Alle Prüfungen bestanden." : "Fehler – siehe Console.", "OK");

        public static void RunBatch() => EditorApplication.Exit(Run() ? 0 : 1);

        public static bool Run()
        {
            var fails = new List<string>();
            void Ok(bool c, string what) { if (!c) fails.Add(what); }
            string root = Path.Combine(Path.GetTempPath(), "jogging-share-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            RouteStore.RootOverride = root;
            try
            {
                // Ids: only our format; a path in the id is replaced, never used as a file name.
                Ok(RouteStore.IsValidId(RouteStore.NewId()), "neue Id ungültig");
                foreach (var bad in new[] { "../../evil", "rt_../../x", "rt_" + new string('A', 25) + "/", "rt_abc", "" })
                    Ok(!RouteStore.IsValidId(bad), $"Id „{bad}“ gilt als gültig");
                var evil = RoutePresets.Create("flach", 7);
                evil.id = "../../../ausbruch";
                var parsed = RouteStore.FromJson(JsonUtility.ToJson(evil));
                Ok(parsed != null && RouteStore.IsValidId(parsed.id), "fremde Id nicht ersetzt");
                bool threw = false;
                try { RouteStore.PathFor(evil); } catch (ArgumentException) { threw = true; }
                Ok(threw, "PathFor mit Pfad in der Id");
                RouteStore.Delete(evil); // must not throw or touch anything
                evil.profile = new RouteProfile(); // like a real code: the profile is regenerated on import
                var viaCode = RouteShare.FromCode(Code(Packet(JsonUtility.ToJson(evil))), out _, out string err);
                Ok(viaCode != null && RouteStore.IsValidId(viaCode.id), "Teilen-Code mit Pfad-Id: " + err);
                if (viaCode != null)
                {
                    string saved = RouteStore.Save(viaCode);
                    Ok(Path.GetDirectoryName(Path.GetFullPath(saved)) == Path.GetFullPath(RouteStore.Folder), "Strecke außerhalb des Ordners gespeichert");
                    Ok(!File.Exists(Path.Combine(root, "ausbruch.jogroute")) && !File.Exists(Path.Combine(Path.GetDirectoryName(root), "ausbruch.jogroute")), "Pfad-Ausbruch geschrieben");
                    RouteStore.Delete(viaCode);
                    Ok(RouteStore.LoadAll().Count == 0 && Directory.GetFiles(RouteStore.Folder).Length == 0, "Löschen lässt Dateien (.bak) liegen");
                }

                // Size bounds: overlong code, gzip bomb, clamped values.
                RouteShare.FromCode("JOG1:" + new string('A', RouteShare.MaxCodeChars + 10), out _, out err);
                Ok(err != null, "überlanger Code angenommen");
                var bomb = RouteShare.FromCode(Code(new string(' ', 4 * 1024 * 1024)), out _, out err);
                Ok(bomb == null && err != null && err.Contains("zu groß"), "Gzip-Bombe: " + err);
                var big = RoutePresets.Create("flach", 3);
                big.@params.lengthKm = 1e7f; big.revision = int.MaxValue; big.generator.seed = int.MinValue;
                for (int i = 0; i < 600; i++) big.edits.Add(new RouteEdit { type = "lake", atM = i });
                for (int i = 0; i < 50; i++) big.@params.climbs.Add(new RouteClimb { atKm = i * 0.1f, heightM = 5f });
                var clamped = RouteStore.FromJson(JsonUtility.ToJson(big));
                Ok(clamped != null && clamped.@params.lengthKm <= 100f && clamped.revision == RouteDoc.MaxRevision
                   && clamped.edits.Count == RouteStore.MaxEdits && clamped.@params.climbs.Count == RouteStore.MaxClimbs
                   && Math.Abs((long)clamped.generator.seed) <= 1_000_000_000L, "Werte nicht begrenzt");
                var tiny = RoutePresets.Create("flach", 3); tiny.@params.lengthKm = -5f; tiny.revision = -3;
                var t2 = RouteStore.FromJson(JsonUtility.ToJson(tiny));
                Ok(t2 != null && t2.@params.lengthKm >= 0.5f && t2.revision == 1, "negative Werte nicht begrenzt");

                // Import states and no lost local content.
                var mine = RoutePresets.Create("huegelig", 11);
                mine.meta.name = "Meine Runde";
                RouteStore.Save(mine);
                var local = RouteStore.LoadAll();
                var same = RouteShare.FromCode(RouteShare.ToCode(mine), out _, out _);
                Ok(RouteShare.StateOf(same, local, out bool imp) == "schon vorhanden" && !imp, "gleiche Strecke nicht „schon vorhanden“");
                RouteShare.Import(same);
                Ok(RouteStore.LoadAll().Count == 1, "gleiche Strecke doppelt importiert");

                var renamed = RouteShare.FromCode(RouteShare.ToCode(mine), out _, out _);
                renamed.meta.name = "Andere Runde";
                string st = RouteShare.StateOf(renamed, local, out imp);
                Ok(imp && st.StartsWith("gleiche Version"), $"gleiche Version, anderer Inhalt: „{st}“");
                RouteShare.Import(renamed);
                var afterCopy = RouteStore.LoadAll();
                Ok(afterCopy.Count == 2 && afterCopy.Exists(r => r.id == mine.id && r.meta.name == "Meine Runde")
                   && afterCopy.Exists(r => r.id != mine.id && r.meta.name.StartsWith("Andere Runde")), "gleiche Version: nicht als Kopie übernommen");

                var newer = RouteShare.FromCode(RouteShare.ToCode(mine), out _, out _);
                newer.revision = mine.revision + 1; newer.@params.lengthKm += 1f; RouteGenerator.Generate(newer);
                st = RouteShare.StateOf(newer, RouteStore.LoadAll(), out imp);
                Ok(imp && st.StartsWith("neuere Version"), $"neuere Version: „{st}“");
                RouteShare.Import(newer);
                var afterNewer = RouteStore.LoadAll();
                Ok(afterNewer.Count == 3 && afterNewer.Exists(r => r.id == mine.id && r.revision == newer.revision)
                   && afterNewer.Exists(r => r.id != mine.id && r.meta.name == "Meine Runde (lokal)" && Mathf.Approximately(r.@params.lengthKm, mine.@params.lengthKm)),
                   "neuere Version: lokale Strecke nicht als „(lokal)“ behalten");
                var older = RouteShare.FromCode(RouteShare.ToCode(mine), out _, out _);
                st = RouteShare.StateOf(older, RouteStore.LoadAll(), out imp);
                Ok(!imp && st.StartsWith("ältere Version"), $"ältere Version: „{st}“");
            }
            catch (Exception e) { fails.Add("Ausnahme: " + e); }
            finally
            {
                RouteStore.RootOverride = null;
                try { Directory.Delete(root, true); } catch { }
            }
            if (fails.Count == 0) Debug.Log("[RouteShareCheck] Alle Prüfungen bestanden.");
            else Debug.LogError("[RouteShareCheck] FAIL → " + string.Join("; ", fails));
            return fails.Count == 0;
        }

        // A share packet around a raw route JSON (bypasses ToCode, which already sanitizes).
        private static string Packet(string docJson) => "{\"doc\":" + docJson + ",\"profileCheck\":\"\"}";

        private static string Code(string json)
        {
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal, true))
            {
                var b = Encoding.UTF8.GetBytes(json);
                gz.Write(b, 0, b.Length);
            }
            return "JOG1:" + Convert.ToBase64String(ms.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
