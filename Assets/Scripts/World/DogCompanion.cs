using UnityEngine;
using Jogging.Profile;

namespace Jogging.World
{
    /// <summary>
    /// The runner's own dog (Profil → Begleithund; model from Resources/Dogs, Editor/DogAssets). It runs along like
    /// a real dog, not on a rail: mostly at heel (left or right, now and then changing sides), sometimes it runs
    /// ahead and waits looking back, stays behind sniffing at the wayside and catches up at a gallop, or makes an
    /// extra round out into the meadow and back. It walks, trots and gallops with the ground speed (paws fitting
    /// the ground), leans into curves, keeps off lakes and wayside objects and never sits between the camera and
    /// you. When you stop, it comes to you and waits. It controls nothing.
    /// Position: along the trail (s, metres) and sideways (lat, right = +), like the other figures.
    /// </summary>
    public class DogCompanion : MonoBehaviour
    {
        public enum Mode { Heel, Ahead, Wait, Sniff, Catch, Round, Idle }

        /// <summary>Breeds with a model in this build (Resources/Dogs): id → German name.</summary>
        public static readonly (string id, string name)[] Breeds = { ("germanshepherd", "Deutscher Schäferhund") };

        public static bool Available(string breed) => !string.IsNullOrEmpty(breed) && Resources.Load<GameObject>("Dogs/" + breed) != null;

        public static DogCompanion Current { get; private set; }
        public Mode State => mode;
        /// <summary>Distance to the runner along the trail (m; + = ahead) – tests.</summary>
        public float Gap => s - runnerS;
        /// <summary>The clip playing, its rate and the turn rate (tests).</summary>
        public string Playing => $"{playing} ×{(rig != null && rig.anim != null && rig.anim[playing] != null ? rig.anim[playing].speed : 0f):0.00}, {yawRate:0}°/s, {new Vector2(vl, vs).magnitude:0.0} m/s";

        private DogRig rig;
        private string breed = "";
        private Transform body;
        private string dogName = "";
        private float s, lat, vs, vl;           // position and velocity in trail coordinates
        private float runnerS, runnerLat, runnerV;
        private Mode mode = Mode.Heel;
        private float modeUntil, nextChoice, side = 1f, heelAhead = 1.0f, sniffS, roundT, roundLen, roundAmp, aheadGoal;
        private float lastMessage = -100f, yaw, yawRate;
        private string playing = "";
        private TrackManager track;
        private AiRunnerManager field;
        private readonly System.Collections.Generic.List<Vector2> others = new System.Collections.Generic.List<Vector2>();
        private float lastHeel;
        private const float Clear = 0.95f, ClearAlong = 1.3f; // room it keeps around every runner (sideways, along)
        private RealPlayerFigure player;
        private System.Random rnd = new System.Random();

        // ground speed of the clips as played at rate 1 (from their cycle: a walk step 0.6 s, a gallop stride 0.4 s);
        // up to WalkTop a brisk walk (closest to a trot, which the model doesn't have), above it the gallop
        private const float WalkNatural = 1.3f, RunNatural = 4.2f, WalkTop = 2.65f, MaxSpeed = 10f, Accel = 7f;

        /// <summary>Puts the active runner's dog into the run (none if switched off or no model in this build).</summary>
        public static void ForActiveRunner()
        {
            var ps = ProfileService.Instance;
            var p = ps != null ? ps.Profile : null;
            var a = Jogging.Core.Args.All; // tests: -dog <breed> [name]
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-dog" && p != null) { p.dog = a[i + 1]; if (i + 2 < a.Length && !a[i + 2].StartsWith("-")) p.dogName = a[i + 2]; }
            bool want = p != null && Available(p.dog);
            if (Current != null && (!want || Current.breed != p.dog)) { Destroy(Current.gameObject); Current = null; }
            if (!want || Current != null) { if (Current != null) Current.dogName = DogName(p); return; }
            var prefab = Resources.Load<GameObject>("Dogs/" + p.dog);
            Debug.Log($"[Hund] {DogName(p)} ({p.dog}) läuft mit");
            var go = new GameObject("Hund");
            var dog = go.AddComponent<DogCompanion>();
            dog.rig = Instantiate(prefab, go.transform, false).GetComponent<DogRig>();
            dog.body = dog.rig.transform;
            dog.dogName = DogName(p);
            dog.breed = p.dog;
            Current = dog;
        }

