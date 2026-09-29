using UnityEngine;
using Jogging.Route;

namespace Jogging.UI
{
    /// <summary>A QR code as a sharp texture (quiet zone of 4 modules, point filtered).</summary>
    public static class QrTexture
    {
        public static Texture2D Create(QrCode qr, int scale = 4)
        {
            int border = 4, n = (qr.Size + border * 2) * scale;
            var tex = new Texture2D(n, n, TextureFormat.RGB24, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    bool dark = qr[x / scale - border, (n - 1 - y) / scale - border]; // texture origin is bottom-left
                    px[y * n + x] = dark ? new Color32(0, 0, 0, 255) : new Color32(255, 255, 255, 255);
                }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
