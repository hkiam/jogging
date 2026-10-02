#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace Jogging.EditorTools
{
    /// <summary>
    /// iPad build: the Bluetooth permission text (without it iOS ends the app on the first Bluetooth
    /// access), a Swift version for Plugins/iOS/*.swift in the generated Xcode project, and Apple Health
    /// (HealthKit capability + permission texts; Plugins/iOS/JoggingHealth.swift).
    /// </summary>
    public static class IosPostBuild
    {
        [PostProcessBuild(100)]
        public static void OnPostprocessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;

            string plistPath = Path.Combine(path, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetString("NSBluetoothAlwaysUsageDescription",
                "Jogging verbindet sich mit deinem Laufband und deinem Pulsgurt.");
            // Apple Health: runs are written (never read) – Statistik → Export
            plist.root.SetString("NSHealthUpdateUsageDescription",
                "Jogging trägt deine Läufe als Training in Apple Health ein: Dauer, Strecke, Energie, Puls und Höhenmeter.");
            plist.root.SetString("NSHealthShareUsageDescription",
                "Jogging liest keine Gesundheitsdaten, es trägt nur deine Läufe ein.");
            plist.root.SetBoolean("UIFileSharingEnabled", true);          // exchange folder visible in the Files app
            plist.root.SetBoolean("LSSupportsOpeningDocumentsInPlace", true);
            plist.WriteToFile(plistPath);

            string projPath = PBXProject.GetPBXProjectPath(path);
            var proj = new PBXProject();
            proj.ReadFromFile(projPath);
            foreach (var guid in new[] { proj.GetUnityFrameworkTargetGuid(), proj.GetUnityMainTargetGuid() })
            {
                proj.SetBuildProperty(guid, "SWIFT_VERSION", "5.0");
                proj.AddFrameworkToProject(guid, "CoreBluetooth.framework", false);
                proj.AddFrameworkToProject(guid, "HealthKit.framework", false);
            }
            proj.WriteToFile(projPath);

            // the HealthKit capability (entitlement) on the app target
            var caps = new ProjectCapabilityManager(projPath, "Unity-iPhone/Jogging.entitlements", null, proj.GetUnityMainTargetGuid());
            caps.AddHealthKit();
            caps.WriteToFile();
        }
    }
}
#endif
