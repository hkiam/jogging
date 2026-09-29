using System.Collections;
using System.IO;
using UnityEngine;

namespace Jogging.UI
{
    /// <summary>
    /// Pictures of the menu pages (<c>-pageshots &lt;folder&gt;</c>, for the README): waits for the landscape behind
    /// the menu, opens "Who's running?", the route editor and the settings, and saves the app's own picture of
    /// each (ScreenCapture). Quits when done.
    /// </summary>
    public class PageShots : MonoBehaviour
    {
        private string dir;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            var a = Jogging.Core.Args.All;
            for (int i = 0; i < a.Length - 1; i++)
                if (a[i] == "-pageshots" && FindFirstObjectByType<PageShots>() == null)
                    new GameObject("PageShots").AddComponent<PageShots>().dir = a[i + 1];
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(dir);
            yield return new WaitForSecondsRealtime(25f);
            foreach (var pg in new[] { "main", "editor", "settings", "profile" })
            {
                StartMenuUI.OpenPageForTest(pg);
                yield return new WaitForSecondsRealtime(1.5f);
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"page-{pg}.png"));
                yield return new WaitForSecondsRealtime(0.5f);
            }
            Application.Quit();
        }
    }
}
