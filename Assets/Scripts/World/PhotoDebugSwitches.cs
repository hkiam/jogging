using UnityEngine;
using UnityEngine.Rendering;

namespace Jogging.World
{
    /// <summary>
    /// Command-line switches for tests and diagnosis in a built player: -timescale &lt;n&gt; (the run n×
    /// faster once it moves) · -camhigh (look down on the trail) · -diag (a status line every 3 s) ·
    /// rendering: -bm &lt;dist&gt; terrain basemap distance · -nopost · -nofog · -top (camera high up) ·
    /// -sun &lt;intensity&gt;. Launch: open App.app --args -bm 0
    /// </summary>
    public class PhotoDebugSwitches : MonoBehaviour
    {
        private float bm = -1f;
        private bool top, diag;
        private float timeScale = 1f; // tests: run faster once the run moves
        private float nextDiag;

        private void Start()
        {
            var a = Jogging.Core.Args.All;
            for (int i = 0; i < a.Length; i++)
            {
                switch (a[i])
                {
                    case "-bm": if (i + 1 < a.Length) float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out bm); break;
                    case "-nopost": foreach (var v in FindObjectsByType<Volume>(FindObjectsSortMode.None)) v.enabled = false; break;
                    case "-nofog": RenderSettings.fog = false; break;
                    case "-top": top = true; break;
                    case "-camhigh": // tests: look down on the trail from above and behind
                        var fc = Camera.main != null ? Camera.main.GetComponent<Jogging.CameraRig.FollowCamera>() : null;
                        if (fc != null) { fc.Offset = new Vector3(4f, 13f, -20f); fc.LookHeight = 0f; }
                        break;
                    case "-diag": diag = true; break;
                    case "-timescale": if (i + 1 < a.Length) float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out timeScale); break;
                    case "-sun":
                        if (i + 1 < a.Length && RenderSettings.sun != null && float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s))
                            RenderSettings.sun.intensity = s;
                        break;
                }
            }
            Debug.Log($"[PhotoDebug] args: {string.Join(" ", a)}");
        }

        private void Update()
        {
            if (timeScale > 1f && !Jogging.UI.StartMenuUI.IsOpen)
            {
                var tm = FindFirstObjectByType<TrackManager>();
                if (tm != null && !tm.WaitingForTerrain && !tm.IsFinished) Time.timeScale = timeScale;
            }
            if (diag && Time.unscaledTime > nextDiag)
            {
                nextDiag = Time.unscaledTime + 3f;
                var tm = FindFirstObjectByType<TrackManager>();
                var r = GameObject.Find("Runner");
                string g = r != null && TerrainGround.TryHeight(r.transform.position.x, r.transform.position.z, out float h) ? h.ToString("0.0") : "-";
                Debug.Log($"[PhotoDiag] Strecke {(tm != null ? tm.DistanceTraveled : -1):0} m · Steigung {(tm != null ? tm.CurrentInclinePercent : 0):0.0} % · Boden {g} · Kacheln {TerrainGround.Tiles(out _)}");
            }
            if (bm >= 0f) foreach (var t in Terrain.activeTerrains) t.basemapDistance = bm;
            if (top && Camera.main != null)
            {
                foreach (var mb in Camera.main.GetComponents<MonoBehaviour>())
                    if (mb.GetType().Name == "FollowCamera") mb.enabled = false;
                Camera.main.transform.SetPositionAndRotation(new Vector3(0f, 180f, -150f), Quaternion.Euler(35f, 0f, 0f));
            }
        }
    }
}
