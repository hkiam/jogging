using System.Collections.Generic;
using UnityEngine;

namespace Jogging.World
{
    /// <summary>
    /// Scatters tree prefabs on real terrain around the runner (MapMagic's free edition has no
    /// objects output). Deterministic per chunk along the trail (arc length), keeps a clear
    /// corridor around the trail,
    /// places each tree on the sampled ground height, and removes chunks the runner has passed.
    /// Chunks whose ground isn't generated yet are retried until the terrain tile arrives.
    /// </summary>
    public class TerrainTreeScatter : MonoBehaviour
    {
        [SerializeField] private Transform runner;
        [SerializeField] private GameObject[] prefabs = new GameObject[0];
        [SerializeField] private float chunkLength = 40f;
        [SerializeField] private int treesPerChunk = 26;
        [Tooltip("No trunks closer than this to the trail centre (m).")]
        [SerializeField] private float corridor = 7f;
        [Tooltip("If > 0: drop any object whose rendered footprint (below head height) reaches into " +
                 "|x| < this — for wide objects like boulders whose pivot is far from their edge.")]
        [SerializeField] private float footprintClear = 0f;
        [Tooltip("Optional material replacements (same index in both arrays), e.g. toned-down copies.")]
        [SerializeField] private Material[] swapFrom = new Material[0];
        [SerializeField] private Material[] swapTo = new Material[0];

        [Tooltip("Undergrowth scatter (bushes, plants, stones): density follows the undergrowth share of the vegetation plan.")]
        [SerializeField] private bool undergrowth;

        private Dictionary<Material, Material> swaps;
        private GameObject[] pines, broadleaf, stones, plants; // split by prefab name
        private const int MinRes = 200; // main tiles are 257, drafts 65
        [SerializeField] private float spread = 160f;
        [SerializeField] private float ahead = 480f;
        [SerializeField] private float behind = 60f;
        [SerializeField] private Vector2 scaleRange = new Vector2(0.8f, 1.35f);
        [SerializeField] private int seed = 1234;

        private readonly Dictionary<int, GameObject> chunks = new Dictionary<int, GameObject>();

        // Placements are decided per chunk at once (deterministic), but instantiated a few per frame
        // within a time budget: a whole chunk in one frame cost up to ~70 ms.
        private struct Pending { public Transform root; public GameObject prefab; public Vector3 pos; public float rot, scale, s, ground; }
        private readonly Queue<Pending> pending = new Queue<Pending>();
        private readonly HashSet<Transform> dropped = new HashSet<Transform>(); // Destroy takes effect at the frame's end
        private const float BudgetMs = 2.5f, BudgetIdleMs = 12f;
        private readonly List<int> scratch = new List<int>();

        // On a loop the lap is split into whole chunks (lap / n), so chunk c + n lies exactly one lap after c:
        // with a fixed 40 m the built band slid by up to 39 m per lap against the runner.
        private float ChunkLen()
        {
            var p = TrailPath.Active;
            if (p == null || !p.Loop || p.Length <= 0f) return chunkLength;
            return p.Length / Mathf.Max(1, Mathf.RoundToInt(p.Length / chunkLength));
        }

        private void Update()
        {
            long t0 = FrameWork.Start();
            UpdateWork();
            FrameWork.Stop("Bäume", t0);
        }

        private void UpdateWork()
        {
            if (runner == null || prefabs.Length == 0) return;
            var path = TrailPath.Active;
            float z = path != null ? path.RunnerS : runner.position.z; // arc length along the trail
            float len = ChunkLen();
            int first = Mathf.FloorToInt((z - behind) / len);
            int last = Mathf.FloorToInt((z + ahead) / len);

            // Drop chunks outside the window.
            scratch.Clear();
            foreach (var k in chunks.Keys) if (k < first || k > last) scratch.Add(k);
            foreach (var k in scratch) { if (chunks[k] != null) { dropped.Add(chunks[k].transform); Destroy(chunks[k]); } chunks.Remove(k); }

            // Plan at most one missing chunk per frame, and only when the queue has caught up.
            if (pending.Count < 40)
                for (int c = first; c <= last; c++)
                {
                    if (chunks.ContainsKey(c)) continue;
                    var go = BuildChunk(c);
                    if (go != null) { chunks[c] = go; break; }
                }
            InstantiatePending();
        }

