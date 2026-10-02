using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Jogging.Route;

namespace Jogging.Profile
{
    /// <summary>
    /// The playful side, computed from the logbook only (nothing to lose, nothing to get out of step; plain C#,
    /// tested by GameCheck): climb segments with family records, the family journey, the album of things seen,
    /// weekly quests, experience points with levels (unlocking shirt colours) and the streak with rest days.
    /// It rewards running regularly, not running hard.
    /// </summary>
    public static class Game
    {
        public static IEnumerable<SessionSummary> All(IDictionary<string, List<SessionSummary>> byRunner) =>
            byRunner.Values.SelectMany(x => x);

        // ================================================================== climb segments ("Bergwertungen")

        public struct ClimbRecord { public string runnerId; public float seconds; public string start; }

        /// <summary>The family's fastest time on a climb of a route (null if nobody ran it yet).</summary>
        public static ClimbRecord? ClimbBest(IDictionary<string, List<SessionSummary>> byRunner, string routeId, int revision, int index, string excludeFile = null)
        {
            ClimbRecord? best = null;
            if (string.IsNullOrEmpty(routeId)) return null;
            foreach (var kv in byRunner)
                foreach (var s in kv.Value)
                {
                    if (s.routeId != routeId || s.revision != revision || s.file == excludeFile || s.climbS == null || s.climbS.Count <= index) continue;
                    float t = s.climbS[index];
                    if (t > 0f && (best == null || t < best.Value.seconds)) best = new ClimbRecord { runnerId = kv.Key, seconds = t, start = s.start };
                }
            return best;
        }

        /// <summary>How many climb records ("Kronen") a runner holds over all routes.</summary>
        public static int Crowns(IDictionary<string, List<SessionSummary>> byRunner, string runnerId)
        {
            var keys = new HashSet<(string, int, int)>();
            foreach (var s in All(byRunner))
                if (!string.IsNullOrEmpty(s.routeId) && s.climbS != null)
                    for (int i = 0; i < s.climbS.Count; i++) if (s.climbS[i] > 0f) keys.Add((s.routeId, s.revision, i));
            int n = 0;
            foreach (var (id, rev, i) in keys) if (ClimbBest(byRunner, id, rev, i)?.runnerId == runnerId) n++;
            return n;
        }

        // ================================================================== family journey

        public class Journey
        {
            public string id, name;
            public bool elevation;               // collects metres up instead of kilometres
            public (string name, float at)[] stops; // km (or m up) from the start, the last one is the goal
            public float Total => stops[stops.Length - 1].at;
        }

        public static readonly Journey[] Journeys =
        {
            new Journey { id = "hh-muc", name = "Von Hamburg nach München", stops = new[] {
                ("Hamburg", 0f), ("Lüneburg", 55f), ("Hannover", 155f), ("Göttingen", 275f), ("Kassel", 320f), ("Fulda", 425f),
                ("Würzburg", 525f), ("Nürnberg", 630f), ("Ingolstadt", 720f), ("München", 780f) } },
            new Journey { id = "gipfel", name = "Höhenmeter bis zur Zugspitze", elevation = true, stops = new[] {
                ("Meereshöhe", 0f), ("Kölner Dom", 157f), ("Großer Feldberg (Taunus)", 879f), ("Brocken", 1141f), ("Großer Arber", 1456f),
                ("Feldberg (Schwarzwald)", 1493f), ("Watzmann", 2713f), ("Zugspitze", 2962f) } },
            new Journey { id = "bodensee", name = "Einmal um den Bodensee", stops = new[] {
                ("Konstanz", 0f), ("Überlingen", 40f), ("Friedrichshafen", 70f), ("Lindau", 100f), ("Bregenz", 112f), ("Rorschach", 135f),
                ("Romanshorn", 160f), ("Stein am Rhein", 205f), ("Radolfzell", 230f), ("Konstanz", 260f) } },
            new Journey { id = "rhein", name = "Den Rhein entlang: Basel → Rotterdam", stops = new[] {
                ("Basel", 0f), ("Straßburg", 145f), ("Karlsruhe", 230f), ("Mannheim", 300f), ("Mainz", 375f), ("Loreley", 445f), ("Koblenz", 480f),
                ("Bonn", 545f), ("Köln", 580f), ("Düsseldorf", 620f), ("Nimwegen", 760f), ("Rotterdam", 880f) } },
        };

        public struct JourneyState
        {
            public Journey journey;
            public float done;                  // km or m up so far
            public int round;                   // how many journeys the family has finished
            public (string name, float at) next; // the next stop
            public (string name, float at) last; // the last stop reached
            public float Fraction => Mathf.Clamp01(done / journey.Total);
        }

