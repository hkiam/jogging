using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Jogging.Profile;
using Jogging.Route;

namespace Jogging.EditorTools
{
    /// <summary>
    /// The playful side (Profile/Game, Route/ClimbSegments): climbs found in a profile, family records and
    /// crowns, the family journey, album, weekly quests, points and levels, the streak with rest days.
    /// Menu: Jogging → Prüfen → Spiel. Headless: -executeMethod Jogging.EditorTools.GameCheck.RunBatch
    /// </summary>
    public static class GameCheck
    {
        [MenuItem("Jogging/Prüfen/Spiel")]
        public static void RunMenu() => EditorUtility.DisplayDialog("Jogging", Run() ? "Alle Prüfungen bestanden." : "Fehler – siehe Console.", "OK");
        public static void RunBatch() => EditorApplication.Exit(Run() ? 0 : 1);

        private static SessionSummary S(DateTime local, float km, Action<SessionSummary> f = null)
        {
            var s = new SessionSummary { start = local.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"), distanceM = km * 1000f, seconds = km * 360f, file = Guid.NewGuid().ToString("N") };
            f?.Invoke(s);
            return s;
        }

        public static bool Run()
        {
            var fails = new List<string>();
            void Ok(bool c, string m) { if (!c) fails.Add(m); }

            // climbs: flat 200 m, +30 m over 600 m (with a 2 m dip), flat, a 5 m bump (no climb), down
            var h = new List<float>();
            for (int i = 0; i < 100; i++) h.Add(0f);
            for (int i = 0; i < 300; i++) h.Add(i * 0.1f - (i > 150 && i < 160 ? 2f : 0f));
            for (int i = 0; i < 100; i++) h.Add(30f);
            for (int i = 0; i < 50; i++) h.Add(30f + 5f * Mathf.Sin(i / 50f * Mathf.PI));
            for (int i = 0; i < 150; i++) h.Add(30f - i * 0.2f);
            var climbs = ClimbSegments.Find(h.ToArray(), 2f);
            Ok(climbs.Count == 1, $"Bergwertung: {climbs.Count} statt 1 Anstieg (Delle und kleine Kuppe zählen nicht)");
            if (climbs.Count == 1) Ok(Mathf.Abs(climbs[0].startM - 198f) < 6f && Mathf.Abs(climbs[0].gainM - 29.9f) < 0.5f && climbs[0].LengthM > 550f, $"Bergwertung: {climbs[0].startM:0} m, ↑{climbs[0].gainM:0.0}, {climbs[0].LengthM:0} m");
            var pass = RoutePresets.Create("pass", 1);
            Ok(ClimbSegments.Of(pass).Count >= 1, "Bergpass ohne Bergwertung");
            Ok(ClimbSegments.Of(RoutePresets.Create("flach", 1)).Count == 0, "Flach mit Bergwertung");

            // family records and crowns
            var now = new DateTime(2026, 10, 7, 18, 0, 0, DateTimeKind.Local); // a Wednesday
            var fam = new Dictionary<string, List<SessionSummary>>
            {
                ["a"] = new List<SessionSummary> { S(now.AddDays(-1), 5, s => { s.routeId = "rt_x"; s.revision = 1; s.climbS = new List<float> { 150f, 0f }; }) },
                ["b"] = new List<SessionSummary> { S(now.AddDays(-2), 5, s => { s.routeId = "rt_x"; s.revision = 1; s.climbS = new List<float> { 160f, 90f }; }),
                                                   S(now.AddDays(-3), 5, s => { s.routeId = "rt_x"; s.revision = 2; s.climbS = new List<float> { 100f }; }) },
            };
            var best = Game.ClimbBest(fam, "rt_x", 1, 0);
            Ok(best?.runnerId == "a" && best?.seconds == 150f, "Bergwertung: Rekord falsch");
            Ok(Game.ClimbBest(fam, "rt_x", 1, 1)?.runnerId == "b", "Bergwertung: 0 = nicht gelaufen");
            Ok(Game.Crowns(fam, "a") == 1 && Game.Crowns(fam, "b") == 2, $"Kronen {Game.Crowns(fam, "a")} / {Game.Crowns(fam, "b")} statt 1 / 2 (andere Revision zählt extra)");

            // family journey: 160 km over two runners → Hannover (155) reached; finishing starts the next journey
            var j1 = new Dictionary<string, List<SessionSummary>> { ["a"] = new List<SessionSummary> { S(now.AddDays(-5), 100) }, ["b"] = new List<SessionSummary> { S(now.AddDays(-4), 60) } };
            var st = Game.JourneyNow(j1);
            Ok(st.journey.id == Game.Journeys[0].id && Mathf.Abs(st.done - 160f) < 0.01f && st.last.name == "Hannover" && st.next.name == "Göttingen", $"Reise: {st.done} km, zuletzt {st.last.name}");
            var before = new Dictionary<string, List<SessionSummary>> { ["a"] = j1["a"], ["b"] = new List<SessionSummary>() };
            var (stops, done) = Game.JourneyStep(Game.JourneyNow(before), st);
            Ok(!done && stops.SequenceEqual(new[] { "Hannover" }), "Reise: Etappe nicht gemeldet");
            j1["a"].Add(S(now.AddDays(-3), 700));
            var st2 = Game.JourneyNow(j1);
            Ok(st2.round == 1 && st2.journey.id == Game.Journeys[1].id && st2.journey.elevation, "Reise: nach München die Gipfelreise");
            Ok(Game.JourneyStep(st, st2).finished, "Reise: Ziel nicht gemeldet");

            // album
            var r1 = S(now.AddDays(-2), 3, s => s.sights = new List<string> { "Hase:2", "Kapelle" });
            var r2 = S(now.AddDays(-1), 3, s => s.sights = new List<string> { "Hase", "Fuchs", "Nachtlauf" });
            var album = Game.Album(new[] { r1, r2 });
            Ok(album["Hase"].count == 3 && album["Hase"].first == r1.start && album.ContainsKey("Fuchs"), "Album: Zählung");
            Ok(Game.NewCards(new[] { r1 }, r2).SequenceEqual(new[] { "Fuchs", "Nachtlauf" }), $"Album neu: {string.Join(",", Game.NewCards(new[] { r1 }, r2))}");
            var moods = Game.Moods(new RouteParams { timeOfDay = 22f, weather = "clear", haze = 0.7f }, 14.5);
            Ok(moods.Contains("Nachtlauf") && moods.Contains("Vollmond") && moods.Contains("Nebel") && !moods.Contains("Morgenrot"), "Album: Stimmungen");
            foreach (var c in Game.Cards) Ok(Jogging.Core.Loc.HasEnglish(c.key), "Album-Karte ohne Übersetzung: " + c.key);

            // weekly quests: three, the same for everybody in a week, other ones next week (mostly)
            var ws = RunnerStats.WeekStart(now);
            var q = Game.Quests(ws);
            Ok(q.Count == 3 && q.Select(x => x.id).Distinct().Count() == 3, "Wochenaufgaben: nicht 3 verschiedene");
            Ok(Game.Quests(ws).Select(x => x.id).SequenceEqual(q.Select(x => x.id)), "Wochenaufgaben: nicht stabil");
            int differ = 0; for (int k = 1; k <= 8; k++) if (!Game.Quests(ws.AddDays(7 * k)).Select(x => x.id).SequenceEqual(q.Select(x => x.id))) differ++;
            Ok(differ >= 6, "Wochenaufgaben wechseln kaum");
            // a week where "3 days" and "100 m up" and "over 5 km" are all done
            var week = new List<SessionSummary> { S(ws.AddHours(7), 6, s => s.gainM = 60), S(ws.AddDays(1).AddHours(18), 3, s => s.gainM = 50), S(ws.AddDays(3).AddHours(18), 3) };
            var pool = new[] { "days3", "climb100", "long5" };
            foreach (var quest in Game.Quests(ws).Where(x => pool.Contains(x.id))) Ok(quest.value(week) >= quest.goal, "Wochenaufgabe nicht erfüllt: " + quest.id);

            // points and levels
            Ok(Game.XpFor(1) == 0 && Game.XpFor(2) == 100 && Game.XpFor(3) == 300 && Game.Level(0) == 1 && Game.Level(299) == 2 && Game.Level(300) == 3, "Level-Stufen");
            var xpRun = S(now, 5, s => s.gainM = 40);
            Ok(Game.RunXp(xpRun) == 50 + 4 + 5, $"Punkte je Lauf {Game.RunXp(xpRun)} statt 59");
            Ok(Game.Xp(new[] { r1, r2 }) >= Game.RunXp(r1) + Game.RunXp(r2) + 4 * Game.XpPerCard, "Punkte ohne Album");
            Ok(Game.Shirt("gold").level == 10 && Game.Shirt("nix").id == "" && Game.Shirts.Select(s => s.level).SequenceEqual(Game.Shirts.Select(s => s.level).OrderBy(l => l)), "Shirtfarben");
            foreach (var s in Game.Shirts) Ok(Jogging.Core.Loc.HasEnglish(s.name), "Shirtfarbe ohne Übersetzung: " + s.name);

            // streak with rest days: Mon, Wed, Sat, Sun → alive on Tuesday (2 rest days), dead on Wednesday
            var mon = ws;
            var streakRuns = new[] { S(mon.AddHours(8), 3), S(mon.AddDays(2).AddHours(8), 3), S(mon.AddDays(5).AddHours(8), 3), S(mon.AddDays(6).AddHours(8), 3) };
            var (d1, n1) = Game.Streak(streakRuns, mon.AddDays(8).AddHours(12));
            Ok(d1 == 9 && n1 == 4, $"Serie {d1} Tage / {n1} Lauftage statt 9 / 4");
            Ok(Game.Streak(streakRuns, mon.AddDays(9).AddHours(12)).days == 0, "Serie lebt nach 3 Ruhetagen");
            var gap = new[] { S(mon.AddHours(8), 3), S(mon.AddDays(4).AddHours(8), 3) }; // 3 rest days between: a new streak
            Ok(Game.Streak(gap, mon.AddDays(4).AddHours(12)).days == 1, "Serie über 3 Ruhetage");

            foreach (var f in fails) Debug.LogError("[GameCheck] FAIL " + f);
            if (fails.Count == 0) Debug.Log("[GameCheck] Alle Prüfungen bestanden.");
            return fails.Count == 0;
        }
    }
}
