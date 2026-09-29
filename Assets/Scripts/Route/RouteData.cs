using UnityEngine;

namespace Jogging.Route
{
    /// <summary>
    /// The lap facts the run logic needs (length, finish or not). Created at runtime by
    /// <see cref="Jogging.World.RouteRuntime"/> from the route document (<see cref="RouteDoc"/>).
    /// </summary>
    public class RouteData : ScriptableObject
    {
        [Tooltip("Length of the route in metres. 0 = endless (no finish).")]
        public float lengthMeters;

        /// <summary>No finish line.</summary>
        public bool IsEndless => lengthMeters <= 0f;
    }
}
