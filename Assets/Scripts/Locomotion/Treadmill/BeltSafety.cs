using System;

namespace Jogging.Locomotion.Treadmill
{
    /// <summary>Who sets speed and incline of the belt.</summary>
    public enum ControlMode
    {
        /// <summary>The runner at the belt; the app only reads (never writes).</summary>
        Treadmill,
        /// <summary>The route sets the incline; the speed stays with the runner.</summary>
        Route,
        /// <summary>A workout sets incline and/or speed (Phase 4).</summary>
        Workout,
    }

    /// <summary>What the belt reports in one status frame.</summary>
    public struct BeltReading
    {
        public bool Running;
        public float SpeedKmh;
        public float InclinePercent;
    }

    /// <summary>A command the safety layer lets through: always both values (FitShow sets them together).</summary>
    public struct BeltCommand
    {
        public float SpeedKmh;
        public float InclinePercent;
        public bool ChangesSpeed;
        /// <summary>The incline comes from an app target (not just the belt's own value repeated with a speed change).</summary>
        public bool SetsIncline;
    }

    /// <summary>Hard limits of the safety layer (defaults = the rules verified on the F37).</summary>
    [Serializable]
    public class BeltLimits
    {
        public float MinIncline = 0f;        // no decline (below 0 only with the runner's "Gefälle" per treadmill)
        public float MaxIncline = 12f;       // also clamped to the belt's own range; per treadmill (TreadmillProfile)
        public float MaxInclineStep = 1f;    // % per command
        public float InclineResolution = 1f; // FitShow: whole percent (FTMS: 0.1)
        public float SendInterval = 3f;      // s between commands
        public float SpeedSettle = 3f;       // s of quiet after any speed change
        public float OverridePause = 30f;    // s the app backs off after the runner adjusts the belt
        public float OwnEcho = 5f;           // s in which a changed value may still be our own command
        public bool AllowSpeed = false;      // speed targets only when explicitly allowed (workouts)
        public float MaxSpeedKmh = 16f;
        public float MaxSpeedStep = 0.5f;    // km/h per command
    }

    /// <summary>
    /// The one place that decides whether the app may move the belt — every adapter (FitShow, FTMS)
    /// and every control strategy (route, workout) goes through it. Plain C#, tested by BeltSafetyCheck.
    /// Rules:
    ///   • only while the belt itself reports running with speed &gt; 0 and the run is running
    ///     (the belt's stop, safety clip and pause always win; nothing ever starts the belt),
    ///   • incline clamped to [max(min, belt min), min(max, belt max)], at most one step per command,
    ///   • speed only if <see cref="BeltLimits.AllowSpeed"/>, ramped, capped; otherwise the belt's
    ///     own speed is echoed unchanged,
    ///   • nothing within <see cref="BeltLimits.SpeedSettle"/> s of a speed change, at most one
    ///     command per <see cref="BeltLimits.SendInterval"/> s,
    ///   • a value the runner changes on the belt pauses targeting of that value for
    ///     <see cref="BeltLimits.OverridePause"/> s (manual override).
    /// </summary>
    public class BeltSafety
    {
        public BeltLimits Limits { get; }

        /// <summary>Short text for the HUD / settings ("Steigung → 4 %", "pausiert (manuell verstellt)").</summary>
        public string State { get; private set; } = "";

        private readonly bool requireRange;
        private bool rangeKnown;
        private float beltMinIncline = float.NegativeInfinity, beltMaxIncline = float.PositiveInfinity;
        private float beltMinSpeed = 0f, beltMaxSpeed = float.PositiveInfinity;

        private bool hasReading;
        private BeltReading last;
        private float lastSpeedChange = float.NegativeInfinity;
        private float lastSentTime = float.NegativeInfinity;
        private float? sentIncline, sentSpeed;
        private float rampFromSpeed, rampFromIncline; // belt values when the last command was sent
        private float startedAt = float.NegativeInfinity;
        private const float StartRampSeconds = 8f;          // belt accelerating after its start
        private float inclineOverrideUntil = float.NegativeInfinity, speedOverrideUntil = float.NegativeInfinity;

        /// <param name="requireRange">FitShow: don't command before the belt reported its incline range.</param>
        public BeltSafety(BeltLimits limits = null, bool requireRange = true)
        {
            Limits = limits ?? new BeltLimits();
            this.requireRange = requireRange;
        }

        public bool InclineOverridden(float now) => now < inclineOverrideUntil;
        public bool SpeedOverridden(float now) => now < speedOverrideUntil;

        /// <summary>The belt's own incline range (FitShow SYS_INFO); max &lt;= min = no incline.</summary>
        public void SetInclineRange(float min, float max) { beltMinIncline = min; beltMaxIncline = max; rangeKnown = true; }

