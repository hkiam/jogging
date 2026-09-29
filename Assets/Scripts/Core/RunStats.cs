using UnityEngine;
using Jogging.World;

namespace Jogging.Core
{
    /// <summary>
    /// Accumulates simple per-session stats (distance, time, elevation gain, current grade).
    /// Kept deliberately backend-agnostic: the MVP just holds values in memory.
    /// Stufe 3 will push a finished snapshot through a save-service interface
    /// (local file first, Nakama cloud-save later) — this class stays unchanged.
    /// </summary>
    public class RunStats : MonoBehaviour
    {
        [SerializeField] private TrackManager track;

        public float DistanceMeters { get; private set; }
        public float ElapsedSeconds { get; private set; }
        public float ElevationGainMeters { get; private set; } // total ascent only
        public float CurrentGradePercent { get; private set; }

        /// <summary>Stops the clock (e.g. the real treadmill is stopped) without ending the run.</summary>
        public bool Paused { get; set; }

        private float lastDistance;
        private float startAt;

        private void Update()
        {
            if (track == null || track.IsFinished) return; // stop the clock at the finish line

            // The run's distance counts from where the run starts (a test start mid-route with -starts,
            // or the workshop, is not "run"): until the clock first ticks, the start moves along.
            float distance = track.DistanceTraveled;
            if (ElapsedSeconds <= 0f) { startAt = distance; lastDistance = distance; }

            if (!Paused && !track.WaitingForTerrain && !track.Hold) ElapsedSeconds += Time.deltaTime; // clock runs only while running

            float delta = distance - lastDistance;
            lastDistance = distance;
            DistanceMeters = distance - startAt;

            CurrentGradePercent = track.CurrentInclinePercent;

            // Elevation gain = horizontal distance * grade, counting ascent only.
            // grade% of 5 over 100 m horizontal ≈ 5 m of climb.
            if (delta > 0f && CurrentGradePercent > 0f)
                ElevationGainMeters += delta * (CurrentGradePercent / 100f);
        }

        /// <summary>Snapshot for saving/leaderboards later.</summary>
        public (float distance, float seconds, float gain) Snapshot()
            => (DistanceMeters, ElapsedSeconds, ElevationGainMeters);
    }
}
