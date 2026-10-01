namespace Jogging.Training
{
    /// <summary>
    /// Incline by heart rate (plain C#, tested by HeartRateCheck): keeps the runner in the target zone by
    /// moving the belt's incline – on top of what the route or workout sets – instead of only saying so.
    /// Patient, because the pulse lags behind the effort: only after <see cref="Patience"/> s out of the zone,
    /// then one step, then <see cref="Settle"/> s waiting for the pulse to answer; not in the first
    /// <see cref="WarmUp"/> s; nothing while there is no pulse. In the zone the offset stays (that incline works).
    /// The offset stays within <see cref="MinOffset"/>…<see cref="MaxOffset"/>; the belt's limits and the
    /// safety layer (ramps, only while the belt runs, the runner's change wins) apply on top.
    /// </summary>
    public class PulseIncline
    {
        public const float Patience = 30f, Settle = 60f, WarmUp = 90f;
        public const float MaxOffset = 4f, MinOffset = -10f;

        public enum Change { None, Down, Up, AtLimit }

        /// <summary>Incline % added to the route's / workout's incline.</summary>
        public float Offset { get; private set; }

        private float outSince = -1f, lastChange = -1000f;
        private int outDir;

        public void Reset() { Offset = 0f; outSince = -1f; lastChange = -1000f; outDir = 0; }

        /// <summary>
        /// One step. time = run seconds; zone = current zone (0 = no pulse); target = target zone (0 = none);
        /// effective = the incline now set (base + offset); min/max = the belt's range; step = % per change.
        /// Returns what happened (Down/Up: offset changed; AtLimit: would have, but the belt is at its limit).
        /// </summary>
        public Change Step(float time, int zone, int target, float effective, float min, float max, float step = 1f)
        {
            if (target <= 0 || zone <= 0 || time < WarmUp) { outSince = -1f; return Change.None; }
            int dir = zone > target ? 1 : zone < target ? -1 : 0;
            if (dir == 0) { outSince = -1f; return Change.None; }
            if (outSince < 0f || dir != outDir) { outSince = time; outDir = dir; }
            if (time - outSince < Patience || time - lastChange < Settle) return Change.None;
            lastChange = time;
            outSince = time; // the next step needs another full stretch out of the zone
            if (dir > 0)
            {
                float room = System.Math.Min(Offset - MinOffset, effective - min);
                if (room < 0.25f) return Change.AtLimit;
                Offset -= System.Math.Min(step, room);
                return Change.Down;
            }
            else
            {
                float room = System.Math.Min(MaxOffset - Offset, max - effective);
                if (room < 0.25f) return Change.AtLimit;
                Offset += System.Math.Min(step, room);
                return Change.Up;
            }
        }

        public static string Text(Change c, float step, int zone, int target)
        {
            bool en = Jogging.Core.Loc.En;
            string s = step.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            switch (c)
            {
                case Change.Down: return en ? $"Pulse in zone {zone} – incline down {s} %." : $"Puls in Zone {zone} – Steigung {s} % runter.";
                case Change.Up: return en ? $"Pulse in zone {zone} – incline up {s} %." : $"Puls in Zone {zone} – Steigung {s} % rauf.";
                default: return "";
            }
        }
    }
}
