using System;

namespace Jogging.Core
{
    /// <summary>
    /// The start options (-e2e, -beltsim, -datadir …). On macOS and Android they come from the command
    /// line (Android: intent extra "unity"); Unity's iOS player passes none on, so there the environment
    /// variable JOGGING_ARGS is read too (the simulator sets it: SIMCTL_CHILD_JOGGING_ARGS=… simctl launch).
    /// </summary>
    public static class Args
    {
        private static string[] all;

        public static string[] All
        {
            get
            {
                if (all != null) return all;
                var list = new System.Collections.Generic.List<string>(Environment.GetCommandLineArgs());
                var env = Environment.GetEnvironmentVariable("JOGGING_ARGS");
                if (!string.IsNullOrWhiteSpace(env))
                    list.AddRange(env.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
                return all = list.ToArray();
            }
        }

        public static bool Has(string name) => Array.IndexOf(All, name) >= 0;
    }
}
