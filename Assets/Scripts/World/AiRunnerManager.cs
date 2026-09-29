using System.Collections.Generic;
using UnityEngine;
using Jogging.Locomotion;
using Jogging.UI;

namespace Jogging.World
{
    /// <summary>
    /// Other joggers on the trail (Rocketbox figures), plus one marked rival/pacer. Their pace follows
    /// the runner's own (smoothed over ~30 s): each has a character of 90–110 % of it, so some are
    /// caught and some pull away slowly — at 8 km/h the field runs 7–9 km/h, not 8–21. A rubber band
    /// keeps them in sight (far ahead → just below your pace, far behind → just above), so overtakes
    /// come every few minutes. The rival runs "gleichauf": a few metres ahead at your pace, catchable
    /// when you speed up; once passed he holds on for a while, then drops back and a new one appears.
    /// Each runner has a logical position (lane, metres ahead of the player along the trail);
    /// apparent motion is (aiSpeed − playerSpeed): faster ones overtake you, slower ones you
    /// overtake, and runners that drop out of the window respawn at its other end. Exposes race
    /// state for the HUD (overtakes, rank, rival gap).
    /// </summary>
    public class AiRunnerManager : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour locomotionSourceBehaviour; // ILocomotionSource (player)
        [SerializeField] private int runnerCount = 8;
        [SerializeField] private float minSpeedMps = 2.2f;
        [SerializeField] private float maxSpeedMps = 5.8f;
        [SerializeField] private float aheadZ = 95f;
        [SerializeField] private float behindZ = -28f;
        [SerializeField] private float rivalPaceKmh = 11f; // start value until the runner's pace is known

        [Header("Pace relative to the runner")]
        [Tooltip("Characters: this share of the runner's pace (min, max).")]
        [SerializeField] private Vector2 character = new Vector2(0.90f, 1.10f);
        [Tooltip("Seconds over which the runner's pace is averaged.")]
        [SerializeField] private float paceSmoothing = 30f;
        [Tooltip("How fast a figure changes its pace (m/s per second) — gentle, so it looks natural.")]
        [SerializeField] private float paceChange = 0.25f;
        [Tooltip("Rubber band: beyond this far ahead a figure eases to just below your pace.")]
        [SerializeField] private float bandAhead = 60f;
        [Tooltip("Rubber band: beyond this far behind a figure eases to just above your pace.")]
        [SerializeField] private float bandBehind = -18f;

        [Header("Rival (gleichauf)")]
        [SerializeField] private float rivalGap = 6f;       // metres ahead he tries to keep
        [SerializeField] private float rivalMaxBoost = 0.03f; // at most this much faster than you (catchable)
        [SerializeField] private float rivalHoldSeconds = 30f; // after being passed he holds on this long

        [Header("Figures (Rocketbox)")]
        [SerializeField] private GameObject[] figureModels;
        [SerializeField] private RuntimeAnimatorController maleRunController;
        [SerializeField] private RuntimeAnimatorController femaleRunController;

        [Header("Placement")]
        [Tooltip("The player (for the ground height fallback without a trail).")]
        [SerializeField] private Transform followRunner;
        [Tooltip("Lane spread factor — the trail is ~3.4 m wide.")]
        [SerializeField] private float laneScale = 1f;
        [Tooltip("Minimum sideways distance from the player's line (x = 0), so nobody runs shoulder to shoulder.")]
        [SerializeField] private float minLaneOffset = 0f;

        private static readonly float[] Lanes = { -2.6f, -1.4f, 1.4f, 2.6f, -0.7f, 0.7f };

        private class Runner
        {
            public Transform root;
            public Animator anim;
            public bool isRival;
            public float speed, prevZ;
            public float factor = 1f;   // character: share of the runner's pace
            public float passedAt = -1f; // rival: when he was overtaken (run time)
            public Vector3 pos; // logical: x = lane, z = metres ahead of the player (along the trail)
        }

        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private readonly List<Runner> runners = new List<Runner>();
        private ILocomotionSource loco;
        private int seedCounter;

