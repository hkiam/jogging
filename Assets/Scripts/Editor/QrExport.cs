using System.IO;
using UnityEditor;
using UnityEngine;
using Jogging.Route;

namespace Jogging.EditorTools
{
    /// <summary>
    /// Writes test QR codes (short text, a real route share code, long data, medium ECC) as PNGs plus the
    /// expected text into a folder, for Tools/qrcheck.sh to decode with the macOS barcode detector.
    /// Headless: -executeMethod Jogging.EditorTools.QrExport.RunBatch -qrout &lt;folder&gt;
    /// </summary>
    public static class QrExport
    {
        public static void RunBatch()
        {
            string dir = "qr-out";
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-qrout") dir = a[i + 1];
            Directory.CreateDirectory(dir);
            var route = RoutePresets.Create("wald", 42);
            route.id = RouteStore.NewId();
            string share = RouteShare.ToCode(route);
            var cases = new (string name, string text, QrCode.Ecc ecc)[]
            {
                ("kurz", "Jogging", QrCode.Ecc.Low),
                ("strecke", share, QrCode.Ecc.Low),
                ("strecke-m", share, QrCode.Ecc.Medium),
                ("lang", new string('x', 1400) + "Ende", QrCode.Ecc.Low),
                ("umlaute", "Waldrunde – Höhe 120 m · Grüße", QrCode.Ecc.Medium),
            };
            foreach (var (name, text, ecc) in cases)
            {
                var qr = QrCode.EncodeText(text, ecc);
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), Render(qr, 6).EncodeToPNG());
                File.WriteAllText(Path.Combine(dir, name + ".txt"), text);
                Debug.Log($"[QrExport] {name}: Version {qr.Version}, {qr.Size}×{qr.Size}, {text.Length} Zeichen");
            }
            EditorApplication.Exit(0);
        }

        public static Texture2D Render(QrCode qr, int scale) => Jogging.UI.QrTexture.Create(qr, scale);
    }
}
