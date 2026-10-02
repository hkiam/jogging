using System;
using System.Collections.Generic;
using UnityEngine;

namespace Jogging.Profile
{
    /// <summary>
    /// Runtime owner of the runners (family members): loads them, knows the active runner, records
    /// finished runs into the active runner's lifetime stats, achievements and logbook, and persists
    /// via an <see cref="IProfileStore"/> / <see cref="SessionStore"/>. <see cref="Profile"/> is
    /// always the active runner, so callers written for a single profile keep working.
    /// The choice survives scene reloads (static) and app restarts (<see cref="Core.AppSettings"/>).
    /// </summary>
    [DefaultExecutionOrder(-150)] // before the device UI (it asks for the runner's strap) and everything else that reads the runner
    public class ProfileService : MonoBehaviour
    {
        public static ProfileService Instance { get; private set; }

        /// <summary>Raised with an achievement title when it is newly unlocked.</summary>
        public event Action<string> AchievementUnlocked;

        /// <summary>Raised with the new figure model name when the (active runner's) figure changes.</summary>
        public event Action<string> FigureChanged;

        /// <summary>Raised when another runner becomes active.</summary>
        public event Action<ProfileData> ActiveChanged;

        /// <summary>The active runner (a transient default until the first runner exists).</summary>
        public ProfileData Profile { get; private set; }

        public IReadOnlyList<ProfileData> Runners => runners;
        public bool HasRunners => runners.Count > 0;

        /// <summary>True once a runner was picked in this app session (menu then opens at their home).</summary>
        public static bool ChosenThisSession => chosenId != null;

        public SessionStore Sessions { get; private set; }

        private static string chosenId; // survives scene reloads
        private IProfileStore store;
        private List<ProfileData> runners = new List<ProfileData>();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            store = new LocalProfileStore();
            Sessions = new SessionStore();
            runners = store.LoadAll();
            string id = chosenId ?? LastRunnerId;
            Profile = Find(id) ?? (runners.Count > 0 ? runners[0] : new ProfileData());
            if (!checkpointsRecovered) { checkpointsRecovered = true; RecoverCheckpoints(); }
        }

        private static bool checkpointsRecovered; // once per app start, not on every scene load

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => checkpointsRecovered = false; // editor without domain reload

        // Runs cut short by a crash (their checkpoint is left) go into the logbook and lifetime stats.
        private void RecoverCheckpoints()
        {
            foreach (var runnerId in Sessions.RunnersWithCheckpoint())
            {
                var r = Find(runnerId);
                if (r == null) { Sessions.DeleteCheckpoint(runnerId); continue; } // runner gone
                var rec = Sessions.RecoverCheckpoint(runnerId);
                if (rec == null) continue;
                AddToTotals(r, rec.summary);
                store.Save(r);
            }
        }

        private static void AddToTotals(ProfileData p, SessionSummary s)
        {
            p.totalRuns++;
            p.totalDistanceMeters += s.distanceM;
            p.totalTimeSeconds += s.seconds;
            p.totalElevationMeters += s.gainM;
            if (s.distanceM > p.bestDistanceMeters) p.bestDistanceMeters = s.distanceM;
        }

