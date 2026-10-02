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

        public static bool IsFemale(GameObject model) => model != null && model.name.Contains("Female"); // "Female_Adult_…", "Sports_Female_…"

        /// <summary>Figures that only run (sports kits): never among the spectators.</summary>
        public static bool RunnerOnly(GameObject model) => model != null && model.name.StartsWith("Sports_");

        /// <summary>The models without the runner-only ones (spectators).</summary>
        public static GameObject[] Spectators(GameObject[] models)
        {
            if (models == null) return models;
            var list = new System.Collections.Generic.List<GameObject>();
            foreach (var m in models) if (m != null && !RunnerOnly(m)) list.Add(m);
            return list.Count > 0 ? list.ToArray() : models;
        }

        private static readonly System.Collections.Generic.Dictionary<string, Material> sportCache = new System.Collections.Generic.Dictionary<string, Material>();

        private static readonly System.Collections.Generic.Dictionary<string, Material> shirtCache = new System.Collections.Generic.Dictionary<string, Material>();
        private static Material tint;

        /// <summary>The shirt in a colour of Profile/Game.Shirts (unlocked by level): only the shirt area (Resources/Shirt mask).</summary>
        private static void Recolour(GameObject go, string shirt)
        {
            var colour = Profile.Game.Shirt(shirt);
            if (colour.id == "") return;
            if (tint == null) tint = Resources.Load<Material>("Shirt/ShirtTint");
            if (tint == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || !m.name.EndsWith("_body")) continue;
                    string key = m.name + "|" + shirt + "|" + m.GetInstanceID();
                    if (!shirtCache.TryGetValue(key, out var made) || made == null)
                    {
                        var mask = Resources.Load<Texture2D>("Shirt/" + m.name);
                        var body = m.GetTexture("_BaseMap");
                        if (mask == null || body == null) continue;
                        var rt = new RenderTexture(body.width, body.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { useMipMap = true, autoGenerateMips = true, name = m.name + "_" + shirt };
                        tint.SetTexture("_Mask", mask);
                        tint.SetColor("_Tint", colour.color);
                        Graphics.Blit(body, rt, tint);
                        made = new Material(m) { name = m.name };
                        made.SetTexture("_BaseMap", rt);
                        shirtCache[key] = made;
                    }
                    mats[i] = made; changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        private static void Dress(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    if (!sportCache.TryGetValue(mats[i].name, out var s)) sportCache[mats[i].name] = s = Resources.Load<Material>("Sport/" + mats[i].name);
                    if (s != null) { mats[i] = s; changed = true; }
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        /// <summary>
        /// The figure in the world. sport = in running clothes (runners: you, fellow runners, the rival, the
        /// ghost): materials that have a sport version (Resources/Sport, same name; Editor/RocketboxSetup) are
        /// swapped; spectators keep their everyday clothes.
        /// </summary>
        public static Animator Spawn(Transform parent, GameObject model,
            RuntimeAnimatorController male, RuntimeAnimatorController female, bool sport = false, string shirt = "")
        {
            var go = Object.Instantiate(model, parent, false);
            go.name = model.name;
            if (sport) Dress(go);
            if (!string.IsNullOrEmpty(shirt)) Recolour(go, shirt);
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
