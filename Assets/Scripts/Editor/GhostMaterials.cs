using UnityEditor;
using UnityEngine;
using Jogging.World;

namespace Jogging.EditorTools
{
    /// <summary>
    /// The ghost runner's transparent URP Lit variants (<see cref="GhostLook"/>) only exist at runtime;
    /// these two materials in Resources/Ghost keep them in the build (with and without normal map).
    /// Menu: Jogging → Build → Geist-Materialien.
    /// </summary>
    public static class GhostMaterials
    {
        [MenuItem("Jogging/Build/Geist-Materialien")]
        public static void Create()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            Make(lit, "Assets/Resources/Ghost/GhostVariant.mat", normalMap: true);
            Make(lit, "Assets/Resources/Ghost/GhostVariantPlain.mat", normalMap: false);
            AssetDatabase.SaveAssets();
            Debug.Log("[Jogging] Geist-Materialien angelegt");
        }

        private static void Make(Shader lit, string path, bool normalMap)
        {
            var m = new Material(lit);
            GhostLook.MakeGhost(m);
            if (normalMap) // URP keeps _NORMALMAP only with a normal map assigned: any figure's will do
            {
                var bump = AssetDatabase.LoadAssetAtPath<Material>("Assets/Rocketbox/Avatars/Female_Adult_17/Materials/f006_body.mat");
                m.SetTexture("_BumpMap", bump != null ? bump.GetTexture("_BumpMap") : null);
                m.EnableKeyword("_NORMALMAP");
            }
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
        }
    }
}
