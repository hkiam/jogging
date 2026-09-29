using UnityEngine;

namespace Jogging.Locomotion
{
    /// <summary>
    /// Single source of truth for how fast and how steeply the runner is moving.
    /// The whole game reads movement through this interface, so the concrete
    /// source can be swapped without touching gameplay:
    ///   - MVP (Stufe 1):   <see cref="KeyboardLocomotionSource"/>
    ///   - Later (Stufe 5): a BLE/FTMS treadmill reader implementing this same interface.
    /// </summary>
    public interface ILocomotionSource
    {
        /// <summary>Current forward speed in meters per second (>= 0).</summary>
        float SpeedMps { get; }

        /// <summary>
        /// Current incline as a grade percentage (e.g. 5 = 5% uphill, -3 = downhill).
        /// A treadmill reports this directly; keyboard/route can synthesize it.
        /// </summary>
        float InclinePercent { get; }

        /// <summary>True once the source is producing valid data (e.g. treadmill connected).</summary>
        bool IsActive { get; }

        /// <summary>Called once per frame so polling sources can update their state.</summary>
        void Tick(float deltaTime);
    }
}
