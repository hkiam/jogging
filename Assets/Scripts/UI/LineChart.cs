using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Jogging.UI
{
    /// <summary>
    /// Several value series over the same x axis as lines (uGUI graphic, no textures). Each series has
    /// its own vertical range, so speed and pulse can share one chart. Optional horizontal grid.
    /// </summary>
    public class LineChart : MaskableGraphic
    {
        private class Series { public float[] v; public Color c; public float lo, hi; }
        private readonly List<Series> series = new List<Series>();
        public float lineWidth = 3f;

        public void Clear() { series.Clear(); SetVerticesDirty(); }

        /// <summary>Add a series; lo/hi = its vertical range (NaN = from the data, with headroom). Values &lt;= 0 are gaps.</summary>
        public void Add(float[] values, Color c, float lo = float.NaN, float hi = float.NaN)
        {
            if (values == null || values.Length < 2) return;
            float mn = float.MaxValue, mx = float.MinValue;
            foreach (var x in values) if (x > 0f) { mn = Mathf.Min(mn, x); mx = Mathf.Max(mx, x); }
            if (mn == float.MaxValue) return;
            float span = Mathf.Max(mx - mn, 1f);
            series.Add(new Series { v = values, c = c, lo = float.IsNaN(lo) ? mn - span * 0.15f : lo, hi = float.IsNaN(hi) ? mx + span * 0.15f : hi });
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            var grid = new Color(1f, 1f, 1f, 0.08f);
            for (int g = 0; g <= 4; g++) Quad(vh, new Vector2(r.xMin, r.yMin + r.height * g / 4f), new Vector2(r.xMax, r.yMin + r.height * g / 4f), 1f, grid);

            foreach (var s in series)
            {
                int cols = Mathf.Min(s.v.Length, Mathf.Max(2, Mathf.RoundToInt(r.width / 3f)));
                Vector2? prev = null;
                for (int c = 0; c < cols; c++)
                {
                    float t = c / (cols - 1f);
                    // average of the samples in this column (smooth, no aliasing)
                    int a = Mathf.FloorToInt(t * (s.v.Length - 1)), b = Mathf.Min(s.v.Length - 1, a + Mathf.Max(1, s.v.Length / cols));
                    float sum = 0f; int n = 0;
                    for (int k = a; k <= b; k++) if (s.v[k] > 0f) { sum += s.v[k]; n++; }
                    if (n == 0) { prev = null; continue; }
                    var p = new Vector2(r.xMin + t * r.width, r.yMin + Mathf.Clamp01((sum / n - s.lo) / (s.hi - s.lo)) * r.height);
                    if (prev.HasValue) Quad(vh, prev.Value, p, lineWidth, s.c);
                    prev = p;
                }
            }
        }

        private static void Quad(VertexHelper vh, Vector2 a, Vector2 b, float w, Color c)
        {
            Vector2 n = new Vector2(-(b - a).y, (b - a).x).normalized * (w * 0.5f);
            int i = vh.currentVertCount;
            vh.AddVert(a - n, c, Vector2.zero); vh.AddVert(a + n, c, Vector2.zero);
            vh.AddVert(b + n, c, Vector2.zero); vh.AddVert(b - n, c, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
        }
    }
}
