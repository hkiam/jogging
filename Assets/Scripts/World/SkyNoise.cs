using UnityEngine;

namespace Jogging.World
{
    /// <summary>
    /// Tileable noise for the sky (<see cref="Sky"/>), made once per app start (256², a few hundred ms on a
    /// weak tablet) and kept across scene reloads. Channels: R = cloud shapes (fbm, equalised: a value
    /// above 1 − c covers exactly the share c of the sky), G = fine detail that frays the edges,
    /// B = cirrus streaks (stretched fbm), A = ridged noise (moon maria, dust lanes of the Milky Way).
    /// R is also kept on the CPU: the cloud shadows on the ground (<see cref="Sky"/>'s light cookie)
    /// use exactly the shapes the sky shows.
    /// </summary>
    public static class SkyNoise
    {
        public const int Size = 256;
        private static Texture2D tex;
        private static float[] shapes;

        public static Texture2D Texture { get { Make(); return tex; } }

        /// <summary>Cloud shape (R) at uv in tiles, bilinear and repeating — as the GPU samples it.</summary>
        public static float Shape(float u, float v)
        {
            Make();
            float x = (u - Mathf.Floor(u)) * Size - 0.5f, y = (v - Mathf.Floor(v)) * Size - 0.5f;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            float a = At(x0, y0), b = At(x0 + 1, y0), c = At(x0, y0 + 1), d = At(x0 + 1, y0 + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// <summary>Cloud density of the sky shader's base shape at coverage c (0 clear … 1 overcast).</summary>
        public static float Density(float shape, float coverage) => Mathf.Clamp01((shape - (1f - coverage)) / 0.24f);

        private static float At(int x, int y) => shapes[((y % Size + Size) % Size) * Size + (x % Size + Size) % Size];

        private static void Make()
        {
            if (tex != null) return;
            int n = Size * Size;
            var r = new float[n]; var g = new float[n]; var b = new float[n]; var a = new float[n];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    int i = y * Size + x;
                    r[i] = Fbm(u, v, 4, 4, 5, 11);
                    g[i] = Fbm(u, v, 16, 16, 4, 23);
                    b[i] = Fbm(u, v, 2, 14, 4, 37);
                    a[i] = Ridged(u, v, 8, 8, 4, 51);
                }
            Equalise(r); Normalise(g); Normalise(b); Normalise(a);
            shapes = r;
            tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true, true)
            {
                name = "SkyNoise", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 2,
                hideFlags = HideFlags.DontSave,
            };
            var px = new Color32[n];
            for (int i = 0; i < n; i++)
                px[i] = new Color32((byte)(r[i] * 255f + 0.5f), (byte)(g[i] * 255f + 0.5f), (byte)(b[i] * 255f + 0.5f), (byte)(a[i] * 255f + 0.5f));
            tex.SetPixels32(px);
            tex.Apply(true, true); // GPU only; the CPU keeps its own copy of R
            for (int i = 0; i < n; i++) shapes[i] = px[i].r / 255f; // the exact bytes the GPU filters
        }

        // fbm of periodic gradient noise: periods px, py cells over the tile, doubling per octave
        private static float Fbm(float u, float v, int px, int py, int octaves, int seed)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * Perlin(u * px, v * py, px, py, seed + o * 101);
                norm += amp; amp *= 0.5f; px *= 2; py *= 2;
            }
            return sum / norm;
        }

        private static float Ridged(float u, float v, int px, int py, int octaves, int seed)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * (1f - Mathf.Abs(Perlin(u * px, v * py, px, py, seed + o * 101)));
                norm += amp; amp *= 0.5f; px *= 2; py *= 2;
            }
            return sum / norm;
        }

        private static float Perlin(float x, float y, int px, int py, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            float u = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f), w = fy * fy * fy * (fy * (fy * 6f - 15f) + 10f);
            float g00 = Grad(x0, y0, px, py, seed, fx, fy), g10 = Grad(x0 + 1, y0, px, py, seed, fx - 1f, fy);
            float g01 = Grad(x0, y0 + 1, px, py, seed, fx, fy - 1f), g11 = Grad(x0 + 1, y0 + 1, px, py, seed, fx - 1f, fy - 1f);
            return Mathf.Lerp(Mathf.Lerp(g00, g10, u), Mathf.Lerp(g01, g11, u), w) * 1.41f;
        }

        private static float Grad(int ix, int iy, int px, int py, int seed, float dx, float dy)
        {
            ix = (ix % px + px) % px; iy = (iy % py + py) % py;
            uint h = (uint)(ix * 374761393 + iy * 668265263 + seed * 144269504);
            h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
            float ang = (h & 0xffff) / 65536f * Mathf.PI * 2f;
            return Mathf.Cos(ang) * dx + Mathf.Sin(ang) * dy;
        }

        private static void Normalise(float[] f)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var x in f) { if (x < lo) lo = x; if (x > hi) hi = x; }
            float k = hi > lo ? 1f / (hi - lo) : 0f;
            for (int i = 0; i < f.Length; i++) f[i] = (f[i] - lo) * k;
        }

        // Rank → value: uniform distribution, so a threshold is a sky share
        private static void Equalise(float[] f)
        {
            var idx = new int[f.Length];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            var keys = (float[])f.Clone();
            System.Array.Sort(keys, idx);
            for (int k = 0; k < idx.Length; k++) f[idx[k]] = k / (float)(idx.Length - 1);
        }
    }
}
