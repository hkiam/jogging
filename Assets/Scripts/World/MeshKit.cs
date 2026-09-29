using System.Collections.Generic;
using UnityEngine;

namespace Jogging.World
{
    /// <summary>
    /// Builds one mesh from simple parts (boxes, cylinders, quads), one submesh per material slot, with
    /// UVs in metres (× a texture scale), so planks, bark and stone keep their real size on any part.
    /// Used by <see cref="WaysideProps"/> for the man-made things along the trail.
    /// </summary>
    public class MeshKit
    {
        private readonly List<Vector3> v = new List<Vector3>();
        private readonly List<Vector3> n = new List<Vector3>();
        private readonly List<Vector2> uv = new List<Vector2>();
        private readonly List<List<int>> tris = new List<List<int>>();

        private List<int> Slot(int s) { while (tris.Count <= s) tris.Add(new List<int>()); return tris[s]; }

        /// <summary>Quad a-b-c-d (clockwise seen from the front, Unity's front faces), uv from width/height in metres.</summary>
        public void Quad(int slot, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float uvScale = 1f, Vector2 uvOffset = default)
        {
            var t = Slot(slot);
            int i = v.Count;
            Vector3 nr = Vector3.Cross(b - a, d - a).normalized;
            float w = (b - a).magnitude * uvScale, h = (d - a).magnitude * uvScale;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            n.Add(nr); n.Add(nr); n.Add(nr); n.Add(nr);
            uv.Add(uvOffset); uv.Add(uvOffset + new Vector2(w, 0)); uv.Add(uvOffset + new Vector2(w, h)); uv.Add(uvOffset + new Vector2(0, h));
            t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        /// <summary>Quad with its own uv corners (e.g. a sign face showing a whole texture).</summary>
        public void QuadUv(int slot, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            var t = Slot(slot);
            int i = v.Count;
            Vector3 nr = Vector3.Cross(b - a, d - a).normalized;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            n.Add(nr); n.Add(nr); n.Add(nr); n.Add(nr);
            uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
            t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        /// <summary>Box around centre with size, rotated; each face mapped in metres.</summary>
        public void Box(int slot, Vector3 centre, Vector3 size, Quaternion rot, float uvScale = 1f, int endSlot = -1)
        {
            Vector3 h = size * 0.5f;
            Vector3 P(float x, float y, float z) => centre + rot * new Vector3(x * h.x, y * h.y, z * h.z);
            int es = endSlot >= 0 ? endSlot : slot;
            Quad(slot, P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), uvScale);      // +z
            Quad(slot, P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), uvScale);  // -z
            Quad(es, P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), uvScale);        // +x
            Quad(es, P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), uvScale);    // -x
            Quad(slot, P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1), uvScale);      // top
            Quad(slot, P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), uvScale);  // bottom
        }

        /// <summary>Box between two points (a beam, a plank): width across, height up relative to 'up'.</summary>
        public void Beam(int slot, Vector3 a, Vector3 b, float width, float height, Vector3 up, float uvScale = 1f, int endSlot = -1)
        {
            Vector3 d = b - a;
            if (d.sqrMagnitude < 1e-6f) return;
            var rot = Quaternion.LookRotation(d.normalized, Mathf.Abs(Vector3.Dot(d.normalized, up.normalized)) > 0.99f ? Vector3.right : up);
            Box(slot, (a + b) * 0.5f, new Vector3(width, height, d.magnitude), rot, uvScale, endSlot);
        }

        /// <summary>Cylinder from a to b; sides mapped around (u) and along (v) in metres; optional caps.</summary>
        public void Cylinder(int slot, Vector3 a, Vector3 b, float ra, float rb, int segments = 10, int capSlot = -1, float uvScale = 1f, float taperNoise = 0f, int seed = 0, Vector3 capUv = default)
        {
            var t = Slot(slot);
            Vector3 axis = (b - a).normalized;
            Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
            Vector3 up = Vector3.Cross(side, axis);
            float len = (b - a).magnitude, circ = Mathf.PI * (ra + rb);
            int i0 = v.Count;
            for (int k = 0; k <= segments; k++)
            {
                float ang = k * Mathf.PI * 2f / segments;
                float wob = taperNoise > 0f ? 1f + taperNoise * Mathf.Sin(ang * 3f + seed) : 1f;
                Vector3 dir = (side * Mathf.Cos(ang) + up * Mathf.Sin(ang));
                v.Add(a + dir * ra * wob); n.Add(dir); uv.Add(new Vector2(k / (float)segments * circ * uvScale, 0f));
                v.Add(b + dir * rb * wob); n.Add(dir); uv.Add(new Vector2(k / (float)segments * circ * uvScale, len * uvScale));
            }
            for (int k = 0; k < segments; k++)
            {
                int i = i0 + k * 2;
                t.Add(i); t.Add(i + 1); t.Add(i + 3); t.Add(i); t.Add(i + 3); t.Add(i + 2);
            }
            if (capSlot >= 0) { Cap(capSlot, a, -axis, side, up, ra, segments, capUv); Cap(capSlot, b, axis, side, up, rb, segments, capUv); }
        }

        // Disc facing 'normal', uv = the texture centred (end grain: the log-end texture's rings)
        private void Cap(int slot, Vector3 c, Vector3 normal, Vector3 side, Vector3 up, float r, int segments, Vector3 capUv)
        {
            var t = Slot(slot);
            int ic = v.Count;
            // capUv: centre (x, y) and radius (z) in the texture; default: the whole texture
            Vector2 cu = capUv.z > 0f ? new Vector2(capUv.x, capUv.y) : new Vector2(0.5f, 0.5f);
            float cr = capUv.z > 0f ? capUv.z : 0.5f;
            v.Add(c); n.Add(normal); uv.Add(cu);
            for (int k = 0; k <= segments; k++)
            {
                float ang = k * Mathf.PI * 2f / segments;
                Vector3 dir = side * Mathf.Cos(ang) + up * Mathf.Sin(ang);
                v.Add(c + dir * r); n.Add(normal); uv.Add(cu + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * cr);
            }
            bool flip = Vector3.Dot(Vector3.Cross(side, up), normal) > 0f;
            for (int k = 0; k < segments; k++)
            {
                if (flip) { t.Add(ic); t.Add(ic + 1 + k); t.Add(ic + 2 + k); }
                else { t.Add(ic); t.Add(ic + 2 + k); t.Add(ic + 1 + k); }
            }
        }

        /// <summary>A mound (half ellipsoid) on the ground: ant hill, leaf pile, hay heap.</summary>
        public void Mound(int slot, Vector3 foot, float radius, float height, int rings = 5, int segments = 14, float noise = 0.12f, int seed = 0, float uvScale = 1f)
        {
            var t = Slot(slot);
            int i0 = v.Count;
            var rnd = new System.Random(seed);
            float[] wob = new float[segments];
            for (int k = 0; k < segments; k++) wob[k] = 1f + noise * ((float)rnd.NextDouble() * 2f - 1f);
            for (int r = 0; r <= rings; r++)
            {
                float phi = r / (float)rings * Mathf.PI * 0.5f;
                for (int k = 0; k <= segments; k++)
                {
                    float ang = k * Mathf.PI * 2f / segments;
                    float w = wob[k % segments];
                    float rr = Mathf.Cos(phi) * radius * w;
                    var p = foot + new Vector3(Mathf.Cos(ang) * rr, Mathf.Sin(phi) * height, Mathf.Sin(ang) * rr);
                    v.Add(p);
                    n.Add(new Vector3(Mathf.Cos(ang) * Mathf.Cos(phi) / radius, Mathf.Sin(phi) / Mathf.Max(0.01f, height), Mathf.Sin(ang) * Mathf.Cos(phi) / radius).normalized);
                    uv.Add(new Vector2((p.x - foot.x) * uvScale, (p.z - foot.z) * uvScale + p.y * uvScale));
                }
            }
            int row = segments + 1;
            for (int r = 0; r < rings; r++)
                for (int k = 0; k < segments; k++)
                {
                    int a = i0 + r * row + k, b = a + 1, c = a + row, d = c + 1;
                    t.Add(a); t.Add(c); t.Add(b); t.Add(b); t.Add(c); t.Add(d);
                }
        }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv);
            m.subMeshCount = tris.Count;
            for (int s = 0; s < tris.Count; s++) m.SetTriangles(tris[s], s);
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }

        /// <summary>A game object showing the mesh with the materials (by slot).</summary>
        public GameObject Make(string name, Transform parent, params Material[] mats)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            var mesh = Build(name);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            OwnedAssets.Own(go, mesh); // freed with the object
            var used = new Material[tris.Count];
            for (int i = 0; i < used.Length; i++) used[i] = i < mats.Length ? mats[i] : mats[mats.Length - 1];
            go.GetComponent<MeshRenderer>().sharedMaterials = used;
            return go;
        }
    }
}
