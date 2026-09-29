using System.Collections.Generic;
using UnityEngine;
using Jogging.Route;

namespace Jogging.World
{
    /// <summary>
    /// Seasonal variants of the scenery materials (built by the scene builder): for every base
    /// material (birch, pine, the toned-down bushes) a spring, autumn and winter version. Summer is
    /// the base. <see cref="Swap"/> maps a material to the current route's season; the scatterers
    /// call it for every object they place.
    /// </summary>
    public class SeasonAssets : MonoBehaviour
    {
        [SerializeField] private Material[] baseMaterials = new Material[0];
        [SerializeField] private Material[] spring = new Material[0];
        [SerializeField] private Material[] autumn = new Material[0];
        [SerializeField] private Material[] winter = new Material[0];

        private static SeasonAssets active;
        private static Dictionary<Material, Material> map;
        private static string mapSeason;

        private void Awake() { active = this; map = null; }

        public static string Current => RouteRuntime.Current != null ? RouteRuntime.Current.@params.season : "summer";

        /// <summary>The material to use in the current season (the input itself in summer or if unknown).</summary>
        public static Material Swap(Material m)
        {
            if (m == null || active == null) return m;
            string season = Current;
            if (season == "summer" || string.IsNullOrEmpty(season)) return m;
            if (map == null || mapSeason != season)
            {
                mapSeason = season;
                map = new Dictionary<Material, Material>();
                var set = season == "spring" ? active.spring : season == "autumn" ? active.autumn : active.winter;
                for (int i = 0; i < active.baseMaterials.Length && i < set.Length; i++)
                    if (active.baseMaterials[i] != null && set[i] != null) map[active.baseMaterials[i]] = set[i];
            }
            return map.TryGetValue(m, out var s) ? s : m;
        }
    }
}
