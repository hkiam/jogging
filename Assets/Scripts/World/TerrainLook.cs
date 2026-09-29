using System.Collections.Generic;
using UnityEngine;
using MapMagic.Core;
using MapMagic.Nodes.MatrixGenerators;
using Jogging.Route;

namespace Jogging.World
{
    /// <summary>
    /// Adapts the MapMagic terrain to the route before the first tile is generated (called from
    /// <see cref="RouteRuntime"/> on Awake): relief sets the terrain's height scale (the trail keeps
    /// its own profile), the vegetation type swaps the ground layers (conifer: dark forest floor,
    /// birch: fresh green, meadow: golden) and tints the detail grass; the season goes on top
    /// (winter: snow, autumn: browner, spring: greener). Works on the graph in memory;
    /// the original values are cached so a scene reload with another route starts from them.
    /// </summary>
    public class TerrainLook : MonoBehaviour
    {
        [SerializeField] private MapMagicObject mapMagic;
        [SerializeField] private TerrainLayer grassGreen;   // fine green grass
        [SerializeField] private TerrainLayer grassYellow;  // fine dry grass
        [SerializeField] private TerrainLayer forestFloor;  // dirt / needles
        [SerializeField] private TerrainLayer snow;

        private static float baseHeight = -1f;
        private static readonly Dictionary<object, TerrainLayer> origLayers = new Dictionary<object, TerrainLayer>();
        private static readonly Dictionary<object, (Color dry, Color healthy)> origGrass = new Dictionary<object, (Color, Color)>();

        public void Apply(RouteParams p)
        {
            if (mapMagic == null) mapMagic = FindFirstObjectByType<MapMagicObject>();
            if (mapMagic == null || mapMagic.graph == null) return;

            // Relief: 0 → 40 m, 0.5 → 100 m, 1 → 190 m of terrain height (the trail profile is separate).
            if (baseHeight < 0f) baseHeight = mapMagic.globals.height;
            float r = Mathf.Clamp01(p.relief);
            mapMagic.globals.height = r < 0.5f ? Mathf.Lerp(40f, 100f, r * 2f) : Mathf.Lerp(100f, 190f, (r - 0.5f) * 2f);

            // Ground layers: the two grass slots of the graph, per vegetation type.
            foreach (var tex in mapMagic.graph.GeneratorsOfType<TexturesOutput200>())
                foreach (var layer in tex.layers)
                {
                    if (!origLayers.TryGetValue(layer, out var orig)) origLayers[layer] = orig = layer.prototype;
                    if (orig == null) continue;
                    bool yellowSlot = orig == grassYellow, greenSlot = orig == grassGreen;
                    TerrainLayer pick = orig;
                    switch (p.vegetation)
                    {
                        case "conifer": if (yellowSlot && forestFloor != null) pick = forestFloor; break;
                        case "birch": if (yellowSlot) pick = grassGreen; break;
                        case "meadow": if (greenSlot) pick = grassYellow; else if (yellowSlot) pick = grassGreen; break;
                    }
                    // Season on top: winter snow over all grass, autumn browner, spring greener.
                    bool grassy = pick == grassGreen || pick == grassYellow || pick == forestFloor;
                    switch (p.season)
                    {
                        case "winter": if (grassy && snow != null) pick = snow; break;
                        case "autumn": if (pick == grassGreen) pick = grassYellow; break;
                        case "spring": if (pick == grassYellow) pick = grassGreen; break;
                    }
                    layer.prototype = pick;
                }

            // Detail grass colours.
            foreach (var g in mapMagic.graph.GeneratorsOfType<GrassOutput200>())
            {
                if (!origGrass.TryGetValue(g, out var o)) origGrass[g] = o = (g.prototype.dryColor, g.prototype.healthyColor);
                Color dry = o.dry, healthy = o.healthy;
                switch (p.vegetation)
                {
                    case "conifer": dry = new Color(0.55f, 0.55f, 0.36f); healthy = new Color(0.30f, 0.42f, 0.22f); break;
                    case "birch": dry = new Color(0.72f, 0.85f, 0.45f); healthy = new Color(0.42f, 0.66f, 0.28f); break;
                    case "meadow": dry = new Color(0.92f, 0.84f, 0.50f); healthy = new Color(0.62f, 0.70f, 0.34f); break;
                }
                switch (p.season)
                {
                    case "winter": dry = new Color(0.93f, 0.94f, 0.96f); healthy = new Color(0.84f, 0.87f, 0.90f); break; // frosted
                    case "autumn": dry = new Color(0.86f, 0.62f, 0.32f); healthy = new Color(0.64f, 0.50f, 0.28f); break;
                    case "spring": dry = new Color(0.72f, 0.90f, 0.46f); healthy = new Color(0.44f, 0.76f, 0.30f); break;
                }
                g.prototype.dryColor = dry;
                g.prototype.healthyColor = healthy;
            }
            Debug.Log($"[Jogging] Gelände: Höhe {mapMagic.globals.height:0} m, Boden „{p.vegetation}“, Jahreszeit „{p.season}“");
        }
    }
}
