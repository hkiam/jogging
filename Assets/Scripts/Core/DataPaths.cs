using System.IO;
using UnityEngine;

namespace Jogging.Core
{
    /// <summary>
    /// Where the app keeps its data: <see cref="Application.persistentDataPath"/>, or — for tests —
    /// the folder given with <c>-datadir &lt;path&gt;</c> (the end-to-end run never touches real data,
    /// not even Downloads or the Desktop).
    /// </summary>
    public static class DataPaths
    {
        private static string root;

        public static string Root
        {
            get
            {
                if (root != null) return root;
                var a = Jogging.Core.Args.All;
                for (int i = 0; i < a.Length - 1; i++)
                    if (a[i] == "-datadir") { root = Path.GetFullPath(a[i + 1]); Directory.CreateDirectory(root); break; }
                return root ??= Application.persistentDataPath;
            }
        }

        public static bool IsTest => Root != Application.persistentDataPath;

        /// <summary>~/Downloads (exports, backups, shared routes) — in a test run a folder inside the test data.</summary>
        public static string Downloads => UserFolder("Downloads",
            Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Downloads"));

        /// <summary>What the exchange folder is called in texts: "Downloads" on the Mac, "Austausch" on
        /// iPad/Android (Files app → Jogging; on Android over USB: Android/data/local.jogging.app/files).</summary>
        public static string ExchangeName => Platform.IsMobile ? "Austausch" : "Downloads";
        /// <summary>Where files are looked for: "Downloads, Schreibtisch" on the Mac.</summary>
        public static string ExchangeNames => Platform.IsMobile ? "Austausch" : "Downloads, Schreibtisch";

        /// <summary>The Desktop (screenshots, shared routes) — in a test run a folder inside the test data.</summary>
        public static string Desktop => UserFolder("Desktop",
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory));

        private static string UserFolder(string name, string real)
        {
            // iPad/Android: no Downloads/Desktop — one exchange folder inside the app's data
            // (on the iPad visible in the Files app).
            if (Platform.IsMobile && !IsTest) { string m = Path.Combine(Root, "Austausch"); Directory.CreateDirectory(m); return m; }
            if (!IsTest) return real;
            string d = Path.Combine(Root, name);
            Directory.CreateDirectory(d);
            return d;
        }
    }
}
