using System.Collections.Generic;
using UnityEngine;
using Jogging.Route;

namespace Jogging.World
{
    /// <summary>
    /// The trail's centre line on the ground plane, addressed by arc length s (metres along the
    /// trail; s = 0 at the start, the runner is at s = DistanceTraveled). Everything that follows
    /// the trail (runner, trail mesh, crowd, spectators, scatter, grass clearing, terrain shaping)
    /// asks this for positions.
    ///
    /// The line comes from the route (<see cref="RouteGenerator.Path"/>, one point per metre).
    /// Loops wrap around (s modulo the length); open courses continue straight before the start
    /// and after the end.
    /// </summary>
    public class TrailPath : MonoBehaviour
    {
        public static TrailPath Active { get; private set; }

        /// <summary>Runner's arc length (set by TrackManager every frame).</summary>
        public float RunnerS { get; set; }

        public bool Loop { get; private set; }
        public float Length { get; private set; }

        private List<Vector2> pts;
        private const float Step = RouteGenerator.PathStep;

        private void Awake()
        {
            Active = this;
            if (pts == null) // no route assigned yet: a default free run
            {
                var d = RoutePresets.Create("frei", 7);
                SetRoute(RouteGenerator.Path(d.@params, d.generator.seed), false);
            }
        }

        private void OnDestroy() { if (Active == this) Active = null; }

        public void SetRoute(List<Vector2> points, bool loop)
        {
            Active = this;
            pts = points;
            Loop = loop;
            Length = loop ? points.Count * Step : (points.Count - 1) * Step;
        }

        // Point at a fractional sample index (with loop wrap / straight extrapolation).
        private Vector2 At(float s)
        {
            int n = pts.Count;
            if (Loop)
            {
                s = Mathf.Repeat(s, Length);
                float x = s / Step;
                int i = Mathf.FloorToInt(x);
                return Vector2.LerpUnclamped(pts[i % n], pts[(i + 1) % n], x - i);
            }
            if (s <= 0f) return pts[0] + (pts[1] - pts[0]).normalized * s;
            if (s >= Length) return pts[n - 1] + (pts[n - 1] - pts[n - 2]).normalized * (s - Length);
            float xi = s / Step;
            int j = Mathf.Min(Mathf.FloorToInt(xi), n - 2);
            return Vector2.LerpUnclamped(pts[j], pts[j + 1], xi - j);
        }

        private Vector2 Dir(float s)
        {
            int n = pts.Count;
            if (Loop)
            {
                int i = Mathf.FloorToInt(Mathf.Repeat(s, Length) / Step);
                return (pts[(i + 1) % n] - pts[i % n]).normalized;
            }
            if (s <= 0f) return (pts[1] - pts[0]).normalized;
            if (s >= Length - Step) return (pts[n - 1] - pts[n - 2]).normalized;
            int j = Mathf.FloorToInt(s / Step);
            return (pts[j + 1] - pts[j]).normalized;
        }

        /// <summary>Centre-line point at arc length s (y = 0).</summary>
        public Vector3 Point(float s) { var p = At(s); return new Vector3(p.x, 0f, p.y); }

        /// <summary>Unit forward direction at s (y = 0).</summary>
        public Vector3 Tangent(float s) { var d = Dir(s); return new Vector3(d.x, 0f, d.y); }

        /// <summary>Unit right-hand direction at s (y = 0).</summary>
        public Vector3 Right(float s) { var t = Tangent(s); return new Vector3(t.z, 0f, -t.x); }

        /// <summary>Point at s shifted sideways by <paramref name="lateral"/> metres (right = +).</summary>
        public Vector3 Offset(float s, float lateral) => Point(s) + Right(s) * lateral;

        public Quaternion Rotation(float s) => Quaternion.LookRotation(Tangent(s), Vector3.up);

        /// <summary>
        /// Nearest centre-line position to a world point, searched within ±range metres of sHint.
        /// Returns the arc length; lateral = signed sideways distance (right = +).
        /// </summary>
        public float Project(Vector3 world, float sHint, float range, out float lateral)
        {
            var q = new Vector2(world.x, world.z);
            float best = float.MaxValue, bestS = sHint; lateral = 0f;
            float a = Mathf.Floor((sHint - range) / Step) * Step;
            for (float s = a; s <= sHint + range; s += Step)
            {
                Vector2 p0 = At(s), d = At(s + Step) - p0;
                float t = Mathf.Clamp01(Vector2.Dot(q - p0, d) / Mathf.Max(1e-6f, d.sqrMagnitude));
                Vector2 c = p0 + d * t;
                float dist = (q - c).sqrMagnitude;
                if (dist < best)
                {
                    best = dist;
                    bestS = s + t * Step;
                    Vector2 n = new Vector2(d.y, -d.x).normalized; // right of forward
                    lateral = Vector2.Dot(q - c, n);
                }
            }
            return bestS;
        }
    }
}
