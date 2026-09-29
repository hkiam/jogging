using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Jogging.Core;
using Jogging.Locomotion.Treadmill;

namespace Jogging.World
{
    /// <summary>
    /// Test pictures of the wayside (<c>-waysideshots &lt;folder&gt; -beltsim -timescale 6</c>): runs the route
    /// with the simulated treadmill at 8 km/h (as the E2E does), and at the first object of every kind in the
    /// plan looks at it (in passing: stopping time would also stop the terrain streaming) from the trail at eye height and renders it with a camera of its own into a picture file (the app's
    /// own picture (ScreenCapture). Also a stream bridge and the path edge. Quits when done.
    /// (Jumping ahead with -starts doesn't work for this: the trail bed is only shaped once the start tile
    /// is known.)
    /// </summary>
    [DefaultExecutionOrder(20000)]
    public class WaysideShots : MonoBehaviour
    {
        private static WaysideShots instance;
        private string dir;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            var a = Args.All;
            for (int i = 0; i < a.Length - 1; i++)
                if (a[i] == "-waysideshots" && instance == null)
                {
                    instance = new GameObject("WaysideShots").AddComponent<WaysideShots>();
                    instance.dir = a[i + 1];
                    DontDestroyOnLoad(instance.gameObject);
                }
        }

        private static FitShowEmulator Belt => FindFirstObjectByType<MacBleBridgeTransport>()?.Simulator;

        private IEnumerator Start()
        {
            Directory.CreateDirectory(dir);
            MacBleBridgeTransport.Offline = true;
            var st = AppSettings.Current; st.announcements = false; st.ambience = false; st.beltIncline = false; st.beltSpeed = false;
            yield return new WaitForSecondsRealtime(3f);
            // start a run on this route the way the menu does
            RouteRuntime.Selected = RouteRuntime.Current;
            Jogging.UI.StartMenuUI.SkipNextOpen();
            SceneReload.Now();
            yield return null; yield return null;
            float until = Time.realtimeSinceStartup + 90f;
            while (Time.realtimeSinceStartup < until && (Jogging.UI.RunSessionUI.Session == null || FindFirstObjectByType<TrackManager>() == null || FindFirstObjectByType<TrackManager>().WaitingForTerrain)) yield return null;
            until = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < until && (Belt == null || !FindFirstObjectByType<MacBleBridgeTransport>().IsConnected)) yield return null;
            if (Belt == null) { Debug.LogWarning("[WaysideShots] kein Band-Simulator (-beltsim?)"); Application.Quit(); yield break; }
            Belt.PressStartStop(); Belt.PressSpeed(4f);
            yield return new WaitForSecondsRealtime(6f);
            Belt.PressSpeed(4f);

            var path = TrailPath.Active;
            var targets = new List<(string name, float s, Vector3 at, Wayside.Kind? kind)>();
            var seen = new HashSet<Wayside.Kind>();
            foreach (var f in Wayside.Plan)
            {
                if (!seen.Add(f.kind)) continue;
                Vector3 at = f.line != null && f.line.Count > 6 ? f.line[6].X0Z() : path.Offset(f.s, f.side * Mathf.Max(f.lateral, 3f));
                if (f.kind == Wayside.Kind.TreeMark || f.kind == Wayside.Kind.NestBox) at = path.Offset(f.s, f.side * 5f);
                targets.Add((f.kind.ToString(), f.s, at, f.kind));
            }
            foreach (var s in Lakes.Streams) { targets.Add(("Bruecke", s.s, path.Point(s.s), null)); break; }
            targets.Add(("Wegrand", 700f, path.Offset(715f, 2.5f), null));
            targets.Sort((a, b) => a.s.CompareTo(b.s));
            Debug.Log($"[WaysideShots] {targets.Count} Ziele");

            int n = 0;
            foreach (var t in targets)
            {
                bool big = t.name == "PowerLine" || t.name == "Village" || t.name == "Chapel" || t.name == "FieldBarn" || t.name == "HuntingStand" || t.name == "HayBales" || t.name == "Ruin" || t.name == "TreeGuards" || t.name == "Crows";
                float back = big ? 30f : 9f;
                float stop = t.s - back;
                until = Time.realtimeSinceStartup + 600f;
                while (path.RunnerS < stop && Time.realtimeSinceStartup < until) yield return null;
                if (path.RunnerS > stop + 60f) { Debug.Log($"[WaysideShots] {t.name}: verpasst (Läufer bei {path.RunnerS:0} m)"); continue; }
                // no pause: stopping time would stop the terrain streaming too; objects are built ~480 m ahead
                Debug.Log($"[WaysideShots] {t.name} bei {t.s:0} m: {(t.kind.HasValue && !Wayside.IsBuilt(t.kind.Value) ? "nicht gebaut" : "da")}");
                var cam = Camera.main;
                if (cam != null)
                {
                    Vector3 eye = path.Point(path.RunnerS);
                    if (TerrainGround.TryHeight(eye.x, eye.z, out float he)) eye.y = he + 1.75f;
                    Vector3 target = t.at;
                    if (TerrainGround.TryHeight(target.x, target.z, out float h)) target.y = h + (t.name == "NestBox" ? 3f : t.name == "TreeMark" ? 1.7f : t.name == "PowerLine" ? 5f : 0.8f);
                    Shoot(cam, eye, Quaternion.LookRotation((target - eye).normalized, Vector3.up), Path.Combine(dir, $"{++n:00}-{t.name}.png"));
                    yield return null;
                }
            }
            Debug.Log("[WaysideShots] fertig");
            Application.Quit();
        }

        // A camera of our own (a copy of the main one) renders the view into a texture: independent of
        // whatever steers the main camera, and without the menu or HUD
        private static void Shoot(Camera main, Vector3 pos, Quaternion rot, string file)
        {
            const int w = 1600, h = 1000;
            var go = new GameObject("ShotCam");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(main);
            cam.cullingMask = main.cullingMask & ~(1 << 5); // no UI layer
            go.transform.SetPositionAndRotation(pos, rot);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
            if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, req)) UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Destroy(tex); rt.Release(); Destroy(rt); Destroy(go);
        }
    }
}
