namespace Jogging.Locomotion.Treadmill
{
    /// <summary>
    /// Control authority for the belt: <see cref="Mode"/> says who sets speed and incline, and
    /// <see cref="Targets"/> turns it into targets for the adapters (FitShow, FTMS), which pass them
    /// through their <see cref="BeltSafety"/>. The route mode maps the route grade to the incline;
    /// the workout mode asks <see cref="Workout"/> (set by the workout engine, Phase 4).
    /// </summary>
    public static class BeltControl
    {
        /// <summary>Set from the app settings (<c>TreadmillConnectUI.Apply</c>); a workout switches to <see cref="ControlMode.Workout"/>.</summary>
        public static ControlMode Mode { get; set; } = ControlMode.Treadmill;

        /// <summary>
        /// The incline the app last commanded (kept across scene reloads, so leveling out continues after
        /// "Fertig"); null when the app hasn't set one or the belt is level again.
        /// </summary>
        public static float? AppIncline { get; set; }

        /// <summary>
        /// Remember the incline the app just set (null once it is level again). A speed-only command
        /// repeats the belt's incline — that stays the runner's, so the level-out after the run leaves it.
        /// </summary>
        public static void Sent(BeltCommand cmd)
        {
            if (cmd.SetsIncline) AppIncline = System.Math.Abs(cmd.InclinePercent) > 0.25f ? cmd.InclinePercent : (float?)null;
        }

        /// <summary>
        /// Workouts may set the belt speed (opt-in in the settings, off by default). Even then only via
        /// <see cref="BeltSafety"/>: ramped, capped, only while the belt runs, the runner's change wins.
        /// </summary>
        public static bool SpeedAllowed { get; set; }

        /// <summary>Belt incline % per route grade %.</summary>
        public static float RouteGain { get; set; } = 1f;

        public delegate bool TargetProvider(out float? inclinePercent, out float? speedKmh);

        /// <summary>Workout targets (null = no workout running).</summary>
        public static TargetProvider Workout { get; set; }

        /// <summary>Targets for the current mode; false = the app doesn't want to change anything.</summary>
        public static bool Targets(float routeGradePercent, out float? inclinePercent, out float? speedKmh)
        {
            inclinePercent = speedKmh = null;
            switch (Mode)
            {
                case ControlMode.Route:
                    // downhill only on a treadmill that can decline and where the runner allowed it
                    inclinePercent = (routeGradePercent > 0f || DeclineAllowed ? routeGradePercent : 0f) * RouteGain;
                    return true;
                case ControlMode.Workout:
                    return Workout != null && Workout(out inclinePercent, out speedKmh);
                default:
                    return false; // the runner controls the belt
            }
        }

        /// <summary>
        /// True when no run is in progress (no session, before the start, finished, discarded): the app
        /// then only levels a still running belt out (<see cref="BeltSafety.TryLevelOut"/>). A pause
        /// keeps the incline (the run continues).
        /// </summary>
        public static bool RunIsOver(Core.SessionStateMachine s) =>
            s == null || s.State == Core.SessionState.Finished || s.State == Core.SessionState.Aborted
            || (s.State == Core.SessionState.Preparing && !s.Started);

        /// <summary>The connected treadmill may go below 0 % (its profile, set by the adapters).</summary>
        public static bool DeclineAllowed { get; private set; }

        /// <summary>
        /// The connected treadmill's limits into the safety layer: the runner's caps from its profile
        /// (Geräte → Laufband einstellen); the belt's own range is applied by the safety layer on top.
        /// </summary>
        public static void ApplyProfile(BeltLimits l, string deviceId)
        {
            var p = Core.AppSettings.Treadmill(deviceId);
            l.MaxIncline = p.maxIncline;
            l.MinIncline = p.decline ? -p.maxDecline : 0f;
            l.MaxSpeedKmh = p.maxSpeed;
            if (p.inclineStep > 0f) { l.InclineResolution = p.inclineStep; l.MaxInclineStep = System.Math.Max(1f, p.inclineStep); }
            DeclineAllowed = p.decline;
        }

        /// <summary>The belt told its range (FitShow SYS_INFO, FTMS 2AD4/2AD5): remember it with the treadmill.</summary>
        public static void ReportRange(string deviceId, string name, string protocol, bool incline, float min, float max)
        {
            var p = Core.AppSettings.Treadmill(deviceId);
            if (!string.IsNullOrEmpty(name)) p.name = name;
            if (!string.IsNullOrEmpty(protocol)) p.protocol = protocol;
            bool changed;
            if (incline) { changed = !p.inclineKnown || p.inclineMin != min || p.inclineMax != max; p.inclineMin = min; p.inclineMax = max; p.inclineKnown = true; }
            else { changed = !p.speedKnown || p.speedMin != min || p.speedMax != max; p.speedMin = min; p.speedMax = max; p.speedKnown = true; }
            if (changed) Core.AppSettings.Save();
        }

        public static string Describe(ControlMode m) =>
            m == ControlMode.Route ? "Strecke stellt die Steigung" : m == ControlMode.Workout ? "Workout steuert" : "Band steuert";
    }
}
