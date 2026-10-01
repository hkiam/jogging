using System.Collections.Generic;
using UnityEngine;
using Jogging.Locomotion;

namespace Jogging.World
{
    /// <summary>
    /// Roadside spectators (Rocketbox figures standing, clapping, cheering or waving) beside the
    /// trail. They stand still in the world: their logical position (sideways, metres ahead of the
    /// player) moves back at the player's speed, and once behind they reappear ahead after an irregular gap.
    /// How many: the route's <c>spectators</c> setting (none / few / some / many per km).
    /// </summary>
    public class SpectatorManager : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour locomotionSourceBehaviour; // ILocomotionSource (player)
        [SerializeField] private int count = 6; // most at once (pool)
        [SerializeField] private float aheadZ = 90f;
        [SerializeField] private float behindZ = -25f;

        [Header("Figures (Rocketbox)")]
        [SerializeField] private GameObject[] figureModels;
        [SerializeField] private RuntimeAnimatorController maleController;   // states Idle/Clap/Cheer/Wave
        [SerializeField] private RuntimeAnimatorController femaleController;

        [Header("Placement")]
        [Tooltip("The player (for the ground height fallback without a trail).")]
        [SerializeField] private Transform followRunner;

        private static readonly string[] Moods = { "Idle", "Idle", "Clap", "Cheer", "Wave" };

        private class Spec { public Transform root; public Animator anim; public Vector3 pos; public float yaw; public bool shown = true; }

        private readonly List<Spec> specs = new List<Spec>();
        private ILocomotionSource loco;
        private int seed;
        private float meanGap = 12f; // metres between two people on average

        /// <summary>People per km of trail for the route setting.</summary>
        public static float PerKm(string level) => level == "none" ? 0f : level == "some" ? 20f : level == "many" ? 80f : 8f;

        private TrackManager track;

        private void Awake()
        {
            loco = locomotionSourceBehaviour as ILocomotionSource;
            track = FindFirstObjectByType<TrackManager>();
        }

        private System.Collections.IEnumerator Start()
        {
            if (figureModels == null || figureModels.Length == 0) yield break;
            for (float t = 0f; RouteRuntime.Current == null && t < 5f; t += Time.unscaledDeltaTime) yield return null; // the route's setting
            float perKm = PerKm(RouteRuntime.Current != null ? RouteRuntime.Current.@params.spectators : "few");
            if (perKm <= 0f) yield break;
            meanGap = 1000f / perKm;
            int pool = Mathf.Clamp(Mathf.CeilToInt((aheadZ - behindZ) / meanGap * 1.6f) + 1, 1, count);
            if (GraphicsQuality.Level == "minimal") pool = Mathf.Min(pool, 3); // weak tablets
            float z = behindZ + 3f + Random.Range(0f, meanGap);
            for (int i = 0; i < pool; i++)
            {
                var s = Build();
                Reposition(s, z);
                specs.Add(s);
                z += Gap();
            }
            if (Profile.ProfileService.Instance != null) Profile.ProfileService.Instance.FigureChanged += OnFigureChanged;
        }

        // irregular: sometimes two close together, sometimes a long empty stretch
        private float Gap() => meanGap * (Random.value < 0.25f ? Random.Range(0.05f, 0.25f) : Random.Range(0.5f, 1.8f));

        private void OnDestroy() { if (Profile.ProfileService.Instance != null) Profile.ProfileService.Instance.FigureChanged -= OnFigureChanged; }

        // The runner picks another figure: a spectator wearing it now gets a different one.
        private void OnFigureChanged(string _)
        {
            string player = RealFigure.PlayerFigure();
            foreach (var s in specs)
            {
                if (s.anim == null || s.anim.gameObject.name != player) continue;
                Destroy(s.anim.gameObject);
                s.anim = RealFigure.Spawn(s.root, PickModel(player), maleController, femaleController);
                s.anim.Play(Moods[Random.Range(0, Moods.Length)], 0, Random.value);
            }
        }

        private void Update()
        {
            long t0 = FrameWork.Start();
            UpdateWork();
            FrameWork.Stop("Zuschauer", t0);
        }

        private void UpdateWork()
        {
            float pv = track != null ? track.SpeedMps : (loco != null ? loco.SpeedMps : 0f); // real progress
            float dt = Time.deltaTime;
            float furthest = aheadZ;
            foreach (var s in specs) furthest = Mathf.Max(furthest, s.pos.z);
            foreach (var s in specs)
            {
                s.pos.z -= pv * dt;
                if (s.pos.z < behindZ) { furthest += Gap(); Reposition(s, furthest); }
                Show(s, s.pos.z <= aheadZ);
                if (s.shown) Place(s);
            }
        }

        private void Reposition(Spec s, float z)
        {
            float side = Random.Range(0, 2) == 0 ? -1f : 1f;
            s.pos = new Vector3(side * Random.Range(5f, 9f), 0f, z);
            s.yaw = side > 0 ? -90f : 90f; // face the trail
            Show(s, z <= aheadZ);
            if (s.shown) { s.anim.Play(Moods[Random.Range(0, Moods.Length)], 0, Random.value); Place(s); }
        }

        // further ahead than the window: waiting, hidden (the gap before them is still to be run)
        private void Show(Spec s, bool on)
        {
            if (s.shown == on) return;
            s.shown = on;
            s.root.gameObject.SetActive(on);
            if (on) { s.anim.Play(Moods[Random.Range(0, Moods.Length)], 0, Random.value); Place(s); }
        }

        private Spec Build()
        {
            var root = new GameObject("Spectator").transform;
            root.SetParent(transform, false);
            return new Spec { root = root, anim = RealFigure.Spawn(root, PickModel(RealFigure.PlayerFigure()), maleController, femaleController) };
        }

        // Any figure but the player's own
        private GameObject PickModel(string player) =>
            RealFigure.NotPlayer(figureModels[(seed++ * 3 + Random.Range(0, figureModels.Length)) % figureModels.Length], figureModels, player);

        // Logical (sideways, 0, metres ahead of the player) → world, facing the trail.
        private void Place(Spec s)
        {
            var path = TrailPath.Active;
            Vector3 w;
            Quaternion rot = Quaternion.Euler(0f, s.yaw, 0f);
            if (path != null)
            {
                float a = path.RunnerS + s.pos.z;
                w = path.Offset(a, s.pos.x);
                rot = path.Rotation(a) * rot;
            }
            else
            {
                Vector3 o = followRunner != null ? followRunner.position : Vector3.zero;
                w = new Vector3(s.pos.x, 0f, o.z + s.pos.z);
            }
            if (TerrainGround.TryHeight(w.x, w.z, out float h)) w.y = h;
            s.root.SetPositionAndRotation(w, rot);
        }
    }
}