        public void SetSpeedRange(float min, float max) { beltMinSpeed = min; beltMaxSpeed = max; }

        /// <summary>Connection changed: forget the belt state (ranges stay: same belt).</summary>
        public void Reset()
        {
            hasReading = false; sentIncline = sentSpeed = null;
            lastSpeedChange = lastSentTime = float.NegativeInfinity;
        }

        /// <summary>Feed every status frame. Detects speed changes and the runner's manual adjustments.</summary>
        public void OnReading(BeltReading r, float now)
        {
            if (!r.Running || r.SpeedKmh <= 0.05f)
            {
                hasReading = true; last = r; last.Running = false;
                return;
            }
            bool recentlySent = now - lastSentTime <= Limits.OwnEcho;
            if (!hasReading || !last.Running) { lastSpeedChange = now; startedAt = now; } // (re)start: settle first
            else
            {
                // The belt ramps to a commanded value (e.g. 8.1, 8.2 … on the way to 8.5 km/h): every value
                // on the way from where it was to what the app sent is ours, not the runner's.
                if (!Same(r.SpeedKmh, last.SpeedKmh, 0.05f))
                {
                    lastSpeedChange = now;
                    bool ours = recentlySent && sentSpeed.HasValue && OnTheWay(r.SpeedKmh, rampFromSpeed, sentSpeed.Value, 0.05f);
                    // The belt's own run-up after its start isn't the runner either.
                    bool startRamp = now - startedAt < StartRampSeconds;
                    if (!ours && !startRamp && Limits.AllowSpeed) speedOverrideUntil = now + Limits.OverridePause;
                }
                if (!Same(r.InclinePercent, last.InclinePercent, 0.25f))
                {
                    bool ours = recentlySent && sentIncline.HasValue && OnTheWay(r.InclinePercent, rampFromIncline, sentIncline.Value, 0.25f);
                    if (!ours) inclineOverrideUntil = now + Limits.OverridePause;
                }
            }
            hasReading = true; last = r;
        }

        /// <summary>
        /// May the app send a command now for these targets (null = keep the belt's value)?
        /// Returns false (with <see cref="State"/> saying why) when not.
        /// </summary>
        public bool TryCommand(float now, bool sessionRunning, float? targetIncline, float? targetSpeed, out BeltCommand cmd)
        {
            cmd = default;
            if (!hasReading || !last.Running) { State = Jogging.Core.Loc.T("Band steht"); return false; }
            if (!sessionRunning) { State = Jogging.Core.Loc.T("Lauf pausiert"); return false; }
            if (targetIncline.HasValue && requireRange && !rangeKnown) { State = Jogging.Core.Loc.T("Steigungsbereich unbekannt"); return false; }
            if (targetIncline.HasValue && rangeKnown && beltMaxIncline <= beltMinIncline) targetIncline = null; // belt has no incline

            bool inclineBlocked = InclineOverridden(now), speedBlocked = !Limits.AllowSpeed || SpeedOverridden(now);
            if (inclineBlocked) targetIncline = null;
            if (speedBlocked) targetSpeed = null;
            if (!targetIncline.HasValue && !targetSpeed.HasValue)
            {
                State = inclineBlocked || (Limits.AllowSpeed && SpeedOverridden(now)) ? Jogging.Core.Loc.T("pausiert (manuell verstellt)") : "";
                return false;
            }
            if (now - lastSpeedChange < Limits.SpeedSettle) { State = Jogging.Core.Loc.T("Tempo geändert – warte"); return false; }
            if (now - lastSentTime < Limits.SendInterval) return false;

            // Outside the app's range (the runner went there by hand, e.g. 15 %): that value stays the
            // runner's — no jump back into range, not even step by step.
            if (targetIncline.HasValue && (last.InclinePercent > Math.Min(Limits.MaxIncline, beltMaxIncline) + 0.25f
                                           || last.InclinePercent < Math.Max(Limits.MinIncline, beltMinIncline) - 0.25f))
                targetIncline = null;
            if (targetSpeed.HasValue && (last.SpeedKmh > Math.Min(Limits.MaxSpeedKmh, beltMaxSpeed) + 0.05f
                                         || last.SpeedKmh < Math.Max(0.5f, beltMinSpeed) - 0.05f))
                targetSpeed = null;
            if (!targetIncline.HasValue && !targetSpeed.HasValue) { State = Jogging.Core.Loc.T("außerhalb des Bereichs – du steuerst"); return false; }

            float incline = last.InclinePercent;
            if (targetIncline.HasValue)
            {
                float lo = Math.Max(Limits.MinIncline, beltMinIncline), hi = Math.Min(Limits.MaxIncline, beltMaxIncline);
                float res = Math.Max(0.01f, Limits.InclineResolution);
                float goal = Clamp((float)Math.Round(targetIncline.Value / res) * res, lo, hi);
                incline = Clamp(last.InclinePercent + Clamp(goal - last.InclinePercent, -Limits.MaxInclineStep, Limits.MaxInclineStep), lo, hi);
                State = Jogging.Core.Loc.F("Steigung → {0:0} %", goal);
            }
            float speed = last.SpeedKmh;
            if (targetSpeed.HasValue)
            {
                float lo = Math.Max(0.5f, beltMinSpeed), hi = Math.Min(Limits.MaxSpeedKmh, beltMaxSpeed);
                float goal = Clamp((float)Math.Round(targetSpeed.Value * 10f) / 10f, lo, hi); // 0.1 km/h
                speed = Clamp(last.SpeedKmh + Clamp(goal - last.SpeedKmh, -Limits.MaxSpeedStep, Limits.MaxSpeedStep), lo, hi);
                State = (targetIncline.HasValue ? State + " · " : "") + Jogging.Core.Loc.F("Tempo → {0}", Jogging.Core.Units.FmtSpeed(goal));
            }

            bool speedChanges = !Same(speed, last.SpeedKmh, 0.05f);
            if (Same(incline, last.InclinePercent, 0.25f) && !speedChanges) return false; // already there

            cmd = new BeltCommand { SpeedKmh = speed, InclinePercent = incline, ChangesSpeed = speedChanges, SetsIncline = targetIncline.HasValue };
            sentIncline = incline; sentSpeed = speed; lastSentTime = now;
            rampFromSpeed = last.SpeedKmh; rampFromIncline = last.InclinePercent;
            return true;
        }

