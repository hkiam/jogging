using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Jogging.World;

namespace Jogging.EditorTools
{
    /// <summary>
    /// The scattered trees, stones and plants (and the figures) come with a single, full-detail mesh:
    /// a pine 200 m away was drawn with every needle. Unity's Mesh LOD simplifies each model at import
    /// (up to <see cref="MaxLod"/> coarser levels in the same mesh) and picks the level by screen size at
    /// runtime — no LODGroup, no extra objects. How early it switches is QualitySettings.meshLodThreshold
    /// (per graphics level in <see cref="GraphicsQuality"/>). Menu: Jogging → Build → Mesh-LODs erzeugen.
    /// </summary>
    public static class MeshLods
    {
        private const int MaxLod = 4;

        [MenuItem("Jogging/Build/Mesh-LODs erzeugen")]
        public static void Generate()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/PhotoRun.unity");
            var models = new HashSet<string>();
            foreach (var s in Object.FindObjectsByType<TerrainTreeScatter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                foreach (var prefab in Prefabs(s, "prefabs")) Collect(prefab, models);
            foreach (var m in Object.FindObjectsByType<AiRunnerManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                foreach (var prefab in Prefabs(m, "figureModels")) Collect(prefab, models);
            foreach (var m in Object.FindObjectsByType<SpectatorManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                foreach (var prefab in Prefabs(m, "figureModels")) Collect(prefab, models);

            int changed = 0;
            foreach (var path in models)
            {
                if (!(AssetImporter.GetAtPath(path) is ModelImporter imp)) continue;
                if (imp.generateMeshLods && imp.maximumMeshLod == MaxLod) continue;
                imp.generateMeshLods = true;
                imp.maximumMeshLod = MaxLod;
                imp.SaveAndReimport();
                changed++;
            }
            Debug.Log($"[Jogging] Mesh-LODs: {models.Count} Modelle, {changed} neu importiert");
        }

        private static IEnumerable<GameObject> Prefabs(Object component, string field)
        {
            var so = new SerializedObject(component);
            var arr = so.FindProperty(field);
            if (arr == null || !arr.isArray) yield break;
            for (int i = 0; i < arr.arraySize; i++)
                if (arr.GetArrayElementAtIndex(i).objectReferenceValue is GameObject g) yield return g;
        }

        // Every model file behind the prefab's meshes (MeshFilter and SkinnedMeshRenderer)
        private static void Collect(GameObject prefab, HashSet<string> models)
        {
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) Add(mf.sharedMesh, models);
            foreach (var sr in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (sr.sharedMesh != null) Add(sr.sharedMesh, models);
        }

        private static void Add(Mesh mesh, HashSet<string> models)
        {
            string p = AssetDatabase.GetAssetPath(mesh);
            if (!string.IsNullOrEmpty(p) && AssetImporter.GetAtPath(p) is ModelImporter) models.Add(p);
        }
    }
}
