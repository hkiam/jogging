using System.Collections;
using System.IO;
using UnityEngine;

namespace Jogging.World
{
    /// <summary>
    /// Test pictures of the sky (<c>-skyshots &lt;folder&gt;</c>): waits for the landscape, hides the menus
    /// and photographs the same place at several hours, weathers and nights — once along the trail and
    /// once towards the sun or moon. Only the app's own picture (ScreenCapture), then the app quits.
    /// </summary>
    [DefaultExecutionOrder(20000)] // after the camera rig: the view turns for the second picture
    public class SkyShots : MonoBehaviour
    {
        private string dir;
        private Quaternion? look;
        private float lift;
        private Vector3 liftFrom;
        private Transform closeUp; // -animaltest: the camera follows this animal

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            var a = Jogging.Core.Args.All;
            for (int i = 0; i < a.Length - 1; i++)
                if (a[i] == "-skyshots" && FindFirstObjectByType<SkyShots>() == null)
                    new GameObject("SkyShots").AddComponent<SkyShots>().dir = a[i + 1];
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(dir);
            yield return new WaitForSecondsRealtime(28f);
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) c.enabled = false;
            var sky = Sky.Instance;
            if (sky == null) { Debug.LogWarning("[SkyShots] kein Himmel"); Application.Quit(); yield break; }
            int seed = RouteRuntime.Current != null ? RouteRuntime.Current.generator.seed : 7;
            var own = RouteRuntime.Current.@params; // -skyown: only the route's own mood (README pictures)
            var shots = Jogging.Core.Args.Has("-skyown")
                ? new (string name, float h, string wx, string season, float lp, double moon)[] { ("route", own.timeOfDay, own.weather, own.season, own.lightPollution, own.moonAge >= 0f ? (double)own.moonAge : Sky.MoonAge(System.DateTime.UtcNow)) }
                : new (string name, float h, string wx, string season, float lp, double moon)[]
            {
                ("01-morgen", 6.4f, "clear", "summer", 0.3f, 10),
                ("02-vormittag", 10f, "clear", "summer", 0.3f, 10),
                ("03-mittag-bewoelkt", 13f, "cloudy", "summer", 0.3f, 10),
                ("04-regen", 15f, "rain", "autumn", 0.3f, 10),
                ("05-abendsonne", 20.3f, "clear", "summer", 0.3f, 10),
                ("06-sonnenuntergang", 21.4f, "clear", "summer", 0.3f, 10),
                ("07-daemmerung", 17.6f, "clear", "winter", 0.3f, 5),
                ("08-nacht-dunkel-neumond", 21f, "clear", "winter", 0.03f, 0.5),
                ("09-nacht-ortsnah", 21f, "clear", "winter", 0.8f, 0.5),
                ("10-nacht-vollmond", 21f, "clear", "winter", 0.2f, 14.8),
                ("11-nacht-halbmond-wolken", 21f, "cloudy", "winter", 0.3f, 7.4),
                ("12-tagmond", 16f, "clear", "autumn", 0.3f, 20),
                ("13-wolkenschatten", 11f, "cloudy", "summer", 0.3f, 10),
                ("14-morgennebel", 8.0f, "clear", "autumn", 0.3f, 10),
                ("15-abendnebel", 19.6f, "cloudy", "autumn", 0.3f, 10),
            };
            var cam = Camera.main;
            foreach (var s in shots)
            {
                sky.Preview(s.h, s.wx, s.season, s.lp, s.moon, seed);
                look = null; lift = 0f;
                yield return new WaitForSecondsRealtime(s.name.Contains("nebel") ? 14f : 1.2f); // mist patches fade in
                yield return Capture(s.name + "-a");
                // second view: towards the sun (by day) or the moon (at night), a little above the horizon
                Vector3 toward = sky.SunElevation > -4f ? -RenderSettings.sun.transform.forward : MoonOrUp(sky);
                var flat = new Vector3(toward.x, 0f, toward.z);
                if (flat.sqrMagnitude < 1e-4f) flat = cam != null ? cam.transform.forward : Vector3.forward;
                float pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(toward.y, -1f, 1f)) * Mathf.Rad2Deg, 8f, 55f);
                look = Quaternion.LookRotation(flat.normalized) * Quaternion.Euler(-pitch * 0.85f, 0f, 0f);
                if (s.name.Contains("schatten") || s.name.Contains("morgennebel")) { liftFrom = cam != null ? cam.transform.position : Vector3.zero; lift = 260f; look = Quaternion.LookRotation(cam != null ? cam.transform.forward : Vector3.forward) * Quaternion.Euler(28f, 0f, 0f); } // from above: the patches on the land
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Capture(s.name + "-b");
            }
            // -animaltest: every kind once from the side, close up (colour, size, which way it faces)
            var animals = Jogging.Core.Args.Has("-animaltest") ? FindFirstObjectByType<Animals>() : null;
            if (animals != null)
            {
                var done = new System.Collections.Generic.HashSet<Animals.Kind>();
                foreach (var (kind, t) in new System.Collections.Generic.List<(Animals.Kind, Transform)>(animals.Standing()))
                {
                    if (t == null || !done.Add(kind)) continue;
                    closeUp = t;
                    yield return new WaitForSecondsRealtime(0.6f);
                    yield return Capture("tier-" + kind.ToString().ToLowerInvariant());
                }
                closeUp = null;
            }
            Debug.Log("[SkyShots] fertig: " + dir);
            Application.Quit();
        }

        private static Vector3 MoonOrUp(Sky sky)
        {
            var l = -RenderSettings.sun.transform.forward; // at night the light is the moon when it is up
            return l.y > 0.05f ? l : new Vector3(0.3f, 0.6f, 0.7f).normalized;
        }

        private IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
            yield return null;
        }

        private void LateUpdate()
        {
            if (look.HasValue && Camera.main != null) Camera.main.transform.rotation = look.Value;
            if (lift > 0f && Camera.main != null) Camera.main.transform.position = liftFrom + Vector3.up * lift;
            if (closeUp != null && Camera.main != null)
            {
                var b = new Bounds(closeUp.position, Vector3.zero);
                foreach (var r in closeUp.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                float size = Mathf.Max(b.size.x, b.size.y, b.size.z);
                Vector3 eye = b.center + closeUp.right * size * 1.6f + closeUp.forward * size * 0.5f + Vector3.up * size * 0.25f;
                Camera.main.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(b.center - eye));
            }
        }
    }
}
