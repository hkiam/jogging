using UnityEngine;
using UnityEngine.UI;

namespace Jogging.UI
{
    /// <summary>
    /// Elevation profile as a filled area with a top line (uGUI graphic, no textures). Heights are
    /// scaled to the rect with a little headroom; a flat profile sits in the lower third.
    /// </summary>
    public class ProfileChart : MaskableGraphic
    {
        private float[] heights = new float[0];
        public Color lineColor = UiTheme.Accent;
        public float lineWidth = 3f;

        public void SetData(float[] h)
        {
            heights = h ?? new float[0];
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (heights.Length < 2) return;
            var r = GetPixelAdjustedRect();
            float min = float.MaxValue, max = float.MinValue;
            foreach (var v in heights) { min = Mathf.Min(min, v); max = Mathf.Max(max, v); }
            float span = Mathf.Max(max - min, 30f); // ≥ 30 m so gentle profiles don't look alpine
            float lo = min - span * 0.12f, hi = lo + span * 1.25f;

            int cols = Mathf.Min(heights.Length, Mathf.Max(2, Mathf.RoundToInt(r.width / 2f)));
            var fill = color;
            var top = new Vector2[cols];
            for (int c = 0; c < cols; c++)
            {
                float t = c / (cols - 1f);
                float h = heights[Mathf.Clamp(Mathf.RoundToInt(t * (heights.Length - 1)), 0, heights.Length - 1)];
                top[c] = new Vector2(r.xMin + t * r.width, r.yMin + (h - lo) / (hi - lo) * r.height);
            }
            // Area under the curve.
            for (int c = 0; c < cols; c++)
            {
                vh.AddVert(new Vector3(top[c].x, r.yMin), fill, Vector2.zero);
                vh.AddVert(top[c], fill, Vector2.zero);
            }
            for (int c = 0; c < cols - 1; c++)
            {
                int i = c * 2;
                vh.AddTriangle(i, i + 1, i + 3);
                vh.AddTriangle(i, i + 3, i + 2);
            }
            // Top line.
            int b = vh.currentVertCount;
            for (int c = 0; c < cols; c++)
            {
                Vector2 d = top[Mathf.Min(c + 1, cols - 1)] - top[Mathf.Max(c - 1, 0)];
                Vector2 n = new Vector2(-d.y, d.x).normalized * (lineWidth * 0.5f);
                vh.AddVert(top[c] - n, lineColor, Vector2.zero);
                vh.AddVert(top[c] + n, lineColor, Vector2.zero);
            }
            for (int c = 0; c < cols - 1; c++)
            {
                int i = b + c * 2;
                vh.AddTriangle(i, i + 1, i + 3);
                vh.AddTriangle(i, i + 3, i + 2);
            }
        }
    }
}