        private void InstantiatePending()
        {
            if (pending.Count == 0) { dropped.Clear(); return; }
            var sm = Jogging.UI.RunSessionUI.Session;
            float budget = sm != null && sm.Moves ? BudgetMs : BudgetIdleMs; // before the start: fill quickly
            var path = TrailPath.Active;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            double ticks = budget * System.Diagnostics.Stopwatch.Frequency / 1000.0;
            while (pending.Count > 0 && System.Diagnostics.Stopwatch.GetTimestamp() - t0 < ticks)
            {
                var p = pending.Dequeue();
                if (p.root == null || dropped.Contains(p.root)) continue; // chunk already dropped
                var tree = Instantiate(p.prefab, p.pos, Quaternion.Euler(0f, p.rot, 0f), p.root);
                tree.transform.localScale *= p.scale;
                if (!undergrowth && TrunkRadius(p.prefab.name, out float tr)) trunks.Add((tree.transform, tr * p.scale));
                if (footprintClear > 0f && BlocksTrail(tree, p.ground, path, p.s)) { Destroy(tree); continue; }
                ApplySwaps(tree);
            }
        }

        private GameObject BuildChunk(int c)
        {
            // On a loop, chunk indices wrap with the course, so the same place always gets the same
            // objects (behind the start = the end of the loop).
            var loopPath = TrailPath.Active;
            if (loopPath != null && loopPath.Loop)
            {
                int n = Mathf.Max(1, Mathf.RoundToInt(loopPath.Length / chunkLength));
                c = ((c % n) + n) % n;
            }
            float cl = ChunkLen();
            float z0 = c * cl;
            // Terrain must exist for the chunk before we place anything (MapMagic tiles stream in).
            // Only on detailed (main) tiles — heights on the coarse draft tiles differ, so objects
            // placed there would float or sink once the main tile replaces the draft.
            var path = TrailPath.Active;
            Vector3 pa = At(path, z0, 0f), pb = At(path, z0 + cl, 0f);
            if (!TerrainGround.TryHeight(pa.x, pa.z, out _, MinRes) || !TerrainGround.TryHeight(pb.x, pb.z, out _, MinRes)) return null;
            if (path != null && !TrailShaper.Ready(z0, z0 + cl)) return null; // bed not cut/filled yet

            var rng = new System.Random(seed * 73856093 ^ c * 19349663);
            var root = new GameObject($"Trees_{c}");
            root.transform.SetParent(transform, false);
            // Vegetation plan (forest / pines / birches / clearing / meadow) for this stretch.
            var mix = Vegetation.At(z0 + cl * 0.5f);
            float factor = undergrowth ? mix.undergrowth : mix.trees;
            // Workshop edits can make a stretch denser (up to ×2): draw up to that many, then keep each
            // with probability editFactor / 2 on its side (deterministic, same rng stream).
            const float maxEdit = 2f;
            int count = Mathf.FloorToInt(treesPerChunk * factor * maxEdit + (float)rng.NextDouble());
            float rocks = RouteRuntime.Current != null ? Mathf.Clamp01(RouteRuntime.Current.@params.rocks) : 0.4f;
            SplitPrefabs();
            float keepShare = GraphicsQuality.ScatterFactor;
            for (int i = 0; i < count; i++)
            {
                float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                // Denser near the trail, thinning out with distance.
                float t = (float)rng.NextDouble();
                float x = side * (corridor + t * t * spread);
                float z = z0 + (float)rng.NextDouble() * cl;
                float rot = (float)rng.NextDouble() * 360f;
                var prefab = Choose(rng, mix, rocks);
                double keep = rng.NextDouble();
                float ef = undergrowth ? Mathf.Lerp(1f, Vegetation.EditFactor(z, x), 0.5f) : Vegetation.EditFactor(z, x);
                if (keep > ef / maxEdit) continue;
                Vector3 w = At(path, z, x);
                if (Lakes.Inside(w, undergrowth ? 2f : 6f)) continue; // no trees in / right at the water
                if (Wayside.Blocks(w, undergrowth ? 0.3f : 1.2f)) continue; // nor on side paths and planned objects
                // In a bend, a point far to the inside can come close to another part of the trail.
                if (path != null)
                {
                    path.Project(w, z, 260f, out float lat);
                    if (Mathf.Abs(lat) < corridor * 0.8f) continue;
                }
                if (!TerrainGround.TryHeight(w.x, w.z, out float h, MinRes)) continue;
                if (prefab == null) continue;
                float s = Mathf.Lerp(scaleRange.x, scaleRange.y, (float)rng.NextDouble());
                // weak GPUs: only a share is placed — decided after all random draws, so the others keep their spots
                if (keepShare < 1f && Mathf.Repeat(i * 0.61803f, 1f) >= keepShare) continue;
                pending.Enqueue(new Pending { root = root.transform, prefab = prefab, pos = new Vector3(w.x, h - 0.1f, w.z), rot = rot, scale = s, s = z, ground = h });
            }
            return root;
        }

        private static Dictionary<string, float> trunkRadius;