        // Race state for the HUD.
        public int OvertakenCount { get; private set; }
        public int FieldSize => runners.Count + 1;
        public int PlayerRank { get; private set; } = 1;
        public bool HasRival { get; private set; }
        public float RivalGapMeters { get; private set; }
        /// <summary>You overtook the rival and he hasn't come back ahead yet (he holds on, then drops back as designed).</summary>
        public bool RivalOvertaken { get; private set; }
        /// <summary>How often a figure passed you (the other direction of <see cref="OvertakenCount"/>).</summary>
        public int PassedByCount { get; private set; }
        /// <summary>Your smoothed pace and what the field paces on (km/h; tests, -diag).</summary>
        public float RefPaceKmh => refPace * 3.6f;
        public float PacerKmh => pacer * 3.6f;
        /// <summary>Slowest and fastest figure (km/h), the rival excluded.</summary>
        public Vector2 FieldKmh
        {
            get
            {
                float mn = float.MaxValue, mx = 0f;
                foreach (var r in runners) if (!r.isRival) { mn = Mathf.Min(mn, r.speed); mx = Mathf.Max(mx, r.speed); }
                return new Vector2(mn, mx) * 3.6f;
            }
        }

        private TrackManager track;
        private float refPace = -1f;   // the runner's smoothed pace (m/s), -1 = not known yet
        private float pacer;           // what the field paces on: your pace, in workouts mostly the target

        [Tooltip("In a workout segment with a target pace the field runs this share of the target (rest: your pace).")]
        [SerializeField] private float workoutPull = 0.7f;
        private float runTime;         // seconds the run actually moved
        private float nextDiag;
        private static readonly bool diag = Jogging.Core.Args.Has("-diag");

        private void Awake()
        {
            loco = locomotionSourceBehaviour as ILocomotionSource;
            track = FindFirstObjectByType<TrackManager>();
        }

        private void Start()
        {
            if (figureModels == null || figureModels.Length == 0) { Debug.LogWarning("[Jogging] Keine Läuferfiguren zugewiesen."); return; }
            if (GraphicsQuality.Level == "minimal") runnerCount = Mathf.Min(runnerCount, 5); // weak tablets: every figure is ~16k triangles, skinned
            for (int i = 0; i < runnerCount; i++)
            {
                var r = BuildRunner(i == 0);
                // close around you at the start (the band would turn far-ahead ones into slow ones
                // that take minutes to reach); the rival a few metres ahead
                Respawn(r, r.isRival ? rivalGap + 2f : Random.Range(bandBehind + 2f, bandAhead - 10f));
                runners.Add(r);
                if (r.isRival) HasRival = true;
            }
            if (Profile.ProfileService.Instance != null) Profile.ProfileService.Instance.FigureChanged += OnFigureChanged;
        }

        private void Update()
        {
            long t0 = FrameWork.Start();
            UpdateWork();
            FrameWork.Stop("Mitläufer", t0);
        }

