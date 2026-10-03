using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Jogging.Core;
using Jogging.Locomotion.Treadmill;
using Jogging.Profile;
using Jogging.Route;
using Jogging.Training;
using Jogging.World;

namespace Jogging.UI
{
    /// <summary>
    /// End-to-end run through the whole app (start with <c>-e2e -datadir &lt;empty folder&gt; -beltsim -hrsim
    /// -timescale 20</c>, see Tools/e2e.sh): new runner → Quick Run on the simulated F37 (belt start,
    /// route incline, belt stop = pause, connection lost, finish, incline back to 0) → save the route →
    /// run it twice (ghost) → a training plan session (workout, plan progress) → statistics, achievements,
    /// backup/restore. Every check is logged as "[E2E] OK/FAIL …"; the app quits with exit code 0 when all
    /// passed. It drives the app through its own APIs (no clicks) and never touches the real data folder.
    /// </summary>
    public class EndToEndTest : MonoBehaviour
    {
        private static readonly List<string> Fails = new List<string>();
        private static int oks;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Array.IndexOf(Jogging.Core.Args.All, "-e2e") < 0 || FindFirstObjectByType<EndToEndTest>() != null) return;
            if (!DataPaths.IsTest) { Debug.LogError("[E2E] Abbruch: ohne -datadir würde der Test echte Daten verändern."); Application.Quit(2); return; }
            MacBleBridgeTransport.Offline = true; // before any scene object starts: no bridge, no Bluetooth
            var go = new GameObject("E2E");
            DontDestroyOnLoad(go);
            go.AddComponent<EndToEndTest>();
        }

        private static void Check(bool ok, string what)
        {
            string t = $"[{Time.realtimeSinceStartup:0}s] ";
            if (ok) { oks++; Debug.Log("[E2E] OK   " + t + what); }
            else { Fails.Add(what); Debug.LogError("[E2E] FAIL " + t + what); }
        }

        private void Start() => StartCoroutine(Run());

        // ------------------------------------------------------------------ helpers

        private static IEnumerator Until(Func<bool> cond, float realSeconds, string what, Func<string> diag = null)
        {
            float t0 = Time.realtimeSinceStartup, nextDiag = t0 + 5f;
            while (!cond())
            {
                if (diag != null && Time.realtimeSinceStartup > nextDiag) { nextDiag += 5f; Debug.Log("[E2E] … " + diag()); }
                if (Time.realtimeSinceStartup - t0 > realSeconds) { Check(false, what + " (Zeit abgelaufen)" + (diag != null ? " – " + diag() : "")); yield break; }
                yield return null;
            }
            Check(true, what);
        }

        private static IEnumerator Wait(float realSeconds) { yield return new WaitForSecondsRealtime(realSeconds); }

        private static string BeltDiag()
        {
            var c = FindFirstObjectByType<FitShowBeltControl>();
            return $"Modus {BeltControl.Mode}, Steuerung erlaubt {Bridge?.AllowBeltControl}, Regelung „{c?.StateText}“, " +
                   $"Sicherheit „{c?.Safety.State}“, Band {Belt?.State} {Belt?.SpeedKmh:0.0} km/h {Belt?.InclinePercent:0} %, Lauf {Session?.State} ({Session?.Reason})";
        }

        private static SessionStateMachine Session => RunSessionUI.Session;
        private static ProfileService PS => ProfileService.Instance; // a new one in every scene
        private static FitShowEmulator Belt => FindFirstObjectByType<MacBleBridgeTransport>()?.Simulator;
        private static MacBleBridgeTransport Bridge => FindFirstObjectByType<MacBleBridgeTransport>();

        // Start a run on this route (optionally a workout) the way the menu does: select, reload, run.
        private static IEnumerator StartRun(RouteDoc route, WorkoutDoc workout = null)
        {
            RouteRuntime.Selected = route;
            WorkoutRuntime.Selected = workout;
            StartMenuUI.SkipNextOpen();
            Jogging.World.SceneReload.Now(); // frees the old landscape first
            yield return null; yield return null;
            WorkoutRuntime.Armed = true;
            yield return Until(() => Session != null && FindFirstObjectByType<TrackManager>() != null && !FindFirstObjectByType<TrackManager>().WaitingForTerrain, 60f, $"Gelände bereit: {route.meta.name}");
            yield return Until(() => Belt != null && Bridge.IsConnected, 10f, "Band-Simulator verbunden");
            Belt.PressStartStop();
            Belt.PressSpeed(4f); // 8 km/h once running (ignored during the countdown → pressed again below)
            yield return Until(() => Session.State == SessionState.Running, 20f, "Lauf läuft nach Bandstart");
            Belt.PressSpeed(4f);
        }

        private static IEnumerator FinishRun()
        {
            Session.Finish();
            yield return Until(() => FindFirstObjectByType<TrackManager>().IsFinished, 5f, "Lauf beendet");
            yield return Wait(1f);
        }

        // ------------------------------------------------------------------ the run

        private IEnumerator Run()
        {
            Debug.Log($"[E2E] Start – Daten: {DataPaths.Root} · {Application.platform}");
            MacBleBridgeTransport.Offline = true; // no bridge, no Bluetooth: real devices never take part
            var st = AppSettings.Current;
            st.announcements = false; st.ambience = false; st.beltIncline = true; st.beltSpeed = false; st.bigHud = false;
            st.heartRateAutoConnect = false; // simulated pulse (-hrsim); no real Bluetooth search
            AppSettings.Save();
            yield return Wait(1f);

            // 1. first start: no runner → create one
            Check(PS != null && !PS.HasRunners, "leerer Datenordner: noch kein Läufer");
            PS.Create("E2E", "");
            PS.Profile.birthYear = 1980; PS.Profile.weightKg = 75f; PS.SaveActive();
            Check(PS.HasRunners && PS.Profile.playerName == "E2E", "Läufer angelegt");
            var only = Array.IndexOf(Jogging.Core.Args.All, "-e2eonly") is int oi && oi >= 0 && oi + 1 < Jogging.Core.Args.All.Length
                ? Jogging.Core.Args.All[oi + 1] : "";
            if (only != "") // -e2eonly hill | extras: one part only (faster while working on it)
            {
                if (only == "hill") yield return HillStep();
                if (only == "extras") yield return Extras(null);
                if (only == "pacer") yield return PacerStep();
                if (only == "walk") yield return WalkShots();
                if (only == "perf") yield return PerfRun();
                yield return Result();
                yield break;
            }

            // 2. Quick Run on the simulated F37
            // the companion dog runs along (when its model is in this build – not in the public repository)
            bool dogModel = DogCompanion.Available("germanshepherd");
            if (dogModel) { PS.Profile.dog = "germanshepherd"; PS.Profile.dogName = "Rex"; PS.SaveActive(false); }
            var quick = RoutePresets.NewFreeRun();
            quick.@params.weather = "clear";
            quick.@params.spectators = "many"; // enough figures for the twin check below
            yield return StartRun(quick);
            yield return Until(() => FindFirstObjectByType<RunStats>().DistanceMeters > 400f, 90f, "Quick Run: 400 m gelaufen");
            Check(Mathf.Abs(Belt.SpeedKmh - 8f) < 0.2f, $"Band-Tempo bleibt beim Läufer ({Belt.SpeedKmh:0.0} km/h)");
            if (dogModel)
                Check(DogCompanion.Current != null && Mathf.Abs(DogCompanion.Current.Gap) < 60f, $"Hund läuft mit ({(DogCompanion.Current != null ? $"{DogCompanion.Current.Gap:0} m, {DogCompanion.Current.State}" : "fehlt")})");
            else Debug.Log("[E2E] Hund-Modell nicht in diesem Build – übersprungen");
            string me = FindFirstObjectByType<RealPlayerFigure>().ChosenName;
            int twins = 0, figures = 0; // every figure but yours (and the ghost's): runners, rival, spectators
            foreach (var a in FindObjectsByType<Animator>(FindObjectsSortMode.None))
            {
                if (a.GetComponentInParent<RealPlayerFigure>() != null || a.transform.root.name == "Ghost Runner") continue;
                if (a.GetComponentInParent<AiRunnerManager>() == null && a.GetComponentInParent<SpectatorManager>() == null
                    && a.GetComponentInParent<SpectatorGroups>() == null) continue;
                figures++; if (a.gameObject.name == me) twins++;
            }
            Check(figures > 8 && twins == 0, $"keine andere Figur trägt deine ({figures} Mitläufer und Zuschauer, {me})");
            Check(MacBleBridgeTransport.SimViolationsTotal == 0, "kein Steuerbefehl bei stehendem Band");
            Check(HeartRateMonitor.Current != null && HeartRateMonitor.Current.HasData, "Puls (simuliert) kommt an");

            // belt stop → pause; start → running
            Belt.PressStartStop();
            yield return Until(() => Session.State == SessionState.Paused && Session.Reason == PauseReason.BeltStopped, 5f, "Bandstopp → Pause");
            Belt.PressStartStop();
            yield return Until(() => Session.State == SessionState.Running, 10f, "Band läuft wieder → weiter");
            // connection lost → pause; back → running
            Bridge.SimulatedDrop = true;
            yield return Until(() => Session.State == SessionState.Paused && Session.Reason == PauseReason.ConnectionLost, 10f, "Verbindung weg → Pause (kein Wechsel auf Tastatur)");
            Bridge.SimulatedDrop = false;
            yield return Until(() => Session.State == SessionState.Running, 10f, "Verbindung wieder da → weiter");

            // raise the incline by hand so the app has to level out after the run? no: level-out only
            // lowers what the app set — force an app incline via a hill workout later; here just finish.
            yield return FinishRun();
            Check(Units.SystemRegion.Length == 2, $"Region des Systems erkannt ({Units.SystemRegion} → {(Units.Imperial ? "mph" : "km/h")})");
            Check(FinishController.LastTitle == Loc.T("Lauf beendet"), $"Ziel-Screen: „{FinishController.LastTitle}“");
            var runs = PS.SummariesOf(PS.Profile.id);
            Check(runs.Count == 1, $"Logbuch: {runs.Count} Lauf");
            if (runs.Count > 0)
            {
                var r = runs[0];
                Check(r.source == "belt", "Quelle Laufband");
                Check(r.avgHr > 0 && r.hrZoneSeconds != null && r.hrZoneSeconds.Count == 5, $"Puls im Logbuch (Ø {r.avgHr})");
                Check(r.kcal > 0f, $"Kalorien ({r.kcal:0} kcal)");
                var rec = PS.Sessions.Load(r.runnerId, r.file);
                Check(rec != null && rec.samples.Count > 20, $"Messpunkte ({rec?.samples.Count})");
            }
            Check(MacBleBridgeTransport.SimViolationsTotal == 0, "Quick Run ohne Verstoß");

            // 3. save the discovery, run it twice → ghost
            var saved = RouteRuntime.Current;
            RouteStore.Save(saved);
            Check(!string.IsNullOrEmpty(saved.id) && RouteStore.LoadAll().Exists(x => x.id == saved.id), "Strecke gespeichert");
            savedRoute = saved;
            // share as QR image and read it back (Vision in the bridge) → the same route
            string qrPng = Path.Combine(DataPaths.Root, "Strecke-qr.png");
            File.WriteAllBytes(qrPng, QrTexture.Create(QrCode.EncodeText(RouteShare.ToCode(saved), QrCode.Ecc.Low), 4).EncodeToPNG());
            if (Platform.HasMacBridge) // reading QR images needs the Mac bridge (Vision); iPad/Android: not yet
            {
                var qrTexts = QrReader.Decode(qrPng);
                var fromQr = qrTexts.Count > 0 ? RouteShare.FromCode(qrTexts[0], out bool qrMatch, out _) : null;
                Check(fromQr != null && fromQr.id == saved.id && RouteShare.Checksum(fromQr) == RouteShare.Checksum(saved),
                      $"Strecke per QR-Bild gelesen ({(qrTexts.Count > 0 ? qrTexts[0].Length : 0)} Zeichen)");
            }
            else Debug.Log("[E2E] --   QR-Bild lesen übersprungen (nur mit der Mac-Bridge)");
            yield return StartRun(saved);
            yield return Until(() => FindFirstObjectByType<RunStats>().DistanceMeters > 300f, 90f, "gespeicherte Strecke: 300 m");
            yield return FinishRun();
            yield return StartRun(saved);
            yield return Wait(2f);
            var ghost = FindFirstObjectByType<GhostRunner>();
            Check(ghost != null && ghost.HasGhost, "Geist-Läufer auf der gespeicherten Strecke");
            yield return Wait(4f);
            var ghostGo = GameObject.Find("Ghost Runner");
            var ghostR = ghostGo != null ? ghostGo.GetComponentInChildren<SkinnedMeshRenderer>() : null;
            Check(ghostR != null && ghostR.sharedMaterial.renderQueue >= 3000 && !ghostR.sharedMaterial.IsKeywordEnabled("_ALPHATEST_ON"),
                  "Geist ist durchscheinend");
            var ghostA = ghostGo != null ? ghostGo.GetComponentInChildren<Animator>() : null;
            string mine = FindFirstObjectByType<RealPlayerFigure>().ChosenName;
            Check(ghostA != null && ghostA.gameObject.name == mine, $"Geist trägt deine Figur ({(ghostA != null ? ghostA.gameObject.name : "-")})");
            ScreenCapture.CaptureScreenshot(Path.Combine(DataPaths.Root, "geist.png"));
            float rs = TrailPath.Active != null ? TrailPath.Active.RunnerS : 0f;
            int grass = TrailGrassClearer.GrassOnTrail(rs - 10f, rs + 100f);
            Check(grass == 0, $"kein Gras auf dem Weg ({grass} Stellen auf den nächsten 100 m)");
            Check(Wayside.BuiltCount > 3 && Wayside.Plan.Count > 0, $"Wegrand gebaut ({Wayside.BuiltCount} Stücke, {Wayside.Plan.Count} geplant), Belag hier: {TrailSurface.At(rs)}");
            PS.Profile.ghostOff = true; PS.SaveActive();
            yield return Wait(0.5f);
            Check(GameObject.Find("Ghost Runner") == null || !GameObject.Find("Ghost Runner").activeInHierarchy, "Geist abschaltbar");
            PS.Profile.ghostOff = false; PS.SaveActive();
            // discard it ("Verwerfen", two clicks): not in the logbook, back in the home
            var ui = FindFirstObjectByType<RunSessionUI>();
            ui.Discard(); ui.Discard();
            yield return Wait(2f);
            Check(PS.SummariesOf(PS.Profile.id).Count == 2, "verworfener Lauf nicht im Logbuch");
            Check(StartMenuUI.IsOpen, "nach dem Verwerfen im Home");
            // Treadmill profile: the simulated F37 reported its range; the page with limits and device list opens
            var tp = AppSettings.Treadmill(FindFirstObjectByType<MacBleBridgeTransport>().TreadmillDeviceId);
            Check(tp.inclineKnown && tp.inclineMax == 15f && tp.speedKnown && Mathf.Abs(tp.speedMax - 20f) < 0.01f && tp.maxIncline == 12f,
                  $"Laufband-Profil: {tp.RangeText()}, eigene Grenze {tp.maxIncline} %");
            StartMenuUI.OpenTreadmillSetup();
            FindFirstObjectByType<MacBleBridgeTransport>().StartDeviceSearch();
            yield return Wait(1f);
            ScreenCapture.CaptureScreenshot(Path.Combine(DataPaths.Root, "laufband-einstellen.png"));
            yield return Wait(0.5f);
            foreach (var pg in new[] { "editor", "main", "settings", "profile" })
            {
                StartMenuUI.OpenPageForTest(pg);
                yield return Wait(0.6f);
                ScreenCapture.CaptureScreenshot(Path.Combine(DataPaths.Root, $"seite-{pg}.png"));
                yield return Wait(0.4f);
            }

            // 4. training plan session: a whole workout, plan progress
            PS.StartPlan("plan:5k-6");
            var plan = TrainingPlans.Find("plan:5k-6");
            var wd = plan.sessions[0].workout.Clone();
            wd.planId = plan.id; wd.planIndex = 0;
            // Same segments, 20 s each: at 20× the runner would outrun the terrain streaming in a 29 min session.
            foreach (var seg in wd.segments) seg.durationS = 20f;
            yield return StartRun(RoutePresets.NewFreeRun(), wd);
            yield return Until(() => WorkoutRuntime.Runner != null && WorkoutRuntime.Runner.Index > 0, 60f, "Workout: Abschnitt 2 erreicht");
            yield return Until(() => FindFirstObjectByType<TrackManager>().IsFinished, 180f, "Workout läuft bis zum Ende (17 Abschnitte)");
            yield return Wait(1f);
            Check(FinishController.LastTitle == Loc.T("Workout geschafft!"), $"Ziel-Screen: „{FinishController.LastTitle}“");
            Check(PS.Profile.planDone.Contains(0), "Plan: Einheit 1 gezählt");
            Check(PlanProgress.Next(plan, PS.Profile.planDone) == 1, "Plan: Einheit 2 als Nächstes");
            var last = RunnerStats.Last(PS.SummariesOf(PS.Profile.id));
            Check(last != null && last.workoutCompleted && last.planId == plan.id, "Logbuch: Workout vollständig, Plan vermerkt");

            // 5. statistics, achievements, weekly totals
            var all = PS.SummariesOf(PS.Profile.id);
            Check(all.Count == 3, $"Logbuch: {all.Count} Läufe (Quick Run, Strecke, Plan; der kurze Geist-Test zählt nicht)");
            var week = RunnerStats.Week(all, DateTime.Now);
            Check(week.Runs == 3 && week.DistanceM > 700f, $"Diese Woche: {week.Runs} Läufe, {week.DistanceM / 1000f:0.0} km");
            foreach (var id in new[] { "first-run", "workout-1", "belt-1" })
                Check(PS.Profile.unlockedAchievements.Contains(id), $"Erfolg {id}");
            Check(PS.Profile.totalRuns == 3, $"Summen: {PS.Profile.totalRuns} Läufe");

            // 6. backup and restore (inside the test folder)
            string zipDir = Path.Combine(DataPaths.Root, "e2e-zip");
            string zip = DataBackup.Create(DataPaths.Root, zipDir);
            Check(DataBackup.Inspect(zip)?.runs == 3, "Sicherung enthält 3 Läufe");
            PS.DeleteRun(all[0]);
            Check(PS.SummariesOf(PS.Profile.id).Count == 2, "Lauf gelöscht");
            DataBackup.Restore(zip, DataPaths.Root, Path.Combine(DataPaths.Root, "Sicherungen"));
            Check(new SessionStore().Summaries(PS.Profile.id).Count == 3, "Wiederhergestellt: 3 Läufe");

            yield return HillStep();
            yield return Extras(savedRoute);
            yield return PacerStep();
            yield return Result();
        }

        private static RouteDoc savedRoute;

        // 8.–11.: workshop, two runners with their own straps, belt speed from a workout + the
        // treadmill view, TCX export
        // -e2eonly walk (without -timescale): images of the own figure at 1, 4 and 10 km/h, six each, 0.2 s
        // apart — to see whether it walks / runs or glides (the app's own frames, not the screen)
        private IEnumerator WalkShots()
        {
            yield return StartRun(RoutePresets.NewFreeRun());
            foreach (float kmh in new[] { 1f, 4f, 10f })
            {
                float want = kmh;
                yield return Until(() => { if (Mathf.Abs(Belt.TargetSpeedKmh - want) > 0.05f) Belt.PressSpeed(want - Belt.TargetSpeedKmh); return Mathf.Abs(Belt.SpeedKmh - want) < 0.15f; },
                                   60f, $"Band auf {kmh:0} km/h");
                yield return new WaitForSecondsRealtime(4f);
                var fig = FindFirstObjectByType<RealPlayerFigure>();
                var an = fig != null ? fig.GetComponentInChildren<Animator>() : null;
                if (an != null)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var ci in an.GetCurrentAnimatorClipInfo(0)) sb.Append($"{ci.clip.name} {ci.weight:0.00}  ");
                    Debug.Log($"[Gang] {kmh} km/h: Lauf {FindFirstObjectByType<TrackManager>().SpeedMps * 3.6f:0.0} km/h, Speed-Parameter {an.GetFloat("Speed"):0.00}, Abspieltempo {an.speed:0.00} · {sb}");
                }
                for (int i = 0; i < 6; i++)
                {
                    ScreenCapture.CaptureScreenshot(Path.Combine(DataPaths.Root, $"gang-{kmh:0}kmh-{i}.png"));
                    yield return new WaitForSecondsRealtime(0.2f);
                }
            }
            yield return FinishRun();
        }

        // -e2eonly perf (without -timescale, with -perf): a steady run at 8 km/h — 20 s to warm up (the
        // detailed tiles stream in), then 30 s measured (FrameStats logs every 10 s)
        private IEnumerator PerfRun()
        {
            yield return StartRun(RoutePresets.NewFreeRun());
            yield return Until(() => { if (Mathf.Abs(Belt.TargetSpeedKmh - 8f) > 0.05f) Belt.PressSpeed(8f - Belt.TargetSpeedKmh); return Mathf.Abs(Belt.SpeedKmh - 8f) < 0.15f; },
                               60f, "Band auf 8 km/h");
            yield return new WaitForSecondsRealtime(52f);
            yield return FinishRun();
        }

        private IEnumerator Extras(RouteDoc route)
        {
            // 8. workshop: lakes placed beside the trail are built into the landscape
            route ??= RoutePresets.NewFreeRun();
            if (string.IsNullOrEmpty(route.id)) RouteStore.Save(route);
            yield return StartRun(route);
            if (!Platform.HasMacBridge && Announcer.Current != null) // iPad/Android: the system voice (the Mac stays quiet in tests)
            {
                AppSettings.Current.announcements = true;
                Announcer.Current.Say("Ansagen funktionieren.");
                AppSettings.Current.announcements = false;
                Check(NativeSpeech.Available, "Ansage über die Systemstimme");
            }
            int lakesBefore = Lakes.All.Count;
            yield return FinishRun();
            foreach (float at in new[] { 400f, 800f, 1200f, 1600f })
                route.edits.Add(new RouteEdit { type = "lake", atM = at, side = at % 800f == 0f ? "left" : "right" });
            route.edits.Add(new RouteEdit { type = "spectators", atM = 120f, side = "right", count = 8 });
            RouteStore.Save(route);
            var reloaded = RouteStore.LoadAll().Find(x => x.id == route.id);
            Check(reloaded != null && reloaded.edits.Count >= 5, $"Werkstatt: Änderungen gespeichert ({reloaded?.edits.Count})");
            yield return StartRun(reloaded);
            Check(Lakes.All.Count > lakesBefore, $"Werkstatt: See(n) im Gelände ({lakesBefore} → {Lakes.All.Count})");
            yield return Until(() => GameObject.Find("SpectatorGroup") != null, 20f, "Werkstatt: Zuschauergruppe steht an der Strecke");

            // 9. two runners, each with their own strap
            var me = PS.Profile;
            Bridge.SimulateStrap("sim-strap-a", "Gurt A");
            yield return Wait(0.5f);
            Check(PS.Profile.hrDeviceId == "sim-strap-a", "erster Gurt gehört dem aktiven Läufer");
            Bridge.SimulateBeat(123);
            yield return Wait(0.3f);
            Check(HeartRateMonitor.Current.Bpm == 123 && HeartRateMonitor.Current.Source == "Pulsgurt", $"Puls vom Gurt ({HeartRateMonitor.Current.Bpm})");
            var two = PS.Create("Zwei", "");
            yield return Wait(0.3f);
            Check(HeartRateMonitor.Current.ForeignStrap, "zweiter Läufer: fremder Gurt erkannt");
            Bridge.SimulateBeat(150);
            yield return Wait(0.3f);
            Check(!HeartRateMonitor.Current.HasData && HeartRateMonitor.Current.Bpm != 150, "fremder Puls wird nicht übernommen (auch nicht der letzte Wert)");
            PS.Select(me.id);
            yield return Wait(0.3f);
            Bridge.SimulateBeat(126);
            yield return Wait(0.3f);
            Check(!HeartRateMonitor.Current.ForeignStrap && HeartRateMonitor.Current.Bpm == 126, "zurück: eigener Gurt, eigener Puls");
            Check(PS.StrapOwner("sim-strap-a")?.id == me.id && string.IsNullOrEmpty(two.hrDeviceId), "Zuordnung bleibt beim ersten Läufer");
            yield return FinishRun();

            // 10. workout sets the speed (opt-in) and shows in the treadmill view
            var st = AppSettings.Current;
            st.beltSpeed = true; st.bigHud = true; AppSettings.Save();
            var fast = new WorkoutDoc { id = "e2e:tempo", name = "E2E Tempo" };
            fast.segments.Add(new WorkoutSegment { kind = "warmup", durationS = 100f });
            fast.segments.Add(new WorkoutSegment { kind = "fast", durationS = 1200f, speedKmh = 10f });
            fast.segments.Add(new WorkoutSegment { kind = "cooldown", durationS = 1200f, speedKmh = 7f });
            yield return StartRun(RoutePresets.NewFreeRun(), fast);
            yield return Wait(0.5f);
            Check(WorkoutHud.BigVisible, "Laufband-Ansicht: großer Workout-Streifen sichtbar");
            yield return Until(() => Belt.SpeedKmh >= 9.95f, 90f, "Workout stellt das Tempo: 8 → 10 km/h", BeltDiag);
            Check(Belt.SpeedKmh <= 10.05f, $"nicht über das Ziel ({Belt.SpeedKmh:0.0} km/h)");
            Belt.PressSpeed(-1.5f); // the runner at the belt: slower
            yield return Wait(4f);
            var ctl = FindFirstObjectByType<FitShowBeltControl>();
            Check(ctl != null && ctl.Safety.SpeedOverridden(Time.unscaledTime), "Handverstellung erkannt (App hält sich zurück)");
            yield return Wait(20f); // well inside the 30 s back-off: the app must not have ramped back up
            Check(Belt.SpeedKmh < 8.75f, $"Hand am Band hat Vorrang (nach 24 s noch {Belt.SpeedKmh:0.0} km/h)");
            var aiW = FindFirstObjectByType<AiRunnerManager>();
            float segKmh = Jogging.Training.WorkoutRuntime.Runner?.Current?.speedKmh ?? 0f; // the segment by now
            float expect = Mathf.Lerp(aiW.RefPaceKmh, segKmh, 0.7f);
            Check(segKmh > 0f && Mathf.Abs(aiW.PacerKmh - expect) < 0.3f,
                  $"Mitläufer im Workout: richten sich nach dem Zieltempo ({aiW.PacerKmh:0.0} km/h, du {aiW.RefPaceKmh:0.0}, Ziel {segKmh:0.0})");
            yield return FinishRun();
            yield return Wait(3f);
            Check(Belt.State == FitShowEmulator.Status.Running && Belt.SpeedKmh < 9.5f, "nach dem Lauf: kein Tempobefehl mehr");
            st.beltSpeed = false; st.bigHud = false; AppSettings.Save();

            // 11. TCX export (as a string: nothing is written to Downloads)
            var last = PS.SummariesOf(PS.Profile.id).OrderByDescending(x => x.start).FirstOrDefault();
            var rec = last != null ? PS.Sessions.Load(last.runnerId, last.file) : null;
            string tcx = rec != null ? TcxExport.ToTcx(rec) : "";
            bool xmlOk;
            try { new System.Xml.XmlDocument().LoadXml(tcx); xmlOk = true; } catch { xmlOk = false; }
            Check(xmlOk && tcx.Contains("<Trackpoint>") && tcx.Contains("HeartRateBpm"), $"TCX-Export gültig ({tcx.Length / 1024} KB, mit Puls)");
            // all runs into the export folder (in a test run inside the test data), a second time nothing new
            var (written, total) = PS.ExportAll(PS.Profile);
            int files = System.IO.Directory.Exists(ProfileService.ExportFolder) ? System.IO.Directory.GetFiles(ProfileService.ExportFolder, "*.tcx").Length : 0;
            var (again, _) = PS.ExportAll(PS.Profile);
            Check(total > 0 && written == total && files == total && again == 0, $"Alle Läufe exportiert ({written} von {total}, {files} Dateien, beim 2. Mal {again})");

            // 11a. the game: climbs logged per route, points and level from the logbook
            var mineAll = PS.SummariesOf(PS.Profile.id);
            var routeRun = mineAll.FirstOrDefault(x => !string.IsNullOrEmpty(x.routeId));
            var rdoc = routeRun != null ? RouteStore.LoadAll().FirstOrDefault(r => r.id == routeRun.routeId) : null;
            Check(rdoc == null || routeRun.climbS.Count == ClimbSegments.Of(rdoc).Count, $"Bergwertungen im Logbuch ({routeRun?.climbS.Count ?? -1} von {(rdoc != null ? ClimbSegments.Of(rdoc).Count : -1)})");
            int xpAll = Game.Xp(mineAll);
            Check(xpAll > 0 && Game.Level(xpAll) >= 1 && Game.QuestProgress(mineAll, DateTime.Now).Count == 3, $"Spiel: {xpAll} Punkte, Level {Game.Level(xpAll)}");

            // 11b. a GPX file in Downloads becomes a route: found, imported, with its own profile
            var gpx = new System.Text.StringBuilder("<?xml version=\"1.0\"?><gpx version=\"1.1\" xmlns=\"http://www.topografix.com/GPX/1/1\"><trk><name>E2E-Runde</name><trkseg>");
            for (int i = 0; i <= 400; i++)
            {
                double a = i / 400.0 * 2 * System.Math.PI;
                gpx.Append(System.FormattableString.Invariant($"<trkpt lat=\"{50 + 400 * System.Math.Sin(a) / 111320.0:0.0000000}\" lon=\"{8 + 400 * (1 - System.Math.Cos(a)) / 71560.0:0.0000000}\"><ele>{300 + 20 * System.Math.Sin(a / 2):0.0}</ele></trkpt>"));
            }
            gpx.Append("</trkseg></trk></gpx>");
            System.IO.File.WriteAllText(System.IO.Path.Combine(Jogging.Core.DataPaths.Downloads, "e2e-runde.gpx"), gpx.ToString());
            var cand = RouteShare.FindFiles().FirstOrDefault(c => c.doc != null && c.doc.meta.name == "E2E-Runde");
            Check(cand != null && cand.importable && GpxImport.IsGpx(cand.doc.@params) && cand.doc.@params.loop
                  && Mathf.Abs(cand.doc.profile.ascentM - 20f) < 3f, $"GPX-Datei gefunden ({(cand != null ? $"{cand.doc.@params.lengthKm:0.00} km, ↑{cand.doc.profile.ascentM:0} m" : "fehlt")})");
            if (cand != null) RouteShare.Import(cand.doc);
            Check(RouteStore.LoadAll().Any(r => r.meta.name == "E2E-Runde" && GpxImport.IsGpx(r.@params)), "GPX-Strecke übernommen");
            Check(MacBleBridgeTransport.SimViolationsTotal == 0, "Zusatzteil ohne Verstoß (alle Szenen)");
        }

        // 12. fellow runners follow your pace (6 and 12 km/h), the rival stays in reach, overtakes happen
        private IEnumerator PacerStep()
        {
            yield return StartRun(RoutePresets.NewFreeRun()); // belt at 8 km/h
            var ai = FindFirstObjectByType<AiRunnerManager>();
            foreach (float kmh in new[] { 6f, 12f })
            {
                // press until the belt has taken it (a press is ignored while the belt isn't "running",
                // e.g. for a moment after the previous step) — from the target, not the still ramping speed
                float want = kmh;
                yield return Until(() => { if (Mathf.Abs(Belt.TargetSpeedKmh - want) > 0.05f) Belt.PressSpeed(want - Belt.TargetSpeedKmh); return Mathf.Abs(Belt.SpeedKmh - want) < 0.15f; },
                                   30f, $"Band auf {kmh:0} km/h");
                yield return Wait(12f); // ~4 run minutes at 20×: the smoothed pace settles, the field follows
                var f = ai.FieldKmh;
                Check(Mathf.Abs(ai.RefPaceKmh - kmh) < 0.4f, $"Mitläufer: dein Tempo erkannt ({ai.RefPaceKmh:0.0} bei {kmh:0} km/h)");
                Check(f.x >= kmh * 0.85f && f.y <= kmh * 1.15f, $"Mitläufer bei {kmh:0} km/h: Feld {f.x:0.0}–{f.y:0.0} km/h");
                // in reach — or you just overtook him (speeding up to 12 km/h does that): then he holds on for 30 s
                // and drops back by design until a new rival turns up ahead; that is fine as long as he is in view
                bool inReach = ai.RivalGapMeters > -5f && ai.RivalGapMeters < 20f;
                Check(inReach || ai.RivalOvertaken && ai.RivalGapMeters > -60f,
                      $"Rivale in Reichweite ({ai.RivalGapMeters:+0;-0} m{(ai.RivalOvertaken ? ", von dir überholt, fällt zurück" : "")})");
            }
            int before = ai.OvertakenCount + ai.PassedByCount;
            yield return Wait(25f); // ~8 run minutes
            int moves = ai.OvertakenCount + ai.PassedByCount - before;
            Check(moves >= 2, $"Überholen findet statt ({moves}× in ~8 min: {ai.OvertakenCount} überholt, {ai.PassedByCount}× überholt worden)");
            yield return FinishRun();
        }

        private IEnumerator HillStep()
        {
            // 7. level-out after a hill session: the app set the incline, leaves the belt level
            var hill = WorkoutPresets.All().Find(w => w.id == "builtin:huegel-25").Clone();
            // The belt works in real time (1 command / 2 s, settle after the start) while the run is at 20×:
            // warm-up ~5 s real, the 3 % climb ~60 s real.
            hill.segments[0].durationS = 100f;
            for (int k = 1; k < hill.segments.Count; k++) hill.segments[k].durationS = 1200f;
            yield return StartRun(RoutePresets.NewFreeRun(), hill);
            yield return Until(() => Belt.InclinePercent >= 2.5f, 90f, "Hügel-Workout stellt Steigung ein", BeltDiag);
            yield return FinishRun();
            yield return Until(() => Belt.InclinePercent < 0.5f, 60f, "nach dem Lauf zurück auf 0 %");
            Check(Belt.State == FitShowEmulator.Status.Running && Mathf.Abs(Belt.SpeedKmh - 8f) < 0.2f, "Band läuft weiter, Tempo unverändert");
            Check(MacBleBridgeTransport.SimViolationsTotal == 0, "gesamter Test ohne Verstoß (alle Szenen)");
        }

        private IEnumerator Result()
        {
            Debug.Log($"[E2E] ERGEBNIS: {oks} OK, {Fails.Count} FAIL" + (Fails.Count > 0 ? " → " + string.Join("; ", Fails) : ""));
            File.WriteAllText(Path.Combine(DataPaths.Root, "e2e-result.txt"), Fails.Count == 0 ? $"OK {oks}" : "FAIL\n" + string.Join("\n", Fails));
            yield return Wait(0.5f);
            Application.Quit(Fails.Count == 0 ? 0 : 1);
        }
    }
}