        public static string DogName(ProfileData p) => string.IsNullOrWhiteSpace(p?.dogName) ? Jogging.Core.Loc.T("Bello") : p.dogName.Trim();

        private void Start()
        {
            track = FindFirstObjectByType<TrackManager>();
            field = FindFirstObjectByType<AiRunnerManager>();
            lastHeel = Time.time;
            player = FindFirstObjectByType<RealPlayerFigure>();
            var path = TrailPath.Active;
            ReadRunner(path);
            s = runnerS + heelAhead; lat = side * 0.95f;
            nextChoice = Time.time + Range(8f, 18f);
            if (rig != null && rig.anim != null)
                foreach (AnimationState st in rig.anim) st.wrapMode = WrapMode.Loop;
        }

        private void OnEnable() { if (ProfileService.Instance != null) ProfileService.Instance.ActiveChanged += OnRunner; }
        private void OnDisable() { if (ProfileService.Instance != null) ProfileService.Instance.ActiveChanged -= OnRunner; }
        private void OnDestroy() { if (Current == this) Current = null; }
        private static void OnRunner(ProfileData _) => ForActiveRunner(); // another runner, or the dog switched on/off/renamed

        private void ReadRunner(TrailPath path)
        {
            if (path == null) return;
            runnerS = path.RunnerS;
            runnerV = track != null ? track.SpeedMps : 0f;
            runnerLat = 0f;
            if (player != null) { path.Project(player.transform.position, runnerS, 10f, out float l); runnerLat = l; }
        }

        private void Update()
        {
            var path = TrailPath.Active;
            if (path == null || rig == null) return;
            float frame = Time.deltaTime;
            ReadRunner(path);
            var sm = Jogging.UI.RunSessionUI.Session;
            bool moving = runnerV > 0.4f && sm != null && sm.Started;

            // never lost: far off (a jump in the run, a reload) → simply beside you again
            if (Mathf.Abs(s - runnerS) > 120f) { s = runnerS + heelAhead; lat = runnerLat + side * 0.95f; vs = runnerV; vl = 0f; Switch(Mode.Heel, 0f); }
            Decide(moving, path);
            var (ts, tl, vmax) = Target(path);

            others.Clear();
            if (field != null) field.Positions(others);
            // small steps (a slow frame or the fast-forward of the tests must not leave it behind)
            int steps = Mathf.Clamp(Mathf.CeilToInt(frame / 0.04f), 1, 40);
            float dt = frame / steps;
            for (int k = 0; k < steps; k++)
            {
                float rs = runnerS - runnerV * (frame - dt * (k + 1)); // where you were at this step
                // steer towards the target point (arrive), the runner's pace fed forward so heel stays smooth
                float feed = mode == Mode.Wait || mode == Mode.Sniff || mode == Mode.Idle ? 0f : runnerV;
                float ds = ts - (runnerS - rs) - s, dl = tl - lat;
                float wantS = feed + Mathf.Clamp(ds * 1.6f, -vmax, vmax), wantL = Mathf.Clamp(dl * 2.0f, -3.5f, 3.5f);
                var want = new Vector2(wantL, wantS);
                if (want.magnitude > vmax) want = want.normalized * vmax;
                var v = Vector2.MoveTowards(new Vector2(vl, vs), want, Accel * dt);
                vl = v.x; vs = v.y;
                s += vs * dt; lat += vl * dt;
                KeepClear(rs);
            }
            lat = Mathf.Clamp(lat, -14f, 14f);

            Place(path, frame, moving);
        }