        /// <summary>
        /// Where the family is: journeys follow each other (the first one again after the last); a run counts
        /// for the journey that was going on when it started. The kilometres (or metres up) of all runners.
        /// </summary>
        public static JourneyState JourneyNow(IDictionary<string, List<SessionSummary>> byRunner)
        {
            var runs = All(byRunner).OrderBy(r => r.start, StringComparer.Ordinal).ToList();
            int k = 0; float done = 0f;
            foreach (var r in runs)
            {
                var j = Journeys[k % Journeys.Length];
                done += j.elevation ? r.gainM : r.distanceM / 1000f;
                if (done >= j.Total) { done = 0f; k++; } // the rest of the run doesn't carry over: every journey starts at its start
            }
            var cur = Journeys[k % Journeys.Length];
            var st = new JourneyState { journey = cur, done = done, round = k, next = cur.stops[cur.stops.Length - 1], last = cur.stops[0] };
            foreach (var s in cur.stops) { if (s.at <= done) st.last = s; else { st.next = s; break; } }
            return st;
        }

        /// <summary>What a run did for the journey: stops reached ("Hannover"), and whether it finished it.</summary>
        public static (List<string> stops, bool finished) JourneyStep(JourneyState before, JourneyState after)
        {
            var reached = new List<string>();
            if (after.round > before.round)
            {
                foreach (var s in before.journey.stops) if (s.at > before.done) reached.Add(s.name);
                return (reached, true);
            }
            foreach (var s in after.journey.stops) if (s.at > before.done && s.at <= after.done) reached.Add(s.name);
            return (reached, false);
        }

        // ================================================================== album

        public struct Card { public string key, group; public int rarity; } // rarity 1 common, 2 rare, 3 very rare

        public static readonly Card[] Cards =
        {
            new Card { key = "Hase", group = "Tiere", rarity = 1 }, new Card { key = "Kaninchen", group = "Tiere", rarity = 2 },
            new Card { key = "Hirsch", group = "Tiere", rarity = 2 }, new Card { key = "Fuchs", group = "Tiere", rarity = 3 },
            new Card { key = "Krähen", group = "Tiere", rarity = 1 }, new Card { key = "Ameisenhaufen", group = "Tiere", rarity = 2 },
            new Card { key = "Brücke", group = "Am Weg", rarity = 1 }, new Card { key = "Hochsitz", group = "Am Weg", rarity = 1 },
            new Card { key = "Holzpolter", group = "Am Weg", rarity = 1 }, new Card { key = "umgestürzter Baum", group = "Am Weg", rarity = 1 },
            new Card { key = "Feldscheune", group = "Am Weg", rarity = 2 }, new Card { key = "Schutzhütte", group = "Am Weg", rarity = 2 },
            new Card { key = "Ortsrand", group = "Am Weg", rarity = 1 }, new Card { key = "Wegkreuz", group = "Am Weg", rarity = 2 },
            new Card { key = "Kapelle", group = "Am Weg", rarity = 3 }, new Card { key = "Ruine", group = "Am Weg", rarity = 3 },
            new Card { key = "Aussichtspunkt", group = "Am Weg", rarity = 3 },
            new Card { key = "Morgenrot", group = "Stimmungen", rarity = 2 }, new Card { key = "Abendrot", group = "Stimmungen", rarity = 2 },
            new Card { key = "Nachtlauf", group = "Stimmungen", rarity = 2 }, new Card { key = "Vollmond", group = "Stimmungen", rarity = 3 },
            new Card { key = "Schnee", group = "Stimmungen", rarity = 2 }, new Card { key = "Regen", group = "Stimmungen", rarity = 1 },
            new Card { key = "Nebel", group = "Stimmungen", rarity = 2 },
        };

        public struct Collected { public int count; public string first; }

        /// <summary>The runner's album: card key → how often seen and when first.</summary>
        public static Dictionary<string, Collected> Album(IEnumerable<SessionSummary> runs)
        {
            var d = new Dictionary<string, Collected>();
            foreach (var r in runs.OrderBy(x => x.start, StringComparer.Ordinal))
            {
                if (r.sights == null) continue;
                foreach (var e in r.sights)
                {
                    int c = e.IndexOf(':');
                    string key = c > 0 ? e.Substring(0, c) : e;
                    int n = c > 0 && int.TryParse(e.Substring(c + 1), out int v) ? v : 1;
                    d[key] = d.TryGetValue(key, out var x) ? new Collected { count = x.count + n, first = x.first } : new Collected { count = n, first = r.start };
                }
            }
            return d;
        }

