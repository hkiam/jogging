using UnityEngine;
using Jogging.Route;

namespace Jogging.World
{
    /// <summary>
    /// Rain or snow of the route (<see cref="RouteParams.weather"/>) as a particle volume that moves with
    /// the camera: rain as fast streaks, snow as slow drifting flakes. Light and haze are set by
    /// <see cref="Mood"/>. The material comes from the scene builder (URP particle shader, soft dot).
    /// </summary>
    public class WeatherFx : MonoBehaviour
    {
        [SerializeField] private Material particleMaterial;

        private ParticleSystem ps;
        private Transform cam;

        private void Start()
        {
            var wx = RouteRuntime.Current != null ? RouteRuntime.Current.@params.weather : "clear";
            if (wx != "rain" && wx != "snow" || particleMaterial == null) return;
            cam = Camera.main != null ? Camera.main.transform : null;
            Build(wx == "rain");
            Debug.Log($"[Jogging] Wetter: {wx}");
        }

        private void Build(bool rain)
        {
            var go = new GameObject(rain ? "Rain" : "Snow");
            go.transform.SetParent(transform, false);
            ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.loop = true;
            main.playOnAwake = false;
            float q = GraphicsQuality.ParticleFactor;
            main.maxParticles = Mathf.RoundToInt((rain ? 6000 : 9000) * q);
            main.startLifetime = rain ? 1.1f : 9f;
            main.startSpeed = rain ? new ParticleSystem.MinMaxCurve(14f, 17f) : new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            main.startSize = rain ? new ParticleSystem.MinMaxCurve(0.018f, 0.028f) : new ParticleSystem.MinMaxCurve(0.07f, 0.13f);
            main.startColor = rain ? new Color(0.78f, 0.82f, 0.9f, 0.45f) : new Color(1f, 1f, 1f, 0.9f);
            main.gravityModifier = rain ? 0.4f : 0.015f;

            var em = ps.emission;
            em.rateOverTime = (rain ? 4500f : 1000f) * q;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = rain ? new Vector3(36f, 1f, 36f) : new Vector3(28f, 1f, 28f);
            shape.rotation = new Vector3(90f, 0f, 0f); // emit downwards

            if (!rain)
            {
                var noise = ps.noise;
                noise.enabled = true;
                noise.strength = 0.6f;
                noise.frequency = 0.25f;
            }

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = particleMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (rain)
            {
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.035f;
                r.lengthScale = 1f;
            }
            ps.Play();
        }

        private void LateUpdate()
        {
            long t0 = FrameWork.Start();
            LateUpdateWork();
            FrameWork.Stop("Wetter", t0);
        }

        private void LateUpdateWork()
        {
            if (ps == null || cam == null) return;
            // The volume sits above and ahead of the camera, so the view is always inside the weather.
            ps.transform.position = cam.position + cam.forward * 8f + Vector3.up * (ps.main.startSpeed.constantMax > 5f ? 12f : 6f);
        }
    }
}