        // Always makes way: never inside a runner (you or a fellow runner), never between the camera and you.
        private void KeepClear(float rs)
        {
            void Away(float os, float ol, float along, float sideways)
            {
                float ds = s - os, dl = lat - ol;
                if (Mathf.Abs(ds) >= along || Mathf.Abs(dl) >= sideways) return;
                // to the side it is on – or, right behind/in front, to the outer side of the trail
                float dir = Mathf.Abs(dl) > 0.05f ? Mathf.Sign(dl) : (Mathf.Abs(ol + sideways) < Mathf.Abs(ol - sideways) ? 1f : -1f);
                lat = ol + dir * sideways;
                if (Mathf.Sign(vl) != dir) vl = 0f;
            }
            Away(rs, runnerLat, ClearAlong, Clear);
            Away(rs - 4.5f, runnerLat, 4.5f, 1.3f); // the camera's view of you: not behind you on your line
            foreach (var o in others) Away(rs + o.x, o.y, ClearAlong, Clear);
        }

        // ------------------------------------------------------------------ behaviour

        private void Decide(bool moving, TrailPath path)
        {
            float t = Time.time;
            if (!moving)
            {
                if (mode != Mode.Idle && mode != Mode.Wait) Switch(Mode.Idle, 0f);
                return;
            }
            if (mode == Mode.Idle) Switch(Mode.Heel, 0f);
            // never away for long: after 40 s it comes back to you, whatever it was doing
            if (mode == Mode.Heel) lastHeel = t;
            else if (t - lastHeel > 40f && mode != Mode.Catch) Switch(Mode.Catch, 0f);

            switch (mode)
            {
                case Mode.Ahead:
                    if (s >= aheadGoal - 1.5f) { Switch(Mode.Wait, 0f); Say("{0} wartet auf dich"); }
                    break;
                case Mode.Wait:
                    if (s - runnerS < 4f) Switch(Mode.Heel, 0f);
                    break;
                case Mode.Sniff:
                    if (t > modeUntil || runnerS - s > 30f) { Switch(Mode.Catch, 0f); }
                    break;
                case Mode.Catch:
                    if (Mathf.Abs(s - (runnerS + heelAhead)) < 1.5f) Switch(Mode.Heel, 0f);
                    break;
                case Mode.Round:
                    roundT += Time.deltaTime / roundLen;
                    if (roundT >= 1f) Switch(Mode.Heel, 0f);
                    break;
                case Mode.Heel:
                    if (t > nextChoice) Choose(path);
                    // the heel spot wanders a little: never exactly the same distance
                    heelAhead = Mathf.Lerp(heelAhead, 0.8f + Mathf.PerlinNoise(t * 0.07f, 3.1f) * 1.4f, Time.deltaTime);
                    break;
            }
        }

        private void Choose(TrailPath path)
        {
            nextChoice = Time.time + Range(10f, 28f);
            double r = rnd.NextDouble();
            if (r < 0.30) { aheadGoal = runnerS + Range(14f, 34f); side = RandomSide(); Switch(Mode.Ahead, 0f); Say("{0} läuft voraus"); }
            else if (r < 0.55) { sniffS = s; side = RandomSide(); Switch(Mode.Sniff, Range(4f, 10f)); Say("{0} schnüffelt am Wegrand"); }
            else if (r < 0.75 && RoundFree(path)) { roundT = 0f; roundLen = Range(7f, 11f); Switch(Mode.Round, 0f); Say("{0} dreht eine Extrarunde"); }
            else side = -side; // change sides (crosses in front of you)
        }

        // an extra round needs open ground: no lake, no wayside object out there
        private bool RoundFree(TrailPath path)
        {
            side = RandomSide();
            roundAmp = Range(6f, 11f);
            for (int k = 0; k < 2; k++)
            {
                bool ok = true;
                for (float f = 0.2f; f <= 1f; f += 0.2f)
                {
                    float ss = runnerS + 4f + 10f * f, ll = side * roundAmp * Mathf.Sin(Mathf.PI * f);
                    var w = path.Offset(ss, ll);
                    if (Lakes.Inside(w, 1f) || Wayside.Blocks(w, 1.5f)) { ok = false; break; }
                }
                if (ok) return true;
                side = -side;
            }
            return false;
        }

