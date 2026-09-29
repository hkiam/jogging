using System.Collections.Generic;
using UnityEngine;
using Jogging.Route;

namespace Jogging.World
{
    /// <summary>
    /// Ground fog around the runner (<c>Jogging/Mist</c> sheets): in hollows and valleys, and in the morning
    /// also over flat open land. How much depends on the hour (strongest around sunrise, dissolving as the
    /// sun climbs; forming again after sunset), the season (autumn and spring), the haze and the weather.
    /// Patches are placed where the terrain around a point lies higher than the point itself, a few
    /// metres above the ground, drift slowly with the wind and fade in and out — never at the camera.
    /// </summary>
    public class GroundMist : MonoBehaviour
    {
        private class Patch { public Transform root; public Renderer[] layers; public float alpha, target; public Vector3 pos; }

        private static int Layers => GraphicsQuality.Level == "minimal" ? 2 : 4; // weak tablets: less overdraw
        private readonly List<Patch> patches = new List<Patch>();
        private RouteParams prm;
        private Material mat;
        private Mesh quad;
        private MaterialPropertyBlock mpb;
        private float nextSpawn, amount, humidity;
        private Vector2 drift, windDir;
        private int maxPatches;
        private System.Random rnd;

        private static readonly int idAlpha = Shader.PropertyToID("_Alpha"), idColor = Shader.PropertyToID("_MistColor"), idDrift = Shader.PropertyToID("_MistDrift");

        public void Setup(RouteParams p, int seed, Vector2 wind)
        {
            prm = p;
            rnd = new System.Random(seed * 13 + 5);
            windDir = wind.sqrMagnitude > 0.01f ? wind.normalized : Vector2.right;
            string l = GraphicsQuality.Level;
            maxPatches = l == "minimal" ? 6 : l == "low" ? 10 : l == "medium" ? 14 : 18;
            humidity = Mathf.Clamp01(p.haze) * 0.8f + (p.season == "autumn" ? 0.35f : p.season == "spring" ? 0.25f : p.season == "winter" ? 0.2f : 0.05f)
                     + (p.weather == "rain" ? 0.15f : p.weather == "cloudy" ? 0.1f : 0f);
            if (mat == null)
            {
                var sh = Resources.Load<Shader>("JoggingMist");
                if (sh == null) { Debug.LogWarning("[Jogging] Bodennebel: Shader fehlt"); enabled = false; return; }
                mat = new Material(sh) { name = "GroundMist", renderQueue = 3050 };
                mat.SetTexture("_Noise", SkyNoise.Texture);
                quad = MakeQuad();
                mpb = new MaterialPropertyBlock();
            }
            foreach (var pt in patches) if (pt.root != null) Destroy(pt.root.gameObject);
            patches.Clear();
            nextSpawn = 0f;
        }

        private void OnDestroy() { if (mat != null) Destroy(mat); if (quad != null) Destroy(quad); }

        private float nextLog;
        private int tries, noGround, rejected;

        private void Update()
        {
            var sky = Sky.Instance;
            var cam = Camera.main;
            if (sky == null || cam == null || prm == null) return;
            float e = sky.SunElevation, h = sky.Hours;
            float morning = h < 13f ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(3f, 16f, e)) : 0f;
            float evening = h >= 13f ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(3f, -6f, e)) * 0.7f : 0f;
            amount = Mathf.Clamp01(Mathf.Max(morning, evening) * (0.3f + humidity) + (Mathf.Clamp01(prm.haze) - 0.55f) * 0.8f);
            if (prm.weather == "snow") amount *= 0.4f;

            // colour: the fog lit by the sun (warm in the morning), dim at night
            Color fog = RenderSettings.fogColor;
            var sun = RenderSettings.sun;
            Color lit = sun != null ? sun.color * sun.intensity * 0.22f : Color.black;
            mat.SetVector(idColor, new Vector4(fog.r * 0.85f + lit.r, fog.g * 0.85f + lit.g, fog.b * 0.85f + lit.b, amount));
            drift += windDir * 0.6f * Time.deltaTime;                         // ground air barely moves
            mat.SetVector(idDrift, new Vector4(-drift.x, -drift.y, Time.time, 0f));

