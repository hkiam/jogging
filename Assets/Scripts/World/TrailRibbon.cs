using UnityEngine;

namespace Jogging.World
{
    /// <summary>
    /// A dirt trail that hugs real terrain: a strip mesh along the trail centre line
    /// (<see cref="TrailPath"/>, or x = 0 without one) from a little behind to far ahead, rebuilt
    /// as the runner advances and while terrain tiles refine (draft → full resolution). A slightly sunk outer skirt tucks the edges into the ground.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class TrailRibbon : MonoBehaviour
    {
        [SerializeField] private Transform runner;
        [SerializeField] private float behind = 30f;
        [SerializeField] private float ahead = 260f;
        [SerializeField] private float step = 1f;
        [SerializeField] private float lift = 0.05f;
        [Tooltip("Rebuild after the runner moved this far, or at least every rebuildInterval seconds.")]
        [SerializeField] private float rebuildDistance = 4f;
        [SerializeField] private float rebuildInterval = 1.5f;

        private Mesh mesh;
        private float lastZ = float.MinValue, lastTime = -999f;
        private Vector3[] verts;
        private Vector2[] uvs, uvs2;
        private Color[] cols4;
        private readonly float[] xs = new float[4];
        private readonly float[] weights = new float[TrailSurface.Count];
        private int[] tris;

        private void Awake()
        {
            mesh = new Mesh { name = "TrailRibbon" };
            mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = mesh;
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            SetupSurfaces();
        }

        /// <summary>
        /// The surfaces material (<c>Jogging/TrailSurface</c>): asphalt, gravel (this trail's own texture),
        /// earth, forest floor, meadow path — blended per vertex by <see cref="TrailSurface"/>.
        /// </summary>
        private void SetupSurfaces()
        {
            var mr = GetComponent<MeshRenderer>();
            var shader = Resources.Load<Shader>("JoggingTrail");
            if (shader == null) { Debug.LogWarning("[Weg] Shader JoggingTrail fehlt – einfacher Schotterweg"); return; }
            var old = mr.sharedMaterial;
            var m = new Material(shader) { name = "TrailSurfaces" };
            void Set(int i, string id, Color tint, float smooth, Texture fallbackBase = null, Texture fallbackN = null)
            {
                var src = Resources.Load<Material>("Wayside/Mat_" + id);
                m.SetTexture("_T" + i, src != null ? src.GetTexture("_BaseMap") : fallbackBase);
                var n = src != null ? src.GetTexture("_BumpMap") : fallbackN;
                if (n != null) m.SetTexture("_N" + i, n);
                m.SetVector("_Tint" + i, new Vector4(tint.r, tint.g, tint.b, smooth));
            }
            Set(0, "asphalt_02", new Color(1.0f, 1.0f, 1.0f), 0.18f);
            if (old != null && old.HasProperty("_BaseMap"))
            {
                m.SetTexture("_T1", old.GetTexture("_BaseMap"));
                if (old.GetTexture("_BumpMap") != null) m.SetTexture("_N1", old.GetTexture("_BumpMap"));
                var bc = old.HasProperty("_BaseColor") ? old.GetColor("_BaseColor") : Color.white;
                m.SetVector("_Tint1", new Vector4(bc.r, bc.g, bc.b, 0.08f));
            }
            else Set(1, "gravel_road", Color.white, 0.08f);
            Set(2, "brown_mud_leaves_01", new Color(0.9f, 0.88f, 0.85f), 0.14f); // earth: dark forest soil with a few leaves
            Set(3, "forest_ground_04", new Color(1.0f, 1.0f, 1.0f), 0.08f);
            Set(4, "grass_path_3", new Color(1.0f, 1.0f, 1.0f), 0.05f);
            m.SetTexture("_Noise", SkyNoise.Texture);
            m.SetVector("_Tile", new Vector4(3.5f, 3f, 2.6f, 2.6f));   // metres per tile: asphalt, gravel, earth, forest
            m.SetVector("_Tile2", new Vector4(3f, 0f, 0f, 0f));        // meadow
            m.SetVector("_Ragged", new Vector4(0.02f, 0.35f, 0.55f, 0.7f));
            m.SetVector("_Ragged2", new Vector4(0.8f, 0f, 0f, 0f));
            var p = RouteRuntime.Current != null ? RouteRuntime.Current.@params : null;
            float snow = p != null && p.season == "winter" ? (p.weather == "snow" ? 0.85f : 0.55f) : 0f;
            float wet = p != null && p.weather == "rain" ? 1f : 0f;
            m.SetVector("_Weather", new Vector4(snow, wet, 0f, 0f));
            mr.sharedMaterial = m;
        }

        private void LateUpdate()
        {
            long t0 = FrameWork.Start();
            LateUpdateWork();
            FrameWork.Stop("Wegband", t0);
        }

        private void LateUpdateWork()
        {
            if (runner == null) return;
            var path = TrailPath.Active;
            float z = path != null ? path.RunnerS : runner.position.z;
            if (Mathf.Abs(z - lastZ) < rebuildDistance && Time.unscaledTime - lastTime < rebuildInterval) return;
            Rebuild(z, path);
        }

        private void Rebuild(float runnerZ, TrailPath path)
        {
            int rows = Mathf.CeilToInt((behind + ahead) / step) + 1;
            const int cols = 4; // outer-left, left, right, outer-right (outer = sunk skirt)
            if (verts == null || verts.Length != rows * cols)
            {
                verts = new Vector3[rows * cols];
                uvs = new Vector2[rows * cols];
                uvs2 = new Vector2[rows * cols];
                cols4 = new Color[rows * cols];
                tris = new int[(rows - 1) * (cols - 1) * 6];
                int t = 0;
                for (int r = 0; r < rows - 1; r++)
                for (int c = 0; c < cols - 1; c++)
                {
                    int a = r * cols + c, b = a + 1, d = a + cols, e = d + 1;
                    tris[t++] = a; tris[t++] = d; tris[t++] = b;
                    tris[t++] = b; tris[t++] = d; tris[t++] = e;
                }
            }

            bool any = false;
            // metres along, minus whole periods of all tile lengths (3.5 / 3 / 2.6 m → 273 m): the texture doesn't
            // move, and uv stays small on a 30 km run (float precision on mobile GPUs)
            float uvBase = Mathf.Floor((runnerZ - behind) / 273f) * 273f;
            float z0 = Mathf.Floor((runnerZ - behind) / step) * step; // world-aligned rows → no texture swim
            for (int r = 0; r < rows; r++)
            {
                float z = z0 + r * step; // arc length along the trail (or world z without a path)
                Vector3 centre = path != null ? path.Point(z) : new Vector3(0f, 0f, z);
                Vector3 right = path != null ? path.Right(z) : Vector3.right;
                float half = TrailSurface.HalfWidth(z), skirt = half + 0.5f;
                TrailSurface.Weights(z, weights);
                var wc = new Color(weights[0], weights[1], weights[2], weights[3]);
                xs[0] = -skirt; xs[1] = -half; xs[2] = half; xs[3] = skirt;
                for (int c = 0; c < cols; c++)
                {
                    float x = xs[c];
                    Vector3 w = centre + right * x;
                    if (TerrainGround.TryHeight(w.x, w.z, out float h)) any = true;
                    bool outer = c == 0 || c == cols - 1;
                    verts[r * cols + c] = new Vector3(w.x, h + (outer ? -0.02f : lift), w.z);
                    uvs[r * cols + c] = new Vector2(x / half * 0.5f + 0.5f, z - uvBase);   // x: 0 and 1 at the path edges; y: metres (kept small)
                    uvs2[r * cols + c] = new Vector2(weights[4], 0f);
                    cols4[r * cols + c] = wc;
                }
            }
            if (!any) return; // no terrain yet

            mesh.Clear();
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.uv2 = uvs2;
            mesh.colors = cols4;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            lastZ = runnerZ;
            lastTime = Time.unscaledTime;
        }
    }
}