        /// <summary>
        /// Outside a run (finished, discarded, menu): bring a still running belt back to level — only
        /// ever lowering the incline one step at a time towards 0 %, never raising it, never changing
        /// the speed, and only if the belt is still where the app put it (<paramref name="appIncline"/>):
        /// an incline the runner set is never touched. Same rules otherwise: belt running, settle,
        /// interval, manual change wins.
        /// </summary>
        public bool TryLevelOut(float now, float? appIncline, out BeltCommand cmd)
        {
            cmd = default;
            if (!hasReading || !last.Running) return false;
            if (!appIncline.HasValue || !Same(last.InclinePercent, appIncline.Value, 0.5f)) { State = ""; return false; }
            if (requireRange && !rangeKnown) return false;
            if (rangeKnown && beltMaxIncline <= beltMinIncline) return false;   // belt has no incline
            // level = 0 % (or the belt's lowest if that is above 0); from a decline the way back is up
            float level = Math.Min(Math.Max(0f, beltMinIncline), beltMaxIncline);
            if (Math.Abs(last.InclinePercent - level) <= 0.25f) { State = ""; return false; }
            if (last.InclinePercent < level && Limits.MinIncline >= 0f) { State = ""; return false; } // a decline the app can't have set: the runner's
            if (InclineOverridden(now)) { State = Jogging.Core.Loc.T("pausiert (manuell verstellt)"); return false; }
            if (now - lastSpeedChange < Limits.SpeedSettle) return false;
            if (now - lastSentTime < Limits.SendInterval) return false;

            float res = Math.Max(0.01f, Limits.InclineResolution);
            bool down = last.InclinePercent > level;
            float incline = down ? Math.Max(level, (float)Math.Round((last.InclinePercent - Limits.MaxInclineStep) / res) * res)
                                 : Math.Min(level, (float)Math.Round((last.InclinePercent + Limits.MaxInclineStep) / res) * res);
            if (down && incline >= last.InclinePercent) incline = Math.Max(level, last.InclinePercent - res);  // always a step
            if (!down && incline <= last.InclinePercent) incline = Math.Min(level, last.InclinePercent + res);
            cmd = new BeltCommand { SpeedKmh = last.SpeedKmh, InclinePercent = incline, ChangesSpeed = false, SetsIncline = true };
            sentIncline = incline; sentSpeed = last.SpeedKmh; lastSentTime = now;
            rampFromSpeed = last.SpeedKmh; rampFromIncline = last.InclinePercent;
            State = Jogging.Core.Loc.F("Steigung zurück → {0:0} %", level);
            return true;
        }

        private static bool OnTheWay(float v, float from, float to, float eps) =>
            v >= Math.Min(from, to) - eps && v <= Math.Max(from, to) + eps;

        private static bool Same(float a, float b, float eps) => Math.Abs(a - b) <= eps;
        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