        private void UpdateWork()
        {
            // The player's real progress (0 while the run waits for the terrain); the crowd holds
            // its positions until the run actually moves.
            float pv = track != null ? track.SpeedMps : (loco != null ? loco.SpeedMps : 0f);
            float dt = Time.deltaTime;
            if (track != null && (track.WaitingForTerrain || track.IsFinished || track.Frozen)) dt = 0f;
            int ahead = 0;

            // The runner's pace, smoothed (the field follows it; a sudden sprint is not matched at once).
            if (dt > 0f && pv > 0.4f)
            {
                runTime += dt;
                // a running mean for the first seconds (the belt is still ramping up), then the EMA
                // follows the belt's ramp-up closely at first (a mean over the ramp would be far too slow),
                // then smooths more and more: time constant 3 s at the start, 30 s after ~half a minute
                refPace = refPace < 0f ? pv : Mathf.Lerp(refPace, pv, 1f - Mathf.Exp(-dt / Mathf.Min(paceSmoothing, 3f + runTime)));
            }

            // Workouts: intervals pull the field up, recoveries let it jog along — mostly the target pace,
            // partly yours, so it doesn't leave you when you can't quite make the target.
            pacer = refPace;
            var seg = Jogging.Training.WorkoutRuntime.Runner?.Current;
            if (refPace > 0f && seg != null && seg.speedKmh > 0f) pacer = Mathf.Lerp(refPace, seg.speedKmh / 3.6f, workoutPull);

            foreach (var r in runners)
            {
                if (dt > 0f && refPace > 0f)
                {
                    float target = TargetPace(r);
                    // the rival matches you quickly (he stays level); the others change pace gently
                    r.speed = Mathf.MoveTowards(r.speed, target, (r.isRival ? 4f : 1f) * paceChange * dt);
                }
                var p = r.pos;
                p.z += (r.speed - pv) * dt;

                bool recycled = false;
                if (p.z < behindZ)
                {
                    // a dropped rival comes back as a new one a little ahead, in reach — not 95 m out
                    Respawn(r, r.isRival ? 20f : aheadZ); recycled = true;
                    if (r.isRival && refPace > 0f) RaceMessages.Post(Jogging.Core.Loc.T("Ein neuer Rivale vor dir!"));
                }
                else if (p.z > aheadZ) { Respawn(r, behindZ); recycled = true; }
                else r.pos = p;

                float z = r.pos.z;
                if (!recycled)
                {
                    if (r.prevZ > 0f && z <= 0f)
                    {
                        OvertakenCount++;
                        if (r.isRival && runTime > 20f) r.passedAt = runTime; // not while the belt is still ramping up
                        RaceMessages.Post(r.isRival ? Jogging.Core.Loc.T("🎉  Rivale eingeholt!") : Jogging.Core.Loc.T("Läufer überholt! 🏃"));
                    }
                    else if (r.prevZ < 0f && z >= 0f)
                    {
                        PassedByCount++;
                        RaceMessages.Post(r.isRival ? Jogging.Core.Loc.T("Der Rivale zieht davon…") : Jogging.Core.Loc.T("Wurdest überholt"));
                    }
                    RealFigure.Drive(r.anim, dt > 0f ? r.speed : 0f); // stand while the run is held
                }
                r.prevZ = z;
                Place(r.root, r.pos);

                if (z > 0f) ahead++;
                if (r.isRival) { RivalGapMeters = z; RivalOvertaken = r.passedAt >= 0f && z < 0f; }
            }

            PlayerRank = ahead + 1;

            if (diag && Time.unscaledTime > nextDiag && refPace > 0f)
            {
                nextDiag = Time.unscaledTime + 10f;
                float min = float.MaxValue, max = float.MinValue, rival = 0f;
                foreach (var r in runners) { min = Mathf.Min(min, r.speed); max = Mathf.Max(max, r.speed); if (r.isRival) rival = r.pos.z; }
                Debug.Log($"[Mitläufer] du {pv * 3.6f:0.0} km/h (Ø {refPace * 3.6f:0.0}, Feld richtet sich nach {pacer * 3.6f:0.0}) · Feld {min * 3.6f:0.0}–{max * 3.6f:0.0} km/h · Rivale {rival:+0;-0} m · überholt {OvertakenCount}");
            }
        }

        // Pace a figure aims for: its character × the runner's pace, bent by the rubber band; the rival
        // keeps a few metres ahead at your pace but is never much faster (you can catch him).
        private float TargetPace(Runner r)
        {
            float z = r.pos.z;
            if (r.isRival)
            {
                if (r.passedAt >= 0f && z < 0f)
                    return pacer * (runTime - r.passedAt < rivalHoldSeconds ? 0.995f : 0.95f); // holds on, then drops back
                if (z >= 0f) r.passedAt = -1f; // he is ahead again
                float boost = Mathf.Clamp((rivalGap - z) / 8f * 0.05f, -0.15f, rivalMaxBoost); // pulls back quickly after you slow down
                return pacer * (1f + boost);
            }
            // Rubber band: far ahead, a fast one becomes a slower one (you'll catch it); far behind, a
            // slow one becomes a faster one (it will pass you) — the field keeps coming by.
            if (z > bandAhead && r.factor > 0.97f) r.factor = Random.Range(character.x, 0.95f);
            else if (z < bandBehind && r.factor < 1.03f) r.factor = Random.Range(1.05f, character.y);
            return pacer * r.factor;
        }