        private (float s, float lat, float vmax) Target(TrailPath path)
        {
            float heelS = runnerS + heelAhead, heelL = runnerLat + side * 0.95f;
            switch (mode)
            {
                case Mode.Ahead: return (aheadGoal, runnerLat + side * 1.2f, runnerV + 4.5f);
                case Mode.Wait: return (aheadGoal, runnerLat + side * 1.2f, 2f);
                case Mode.Sniff: return (sniffS, runnerLat + side * 2.3f, 2f);
                case Mode.Catch: return (heelS, heelL, runnerV + (runnerS - s > 25f ? 8f : 4f)); // far behind: a real sprint
                case Mode.Round:
                    {
                        float f = Mathf.Clamp01(roundT);
                        // out into the meadow ahead and back in a loop around you
                        float along = 3f + 9f * Mathf.Sin(Mathf.PI * f) - 4f * f;
                        return (runnerS + along, runnerLat + side * (0.95f + roundAmp * Mathf.Sin(Mathf.PI * f)), Mathf.Min(MaxSpeed, runnerV + 5f));
                    }
                case Mode.Idle: return (runnerS + 1.0f, runnerLat + side * 1.0f, 1.6f);
                default: return (heelS, heelL, Mathf.Min(MaxSpeed, runnerV + 2.5f));
            }
        }

        private void Switch(Mode m, float seconds)
        {
            mode = m;
            modeUntil = Time.time + seconds;
        }

        private void Say(string text)
        {
            if (Time.time - lastMessage < 45f) return; // sparse: a dog, not a commentator
            lastMessage = Time.time;
            Jogging.UI.RaceMessages.Post("🐕  " + Jogging.Core.Loc.F(text, dogName));
        }

        private float Range(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
        private float RandomSide() => rnd.NextDouble() < 0.5 ? -1f : 1f;

        // ------------------------------------------------------------------ in the world

        private void Place(TrailPath path, float dt, bool moving)
        {
            var w = path.Offset(s, lat);
            if (TerrainGround.TryHeight(w.x, w.z, out float h)) w.y = h;
            float speed = new Vector2(vl, vs).magnitude;
            float targetYaw;
            if (speed > 0.25f) targetYaw = Mathf.Atan2(vl, vs) * Mathf.Rad2Deg;          // where it runs
            else if (mode == Mode.Wait) targetYaw = 180f + Mathf.Atan2(runnerLat - lat, 0.5f) * Mathf.Rad2Deg; // looking back at you
            else targetYaw = yaw;
            float before = yaw;
            yaw = Mathf.MoveTowardsAngle(yaw, targetYaw, (speed > 3f ? 220f : 360f) * dt);
            yawRate = Mathf.Lerp(yawRate, Mathf.DeltaAngle(before, yaw) / Mathf.Max(dt, 1e-4f), dt * 6f);
            transform.SetPositionAndRotation(w, path.Rotation(s) * Quaternion.Euler(0f, yaw, 0f));
            Animate(speed, moving);
        }

        private void Animate(float speed, bool moving)
        {
            var a = rig.anim;
            if (a == null) return;
            AnimationClip clip; float rate;
            bool turnL = yawRate < -40f, turnR = yawRate > 40f;
            if (speed < 0.25f) { clip = (mode == Mode.Wait || (!moving && Time.time % 14f > 9f)) && rig.play != null ? rig.play : rig.idle; rate = 1f; }
            else if (speed < WalkTop) { clip = turnL && rig.walkLeft ? rig.walkLeft : turnR && rig.walkRight ? rig.walkRight : rig.walk; rate = Mathf.Clamp(speed / WalkNatural, 0.5f, 2.1f); }
            else { clip = turnL && rig.runLeft ? rig.runLeft : turnR && rig.runRight ? rig.runRight : rig.run; rate = Mathf.Clamp(speed / RunNatural, 0.62f, 1.6f); }
            if (clip == null) return;
            if (playing != clip.name) { a.CrossFade(clip.name, 0.25f); playing = clip.name; }
            var st = a[clip.name];
            if (st != null) st.speed = rate;
        }
    }
}
