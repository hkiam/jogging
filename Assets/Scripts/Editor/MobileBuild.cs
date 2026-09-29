using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Jogging.EditorTools
{
    /// <summary>
    /// iPad and Android builds (landscape, IL2CPP, touch control; no treadmill Bluetooth yet).
    /// Headless — start Unity directly in the target so it doesn't switch platforms in-process:
    ///   Unity -batchmode -quit -buildTarget iOS     -executeMethod Jogging.EditorTools.MobileBuild.BuildIosSimulatorBatch
    ///   Unity -batchmode -quit -buildTarget Android -executeMethod Jogging.EditorTools.MobileBuild.BuildAndroidBatch
    /// iOS gives an Xcode project for the simulator (Builds/iOS); Tools/ios-sim.sh builds and starts it.
    /// Android gives Builds/Android/Jogging.apk (arm64); Tools/android-emu.sh installs and starts it.
    /// </summary>
    public static class MobileBuild
    {
        public const string IosOut = "Builds/iOS", IosDeviceOut = "Builds/iOS-Device";
        public const string AndroidOut = "Builds/Android/Jogging.apk";
        public const string BundleId = "local.jogging.app";

        [MenuItem("Jogging/Build/iPad (Simulator)")]
        public static void BuildIosSimulatorMenu() { PhotoRunSceneBuilder.Build(); BuildIos(simulator: true); }

        [MenuItem("Jogging/Build/Android (APK)")]
        public static void BuildAndroidMenu() { PhotoRunSceneBuilder.Build(); BuildAndroid(); }

        public static void BuildIosSimulatorBatch()
        {
            PhotoRunSceneBuilder.Build();
            bool ok = BuildIos(simulator: true);
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>For a real iPad (Tools/ios-device.sh signs, installs and starts it): Builds/iOS-Device.</summary>
        public static void BuildIosDeviceBatch()
        {
            PhotoRunSceneBuilder.Build();
            bool ok = BuildIos(simulator: false);
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        public static void BuildAndroidBatch()
        {
            PhotoRunSceneBuilder.Build();
            bool ok = BuildAndroid();
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        private static void Common()
        {
            PlayerSettings.productName = "Jogging";
            if (PlayerSettings.colorSpace != ColorSpace.Linear) PlayerSettings.colorSpace = ColorSpace.Linear;
            // Landscape only: the view along the trail and the HUD are laid out for it.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.runInBackground = false;
            PlayerSettings.enableFrameTimingStats = true; // -perf: CPU/GPU time per frame on the device
            BuildTools.FixUrpGlobalSettings();
            ExcludeDesktopPlugins();
            AppIcon.Apply();
        }

        // MapMagic's native erosion plugins (Windows/macOS) are marked "any platform" and collide in a
        // mobile build (two NativePlugins.lib). Phones and tablets use the managed erosion anyway.
        private static void ExcludeDesktopPlugins()
        {
            foreach (var guid in AssetDatabase.FindAssets("NativePlugins", new[] { "Assets/MapMagic/Tools/Plugins" }))
            {
                var imp = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as PluginImporter;
                if (imp == null) continue;
                bool changed = false;
                if (imp.GetCompatibleWithAnyPlatform())
                {
                    imp.SetCompatibleWithAnyPlatform(false);
                    imp.SetCompatibleWithEditor(true);
                    imp.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX, true);
                    imp.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
                    changed = true;
                }
                foreach (var t in new[] { BuildTarget.Android, BuildTarget.iOS })
                    if (imp.GetCompatibleWithPlatform(t)) { imp.SetCompatibleWithPlatform(t, false); changed = true; }
                if (changed) { imp.SaveAndReimport(); Debug.Log($"[Jogging] {imp.assetPath}: nicht für iOS/Android"); }
            }
        }

        public static bool BuildIos(bool simulator)
        {
            Common();
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BundleId);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPadOnly;
            PlayerSettings.iOS.targetOSVersionString = "16.0";
            PlayerSettings.iOS.sdkVersion = simulator ? iOSSdkVersion.SimulatorSDK : iOSSdkVersion.DeviceSDK;
            PlayerSettings.iOS.appleEnableAutomaticSigning = !simulator;
            string outDir = simulator ? IosOut : IosDeviceOut;
            Directory.CreateDirectory(outDir);
            return Report(BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { PhotoRunSceneBuilder.ScenePath },
                locationPathName = outDir,
                target = BuildTarget.iOS,
                options = BuildOptions.None,
            }), "iOS");
        }

        public static bool BuildAndroid()
        {
            Common();
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, BundleId);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64; // tablets and the Apple-silicon emulator
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            EditorUserBuildSettings.buildAppBundle = false; // an .apk to install directly
            Directory.CreateDirectory(Path.GetDirectoryName(AndroidOut));
            return Report(BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { PhotoRunSceneBuilder.ScenePath },
                locationPathName = AndroidOut,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            }), "Android");
        }

        private static bool Report(BuildReport report, string what)
        {
            var s = report.summary;
            Debug.Log($"[Jogging] {what}-Build: {s.result}, {s.totalSize / (1024 * 1024)} MB, {s.totalTime.TotalSeconds:0} s, Fehler {s.totalErrors}");
            return s.result == BuildResult.Succeeded;
        }
    }
}
