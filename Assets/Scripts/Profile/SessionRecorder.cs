using System;
using System.Collections.Generic;
using UnityEngine;
using Jogging.Core;
using Jogging.Locomotion;
using Jogging.World;

namespace Jogging.Profile
{
    /// <summary>
    /// Records the current run into the active runner's logbook: one sample per second of running
    /// time (distance, speed, incline) and, when the run ends (finish, scene reload, app quit) or on
    /// demand via <see cref="FinishNow"/>, a <see cref="SessionRecord"/> plus the lifetime stats.
    /// Guards against double-recording; runs under 1 m or 30 s are ignored (accidental starts).
    /// Every minute of running the run so far is checkpointed, so a crash loses at most a minute
    /// (<see cref="ProfileService"/> turns a leftover checkpoint into a run on the next start).
    /// </summary>
    public class SessionRecorder : MonoBehaviour
    {
        [SerializeField] private RunStats stats;

        private const float CheckpointEvery = 60f; // seconds of running

        private TrackManager track;
        private LocomotionRouter router;
        private readonly List<SessionSample> samples = new List<SessionSample>();
        private float nextSampleAt;
        private float maxKmh;
        private bool usedBelt;
        private DateTime startUtc;
        private readonly Training.HeartRateStats hrStats = new Training.HeartRateStats();
        private double kcal;
        private bool recorded;
        private float nextCheckpointAt = CheckpointEvery;
        private string checkpointRunner; // runner whose checkpoint this run wrote

        /// <summary>Achievements unlocked by the recorded run (for the finish screen).</summary>
        public IReadOnlyList<string> NewAchievements { get; private set; } = Array.Empty<string>();

        /// <summary>Heart rate of the recorded run (0 = none), for the finish screen.</summary>
        public int LastAvgHr { get; private set; }
        public int LastMaxHr { get; private set; }
        public float LastKcal { get; private set; }

        private void Start()
        {
            track = FindFirstObjectByType<TrackManager>();
            router = FindFirstObjectByType<LocomotionRouter>();
        }

        private void Update()
        {
            if (recorded || stats == null || track == null || track.IsFinished) return;
            // Clock only runs while running; the first sample marks the real start (not the scene load).
            if (stats.ElapsedSeconds <= 0f || stats.ElapsedSeconds < nextSampleAt) return;

            if (samples.Count == 0) startUtc = DateTime.UtcNow.AddSeconds(-stats.ElapsedSeconds);
            float kmh = track.SpeedMps * 3.6f;
            var hrm = Training.HeartRateMonitor.Current;
            int bpm = hrm != null && hrm.HasData ? hrm.Bpm : 0;
            hrStats.Add(bpm, samples.Count == 0 ? 0f : 1f, Training.HeartRateMonitor.MaxHr); // one sample ≈ one second of running
            samples.Add(new SessionSample
            {
                t = stats.ElapsedSeconds, distM = stats.DistanceMeters, kmh = kmh, incline = track.CurrentInclinePercent, hr = bpm,
            });
            maxKmh = Mathf.Max(maxKmh, kmh);
            var who = ProfileService.Instance != null ? ProfileService.Instance.Profile : null;
            if (who != null && samples.Count > 1) kcal += Training.Calories.For(kmh, track.CurrentInclinePercent, 1f, who.weightKg);
            if (router != null && router.UsingTreadmill) usedBelt = true;
            nextSampleAt = Mathf.Floor(stats.ElapsedSeconds) + 1f;
            if (stats.ElapsedSeconds >= nextCheckpointAt) { nextCheckpointAt = stats.ElapsedSeconds + CheckpointEvery; Checkpoint(); }
        }

        private void Checkpoint()
        {
            var ps = ProfileService.Instance;
            if (ps == null || string.IsNullOrEmpty(ps.Profile.id)) return; // no runner yet: nothing to attach it to
            var rec = BuildRecord(ps.Profile.id);
            if (rec == null) return;
            checkpointRunner = ps.Profile.id;
            ps.Sessions.SaveCheckpoint(rec);
        }

        private void DropCheckpoint()
        {
            if (checkpointRunner == null || ProfileService.Instance == null) return;
            ProfileService.Instance.Sessions.DeleteCheckpoint(checkpointRunner);
            checkpointRunner = null;
        }

        public void FinishNow() => TryRecord();

        /// <summary>The runner discards this run: nothing is recorded, not even on scene reload or quit.</summary>
        public void Discard()
        {
            if (recorded) return;
            recorded = true;
            DropCheckpoint();
            Debug.Log("[Jogging] Lauf verworfen.");
        }
        private void OnApplicationQuit() => TryRecord();
        private void OnDisable() => TryRecord();

        private void TryRecord()
        {
            if (recorded || stats == null || ProfileService.Instance == null) return;

            var rec = BuildRecord("");
            if (rec == null) return;
            recorded = true;
            var s = rec.summary;
            LastAvgHr = s.avgHr; LastMaxHr = s.maxHr; LastKcal = s.kcal;
            NewAchievements = ProfileService.Instance.RecordSession(rec); // also removes the active runner's checkpoint
            DropCheckpoint(); // (in case the active runner changed meanwhile)
        }

        // The run so far as a record; null if it is too short to count.
        private SessionRecord BuildRecord(string runnerId)
        {
            var (distance, seconds, gain) = stats.Snapshot();
            if (distance < SessionStore.MinMeters || seconds < SessionStore.MinSeconds) return null;

            var route = RouteRuntime.Current;
            var start = samples.Count == 0 ? DateTime.UtcNow.AddSeconds(-seconds) : startUtc;
            var rec = new SessionRecord { samples = new List<SessionSample>(samples) };
            var s = rec.summary;
            s.runnerId = runnerId;
            s.start = start.ToString("yyyy-MM-ddTHH:mm:ssZ");
            s.end = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            if (route != null) { s.routeId = route.id ?? ""; s.revision = route.revision; s.routeName = route.meta.name; }
            s.distanceM = distance;
            s.seconds = seconds;
            s.gainM = gain;
            s.avgKmh = distance / seconds * 3.6f;
            s.maxKmh = maxKmh;
            s.completed = track != null && track.RouteLength > 0f && track.DistanceTraveled >= track.RouteLength - 1f;
            s.source = usedBelt ? "belt" : "keys";
            s.best5kS = RunnerStats.BestSegmentSeconds(rec.samples, 5000f);
            s.kcal = (float)kcal;
            if (hrStats.HasData) { s.avgHr = hrStats.AvgBpm; s.maxHr = hrStats.MaxBpm; s.hrZoneSeconds = new List<float>(hrStats.ZoneSeconds); }
            foreach (var kv in World.Wayside.Sights) s.sights.Add(kv.Value > 1 ? $"{kv.Key}:{kv.Value}" : kv.Key);
            s.surfaceM = new List<float>(World.Wayside.SurfaceMetres);
            var w = Training.WorkoutRuntime.Current;
            if (w != null)
            {
                s.workoutId = w.id; s.workoutName = w.name; s.workoutCompleted = Training.WorkoutRuntime.Completed;
                if (!string.IsNullOrEmpty(w.planId)) { s.planId = w.planId; s.planIndex = w.planIndex; }
            }
            return rec;
        }
    }
}
