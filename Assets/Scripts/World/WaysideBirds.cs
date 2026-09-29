using System.Collections.Generic;
using UnityEngine;

namespace Jogging.World
{
    /// <summary>
    /// Birds: crows on open land that walk and peck and take off when the runner comes within ~28 m, and
    /// now and then a flock high overhead (by day, not in rain). Simple shapes — they are only ever seen
    /// from a distance, as a real runner sees them.
    /// </summary>
    public class WaysideBirds : MonoBehaviour
    {
        private class Bird { public Transform t, wingL, wingR; public Vector3 vel; public float phase; public bool flying; }

        private readonly List<Bird> birds = new List<Bird>();
        private bool ground;
        private static Mesh body, wing;
        private static Material black;

        /// <summary>A group of 3–7 crows on the ground around foot.</summary>
        public static GameObject Crows(Transform parent, Vector3 foot, int seed)
        {
            var go = new GameObject("Krähen");
            go.transform.SetParent(parent, false);
            go.transform.position = foot;
            var wb = go.AddComponent<WaysideBirds>();
            wb.ground = true;
            var rnd = new System.Random(seed);
            int n = 3 + rnd.Next(5);
            for (int i = 0; i < n; i++)
            {
                var p = foot + new Vector3(((float)rnd.NextDouble() - 0.5f) * 10f, 0f, ((float)rnd.NextDouble() - 0.5f) * 10f);
                if (TerrainGround.TryHeight(p.x, p.z, out float h)) p.y = h;
                wb.birds.Add(wb.Make(p, (float)rnd.NextDouble() * 360f, 1f, (float)rnd.NextDouble() * 10f));
            }
            return go;
        }

        /// <summary>A flock crossing the sky around the camera.</summary>
        public static GameObject Flock(Transform parent, Vector3 centre, Vector3 dir, int seed)
        {
            var go = new GameObject("Vogelschwarm");
            go.transform.SetParent(parent, false);
            var wb = go.AddComponent<WaysideBirds>();
            var rnd = new System.Random(seed);
            int n = 8 + rnd.Next(18);
            for (int i = 0; i < n; i++)
            {
                var off = new Vector3(((float)rnd.NextDouble() - 0.5f) * 30f, ((float)rnd.NextDouble() - 0.5f) * 8f, ((float)rnd.NextDouble() - 0.5f) * 30f);
                var b = wb.Make(centre + off, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 0.8f, (float)rnd.NextDouble() * 10f);
                b.flying = true; b.vel = dir * (11f + (float)rnd.NextDouble() * 2f);
                wb.birds.Add(b);
            }
            Destroy(go, 60f);
            return go;
        }

        private Bird Make(Vector3 pos, float yaw, float scale, float phase)
        {
            Ensure();
            var t = new GameObject("Vogel").transform;
            t.SetParent(transform, true);
            t.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            t.localScale = Vector3.one * scale;
            Part(t, body, Vector3.zero);
            var l = Part(t, wing, new Vector3(-0.05f, 0.16f, 0.02f)); l.localScale = new Vector3(-1f, 1f, 1f);
            var r = Part(t, wing, new Vector3(0.05f, 0.16f, 0.02f));
            return new Bird { t = t, wingL = l, wingR = r, phase = phase };
        }

        private static Transform Part(Transform parent, Mesh m, Vector3 pos)
        {
            var go = new GameObject("p", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.GetComponent<MeshFilter>().sharedMesh = m;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = black;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        private static void Ensure()
        {
            if (body != null) return;
            black = WaysideProps.Paint("crow", new Color(0.03f, 0.03f, 0.035f), 0.35f);
            var k = new MeshKit();
            k.Mound(0, new Vector3(0f, 0.1f, 0f), 0.09f, 0.1f, 3, 8);                       // back
            k.Mound(0, new Vector3(0f, 0.2f, 0.12f), 0.055f, 0.06f, 3, 8);                  // head
            k.Cylinder(0, new Vector3(0f, 0.21f, 0.16f), new Vector3(0f, 0.2f, 0.23f), 0.018f, 0.002f, 5); // beak
            k.Box(0, new Vector3(0f, 0.12f, -0.16f), new Vector3(0.1f, 0.015f, 0.16f), Quaternion.Euler(-12f, 0f, 0f)); // tail
            k.Cylinder(0, new Vector3(-0.03f, 0.1f, 0f), new Vector3(-0.03f, 0f, 0.01f), 0.007f, 0.006f, 4); // legs
            k.Cylinder(0, new Vector3(0.03f, 0.1f, 0f), new Vector3(0.03f, 0f, 0.01f), 0.007f, 0.006f, 4);
            body = k.Build("Krähe");
            var w = new MeshKit();
            w.Quad(0, new Vector3(0f, 0f, 0.07f), new Vector3(0.36f, 0f, 0.02f), new Vector3(0.34f, 0f, -0.08f), new Vector3(0f, 0f, -0.08f));
            w.Quad(0, new Vector3(0f, 0f, -0.08f), new Vector3(0.34f, 0f, -0.08f), new Vector3(0.36f, 0f, 0.02f), new Vector3(0f, 0f, 0.07f)); // both sides
            wing = w.Build("Flügel");
        }

        private void Update()
        {
            var cam = Camera.main;
            float dt = Time.deltaTime;
            foreach (var b in birds)
            {
                if (b.t == null) continue;
                if (ground && !b.flying && cam != null)
                {
                    float d = Vector3.Distance(cam.transform.position, b.t.position);
                    if (d < 28f)
                    {
                        // take off away from the runner, climbing
                        b.flying = true;
                        Vector3 away = b.t.position - cam.transform.position; away.y = 0f; away.Normalize();
                        b.vel = (away + Vector3.up * 0.45f).normalized * 9f;
                        b.t.rotation = Quaternion.LookRotation(new Vector3(b.vel.x, 0f, b.vel.z));
                    }
                    else
                    {
                        // on the ground: an occasional hop and peck
                        b.phase += dt;
                        float peck = Mathf.Sin(b.phase * 2.3f) > 0.95f ? 25f : 0f;
                        b.t.localRotation = Quaternion.Euler(peck, b.t.localEulerAngles.y + (Mathf.Sin(b.phase * 0.7f) > 0.99f ? 40f : 0f), 0f);
                        b.wingL.localRotation = b.wingR.localRotation = Quaternion.Euler(0f, 0f, 80f); // folded
                        continue;
                    }
                }
                if (!b.flying) continue;
                b.phase += dt * 9f;
                float flap = Mathf.Sin(b.phase) * 35f;
                b.wingL.localRotation = Quaternion.Euler(0f, 0f, -flap);
                b.wingR.localRotation = Quaternion.Euler(0f, 0f, flap);
                if (ground) b.vel.y = Mathf.Lerp(b.vel.y, 0.8f, dt * 0.3f);
                b.t.position += b.vel * dt;
            }
            if (ground && birds.Count > 0 && birds[0].flying && birds[0].t != null && birds[0].t.position.y > transform.position.y + 60f) Destroy(gameObject);
        }
    }
}
