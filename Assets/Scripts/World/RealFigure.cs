using UnityEngine;

namespace Jogging.World
{
    /// <summary>
    /// Spawns a realistic figure (Microsoft Rocketbox avatar, Generic rig) from a model asset and
    /// gives it the gender-matched animator controller. Female models are named "Female_…".
    /// </summary>
    public static class RealFigure
    {
        /// <summary>Run controller (Editor/RocketboxSetup): walk cycle from WalkMin to WalkTop, run cycle from RunFrom (m/s).</summary>
        public const float WalkMin = 0.9f, WalkTop = 2.0f, RunFrom = 2.2f;
        private const float WalkNatural = 1.35f, RunNatural = 3.2f; // ground speed of the clips as recorded
        private static readonly int SpeedId = Animator.StringToHash("Speed");

        /// <summary>
        /// Walk / run at this ground speed (m/s): one walk and one run cycle, each played faster or slower
        /// so the steps fit the ground — at 1 km/h a slow walk instead of the idle pose sliding along.
        /// </summary>
        public static void Drive(Animator a, float mps)
        {
            if (a == null) return;
            if (mps < 0.05f) { a.SetFloat(SpeedId, 0f); a.speed = 1f; return; }
            a.SetFloat(SpeedId, Mathf.Clamp(mps, WalkMin, 6f)); // WalkTop…RunFrom: the short walk/run crossfade
            float natural = mps < (WalkTop + RunFrom) * 0.5f ? WalkNatural : RunNatural;
            a.speed = Mathf.Clamp(mps / natural, 0.3f, 1.6f);
        }

        /// <summary>The figure the active runner wears ("" if unknown).</summary>
        public static string PlayerFigure()
        {
            var p = Object.FindFirstObjectByType<RealPlayerFigure>();
            return p != null ? p.ChosenName : "";
        }

        /// <summary>The model, or — if it is the player's own figure — the next other one in the list
        /// (nobody meets themselves on the trail; only the ghost wears your figure).</summary>
        public static GameObject NotPlayer(GameObject model, GameObject[] models, string player)
        {
            if (model == null || model.name != player || models.Length < 2) return model;
            int i = System.Array.IndexOf(models, model);
            for (int k = 1; k < models.Length; k++)
            {
                var m = models[(i + k) % models.Length];
                if (m != null && m.name != player) return m;
            }
            return model;
        }

        public static bool IsFemale(GameObject model) => model != null && model.name.StartsWith("Female");

        public static Animator Spawn(Transform parent, GameObject model,
            RuntimeAnimatorController male, RuntimeAnimatorController female)
        {
            var go = Object.Instantiate(model, parent, false);
            go.name = model.name;
            var anim = go.GetComponent<Animator>();
            if (anim == null) anim = go.AddComponent<Animator>();
            anim.runtimeAnimatorController = IsFemale(model) && female != null ? female : male;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>()) r.updateWhenOffscreen = false;
            var bip = go.transform.Find("Bip01");
            if (bip != null) go.AddComponent<InPlaceRoot>().Init(bip);
            return anim;
        }
    }
}