            Vector3 c = cam.transform.position;
            if (Time.unscaledTime >= nextLog && Jogging.Core.Args.Has("-diag"))
            {
                nextLog = Time.unscaledTime + 5f;
                Debug.Log($"[Nebel] Menge {amount:0.00} (Feuchte {humidity:0.00}), Flecken {patches.Count}, Versuche {tries}, ohne Boden {noGround}, verworfen {rejected}, " +
                          $"Wolkenschatten hier {sky.ShadeAtRunner():0.00}");
            }
            int wanted = Mathf.RoundToInt(maxPatches * Mathf.Clamp01(amount * 1.4f));
            if (Jogging.Core.Args.Has("-misttest") && patches.Count == 0 && amount > 0.08f) // test: three patches right ahead
                for (int k = 1; k <= 3; k++)
                {
                    var q = c + new Vector3(cam.transform.forward.x, 0f, cam.transform.forward.z).normalized * (40f + 45f * k);
                    if (Ground(q, out float gh)) Spawn(new Vector3(q.x, gh + 1.5f, q.z), 90f);
                }
            if (Time.time >= nextSpawn && patches.Count < wanted && amount > 0.08f)
            {
                nextSpawn = Time.time + 0.4f;
                TrySpawn(cam.transform, morning);
            }
            for (int i = patches.Count - 1; i >= 0; i--)
            {
                var pt = patches[i];
                pt.pos += new Vector3(windDir.x, 0f, windDir.y) * 0.6f * Time.deltaTime;
                pt.root.position = pt.pos;
                float d = Vector2.Distance(new Vector2(pt.pos.x, pt.pos.z), new Vector2(c.x, c.z));
                pt.target = d > 600f || patches.Count > wanted + 2 ? 0f : 1f;
                pt.alpha = Mathf.MoveTowards(pt.alpha, pt.target, Time.deltaTime / 5f);
                if (pt.alpha <= 0f && pt.target <= 0f) { Destroy(pt.root.gameObject); patches.RemoveAt(i); continue; }
                for (int k = 0; k < pt.layers.Length; k++)
                {
                    mpb.SetFloat(idAlpha, pt.alpha * (1f - k * 0.2f));
                    pt.layers[k].SetPropertyBlock(mpb);
                }
            }
        }

        private void TrySpawn(Transform cam, float morning)
        {
            float R() => (float)rnd.NextDouble();
            // mostly ahead of the runner, 50–450 m away
            float ang = (R() - 0.5f) * 200f * Mathf.Deg2Rad;
            var fwd = new Vector3(cam.forward.x, 0f, cam.forward.z).normalized;
            if (fwd.sqrMagnitude < 0.1f) fwd = Vector3.forward;
            var dir = Quaternion.AngleAxis(ang * Mathf.Rad2Deg, Vector3.up) * fwd;
            var p = cam.position + dir * Mathf.Lerp(50f, 450f, R());
            tries++;
            if (!Ground(p, out float h)) { noGround++; return; }
            foreach (var o in patches) if ((new Vector2(o.pos.x - p.x, o.pos.z - p.z)).sqrMagnitude < 60f * 60f) return;
            float sum = 0f, maxDev = 0f;
            const int n = 6; int got = 0;
            for (int k = 0; k < n; k++)
            {
                float a = k * Mathf.PI * 2f / n;
                if (!Ground(p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 75f, out float hk)) continue;
                sum += hk; got++; maxDev = Mathf.Max(maxDev, Mathf.Abs(hk - h));
            }
            if (got < 4) return;
            float valley = sum / got - h;
            bool hollow = valley > 2.5f;
            bool field = morning > 0.3f && maxDev < 2.5f && R() < 0.5f;
            if (!hollow && !field) { rejected++; return; }
            float size = Mathf.Lerp(70f, 150f, R()) * (hollow ? 1f : 0.8f);
            Spawn(new Vector3(p.x, h + (hollow ? Mathf.Min(valley * 0.5f, 5f) : 1.2f), p.z), size);
        }

        private void Spawn(Vector3 pos, float size)
        {
            var root = new GameObject("Mist").transform;
            root.SetParent(transform, false);
            root.position = pos;
            var pt = new Patch { root = root, pos = pos, layers = new Renderer[Layers], alpha = 0f, target = 1f };
            for (int k = 0; k < Layers; k++)
            {
                var go = new GameObject("Layer" + k, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(root, false);
                go.transform.localPosition = new Vector3(0f, k * 0.9f, 0f);
                float s = size * (1f - k * 0.12f);
                go.transform.localScale = new Vector3(s, 1f, s);
                go.transform.localRotation = Quaternion.Euler(0f, k * 47f, 0f);
                go.GetComponent<MeshFilter>().sharedMesh = quad;
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                pt.layers[k] = r;
            }
            patches.Add(pt);
        }

        // the detailed tile only (not a coarse draft that is about to be replaced)
        private static bool Ground(Vector3 p, out float h) => TerrainGround.TryHeight(p.x, p.z, out h, 200);

        // A flat 1×1 quad in XZ (unit square centred), uv 0..1
        private static Mesh MakeQuad()
        {
            var m = new Mesh { name = "MistQuad" };
            m.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            m.RecalculateBounds();
            m.bounds = new Bounds(Vector3.zero, new Vector3(1f, 0.1f, 1f));
            return m;
        }
    }
}
