using System.Collections.Generic;
using UnityEngine;

namespace Jogging.World
{
    /// <summary>
    /// Lights after dark: the runner's head torch (a spot from the forehead onto the trail ahead, fading in
    /// at dusk) and the street lamps of the village stretch (<see cref="Wayside"/>), which switch on when
    /// it gets dark — glowing glass and a point light on the trail. Follows <see cref="Sky.Night"/>.
    /// </summary>
    public class NightLights : MonoBehaviour
    {
        private static readonly List<(Light light, Renderer[] glass)> lamps = new List<(Light, Renderer[])>();
        private Light torch;
        private Transform head;
        private bool lampsOn;

        /// <summary>Makes a placed street lamp light up at night (a point light under the lamp head, glowing glass).</summary>
        public static void AddLamp(GameObject lamp)
        {
            var go = new GameObject("Licht");
            go.transform.SetParent(lamp.transform, false);
            go.transform.localPosition = new Vector3(0f, 3.55f, 0f);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.range = 14f; l.color = new Color(1f, 0.82f, 0.55f); l.intensity = 0f; l.shadows = LightShadows.None;
            l.enabled = false;
            // the scan has one material for post and lantern: a glowing pane of our own inside the lantern
            var k = new MeshKit();
            k.Box(0, new Vector3(0f, 3.55f, 0f), new Vector3(0.16f, 0.22f, 0.16f), Quaternion.identity);
            var glow = k.Make("Leuchte", lamp.transform, GlowMaterial());
            glow.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            glow.SetActive(false);
            var glass = new List<Renderer> { glow.GetComponent<MeshRenderer>() };
            lamps.Add((l, glass.ToArray()));
        }

        private static Material glowMat;
        private static Material GlowMaterial()
        {
            if (glowMat != null) return glowMat;
            // the URP particle unlit shader is in every build (the rain and snow use it); Shader.Find of a shader
            // no material references could come back null on a device
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Universal Render Pipeline/Lit");
            glowMat = new Material(sh) { name = "Lampenlicht" };
            glowMat.SetColor("_BaseColor", new Color(1f, 0.86f, 0.6f));
            return glowMat;
        }

        private void Update()
        {
            var sky = Sky.Instance;
            if (sky == null) return;
            float night = sky.Night * Mathf.Lerp(1f, 1.15f, sky.CloudCover);
            UpdateTorch(Mathf.Clamp01(night));
            // street lamps switch on at dusk (with a little hysteresis) and fade with the light
            if (!lampsOn && night > 0.35f) lampsOn = true; else if (lampsOn && night < 0.25f) lampsOn = false;
            lamps.RemoveAll(x => x.light == null);
            foreach (var (l, glass) in lamps)
            {
                l.enabled = lampsOn;
                l.intensity = lampsOn ? 2.2f * Mathf.Clamp01(night) : 0f;
                foreach (var r in glass) if (r != null) r.gameObject.SetActive(lampsOn);
            }
        }

        private void UpdateTorch(float night)
        {
            if (night < 0.05f) { if (torch != null) torch.enabled = false; return; }
            if (torch == null)
            {
                var fig = FindFirstObjectByType<RealPlayerFigure>();
                head = fig != null ? fig.transform : Camera.main != null ? Camera.main.transform : null;
                if (head == null) return;
                var go = new GameObject("Stirnlampe");
                go.transform.SetParent(head, false);
                go.transform.localPosition = new Vector3(0f, fig != null ? 1.72f : 0f, 0.15f);
                go.transform.localRotation = Quaternion.Euler(fig != null ? 17f : 12f, 0f, 0f);
                torch = go.AddComponent<Light>();
                torch.type = LightType.Spot; torch.range = 26f; torch.spotAngle = 72f; torch.innerSpotAngle = 38f;
                torch.color = new Color(1f, 0.96f, 0.9f); torch.shadows = LightShadows.None;
                Debug.Log($"[Jogging] Stirnlampe an ({(fig != null ? fig.name : "Kamera")})");
            }
            torch.enabled = true;
            torch.intensity = 2.4f * night;
        }

        private void OnDestroy() { lamps.Clear(); }
    }
}
