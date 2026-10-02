using System.Collections.Generic;
using UnityEngine;
using Jogging.Route;

namespace Jogging.World
{
    /// <summary>
    /// Spectator groups placed in the workshop ("spectators" edits): a cluster of cheering and
    /// clapping figures at a spot beside the trail, shown while the runner is within
    /// [−60 m, +350 m] of it. Figures come from the spectator manager's model list.
    /// </summary>
    public class SpectatorGroups : MonoBehaviour
    {
        [SerializeField] private GameObject[] figureModels;
        [SerializeField] private RuntimeAnimatorController maleController;
        [SerializeField] private RuntimeAnimatorController femaleController;

        private static readonly string[] Moods = { "Cheer", "Clap", "Clap", "Wave", "Cheer" };
        private readonly Dictionary<RouteEdit, GameObject> shown = new Dictionary<RouteEdit, GameObject>();
        private int version = -1;
        private string figureSeen;
        private static int editsVersion;

        /// <summary>Call after the route's edits changed (workshop) to rebuild the groups.</summary>
        public static void EditsChanged() => editsVersion++;

        private void Update()
        {
            long t0 = FrameWork.Start();
            UpdateWork();
            FrameWork.Stop("Zuschauergruppen", t0);
        }

        private void UpdateWork()
        {
            var doc = RouteRuntime.Current;
            var path = TrailPath.Active;
            if (doc == null || path == null || figureModels == null || figureModels.Length == 0) return;
            if (version != editsVersion || figureSeen != Profile.ProfileService.Instance?.Profile.figureModel) // edits or your figure changed: rebuild
            {
                version = editsVersion;
                figureSeen = Profile.ProfileService.Instance?.Profile.figureModel;
                foreach (var g in shown.Values) if (g != null) Destroy(g);
                shown.Clear();
            }
            float s = path.RunnerS;
            foreach (var e in doc.edits)
            {
                if (e.type != "spectators") continue;
                float d = e.atM - s;
                if (path.Loop) d = Mathf.Repeat(d + path.Length * 0.5f, path.Length) - path.Length * 0.5f;
                bool near = d > -60f && d < 350f;
                bool has = shown.TryGetValue(e, out var go) && go != null;
                if (near && !has) shown[e] = Spawn(e, path);
                else if (!near && has) { Destroy(go); shown.Remove(e); }
            }
        }

        private GameObject Spawn(RouteEdit e, TrailPath path)
        {
            var root = new GameObject("SpectatorGroup");
            root.transform.SetParent(transform, false);
            var rng = new System.Random(Mathf.RoundToInt(e.atM * 7f) + 1);
            var models = RealFigure.Spectators(figureModels); // sports kits only run
            string player = RealFigure.PlayerFigure();
            int n = Mathf.Clamp(e.count, 1, 30);
            for (int i = 0; i < n; i++)
            {
                float side = e.side == "left" ? -1f : e.side == "right" ? 1f : (i % 2 == 0 ? 1f : -1f);
                float along = e.atM + ((float)rng.NextDouble() - 0.5f) * Mathf.Min(40f, 4f + n * 1.5f);
                float lateral = side * (3.2f + (float)rng.NextDouble() * 3.5f);
                Vector3 w = path.Offset(along, lateral);
                if (TerrainGround.TryHeight(w.x, w.z, out float h)) w.y = h;
                var t = new GameObject("Fan").transform;
                t.SetParent(root.transform, false);
                // Face the trail (+ a little towards the runner coming up).
                t.SetPositionAndRotation(w, path.Rotation(along) * Quaternion.Euler(0f, side > 0 ? -110f : 110f, 0f));
                var model = RealFigure.NotPlayer(models[rng.Next(models.Length)], models, player);
                var anim = RealFigure.Spawn(t, model, maleController, femaleController);
                anim.Play(Moods[rng.Next(Moods.Length)], 0, (float)rng.NextDouble());
            }
            return root;
        }
    }
}