        /// <summary>
        /// The trunk of the tree nearest to p (within maxDist) at breast height: its axis position (ground)
        /// and radius at 1.7 m — from Resources/Wayside/trunks.json (measured from the tree meshes by
        /// Editor/WaysideAssets), times the tree's scale. False if no (known) tree stands there yet.
        /// </summary>
        // trees with a known trunk as they are placed (for marks and nest boxes): no search over all chunks
        private static readonly List<(Transform t, float r)> trunks = new List<(Transform, float)>();

        private static bool TrunkRadius(string prefab, out float r)
        {
            LoadTrunks();
            return trunkRadius.TryGetValue(prefab, out r) && r > 0f;
        }

        private static void LoadTrunks()
        {
            if (trunkRadius == null)
            {
                trunkRadius = new Dictionary<string, float>();
                var ta = Resources.Load<TextAsset>("Wayside/trunks");
                if (ta != null)
                    foreach (var line in ta.text.Split('\n'))
                    {
                        var parts = line.Split('=');
                        if (parts.Length == 2 && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float r)) trunkRadius[parts[0].Trim()] = r;
                    }
            }
        }

        public static bool NearestTrunk(Vector3 p, float maxDist, out Vector3 trunk, out float radius)
        {
            trunk = default; radius = 0f;
            float best = maxDist * maxDist;
            bool found = false;
            trunks.RemoveAll(x => x.t == null); // chunks behind the runner are gone
            foreach (var (t, r) in trunks)
            {
                Vector3 tp = t.position;
                float dx = tp.x - p.x, dz = tp.z - p.z, d2 = dx * dx + dz * dz;
                if (d2 >= best) continue;
                best = d2; trunk = tp; radius = r; found = true;
            }
            return found;
        }

        /// <summary>Drop all chunks so they are rebuilt with the current plan/edits (workshop).</summary>
        public static void RebuildAll()
        {
            foreach (var sc in FindObjectsByType<TerrainTreeScatter>(FindObjectsSortMode.None))
            {
                foreach (var kv in sc.chunks) if (kv.Value != null) Destroy(kv.Value);
                sc.chunks.Clear();
                sc.pending.Clear();
            }
        }

        private void SplitPrefabs()
        {
            if (pines != null) return;
            var p = new List<GameObject>(); var b = new List<GameObject>(); var st = new List<GameObject>(); var pl = new List<GameObject>();
            foreach (var g in prefabs)
            {
                if (g == null) continue;
                if (g.name.StartsWith("Pine")) p.Add(g);
                else if (g.name.StartsWith("Stone")) st.Add(g);
                else if (undergrowth) pl.Add(g);
                else b.Add(g);
            }
            pines = p.ToArray(); broadleaf = b.ToArray(); stones = st.ToArray(); plants = pl.ToArray();
        }

        // Trees: pine vs broadleaf by the section's pine share. Undergrowth: stones by the rocks setting.
        private GameObject Choose(System.Random rng, Vegetation.Mix mix, float rocks)
        {
            double r = rng.NextDouble();
            GameObject[] set;
            if (undergrowth) set = stones.Length > 0 && r < rocks * 0.45f ? stones : plants.Length > 0 ? plants : prefabs;
            else set = pines.Length > 0 && r < mix.pine ? pines : broadleaf.Length > 0 ? broadleaf : prefabs;
            return set[rng.Next(set.Length)];
        }

        private static Vector3 At(TrailPath path, float s, float lateral) =>
            path != null ? path.Offset(s, lateral) : new Vector3(lateral, 0f, s);

        // Material replacements (toned-down copies), then the season's variant.
        private void ApplySwaps(GameObject go)
        {
            if (swaps == null)
            {
                swaps = new Dictionary<Material, Material>();
                for (int i = 0; i < swapFrom.Length && i < swapTo.Length; i++)
                    if (swapFrom[i] != null && swapTo[i] != null) swaps[swapFrom[i]] = swapTo[i];
            }
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    var m = swaps.TryGetValue(mats[i], out var sw) ? sw : mats[i];
                    m = SeasonAssets.Swap(m);
                    if (m != mats[i]) { mats[i] = m; changed = true; }
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        // Any renderer part that sits within running height (ground … +2.2 m) and reaches the trail?
        private bool BlocksTrail(GameObject go, float ground, TrailPath path, float s)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue; // particle FX have huge bounds
                var b = r.bounds;
                if (b.min.y > ground + 2.2f) continue; // overhead (e.g. a canopy) is fine
                if (path == null)
                {
                    if (b.min.x < footprintClear && b.max.x > -footprintClear) return true;
                    continue;
                }
                // Curved trail: sideways distance of the part's centre minus its horizontal radius.
                path.Project(b.center, s, 260f, out float lat);
                if (Mathf.Abs(lat) - Mathf.Max(b.extents.x, b.extents.z) < footprintClear) return true;
            }
            return false;
        }
    }
}
