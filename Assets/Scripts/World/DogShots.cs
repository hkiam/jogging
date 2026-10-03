using System.Collections;
using System.IO;
using UnityEngine;
using Jogging.Core;
using Jogging.Locomotion.Treadmill;

namespace Jogging.World
{
    /// <summary>
    /// Test pictures of the companion dog (<c>-dogshots &lt;folder&gt; -beltsim -dog germanshepherd</c>): starts a run on
    /// the route behind the menu with the simulated belt and takes the app's own picture every 3 s for a minute –
    /// alternately from the usual view and from the side – logging what the dog does. Quits when done.
    /// </summary>
    [DefaultExecutionOrder(20000)] // after the camera rig: the side view wins
    public class DogShots : MonoBehaviour
    {
        private static DogShots instance;
        private string dir;
        private bool side;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            var a = Args.All;
            for (int i = 0; i < a.Length - 1; i++)
                if (a[i] == "-dogshots" && instance == null)
                {
                    instance = new GameObject("DogShots").AddComponent<DogShots>();
                    instance.dir = a[i + 1];
                    DontDestroyOnLoad(instance.gameObject);
                }
        }

        private static FitShowEmulator Belt => FindFirstObjectByType<MacBleBridgeTransport>()?.Simulator;

        private IEnumerator Start()
        {
            Directory.CreateDirectory(dir);
            MacBleBridgeTransport.Offline = true;
            var st = AppSettings.Current; st.announcements = false; st.beltIncline = false; st.beltSpeed = false;
            yield return new WaitForSecondsRealtime(3f);
            RouteRuntime.Selected = RouteRuntime.Current;
            Jogging.UI.StartMenuUI.SkipNextOpen();
            SceneReload.Now();
            yield return null; yield return null;
            float until = Time.realtimeSinceStartup + 90f;
            while (Time.realtimeSinceStartup < until && (Jogging.UI.RunSessionUI.Session == null || FindFirstObjectByType<TrackManager>() == null || FindFirstObjectByType<TrackManager>().WaitingForTerrain)) yield return null;
            until = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < until && (Belt == null || !FindFirstObjectByType<MacBleBridgeTransport>().IsConnected)) yield return null;
            if (Belt == null) { Debug.LogWarning("[DogShots] kein Band-Simulator (-beltsim?)"); Application.Quit(); yield break; }
            Belt.PressStartStop(); Belt.PressSpeed(4f);
            yield return new WaitForSecondsRealtime(5f);
            Belt.PressSpeed(4f);
            yield return new WaitForSecondsRealtime(4f);
            for (int i = 0; i < 20; i++)
            {
                side = i % 2 == 1;
                yield return new WaitForSecondsRealtime(side ? 0.2f : 2.8f);
                yield return new WaitForEndOfFrame();
                var d = DogCompanion.Current;
                Debug.Log($"[DogShots] {i:00}: {(d != null ? $"{d.State}, {d.Gap:0.0} m, {d.Playing}" : "kein Hund")}");
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"hund-{i:00}{(side ? "-seite" : "")}.png"));
                yield return null;
            }
            side = false;
            // the finish screen too (two columns, nothing may run over)
            Jogging.UI.RunSessionUI.Session?.Finish();
            yield return new WaitForSecondsRealtime(4f);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "ziel.png"));
            yield return new WaitForSecondsRealtime(1f);
            Debug.Log("[DogShots] fertig: " + dir);
            Application.Quit();
        }

        private void LateUpdate()
        {
            var d = DogCompanion.Current; var cam = Camera.main; var p = FindFirstObjectByType<RealPlayerFigure>();
            if (!side || d == null || cam == null || p == null) return;
            Vector3 mid = (d.transform.position + p.transform.position) * 0.5f + Vector3.up * 0.6f;
            Vector3 right = Vector3.Cross(Vector3.up, p.transform.forward).normalized;
            Vector3 eye = mid + right * 7f + Vector3.up * 1.2f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(mid - eye));
        }
    }
}
