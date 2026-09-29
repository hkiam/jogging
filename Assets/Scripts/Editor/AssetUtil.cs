using System.IO;
using UnityEditor;
using UnityEngine;

namespace Jogging.EditorTools
{
    /// <summary>
    /// Generated assets without churn: an existing asset is updated in place (same GUID, same file
    /// IDs) instead of being deleted and recreated — otherwise every build rewrote ~40 assets and all
    /// references to them. Unchanged content leaves the file untouched in git.
    /// </summary>
    public static class AssetUtil
    {
        /// <summary>Store <paramref name="fresh"/> at <paramref name="path"/>: copy into the existing asset, or create it.</summary>
        public static T Upsert<T>(T fresh, string path) where T : Object
        {
            fresh.name = Path.GetFileNameWithoutExtension(path);
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path); // other type: replace
                AssetDatabase.CreateAsset(fresh, path);
                return fresh;
            }
            if (fresh is Material fm && existing is Material em)
            {
                // Values the editor settles on when a material is saved — keep them, else they flip
                // every build: GI flags, and URP's legacy _MainTex mirror of _BaseMap.
                fm.globalIlluminationFlags = em.globalIlluminationFlags;
                if (fm.HasProperty("_BaseMap") && fm.HasProperty("_MainTex")) fm.SetTexture("_MainTex", fm.GetTexture("_BaseMap"));
            }
            EditorUtility.CopySerialized(fresh, existing);
            existing.name = fresh.name;
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(fresh);
            return existing;
        }

        /// <summary>A copy of the asset at <paramref name="sourcePath"/> at <paramref name="path"/> (kept in place if it exists).</summary>
        public static T CopyOf<T>(string sourcePath, string path) where T : Object
        {
            var src = AssetDatabase.LoadAssetAtPath<T>(sourcePath);
            if (src == null) { Debug.LogError($"[Jogging] Quelle fehlt: {sourcePath}"); return null; }
            if (AssetDatabase.LoadAssetAtPath<T>(path) == null)
            {
                AssetDatabase.CopyAsset(sourcePath, path);
                return AssetDatabase.LoadAssetAtPath<T>(path);
            }
            return Upsert(Object.Instantiate(src), path);
        }
    }
}
