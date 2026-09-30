using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;

namespace Jogging.EditorTools
{
    /// <summary>
    /// Builds the standalone macOS app (Builds/macOS/Jogging.app) with the BLE bridge embedded
    /// (Contents/Resources/JoggingBleScan.app — installed to ~/Applications on first run), then
    /// re-signs ad-hoc (modifying a signed bundle would otherwise stop it from launching).
    /// The run scene is (re)configured by <see cref="PhotoRunSceneBuilder"/> first.
    /// Menu: Jogging → Build → macOS App. Headless:
    ///   Unity -batchmode -quit -buildTarget OSXUniversal -executeMethod Jogging.EditorTools.BuildTools.BuildMacBatch
    /// </summary>
    public static class BuildTools
    {
        public const string MacOut = "Builds/macOS/Jogging.app";
        private const string BridgeSrc = "Tools/MacBleBridge";

        [MenuItem("Jogging/Build/macOS App")]
        public static void BuildMacMenu()
        {
            PhotoRunSceneBuilder.Build();
            bool ok = BuildMac();
            EditorUtility.DisplayDialog("Jogging",
                ok ? $"macOS-App gebaut:\n{Path.GetFullPath(MacOut)}" : "Build fehlgeschlagen – siehe Console.", "OK");
            if (ok) EditorUtility.RevealInFinder(MacOut);
        }