        /// <summary>Cards of the album the run just recorded added for the first time.</summary>
        public static List<string> NewCards(IEnumerable<SessionSummary> runsBefore, SessionSummary run)
        {
            var had = Album(runsBefore);
            var list = new List<string>();
            foreach (var e in run.sights ?? new List<string>())
            {
                int c = e.IndexOf(':'); string key = c > 0 ? e.Substring(0, c) : e;
                if (!had.ContainsKey(key) && Cards.Any(k => k.key == key) && !list.Contains(key)) list.Add(key);
            }
            return list;
        }

        /// <summary>The mood of a run as album keys (noted when it starts; RunGame).</summary>
        public static List<string> Moods(RouteParams p, double moonAge)
        {
            var l = new List<string>();
            float h = p.timeOfDay;
            if (h < 5.5f || h >= 21.75f) l.Add("Nachtlauf");
            else if (h < 7.6f) l.Add("Morgenrot");
            else if (h >= 19.3f) l.Add("Abendrot");
            if ((h < 5.5f || h >= 21.75f) && moonAge >= 12.8 && moonAge <= 16.8 && p.weather == "clear") l.Add("Vollmond");
            if (p.weather == "snow") l.Add("Schnee");
            if (p.weather == "rain") l.Add("Regen");
            if (p.haze >= 0.65f) l.Add("Nebel");
            return l;
        }

        // ================================================================== weekly quests

        public class Quest
        {
            public string id, text;
            public float goal;
            public Func<List<SessionSummary>, float> value;  // the week's runs of one runner → progress towards goal
        }

        private static readonly string[] QuestSights = { "Brücke", "Hochsitz", "Hase", "Kapelle", "Aussichtspunkt", "Holzpolter", "Feldscheune", "Wegkreuz" };

        private static List<Quest> QuestPool(System.Random rng)
        {
            string sight = QuestSights[rng.Next(QuestSights.Length)];
            return new List<Quest>
            {
                new Quest { id = "days3", text = "An 3 verschiedenen Tagen laufen", goal = 3, value = w => w.Select(r => RunnerStats.ParseUtc(r.start).ToLocalTime().Date).Distinct().Count() },
                new Quest { id = "climb100", text = "100 Höhenmeter sammeln", goal = 100, value = w => w.Sum(r => r.gainM) },
                new Quest { id = "long5", text = "Ein Lauf über 5 km", goal = 1, value = w => w.Any(r => r.distanceM >= 5000f) ? 1 : 0 },
                new Quest { id = "minutes60", text = "60 Minuten laufen", goal = 60, value = w => w.Sum(r => r.seconds) / 60f },
                new Quest { id = "overtake15", text = "15 Läufer überholen", goal = 15, value = w => w.Sum(r => r.overtakes) },
                new Quest { id = "segment", text = "Eine Bergwertung schaffen", goal = 1, value = w => w.Any(r => r.climbS != null && r.climbS.Any(t => t > 0f)) ? 1 : 0 },
                new Quest { id = "early", text = "Einmal vor 8 Uhr laufen", goal = 1, value = w => w.Any(r => RunnerStats.ParseUtc(r.start).ToLocalTime().Hour < 8) ? 1 : 0 },
                new Quest { id = "zone2", text = "20 Minuten in Zone 2 (Pulsgurt)", goal = 20, value = w => w.Sum(r => r.hrZoneSeconds != null && r.hrZoneSeconds.Count >= 2 ? r.hrZoneSeconds[1] : 0f) / 60f },
                new Quest { id = "sight:" + sight, text = Jogging.Core.Loc.F("Finde: {0}", Jogging.Core.Loc.T(sight)), goal = 1,
                            value = w => w.Any(r => r.sights != null && r.sights.Any(e => e == sight || e.StartsWith(sight + ":"))) ? 1 : 0 },
            };
        }

        /// <summary>The three quests of the week that starts at weekStart (local Monday) – the same for everybody.</summary>
        public static List<Quest> Quests(DateTime weekStart)
        {
            int seed = weekStart.Year * 100 + RunnerStats.IsoWeek(weekStart);
            var rng = new System.Random(seed);
            var pool = QuestPool(rng);
            var pick = new List<Quest>();
            while (pick.Count < 3 && pool.Count > 0) { int i = rng.Next(pool.Count); pick.Add(pool[i]); pool.RemoveAt(i); }
            return pick;
        }

        public static List<SessionSummary> WeekRuns(IEnumerable<SessionSummary> runs, DateTime weekStart) =>
            runs.Where(r => { var t = RunnerStats.ParseUtc(r.start).ToLocalTime(); return t >= weekStart && t < weekStart.AddDays(7); }).ToList();

