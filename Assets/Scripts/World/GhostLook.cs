using UnityEngine;

namespace Jogging.World
{
    /// <summary>
    /// Makes a figure see-through and slightly blue — the ghost runner. Each URP Lit material is copied
    /// and switched to "Transparent" (alpha blend; depth write kept on, so only the front surface is
    /// seen and the body doesn't show through itself); hair and lashes lose their alpha cut-out and
    /// fade by their texture's alpha. The two materials in Resources/Ghost only make sure the build
    /// keeps these shader variants (Editor/GhostMaterials.cs).
    /// </summary>
    public static class GhostLook
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly Color Tint = new Color(0.62f, 0.8f, 1f, 0.6f);

        public static void Apply(GameObject root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                var mats = r.materials; // per-renderer copies
                foreach (var m in mats) MakeGhost(m);
                r.materials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // a ghost casts no shadow
            }
        }

        /// <summary>Switch a URP Lit material to transparent (also used by the editor for the variant materials).</summary>
        public static void MakeGhost(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_AlphaClip", 0f);
            m.SetFloat("_BlendModePreserveSpecular", 0f); // plain alpha blend (no premultiply variant)
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 1f);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetShaderPassEnabled("DepthOnly", false);
            m.SetShaderPassEnabled("ShadowCaster", false);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            if (m.HasProperty(BaseColor))
            {
                var c = m.GetColor(BaseColor);
                m.SetColor(BaseColor, new Color(c.r * Tint.r, c.g * Tint.g, c.b * Tint.b, c.a * Tint.a));
            }
        }
    }
}