        /// <summary>Headless: configures the run scene, then builds the app.</summary>
        public static void BuildMacBatch()
        {
            PhotoRunSceneBuilder.Build();
            bool ok = BuildMac();
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        public static bool BuildMac() => BuildMac(PhotoRunSceneBuilder.ScenePath, MacOut);

        public static bool BuildMac(string scenePath, string outPath)
        {
            PlayerSettings.productName = "Jogging";
            // URP + the imported packs are authored for Linear; Gamma made lit textures garish.
            if (PlayerSettings.colorSpace != ColorSpace.Linear) PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true; // keep receiving belt data when not focused
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP); // native code: MapMagic's terrain (erosion) runs several times faster than on Mono

            if (!FixUrpGlobalSettings()) return false;
            AppIcon.Apply();

            // Rebuild the bridge only when its source changed: every rebuild gets a new ad-hoc signature,
            // and macOS then asks for the Bluetooth permission again.
            string bridgeBin = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal) + "/Applications/JoggingBleScan.app/Contents/MacOS/JoggingBleBridge";
            string bridgeSrcFile = BridgeSrc + "/main.swift";
            if (!System.IO.File.Exists(bridgeBin) || System.IO.File.GetLastWriteTimeUtc(bridgeSrcFile) > System.IO.File.GetLastWriteTimeUtc(bridgeBin)
                || System.IO.File.GetLastWriteTimeUtc(BridgeSrc + "/build.sh") > System.IO.File.GetLastWriteTimeUtc(bridgeBin))
                RunTool("/bin/bash", $"\"{BridgeSrc}/build.sh\"");
            else Debug.Log("[Jogging] BLE-Bridge unverändert – nicht neu gebaut (keine neue Bluetooth-Freigabe nötig)");

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scenePath },
                locationPathName = outPath,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None,
            });
            var sum = report.summary;
            Debug.Log($"[Jogging] macOS-Build: {sum.result}, {sum.totalSize / (1024 * 1024)} MB, {sum.totalTime.TotalSeconds:0} s, Fehler {sum.totalErrors}");
            if (sum.result != BuildResult.Succeeded) return false;

            // Embed the bridge and re-sign the whole app ad-hoc.
            string bridge = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal),
                                         "Applications/JoggingBleScan.app");
            string dest = Path.Combine(outPath, "Contents/Resources/JoggingBleScan.app");
            if (Directory.Exists(bridge))
            {
                if (Directory.Exists(dest)) Directory.Delete(dest, true);
                RunTool("/bin/cp", $"-R \"{bridge}\" \"{dest}\"");
                Debug.Log("[Jogging] BLE-Bridge eingebettet.");
            }
            else Debug.LogWarning("[Jogging] BLE-Bridge nicht gefunden – App ohne Bridge gebaut.");
            RunTool("/usr/bin/codesign", $"--force --deep --sign - \"{outPath}\"");
            return Directory.Exists(outPath);
        }

        public const string WinOut = "Builds/Windows/Jogging.exe";
        private const string WinBridgeSrc = "Tools/WinBleBridge";

        [MenuItem("Jogging/Build/Windows App")]
        public static void BuildWindowsMenu()
        {
            PhotoRunSceneBuilder.Build();
            bool ok = BuildWindows();
            EditorUtility.DisplayDialog("Jogging", ok ? $"Windows-App gebaut:\n{Path.GetFullPath(WinOut)}" : "Build fehlgeschlagen – siehe Console.", "OK");
        }

        /// <summary>Headless: configures the run scene, then builds the Windows app (64-bit).</summary>
        public static void BuildWindowsBatch()
        {
            PhotoRunSceneBuilder.Build();
            bool ok = BuildWindows();
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>
        /// Windows 64-bit, built on the Mac: Mono (IL2CPP for Windows needs a Windows machine). Next to
        /// Jogging.exe the Bluetooth/speech helper JoggingBleBridge.exe (Tools/WinBleBridge, .NET, built here
        /// when its source is newer than the last build).
        /// </summary>
        public static bool BuildWindows()
        {
            PlayerSettings.productName = "Jogging";
            if (PlayerSettings.colorSpace != ColorSpace.Linear) PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            if (!FixUrpGlobalSettings()) return false;
            AppIcon.Apply();

            string bridgeExe = WinBridgeSrc + "/bin/publish/JoggingBleBridge.exe";
            bool stale = !File.Exists(bridgeExe);
            if (!stale)
                foreach (var f in Directory.GetFiles(WinBridgeSrc, "*.*", SearchOption.TopDirectoryOnly))
                    if (File.GetLastWriteTimeUtc(f) > File.GetLastWriteTimeUtc(bridgeExe)) stale = true;
            if (stale) RunTool("/bin/bash", $"\"{WinBridgeSrc}/build.sh\"");

            string dir = Path.GetDirectoryName(WinOut);
            if (Directory.Exists(dir)) Directory.Delete(dir, true); // no leftovers of an older build
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { PhotoRunSceneBuilder.ScenePath },
                locationPathName = WinOut,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            var sum = report.summary;
            Debug.Log($"[Jogging] Windows-Build: {sum.result}, {sum.totalSize / (1024 * 1024)} MB, {sum.totalTime.TotalSeconds:0} s, Fehler {sum.totalErrors}");
            if (sum.result != BuildResult.Succeeded) return false;
            if (File.Exists(bridgeExe)) { File.Copy(bridgeExe, Path.Combine(dir, "JoggingBleBridge.exe"), true); Debug.Log("[Jogging] Windows-Bridge beigelegt."); }
            else Debug.LogWarning("[Jogging] Windows-Bridge fehlt (Tools/WinBleBridge/build.sh, .NET-SDK) – App ohne Bluetooth gebaut.");
            // the player's debugging leftovers are not needed by users
            foreach (var d in Directory.GetDirectories(dir, "*_BurstDebugInformation_DoNotShip")) Directory.Delete(d, true);
            return File.Exists(WinOut);
        }

        // The URP global settings asset was last saved by Unity 6.6 (asset version 11); the 6.3 URP
        // only knows version 10, so the build validator rejects it ("is not at last version").
        // The data is compatible — only the version stamp is newer — so re-stamp it.
        [MenuItem("Jogging/Build/Fix URP Global Settings")]
        public static bool FixUrpGlobalSettings()
        {
            const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;
            // The settings class is internal in this URP version → reach it by reflection only.
            var t = typeof(UniversalRenderPipeline).Assembly
                .GetType("UnityEngine.Rendering.Universal.UniversalRenderPipelineGlobalSettings");
            if (t == null) { Debug.LogWarning("[Jogging] URP Global Settings Typ nicht gefunden – übersprungen."); return true; }
            int last = (int)t.GetField("k_LastVersion", F).GetValue(null);
            RenderPipelineGlobalSettings current = GraphicsSettings.GetSettingsForRenderPipeline<UniversalRenderPipeline>();
            if (current != null)
            {
                int ver = (int)t.GetField("m_AssetVersion", F).GetValue(current);
                if (ver <= last) return true; // fine (older ones are upgraded by URP itself)
                // Don't delete/recreate: Ensure() would adopt some other package's settings asset
                // (e.g. a demo with Compatibility Mode on). Just mark it as the version this URP
                // knows; fields only 6.6 understands are ignored (harmless "missing type" warnings).
                t.GetField("m_AssetVersion", F).SetValue(current, last);
                EditorUtility.SetDirty(current);
                AssetDatabase.SaveAssets();
                Debug.Log($"[Jogging] URP Global Settings v{ver} (Unity 6.6) → v{last} gesetzt: {AssetDatabase.GetAssetPath(current)}");
                return true;
            }
            Debug.LogWarning("[Jogging] Keine URP Global Settings gefunden.");
            return true;
        }

        private static void RunTool(string exe, string args)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Directory.GetCurrentDirectory(),
            };
            using var p = Process.Start(psi);
            string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(120000);
            Debug.Log($"[Jogging] {Path.GetFileName(exe)} → exit {p.ExitCode} {o.Trim()}");
        }
    }
}