        private ProfileData Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var r in runners) if (r.id == id) return r;
            return null;
        }

        /// <summary>Forget the runner chosen in this session (after restoring a backup: runners may differ).</summary>
        public static void ForgetChoice() => chosenId = null;

        /// <summary>Make this runner active (their figure is shown).</summary>
        public void Select(string id)
        {
            var r = Find(id);
            if (r == null) return;
            chosenId = id;
            LastRunnerId = id;
            bool figure = r.figureModel != Profile.figureModel;
            Profile = r;
            ActiveChanged?.Invoke(r);
            if (figure) FigureChanged?.Invoke(r.figureModel);
        }

        /// <summary>Create, save and activate a new runner.</summary>
        public ProfileData Create(string playerName, string figureModel)
        {
            var r = new ProfileData
            {
                playerName = string.IsNullOrWhiteSpace(playerName) ? "Läufer" : playerName.Trim(),
                figureModel = figureModel ?? "",
            };
            store.Save(r); // assigns id + created
            runners.Add(r);
            Select(r.id);
            return r;
        }

        /// <summary>Best record for a route revision, or null.</summary>
        public RouteRecord RecordFor(string routeId, int revision)
        {
            if (string.IsNullOrEmpty(routeId)) return null;
            foreach (var r in Profile.routeRecords) if (r.routeId == routeId && r.revision == revision) return r;
            return null;
        }

        /// <summary>Store a finished run of a saved route; true if it is a new best time.</summary>
        public bool RecordRouteTime(string routeId, int revision, float seconds)
        {
            if (string.IsNullOrEmpty(routeId) || seconds <= 0f) return false;
            var r = RecordFor(routeId, revision);
            bool best = r == null || seconds < r.bestSeconds;
            if (r == null) { r = new RouteRecord { routeId = routeId, revision = revision, bestSeconds = seconds }; Profile.routeRecords.Add(r); }
            if (best) r.bestSeconds = seconds;
            r.runs++;
            r.lastRun = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            Save();
            return best;
        }

        /// <summary>Check achievements against lifetime stats and the logbook; announce and save new ones.</summary>
        public List<string> CheckAchievements(SessionSummary last)
        {
            var ctx = new AchievementContext { Profile = Profile, Runs = Sessions.Summaries(Profile.id), Last = last };
            var unlocked = AchievementCatalog.CheckNew(ctx);
            foreach (var title in unlocked) AchievementUnlocked?.Invoke(title);
            if (unlocked.Count > 0) Save();
            return unlocked;
        }

        /// <summary>Record a finished run: logbook entry + lifetime stats. Returns new achievement titles.</summary>
        public List<string> RecordSession(SessionRecord rec)
        {
            var s = rec.summary;
            // The logbook first (a crash in between then loses no run; the index is rebuilt from the
            // run files), then the lifetime stats and plan progress, then the achievements, which may
            // look at the logbook including this run. A runner without an id is saved first to get one.
            if (string.IsNullOrEmpty(Profile.id)) Save();
            s.runnerId = Profile.id;
            Sessions.Save(rec);
            Sessions.DeleteCheckpoint(Profile.id);
            AddToTotals(Profile, s);
            LastPlanNote = CountPlanSession(s);
            Save();
            var unlocked = CheckAchievements(s);
            if (Core.AppSettings.Current.appleHealth && AppleHealth.Available) AppleHealth.Save(rec); // iPad: into Apple Health
            if (Core.AppSettings.Current.autoExport) // for Strava, Garmin & co.: the run lands in the export folder by itself
            {
                try { Debug.Log($"[Jogging] Lauf exportiert: {TcxExport.SaveTo(ExportFolder, rec, Profile.playerName, true)}"); }
                catch (System.Exception e) { Debug.LogWarning($"[Jogging] Export: {e.Message}"); }
            }
            Debug.Log($"[Jogging] Lauf gespeichert ({Profile.playerName}): {s.distanceM:0} m, {s.seconds:0} s, {rec.samples.Count} Messpunkte, {s.source}");
            return unlocked;
        }

        public List<SessionSummary> SummariesOf(string runnerId) => Sessions.Summaries(runnerId);

        /// <summary>Where exports go: "Jogging-Export" in Downloads (iPad/Android: in the exchange folder).</summary>
        public static string ExportFolder => System.IO.Path.Combine(Core.DataPaths.Downloads, "Jogging-Export");

        /// <summary>Every run of a runner as TCX into <see cref="ExportFolder"/> (runs already there are skipped). Returns (written, total).</summary>
        public (int written, int total) ExportAll(ProfileData runner)
        {
            int written = 0, total = 0;
            foreach (var s in Sessions.Summaries(runner.id))
            {
                var rec = Sessions.Load(runner.id, s.file);
                if (rec == null) continue;
                total++;
                string path = System.IO.Path.Combine(ExportFolder, TcxExport.FileName(rec, runner.playerName) + ".tcx");
                if (System.IO.File.Exists(path)) continue;
                TcxExport.SaveTo(ExportFolder, rec, runner.playerName, true);
                written++;
            }
            return (written, total);
        }

        // ---- training plan ----

        /// <summary>What the last recorded run did for the training plan ("Einheit 4 von 18 geschafft"), or "".</summary>
        public string LastPlanNote { get; private set; } = "";

        public Training.TrainingPlan ActivePlan => Training.TrainingPlans.Find(Profile.planId);

        public void StartPlan(string planId)
        {
            Profile.planId = planId ?? "";
            Profile.planStarted = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            Profile.planDone.Clear();
            Save();
            Debug.Log($"[Jogging] Trainingsplan gestartet ({Profile.playerName}): {planId}");
        }

        public void StopPlan()
        {
            Profile.planId = ""; Profile.planStarted = ""; Profile.planDone.Clear();
            Save();
        }

        // A completed workout of the active plan counts its session; the last one finishes the plan.
        private string CountPlanSession(SessionSummary s)
        {
            var plan = ActivePlan;
            if (plan == null || s.planId != plan.id || s.planIndex < 0 || s.planIndex >= plan.sessions.Count) return "";
            if (!s.workoutCompleted) return Jogging.Core.Loc.F("{0}: Einheit nicht vollständig – zählt noch nicht", Jogging.Core.Loc.T(plan.name));
            if (!Profile.planDone.Contains(s.planIndex)) Profile.planDone.Add(s.planIndex);
            int n = Profile.planDone.Count, total = plan.sessions.Count;
            if (Training.PlanProgress.Finished(plan, Profile.planDone))
            {
                if (!Profile.plansFinished.Contains(plan.id)) Profile.plansFinished.Add(plan.id);
                string name = plan.name;
                Profile.planId = ""; Profile.planStarted = ""; Profile.planDone.Clear();
                Save();
                return Jogging.Core.Loc.F("Trainingsplan „{0}“ geschafft!", name);
            }
            Save();
            return Jogging.Core.Loc.F("{0}: Einheit {1} von {2} geschafft", Jogging.Core.Loc.T(plan.name), n, total);
        }

        /// <summary>The runner whose paired heart rate sensor this is, or null.</summary>
        public ProfileData StrapOwner(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return null;
            foreach (var r in runners) if (r.hrDeviceId == deviceId) return r;
            return null;
        }

        /// <summary>Save the active runner after changing its settings (name, weight, goals, …).</summary>
        public void SaveActive(bool figureChanged = false)
        {
            Save();
            if (figureChanged) FigureChanged?.Invoke(Profile.figureModel);
            ActiveChanged?.Invoke(Profile);
        }

        /// <summary>
        /// Delete a run from the active runner's logbook and take it out of the lifetime stats.
        /// Achievements stay (they were earned); the longest run is recomputed from the logbook.
        /// </summary>
        public void DeleteRun(SessionSummary s)
        {
            if (s == null || s.runnerId != Profile.id) return;
            Sessions.Delete(s.runnerId, s.file);
            Profile.totalRuns = Math.Max(0, Profile.totalRuns - 1);
            Profile.totalDistanceMeters = Math.Max(0f, Profile.totalDistanceMeters - s.distanceM);
            Profile.totalTimeSeconds = Math.Max(0f, Profile.totalTimeSeconds - s.seconds);
            Profile.totalElevationMeters = Math.Max(0f, Profile.totalElevationMeters - s.gainM);
            if (s.distanceM >= Profile.bestDistanceMeters - 0.5f)
            {
                float best = 0f;
                foreach (var r in Sessions.Summaries(Profile.id)) best = Math.Max(best, r.distanceM);
                Profile.bestDistanceMeters = best;
            }
            Save();
            Debug.Log($"[Jogging] Lauf gelöscht ({Profile.playerName}): {s.distanceM:0} m vom {s.start}");
        }

        /// <summary>Delete a runner with the whole logbook. Another runner becomes active (or none).</summary>
        public void DeleteRunner(string id)
        {
            var r = Find(id);
            if (r == null) return;
            store.Delete(id);
            Sessions.DeleteAll(id);
            runners.Remove(r);
            Debug.Log($"[Jogging] Läufer gelöscht: {r.playerName}");
            if (Profile == r)
            {
                chosenId = null;
                if (runners.Count > 0) Select(runners[0].id);
                else { Profile = new ProfileData(); LastRunnerId = ""; ActiveChanged?.Invoke(Profile); }
            }
        }

        private void Save()
        {
            bool isNew = string.IsNullOrEmpty(Profile.id);
            store.Save(Profile);
            if (isNew) { runners.Add(Profile); LastRunnerId = Profile.id; } // e.g. -autostart without a runner
        }

        private static string LastRunnerId
        {
            get => Core.AppSettings.Current.lastRunnerId;
            set { Core.AppSettings.Current.lastRunnerId = value ?? ""; Core.AppSettings.Save(); }
        }
    }
}
