using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Jogging.EditorTools
{
    /// <summary>
    /// The app icon on every platform (made by Tools/make-icon.py from Assets/Art/Icon/Source~/Vorlage.png):
    /// a full-bleed 1024 px square as the default — Unity derives all iPad sizes from it, iOS rounds it —
    /// the rounded tile on transparent for macOS (which shows icons unmasked), and Android's adaptive
    /// icon (the square as the background layer, an empty foreground; legacy and round from the square).
    /// Applied by every build (BuildTools, MobileBuild). Menu: Jogging → Build → App-Icon setzen.
    /// </summary>
    public static class AppIcon
    {
        public const string IconPath = "Assets/Art/Icon/AppIcon.png";
        public const string ForegroundPath = "Assets/Art/Icon/AppIconForeground.png";
        public const string MacPath = "Assets/Art/Icon/AppIconMac.png";

        [MenuItem("Jogging/Build/App-Icon setzen")]
        public static void Apply()
        {
            var icon = Prepare(IconPath);
            var fg = Prepare(ForegroundPath);
            if (icon == null) { Debug.LogWarning("[Jogging] App-Icon fehlt: " + IconPath + " (Tools/make-icon.py)"); return; }

            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any); // default for all platforms

            var mac = Prepare(MacPath);
            if (mac != null) // every standalone size from the tile (Unity scales it down)
            {
                int n = PlayerSettings.GetIconSizes(NamedBuildTarget.Standalone, IconKind.Any).Length;
                var list = new Texture2D[n];
                for (int i = 0; i < n; i++) list[i] = mac;
                PlayerSettings.SetIcons(NamedBuildTarget.Standalone, list, IconKind.Any);
            }

            foreach (var kind in PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.Android))
            {
                var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
                bool adaptive = kind.ToString().Contains("Adaptive");
                foreach (var pi in icons)
                {
                    if (adaptive) pi.SetTextures(icon, fg); // layer 0 = background, layer 1 = foreground
                    else pi.SetTextures(icon);
                }
                PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, icons);
            }
            Debug.Log("[Jogging] App-Icon gesetzt (macOS, iOS, Android)");
        }

        // Icons are read at full quality: no compression, no mipmaps, no downscaling (alpha kept for the Mac tile).
        private static Texture2D Prepare(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) { AssetDatabase.ImportAsset(path); imp = AssetImporter.GetAtPath(path) as TextureImporter; }
            if (imp == null) return null;
            bool changed = imp.textureType != TextureImporterType.Default || imp.mipmapEnabled
                           || imp.textureCompression != TextureImporterCompression.Uncompressed || imp.maxTextureSize < 1024
                           || imp.npotScale != TextureImporterNPOTScale.None;
            if (changed)
            {
                imp.textureType = TextureImporterType.Default;
                imp.mipmapEnabled = false;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.maxTextureSize = 1024;
                imp.npotScale = TextureImporterNPOTScale.None;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