        /// <summary>The week's quests for a runner: (quest, progress 0…1, done).</summary>
        public static List<(Quest q, float fraction, bool done)> QuestProgress(IEnumerable<SessionSummary> runs, DateTime nowLocal)
        {
            var ws = RunnerStats.WeekStart(nowLocal);
            var week = WeekRuns(runs, ws);
            return Quests(ws).Select(q => { float v = q.value(week); return (q, Mathf.Clamp01(v / q.goal), v >= q.goal); }).ToList();
        }

        // ================================================================== experience points, levels, shirt colours

        public const int XpPerQuest = 50, XpPerCard = 15, XpPerClimb = 5;

        public static int RunXp(SessionSummary r) => Mathf.RoundToInt(r.distanceM / 100f) + Mathf.RoundToInt(r.gainM / 10f) + 5
                                                     + (r.climbS?.Count(t => t > 0f) ?? 0) * XpPerClimb;

        /// <summary>All experience points of a runner: runs, quests done in every week, album cards.</summary>
        public static int Xp(IReadOnlyList<SessionSummary> runs)
        {
            int xp = runs.Sum(RunXp);
            foreach (var g in runs.GroupBy(r => RunnerStats.WeekStart(RunnerStats.ParseUtc(r.start).ToLocalTime())))
            {
                var week = g.ToList();
                xp += Quests(g.Key).Count(q => q.value(week) >= q.goal) * XpPerQuest;
            }
            xp += Album(runs).Keys.Count(k => Cards.Any(c => c.key == k)) * XpPerCard;
            return xp;
        }

        /// <summary>Points needed for a level: 0, 100, 300, 600, 1000, 1500 … (50 · L · (L − 1)).</summary>
        public static int XpFor(int level) => 50 * level * (level - 1);

        public static int Level(int xp) { int l = 1; while (xp >= XpFor(l + 1)) l++; return l; }

        public struct ShirtColour { public string id, name; public Color color; public int level; }

        /// <summary>Shirt colours (only looks, no advantage), unlocked by level; "" = the figure's own.</summary>
        public static readonly ShirtColour[] Shirts =
        {
            new ShirtColour { id = "", name = "eigene Farbe", color = Color.white, level = 1 },
            new ShirtColour { id = "red", name = "Rot", color = new Color(0.80f, 0.10f, 0.12f), level = 2 },
            new ShirtColour { id = "blue", name = "Königsblau", color = new Color(0.12f, 0.28f, 0.80f), level = 3 },
            new ShirtColour { id = "neon", name = "Neongelb", color = new Color(0.85f, 0.95f, 0.10f), level = 4 },
            new ShirtColour { id = "green", name = "Grün", color = new Color(0.10f, 0.62f, 0.25f), level = 5 },
            new ShirtColour { id = "pink", name = "Pink", color = new Color(0.95f, 0.30f, 0.62f), level = 6 },
            new ShirtColour { id = "teal", name = "Türkis", color = new Color(0.05f, 0.65f, 0.70f), level = 7 },
            new ShirtColour { id = "orange", name = "Orange", color = new Color(0.98f, 0.48f, 0.08f), level = 8 },
            new ShirtColour { id = "white", name = "Weiß", color = new Color(0.92f, 0.92f, 0.92f), level = 9 },
            new ShirtColour { id = "gold", name = "Gold", color = new Color(0.90f, 0.70f, 0.20f), level = 10 },
        };

        public static ShirtColour Shirt(string id)
        {
            foreach (var s in Shirts) if (s.id == (id ?? "")) return s;
            return Shirts[0];
        }

        // ================================================================== streak with rest days

        /// <summary>
        /// The streak with rest days: days since the first run of a chain in which never more than
        /// <paramref name="rest"/> days in a row were without a run – and it is still alive (the last run at most
        /// that long ago). Returns (days, run days).
        /// </summary>
        public static (int days, int runDays) Streak(IEnumerable<SessionSummary> runs, DateTime nowLocal, int rest = 2)
        {
            var days = runs.Select(r => RunnerStats.ParseUtc(r.start).ToLocalTime().Date).Distinct().OrderByDescending(d => d).ToList();
            if (days.Count == 0 || (nowLocal.Date - days[0]).TotalDays > rest) return (0, 0);
            var first = days[0]; int n = 1;
            for (int i = 1; i < days.Count; i++)
            {
                if ((days[i - 1] - days[i]).TotalDays - 1 > rest) break;
                first = days[i]; n++;
            }
            return ((int)(nowLocal.Date - first).TotalDays + 1, n);
        }
    }
}
