using UnityEngine;
using UnityEngine.SceneManagement;
using MapMagic.Core;

namespace Jogging.World
{
    /// <summary>
    /// Reload the scene (every run start, "Fertig", restore …) without two landscapes in memory at once:
    /// Unity loads the new scene first and frees what the old one left behind — the terrain data MapMagic
    /// created at runtime, with their graphics buffers — only afterwards. On the Fire HD 10 that doubled
    /// peak was more than Android allowed. So the old landscape is stopped and freed first.
    /// </summary>
    public static class SceneReload
    {
        public static void Now()
        {
            foreach (var mm in Object.FindObjectsByType<MapMagicObject>(FindObjectsSortMode.None))
            {
                mm.StopGenerate();
                mm.enabled = false;
            }
            foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var d = t.terrainData;
                t.gameObject.SetActive(false);
                if (d != null) Object.Destroy(d);
            }
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
