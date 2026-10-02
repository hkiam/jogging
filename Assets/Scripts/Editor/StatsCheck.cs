using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Jogging.Profile;

namespace Jogging.EditorTools
{
    /// <summary>
    /// Self-check of statistics and gamification: fastest 5 km from samples, periods, family
    /// ranking, weekly challenges, and achievements that look at the logbook (streak, workouts,
    /// treadmill, heart rate zone, own route) — each unlocked exactly once.
    /// Menu: Jogging → Läufer → Statistik prüfen. Headless: -executeMethod Jogging.EditorTools.StatsCheck.RunBatch
    /// </summary>
    [System.Serializable] internal class HealthProbe { public string id; public double start, distanceM; public double[] t; public int[] hr; }

    public static class StatsCheck
    {
        [MenuItem("Jogging/Läufer/Statistik prüfen")]
        public static void RunMenu() => EditorUtility.DisplayDialog("Jogging", Run() ? "Alle Prüfungen bestanden." : "Fehler – siehe Console.", "OK");

        public static void RunBatch() => EditorApplication.Exit(Run() ? 0 : 1);

        private static SessionSummary S(DateTime local, float m, float s, Action<SessionSummary> more = null)
        {
            var r = new SessionSummary { start = local.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"), distanceM = m, seconds = s };
            more?.Invoke(r);
            return r;
        }

        public static bool Run()
        {
            var fails = new List<string>();
            void Ok(bool c, string what) { if (!c) fails.Add(what); }
            var now = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Local); // Thursday

            // Fastest 5 km: 2 km at 10 km/h, 5 km at 12 km/h, 1 km at 10 km/h (1 Hz samples).
            var samples = new List<SessionSample>();
            float t = 0f, d = 0f;
            void Run(float meters, float kmh) { float v = kmh / 3.6f; for (float x = 0f; x < meters - 0.001f; x += v) { t += 1f; d += v; samples.Add(new SessionSample { t = t, distM = d, kmh = kmh }); } }
            samples.Add(new SessionSample { t = 0f, distM = 0f });
            Run(2000f, 10f); Run(5000f, 12f); Run(1000f, 10f);
            float best = RunnerStats.BestSegmentSeconds(samples, 5000f);
            Ok(Mathf.Abs(best - 1500f) < 3f, $"schnellste 5 km {best:0.0} s statt 1500 s");
            Ok(RunnerStats.BestSegmentSeconds(samples, 20000f) == 0f, "5 km in zu kurzem Lauf");

            // Periods and ranking
            var maik = new List<SessionSummary> { S(now.AddHours(-2), 5000f, 1800f), S(now.AddDays(-1), 3000f, 1000f), S(now.AddDays(-40), 8000f, 2900f) };
            var anna = new List<SessionSummary> { S(now.AddDays(-2), 9000f, 3000f) };
            var per = RunnerStats.Periods(maik, now);
            Ok(per[0].t.Runs == 1 && per[1].t.Runs == 2 && per[4].t.Runs == 3, $"Zeiträume: heute {per[0].t.Runs}, Woche {per[1].t.Runs}, gesamt {per[4].t.Runs}");
            var rank = RunnerStats.WeeklyRanking(new Dictionary<string, List<SessionSummary>> { ["m"] = maik, ["a"] = anna, ["x"] = new List<SessionSummary>() }, now);
            Ok(rank.Count == 2 && rank[0].Key == "a" && rank[1].Key == "m" && Mathf.Approximately(rank[1].Value, 8000f), "Familien-Rangliste");

            // Weekly history: 12 weeks, current week last; the 40-day-old run is 6 weeks back
            var wk = RunnerStats.Weekly(maik, now, 12);
            Ok(wk.Length == 12 && wk[11].Runs == 2 && Mathf.Approximately(wk[11].DistanceM, 8000f), $"Verlauf: aktuelle Woche {wk[11].Runs} Läufe");
            int back = 11 - (int)((RunnerStats.WeekStart(now) - RunnerStats.WeekStart(now.AddDays(-40))).TotalDays / 7);
            Ok(wk[back].Runs == 1 && Mathf.Approximately(wk[back].DistanceM, 8000f), $"Verlauf: alter Lauf nicht in Woche {back}");
            Ok(RunnerStats.Weekly(maik, now, 4)[0].Runs == 0, "Verlauf: Lauf außerhalb des Fensters gezählt");
            Ok(RunnerStats.IsoWeek(new DateTime(2026, 9, 24)) == 39 && RunnerStats.IsoWeek(new DateTime(2027, 1, 1)) == 53 && RunnerStats.IsoWeek(new DateTime(2026, 1, 1)) == 1,
               $"Kalenderwoche: {RunnerStats.IsoWeek(new DateTime(2026, 9, 24))}/{RunnerStats.IsoWeek(new DateTime(2027, 1, 1))}/{RunnerStats.IsoWeek(new DateTime(2026, 1, 1))}");

            // Weekly challenges: 2 runs, 8 km, 46.7 min this week
            var ch = Challenges.ThisWeek(maik, now);
            Ok(ch.Count == 3 && Mathf.Approximately(ch[0].Value, 2f) && !ch[0].Done, $"3 Läufe: {ch[0].Value}");
            Ok(Mathf.Approximately(ch[1].Value, 8f) && Mathf.Approximately(ch[1].Fraction, 8f / 15f), $"15 km: {ch[1].Value}");
            var ch3 = Challenges.ThisWeek(new List<SessionSummary>(maik) { S(now.AddHours(-1), 7000f, 2600f) }, now);
            Ok(ch3[0].Done && ch3[1].Done && ch3[2].Done && Mathf.Approximately(ch3[1].Fraction, 1f), "Wochenziele erreicht");

            // Family challenge: kilometres of this week together
            var fam = Challenges.Family(new Dictionary<string, List<SessionSummary>> { ["m"] = maik, ["a"] = anna }, now);
            Ok(Mathf.Approximately(fam.totalKm, 17f) && fam.perRunner[0].Key == "a" && Mathf.Approximately(fam.perRunner[1].Value, 8f), $"Familie: {fam.totalKm} km");
            Ok(Challenges.FamilyGoalKm(3, 0f) == 30f && Challenges.FamilyGoalKm(0, 0f) == 5f && Challenges.FamilyGoalKm(3, 42f) == 42f, "Familienziel");

            // Goals per runner, max heart rate from the age, calories
            var kid = new ProfileData { weekGoalRuns = 2, weekGoalKm = 5f, weekGoalMinutes = 40 };
            var kc = Challenges.ThisWeek(maik, now, kid);
            Ok(kc[0].Done && kc[1].Done && kc[2].Done && kc[1].Challenge.Target == 5f, "eigene Wochenziele");
            Ok(Challenges.ThisWeek(maik, now, new ProfileData())[1].Challenge.Target == Challenges.DefaultKm, "Standard-Wochenziel");
            Ok(Jogging.Training.HeartRateZones.FromAge(1980, 2026) == 176, $"max. Puls aus Alter {Jogging.Training.HeartRateZones.FromAge(1980, 2026)} statt 176");
            Ok(Jogging.Training.HeartRateZones.ForRunner(185, 1980, 2026) == 185, "eingestellter max. Puls hat Vorrang");
            Ok(Jogging.Training.HeartRateZones.ForRunner(0, 0, 2026) == Jogging.Training.HeartRateZones.DefaultMax, "max. Puls Standard");
            double k10 = Jogging.Training.Calories.For(10f, 0f, 3600f, 70f); // 10 km/h, 1 h, 70 kg ≈ 700–800 kcal
            Ok(k10 > 650 && k10 < 850, $"Kalorien 10 km/h 1 h: {k10:0}");
            Ok(Jogging.Training.Calories.For(10f, 5f, 3600f, 70f) > k10, "Steigung ohne Mehrverbrauch");
            Ok(Jogging.Training.Calories.For(10f, 0f, 3600f, 0f) == 0.0, "Kalorien ohne Gewicht");

            // Ghost: position after t seconds, time at a distance, choosing the ghost run
            var gr = new SessionRecord();
            gr.samples.Add(new SessionSample { t = 0f, distM = 100f });
            gr.samples.Add(new SessionSample { t = 10f, distM = 130f, kmh = 10.8f });
            gr.samples.Add(new SessionSample { t = 20f, distM = 150f, kmh = 7.2f });
            var gt = new GhostTrack(gr);
            Ok(Mathf.Approximately(gt.DistanceAt(5f), 15f) && Mathf.Approximately(gt.DistanceAt(15f), 40f) && Mathf.Approximately(gt.DistanceAt(99f), 50f), $"Geist-Distanz {gt.DistanceAt(5f)}/{gt.DistanceAt(15f)}");
            Ok(Mathf.Approximately(gt.TimeAt(40f), 15f) && float.IsNaN(gt.TimeAt(60f)) && Mathf.Approximately(gt.TimeAt(0f), 0f), $"Geist-Zeit {gt.TimeAt(40f)}");
            var onRoute = new List<SessionSummary>
            {
                S(now.AddDays(-9), 5000f, 1500f, r => { r.routeId = "rt_a"; r.completed = true; }),
                S(now.AddDays(-5), 5000f, 1400f, r => { r.routeId = "rt_a"; r.completed = true; }),
                S(now.AddDays(-1), 2000f, 700f, r => { r.routeId = "rt_a"; }),
                S(now.AddDays(-2), 5000f, 1000f, r => { r.routeId = "rt_a"; r.completed = true; r.workoutId = "builtin:x"; }),
                S(now.AddDays(-3), 5000f, 1300f, r => { r.routeId = "rt_a"; r.completed = true; r.revision = 2; }),
            };
            foreach (var r in onRoute) if (r.revision == 0) r.revision = 1;
            Ok(GhostTrack.Pick(onRoute, "rt_a", 1)?.seconds == 1400f, "Geist: schnellster kompletter Lauf");
            Ok(GhostTrack.Pick(onRoute.GetRange(2, 1), "rt_a", 1)?.distanceM == 2000f, "Geist: sonst letzter Lauf");
            Ok(GhostTrack.Pick(onRoute, "rt_b", 1) == null && GhostTrack.Pick(onRoute, "", 1) == null, "Geist auf fremder Strecke");

            // TCX export: well-formed XML with one trackpoint per sample, heart rate, invariant numbers.
            var rec = new SessionRecord();
            rec.summary.start = "2026-09-24T10:00:00Z"; rec.summary.distanceM = 1234.5f; rec.summary.seconds = 3f;
            rec.summary.avgHr = 140; rec.summary.maxHr = 150; rec.summary.kcal = 12.4f; rec.summary.routeName = "Wald & <Wiese>";
            for (int i = 0; i < 3; i++) rec.samples.Add(new SessionSample { t = i, distM = i * 3.3f, kmh = 12f, hr = 140 + i });
            string tcx = TcxExport.ToTcx(rec);
            try
            {
                var x = System.Xml.Linq.XDocument.Parse(tcx);
                System.Xml.Linq.XNamespace ns = "http://www.garmin.com/xmlschemas/TrainingCenterDatabase/v2";
                int tps = 0; foreach (var _ in x.Descendants(ns + "Trackpoint")) tps++;
                Ok(tps == 3, $"TCX: {tps} Trackpoints statt 3");
                Ok(tcx.Contains("<DistanceMeters>1234.5</DistanceMeters>") && tcx.Contains("<Time>2026-09-24T10:00:02Z</Time>") && tcx.Contains("<Value>142</Value>"), "TCX: Werte fehlen");
                Ok(tcx.Contains("Wald &amp; &lt;Wiese&gt;"), "TCX: Notiz nicht maskiert");
            }
            catch (Exception e) { fails.Add("TCX ist kein gültiges XML: " + e.Message); }

            // Apple Health: the run as the iPad plugin gets it (fixed id → sending again replaces it)
            string hj = AppleHealth.ToJson(rec);
            var hb = JsonUtility.FromJson<HealthProbe>(hj);
            Ok(hb != null && hb.t.Length == 3 && hb.hr[2] == 142 && Mathf.Abs((float)hb.distanceM - 1234.5f) < 0.01f && Mathf.Approximately((float)(hb.start % 86400), 36000f)
               && hb.id.StartsWith("jogging-") && hb.id == JsonUtility.FromJson<HealthProbe>(AppleHealth.ToJson(rec)).id, "Apple Health: Laufdaten (feste Kennung)");
            // TCX altitude: follows the incline (1 km at 5 % → +50 m), never below the start height on a flat run
            var climb = new SessionRecord();
            climb.summary.start = "2026-09-24T10:00:00Z";
            for (int i = 0; i <= 100; i++) climb.samples.Add(new SessionSample { t = i, distM = i * 10f, kmh = 36f, incline = 5f });
            var alts = new List<float>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(TcxExport.ToTcx(climb), "<AltitudeMeters>([0-9.]+)</AltitudeMeters>"))
                alts.Add(float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
            Ok(alts.Count == 101 && Mathf.Abs(alts[100] - alts[0] - 50f) < 0.5f, $"TCX-Höhe: {(alts.Count > 0 ? alts[alts.Count - 1] - alts[0] : -1):0.0} m statt 50 m");

            // Achievements from the logbook
            var p = new ProfileData { totalRuns = 3, totalDistanceMeters = 16000f, totalTimeSeconds = 5700f };
            var runs = new List<SessionSummary>
            {
                S(now.AddDays(-2), 5200f, 1900f, r => r.source = "belt"),
                S(now.AddDays(-1), 4000f, 1500f, r => { r.workoutCompleted = true; r.hrZoneSeconds = new List<float> { 60f, 1300f, 100f, 0f, 0f }; }),
                S(now.AddHours(-1), 6800f, 2300f, r => { r.completed = true; r.routeId = "rt_x"; }),
            };
            var ctx = new AchievementContext { Profile = p, Runs = runs, NowLocal = now };
            var got = AchievementCatalog.CheckNew(ctx);
            foreach (var id in new[] { "first-run", "dist-10k", "single-5k", "time-30", "streak-3", "workout-1", "belt-1", "zone2-20", "route-1" })
                Ok(p.unlockedAchievements.Contains(id), $"Erfolg {id} fehlt");
            foreach (var id in new[] { "run-5", "single-10k", "time-60", "streak-7", "dist-50k", "climb-100" })
                Ok(!p.unlockedAchievements.Contains(id), $"Erfolg {id} zu früh");
            Ok(AchievementCatalog.CheckNew(ctx).Count == 0, "Erfolge doppelt vergeben");
            var ids = new HashSet<string>();
            foreach (var a in AchievementCatalog.All) Ok(ids.Add(a.Id), $"Id doppelt: {a.Id}");
            // Lifetime-only check must not throw on logbook conditions.
            Ok(AchievementCatalog.CheckNew(new ProfileData { totalRuns = 1 }).Contains("Erster Lauf"), "Check ohne Logbuch");

            if (fails.Count == 0) Debug.Log($"[StatsCheck] Alle Prüfungen bestanden ({got.Count} Erfolge im Testprofil).");
            else Debug.LogError("[StatsCheck] FAIL → " + string.Join("; ", fails));
            return fails.Count == 0;
        }
    }
}
