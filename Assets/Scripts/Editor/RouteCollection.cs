using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Jogging.Route;

namespace Jogging.EditorTools
{
    /// <summary>
    /// The route collection in the repository (routes/): a handful of routes worth running, each as a
    /// .jogroute file (the recipe: parameters + seed, a few hundred bytes), its share code as a QR image and a
    /// row in routes/README.md. Fixed ids and seeds, so a re-run gives the same files.
    /// Headless: -executeMethod Jogging.EditorTools.RouteCollection.RunBatch [-routesout routes]
    /// Preview pictures: Tools/route-previews.sh (runs the app with -routefile … -skyown -skyshots).
    /// </summary>
    public static class RouteCollection
    {
        private class Entry
        {
            public string file, name, preset, about;
            public int seed;
            public System.Action<RouteParams> tweak;
        }

        private static readonly Entry[] Entries =
        {
            new Entry { file = "morning-mist-mill-brook", name = "Morning mist at the mill brook", preset = "wald", seed = 4711,
                about = "An autumn forest loop at 7:45 – ground fog in the hollows, a stream with a plank bridge, the sun just up.",
                tweak = p => { p.season = "autumn"; p.timeOfDay = 7.75f; p.haze = 0.6f; p.water = "stream"; p.lengthKm = 8f; } },
            new Entry { file = "golden-hour-lake", name = "Golden hour at the lake", preset = "see", seed = 1703,
                about = "A flat summer loop around a lake, low sun, long shadows – an easy evening run.",
                tweak = p => { p.season = "summer"; p.timeOfDay = 20.25f; p.weather = "clear"; } },
            new Entry { file = "village-edge-intervals", name = "Village-edge intervals", preset = "flach", seed = 2024,
                about = "5 km, starting on asphalt past the street lamps into the fields – made for interval workouts.",
                tweak = p => { p.lightPollution = 0.65f; p.timeOfDay = 18.5f; p.season = "summer"; } },
            new Entry { file = "hunting-stand-hills", name = "Hunting-stand hills", preset = "huegelig", seed = 3141,
                about = "Rolling meadows and forest edges in spring, two real climbs, hunting stands and hay fields.",
                tweak = p => { p.season = "spring"; p.vegetation = "meadow"; p.clearings = 0.6f; p.timeOfDay = 10f; } },
            new Entry { file = "october-pass", name = "October pass", preset = "pass", seed = 1010,
                about = "One long, steady climb to a mountain pass in autumn colours, and the way back down.",
                tweak = p => { p.season = "autumn"; p.timeOfDay = 11.5f; } },
            new Entry { file = "rain-run", name = "Rain run", preset = "wald", seed = 999,
                about = "Grey sky, steady rain, puddles and muddy forest paths – the run you'd skip outside.",
                tweak = p => { p.season = "autumn"; p.weather = "rain"; p.timeOfDay = 15f; p.lengthKm = 7f; } },
            new Entry { file = "snowy-winter-forest", name = "Snowy winter forest", preset = "winterwald", seed = 1224,
                about = "Falling snow in a conifer forest, the trail trodden into the snow.",
                tweak = p => { p.weather = "snow"; } },
            new Entry { file = "starlit-trail", name = "Starlit trail", preset = "mondnacht", seed = 2112,
                about = "A clear winter night at 22:00, new moon, far from any town – stars and the Milky Way, your head torch on the trail.",
                tweak = p => { p.timeOfDay = 22f; p.moonAge = 1f; p.lightPollution = 0.02f; } },
        };

        public static void RunBatch()
        {
            string dir = "routes";
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-routesout") dir = a[i + 1];
            Directory.CreateDirectory(dir);
            var table = new StringBuilder();
            foreach (var e in Entries)
            {
                var d = RoutePresets.Create(e.preset, e.seed);
                e.tweak(d.@params);
                RouteGenerator.Generate(d); // the length may have changed: profile again
                d.id = StableId(e.file); // a real route id (fixed): importing twice doesn't make a copy
                d.revision = 1;
                d.meta.name = e.name;
                d.meta.author = "Jogging route collection";
                d.meta.description = e.about;
                d.meta.created = d.meta.updated = "2026-09-30T00:00:00Z";
                d.meta.license = "CC0-1.0";
                File.WriteAllText(Path.Combine(dir, e.file + RouteStore.Extension), RouteStore.ToJson(d));
                var qr = QrCode.EncodeText(RouteShare.ToCode(d), QrCode.Ecc.Low);
                File.WriteAllBytes(Path.Combine(dir, e.file + "-qr.png"), Jogging.UI.QrTexture.Create(qr, 6).EncodeToPNG());
                var p = d.@params;
                string mood = $"{Season(p.season)}, {Clock(p.timeOfDay)}{(p.weather != "clear" ? ", " + p.weather : "")}";
                table.AppendLine($"| [![{e.name}]({e.file}.jpg)]({e.file}.jpg) | **{e.name}**<br>{e.about} | {d.@params.lengthKm:0.#} km{(p.loop ? " loop" : "")} · ↑{d.profile.ascentM:0} m · max {d.profile.maxGradePercent:0} % | {mood} | [.jogroute]({e.file}.jogroute) · [QR]({e.file}-qr.png) |");
                Debug.Log($"[RouteCollection] {e.file}: {p.lengthKm:0.0} km, ↑{d.profile.ascentM:0} m, Seed {e.seed}");
            }
            File.WriteAllText(Path.Combine(dir, "README.md"), Readme(table.ToString()));
            EditorApplication.Exit(0);
        }

        // "rt_" + 26 Crockford base32 characters from a hash of the file name
        private static string StableId(string key)
        {
            const string abc = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
            var h = System.Security.Cryptography.SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes("jogging-collection:" + key));
            var sb = new StringBuilder("rt_");
            for (int i = 0; i < 26; i++) sb.Append(abc[h[i] % 32]);
            return sb.ToString();
        }

        private static string Season(string s) => s == "spring" ? "spring" : s == "autumn" ? "autumn" : s == "winter" ? "winter" : "summer";
        private static string Clock(float h) => $"{Mathf.FloorToInt(h):00}:{Mathf.RoundToInt(h % 1f * 60f) % 60:00}";

        private static string Readme(string rows) => $@"# Route collection

A few routes worth running – each one is a small recipe (parameters and a seed), so the app builds exactly
the same landscape, trail and wayside for everyone. Free to use (CC0).

| | Route | Course | Mood | Get it |
|---|---|---|---|---|
{rows}
## How to add a route

- **File:** download the `.jogroute` file into your *Downloads* folder (on a tablet: the app's exchange
  folder), then in the app: *My routes* → *Import* – the route is listed there.
- **QR code (Mac):** save the QR image into *Downloads* (or on the Desktop), then *My routes* → *Import* →
  *Read QR image*.
- **Code:** scan the QR code with a phone and paste the text under *My routes* → *Import*.

Made your own? Share it the same way – on a route's page, *Share* writes the file and the QR code.

<sub>Generated by `Editor/RouteCollection.cs`; preview pictures by `Tools/route-previews.sh`.</sub>
";
    }
}
