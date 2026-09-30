using System;

namespace Jogging.Training
{
    /// <summary>
    /// The pulse coach (plain C#, tested by HeartRateCheck): watches the heart-rate zone against a target
    /// and says when to ease off or speed up – calmly: only after the pulse has stayed out of the target zone
    /// for <see cref="Patience"/> seconds, at most every <see cref="Repeat"/> seconds, not in the first minute
    /// of a run (the pulse is still rising) and not while there is no pulse. When the runner is back in the
    /// target zone after a hint, it says so once.
    /// Target: the zone of the current workout segment; in a free run the runner's own target zone
    /// (profile: "Pulscoach"). Off per runner.
    /// </summary>
    public class PulseCoach
    {
        public const float Patience = 20f, Repeat = 60f, WarmUp = 60f;

        public enum Hint { None, Slower, Faster, Good }

        private float outSince = -1f, lastHint = -1000f;
        private int outDir;            // +1 too high, −1 too low
        private bool warned;

        /// <summary>
        /// One step. time = run seconds; zone = current zone (0 = no pulse); target = target zone (0 = none).
        /// Returns what to say now (usually None).
        /// </summary>
        public Hint Step(float time, int zone, int target)
        {
            if (target <= 0 || zone <= 0 || time < WarmUp) { outSince = -1f; return Hint.None; }
            int dir = zone > target ? 1 : zone < target ? -1 : 0;
            if (dir == 0)
            {
                outSince = -1f;
                if (warned) { warned = false; return Hint.Good; }
                return Hint.None;
            }
            if (outSince < 0f || dir != outDir) { outSince = time; outDir = dir; }
            if (time - outSince < Patience || time - lastHint < Repeat) return Hint.None;
            lastHint = time; warned = true;
            return dir > 0 ? Hint.Slower : Hint.Faster;
        }

        /// <summary>The spoken / shown text of a hint (German or English, see <see cref="Jogging.Core.Loc"/>).</summary>
        public static string Text(Hint h, int zone, int target)
        {
            bool en = Jogging.Core.Loc.En;
            switch (h)
            {
                case Hint.Slower: return en ? $"Ease off a little – zone {zone}, your target is zone {target}." : $"Etwas ruhiger – Zone {zone}, dein Ziel ist Zone {target}.";
                case Hint.Faster: return en ? $"A little faster – zone {zone}, your target is zone {target}." : $"Ein bisschen schneller – Zone {zone}, dein Ziel ist Zone {target}.";
                case Hint.Good: return en ? $"Good, back in zone {target}." : $"Gut so, wieder in Zone {target}.";
                default: return "";
            }
        }
    }
}
