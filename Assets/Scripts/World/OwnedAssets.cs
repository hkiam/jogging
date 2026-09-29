using System.Collections.Generic;
using UnityEngine;

namespace Jogging.World
{
    /// <summary>
    /// Meshes, materials and textures made at runtime for one object (the wayside's built props, side paths,
    /// the info board's map): destroyed with it, so rebuilding the window around the runner (and every lap
    /// of a loop) doesn't pile them up until the next scene load.
    /// </summary>
    public class OwnedAssets : MonoBehaviour
    {
        private readonly List<Object> owned = new List<Object>();

        public void Add(Object o) { if (o != null) owned.Add(o); }

        /// <summary>Adds to the owner on go (created if needed).</summary>
        public static void Own(GameObject go, Object o)
        {
            if (go == null || o == null) return;
            var a = go.GetComponent<OwnedAssets>();
            if (a == null) a = go.AddComponent<OwnedAssets>();
            a.Add(o);
        }

        private void OnDestroy()
        {
            foreach (var o in owned) if (o != null) Destroy(o);
            owned.Clear();
        }
    }
}
