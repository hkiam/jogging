#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace Jogging.EditorTools
{
    /// <summary>
    /// iPad build: the Bluetooth permission text (without it iOS ends the app on the first Bluetooth
    /// access) and a Swift version for Plugins/iOS/JoggingBle.swift in the generated Xcode project.
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
            }
            proj.WriteToFile(projPath);
        }
    }
}
#endif
