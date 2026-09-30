using System;
using System.Collections.Generic;
using UnityEngine;

namespace Jogging.Profile
{
    /// <summary>
    /// Serializable runner profile (one per family member): identity, chosen figure, lifetime stats and unlocked
    /// achievements. Plain data (JsonUtility-friendly) so it can be stored locally now and
    /// synced to a backend (Nakama) later without changing the shape.
    /// </summary>
    [Serializable]
    public class ProfileData
    {
        public string id = "";          // stable runner id (file name, links sessions)
        public string created = "";     // ISO 8601 UTC
        public string playerName = "Runner";
        // Chosen runner figure (name of a Rocketbox model, e.g. "Female_Adult_08"); empty = default.
        public string figureModel = "";
        // Max heart rate for the zones; 0 = not set (estimated from the birth year, else 190).
        public int maxHeartRate;
        public int birthYear;       // 0 = not set
        public float weightKg;      // 0 = not set (no calories)

        // Weekly goals (0 = default: 3 runs, 15 km, 90 min).
        public int weekGoalRuns;
        public float weekGoalKm;
        public int weekGoalMinutes;

        // Ghost runner on saved routes (your best run runs along); true = switched off.
        public bool ghostOff;
        public int pulseCoach;          // pulse coach: −1 off, 0 only in workouts with a target zone, 1…5 also in free runs (this zone)

        // Training plan: the active one (empty = none), when it started, the completed session indices,
        // and the plans finished so far.
        public string planId = "";
        public string planStarted = "";   // ISO 8601 UTC
        public List<int> planDone = new List<int>();
        public List<string> plansFinished = new List<string>();

        // The runner's own heart rate sensor (paired on first connect); empty = none yet.
        public string hrDeviceId = "";
        public string hrDeviceName = "";

        public int totalRuns;
        public float totalDistanceMeters;
        public float totalTimeSeconds;
        public float totalElevationMeters;
        public float bestDistanceMeters;

        public List<string> unlockedAchievements = new List<string>();

        // Best time per saved route (and revision: an edited route is a different course).
        public List<RouteRecord> routeRecords = new List<RouteRecord>();
    }

    [Serializable]
    public class RouteRecord
    {
        public string routeId;
        public int revision;
        public float bestSeconds;
        public int runs;
        public string lastRun; // ISO 8601 UTC
    }
}
