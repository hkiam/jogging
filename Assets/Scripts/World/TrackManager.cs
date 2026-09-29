using UnityEngine;
using Jogging.Locomotion;
using Jogging.Route;

namespace Jogging.World
{
    /// <summary>
    /// Drives the run: reads speed from an <see cref="ILocomotionSource"/>, moves the runner along
    /// the trail (<see cref="TrailPath"/>) over the static terrain, tracks the distance, publishes
    /// the current grade (trail profile from <see cref="TrailShaper"/>) and finishes finite routes.
    /// </summary>
    public class TrackManager : MonoBehaviour
    {
        [Tooltip("The locomotion source (LocomotionRouter: treadmill when connected, else keyboard).")]
        [SerializeField] private MonoBehaviour locomotionSourceBehaviour;
        [Tooltip("Optional route (length for a finite run); null = endless.")]
        [SerializeField] private RouteData route;
        [Tooltip("The runner that moves along the trail.")]
        [SerializeField] private Transform movingRunner;
        [Tooltip("Hold the run until the ground (and the shaped trail bed) exists under the runner.")]
        [SerializeField] private bool waitForTerrain = true;

        private ILocomotionSource locomotion;
        private float smoothedGrade;

        /// <summary>Total distance the runner has covered (metres).</summary>
        public float DistanceTraveled { get; private set; }

        /// <summary>
        /// Speed the runner actually moves along the trail (m/s): 0 while waiting for the terrain,
        /// before the start and after the finish. Everything that moves relative to the player
        /// (crowd, spectators, the player's own run animation) uses this, not the set speed.
        /// </summary>
        public float SpeedMps { get; private set; }

        /// <summary>True while the run is held because the ground ahead isn't ready yet.</summary>
        public bool WaitingForTerrain { get; private set; }

        /// <summary>Current grade (%) at the runner's position.</summary>
        public float CurrentInclinePercent { get; private set; }

        public bool IsFinished { get; private set; }
        public event System.Action Finished;

        /// <summary>Route length (m) for a finite route; 0 when endless (HUD progress).</summary>
        public float RouteLength => (route != null && !route.IsEndless) ? route.lengthMeters : 0f;

        public RouteData Route => route;

        /// <summary>Pick the route to run (from the start menu). Resets the finish state.</summary>
        public void SetRoute(RouteData r)
        {
            route = r;
            IsFinished = false;
        }

        /// <summary>End the run now (also for endless routes): stop and raise Finished.</summary>
        public void FinishNow()
        {
            if (IsFinished) return;
            IsFinished = true;
            CurrentInclinePercent = 0f;
            Finished?.Invoke();
        }

        /// <summary>Workshop: the runner doesn't move by itself; <see cref="StartAt"/> places it.</summary>
        public bool Hold { get; set; }

        /// <summary>The session holds the run (before the start, countdown, pause). Set every frame by the session.</summary>
        public bool Frozen { get; set; }

        /// <summary>
        /// A workout runs: on a loop the run doesn't end at the finish line but goes into the next
        /// lap (the workout ends the run). Open courses still end at their finish.
        /// </summary>
        public bool KeepLapping { get; set; }

        /// <summary>True when laps continue past the finish line (loop + <see cref="KeepLapping"/>).</summary>
        public bool Lapping => KeepLapping && TrailPath.Active != null && TrailPath.Active.Loop;

        /// <summary>Current lap (1-based) on a lapping loop.</summary>
        public int Lap => RouteLength > 0f ? Mathf.FloorToInt(DistanceTraveled / RouteLength) + 1 : 1;

        /// <summary>Tests: begin the run at this distance along the trail.</summary>
        public void StartAt(float meters)
        {
            DistanceTraveled = Mathf.Max(0f, meters);
            var path = TrailPath.Active;
            if (path != null && movingRunner != null)
            {
                path.RunnerS = DistanceTraveled;
                Vector3 c = path.Point(DistanceTraveled);
                movingRunner.SetPositionAndRotation(new Vector3(c.x, movingRunner.position.y, c.z), path.Rotation(DistanceTraveled));
                // Move the camera along, so the terrain streams in around the new start right away.
                if (Camera.main != null) Camera.main.transform.position = movingRunner.position - path.Tangent(DistanceTraveled) * 5f + Vector3.up * 3f;
            }
        }

        private void Awake()
        {
            locomotion = locomotionSourceBehaviour as ILocomotionSource;
            if (locomotion == null)
                Debug.LogError("TrackManager: locomotionSourceBehaviour must implement ILocomotionSource.");
        }

        private void Update()
        {
            long t0 = FrameWork.Start();
            UpdateWork();
            FrameWork.Stop("Strecke", t0);
        }

        private void UpdateWork()
        {
            if (locomotion == null || movingRunner == null) return;
            float dt = Time.deltaTime;
            locomotion.Tick(dt);
            SpeedMps = 0f;
            if (IsFinished || Hold) return;

            var path = TrailPath.Active;
            Vector3 rp = movingRunner.position;
            WaitingForTerrain = waitForTerrain &&
                (!TerrainGround.TryHeight(rp.x, rp.z, out _) // tiles not generated yet
                 // …and, on a shaped trail, until the bed ahead is final (no height jumps under you).
                 || (path != null && !TrailShaper.Ready(DistanceTraveled - 5f, DistanceTraveled + 60f)));
            if (WaitingForTerrain || Frozen) return;

            SpeedMps = Mathf.Max(0f, locomotion.SpeedMps);
            float delta = SpeedMps * dt;
            DistanceTraveled += delta;
            if (route != null && !route.IsEndless && DistanceTraveled >= route.lengthMeters && !Lapping)
            {
                DistanceTraveled = route.lengthMeters;
                FinishNow();
                return;
            }

            float g;
            bool hasGrade;
            if (path != null)
            {
                // Along the (curved) trail: position + heading from the centre line at s.
                float s = DistanceTraveled;
                path.RunnerS = s;
                Vector3 c = path.Point(s);
                movingRunner.SetPositionAndRotation(new Vector3(c.x, rp.y, c.z), path.Rotation(s));
                // The shaped trail's own profile is exact and smooth; else sample the ground.
                hasGrade = (TrailShaper.Active != null && TrailShaper.Active.TryGrade(s, out g)) || GradeAlong(path, s, 4f, out g);
            }
            else
            {
                rp.z += delta;
                movingRunner.position = rp;
                hasGrade = TerrainGround.TryGrade(rp.x, rp.z, 4f, out g);
            }
            // (height is kept on the ground by TerrainFollower)

            // Smoothed, so small bumps don't jerk the HUD / treadmill.
            if (hasGrade)
                smoothedGrade = Mathf.Lerp(smoothedGrade, g, 1f - Mathf.Exp(-2f * dt));
            CurrentInclinePercent = smoothedGrade;
            if (locomotion is KeyboardLocomotionSource kb) kb.InclinePercent = CurrentInclinePercent;
        }

        private static bool GradeAlong(TrailPath path, float s, float half, out float gradePercent)
        {
            gradePercent = 0f;
            Vector3 a = path.Point(s - half), b = path.Point(s + half);
            if (!TerrainGround.TryHeight(a.x, a.z, out float ha) || !TerrainGround.TryHeight(b.x, b.z, out float hb)) return false;
            gradePercent = (hb - ha) / (2f * half) * 100f;
            return true;
        }
    }
}