        private void Respawn(Runner r, float z)
        {
            // Characters spread evenly over the field (slow … fast), not left to chance
            int slot = runners.IndexOf(r); if (slot < 0) slot = runners.Count;
            float t = runnerCount > 1 ? ((slot * 3) % runnerCount) / (float)(runnerCount - 1) : 0.5f;
            r.factor = Mathf.Lerp(character.x, character.y, t) + Random.Range(-0.015f, 0.015f);
            r.passedAt = -1f;
            // Until the runner's pace is known, the old spread; afterwards relative to it.
            r.speed = refPace > 0f ? (pacer > 0f ? pacer : refPace) * (r.isRival ? 1f : r.factor)
                                   : (r.isRival ? rivalPaceKmh / 3.6f : Random.Range(minSpeedMps, maxSpeedMps));
            float lane = (r.isRival ? 0.7f : Lanes[Random.Range(0, Lanes.Length)]) * laneScale;
            if (Mathf.Abs(lane) < minLaneOffset) lane = Mathf.Sign(lane) * minLaneOffset;
            r.pos = new Vector3(lane, 0f, z);
            r.prevZ = z;
            Place(r.root, r.pos);
        }

        private Runner BuildRunner(bool rival)
        {
            var root = new GameObject(rival ? "Rival" : "AiRunner").transform;
            root.SetParent(transform, false);
            var r = new Runner { root = root, isRival = rival };
            SetFigure(r, PickModel(RealFigure.PlayerFigure()));
            if (rival) RivalTag.Create(root);
            return r;
        }

        private void SetFigure(Runner r, GameObject model)
        {
            if (r.anim != null) Destroy(r.anim.gameObject);
            r.anim = RealFigure.Spawn(r.root, model, maleRunController, femaleRunController);
            r.anim.SetFloat(SpeedId, 3f);
            r.anim.Play(0, 0, Random.value); // de-sync strides
        }

        // Any figure but the player's own
        private GameObject PickModel(string player) =>
            RealFigure.NotPlayer(figureModels[(seedCounter++ * 7 + Random.Range(0, figureModels.Length)) % figureModels.Length], figureModels, player);

        // The runner picks another figure in the menu: whoever wears it now gets a different one.
        private void OnFigureChanged(string _)
        {
            string player = RealFigure.PlayerFigure();
            foreach (var r in runners)
                if (r.anim != null && r.anim.gameObject.name == player) SetFigure(r, PickModel(player));
        }

        private void OnDestroy() { if (Profile.ProfileService.Instance != null) Profile.ProfileService.Instance.FigureChanged -= OnFigureChanged; }

        // Logical (lane, 0, metres ahead) → world: on the trail centre line relative to the player's
        // arc length, facing along it, standing on the ground.
        private void Place(Transform t, Vector3 logical)
        {
            var path = TrailPath.Active;
            Vector3 w;
            Quaternion rot;
            if (path != null)
            {
                float s = path.RunnerS + logical.z;
                w = path.Offset(s, logical.x);
                rot = path.Rotation(s);
            }
            else
            {
                Vector3 o = followRunner != null ? followRunner.position : Vector3.zero;
                w = new Vector3(logical.x, 0f, o.z + logical.z);
                rot = Quaternion.identity;
            }
            if (TerrainGround.TryHeight(w.x, w.z, out float h)) w.y = h;
            t.SetPositionAndRotation(w, rot);
        }
    }
}
