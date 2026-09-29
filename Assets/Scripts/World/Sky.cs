using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Jogging.Route;

namespace Jogging.World
{
    /// <summary>
    /// Sky, light and air of the run (replaces the procedural skybox). From the route's mood
    /// (<see cref="RouteParams"/>: time of day, season, weather, haze, light pollution) and its seed:
    /// <list type="bullet">
    /// <item>Sun and moon stand where they would at 50° N on that day and hour; the clock runs on during
    /// the run, so shadows wander. The moon has today's phase (real date; <c>-moonage</c> for tests), by
    /// day a pale disc, at night the light.</item>
    /// <item>Clouds (<c>Jogging/Sky</c> shader): cumulus, horizon banks and high cirrus, each at its own height
    /// and speed; cloudy weather closes the cover and darkens some of it, rain makes a dark deck.</item>
    /// <item>Cloud shadows: the sun light carries a cookie made of the same cloud shapes, aligned so the
    /// shadow at the runner belongs to the cloud in front of the sun — it goes dim exactly when the sun
    /// disappears, and bright again.</item>
    /// <item>Stars by brightness, fewer near towns and in haze; the Milky Way only in a truly dark sky.</item>
    /// <item>Haze: fog density and colour; the horizon of the sky takes the fog colour, so distant hills
    /// fade blue into it.</item>
    /// </list>
    /// </summary>
    public class Sky : MonoBehaviour
    {
        public const float Latitude = 50f;
        private const float CumulusH = 1600f, CirrusH = 8500f, BanksH = 2400f;
        private const float ShadowTile = 1400f; // cloud shadows: smaller than the sky tile, so the patches are seen from the trail
        private const float CumulusTile = 3200f, CirrusTile = 16000f, BanksTile = 22000f;

        public static Sky Instance { get; private set; }

        /// <summary>Clock of the sky now (hours; runs on from the route's time of day).</summary>
        public float Hours => startHours + (Time.time - startTime) / 3600f;
        public float SunElevation { get; private set; }
        public float MoonIllumination { get; private set; }
        public float CloudCover => cumulus;
        /// <summary>0 by day … 1 in the dark night (lamps, head torch).</summary>
        public float Night { get; private set; }

        private RouteParams prm;
        private float startHours, startTime;
        private float cumulus, darkness, cirrus, banks, pollution;
        private Vector2 wind1, wind2, wind3, offs1, offs2, offs3;
        private Vector3 townDir;
        private double moonAge;
        private Light lightSrc;
        private UniversalAdditionalLightData lightData;
        private Texture2D cookie;
        private Material mat;
        private float envElevation = -999f, envTime;
        private float baseFogDensity;

        private static readonly int
            idSunDir = Shader.PropertyToID("_SunDir"), idSunColor = Shader.PropertyToID("_SunColor"), idMoonDir = Shader.PropertyToID("_MoonDir"),
            idZenith = Shader.PropertyToID("_Zenith"), idHorizon = Shader.PropertyToID("_Horizon"), idHaze = Shader.PropertyToID("_HazeCol"),
            idGlow = Shader.PropertyToID("_Glow"), idNight = Shader.PropertyToID("_Night"), idTown = Shader.PropertyToID("_Town"),
            idCumulus = Shader.PropertyToID("_Cumulus"), idCirrus = Shader.PropertyToID("_Cirrus"), idBanks = Shader.PropertyToID("_Banks"),
            idOffs1 = Shader.PropertyToID("_Offs1"), idOffs2 = Shader.PropertyToID("_Offs2"), idScale = Shader.PropertyToID("_Scale"),
            idCloudLit = Shader.PropertyToID("_CloudLit"), idCloudAmb = Shader.PropertyToID("_CloudAmb"), idCam = Shader.PropertyToID("_CamXZ");

        /// <summary>Sets up sky, light and air for the route (called when the route is loaded).</summary>
        public static void Begin(RouteParams p, int seed)
        {
            if (Instance == null) Instance = new GameObject("Sky").AddComponent<Sky>();
            Instance.Setup(p, seed);
        }

        /// <summary>Tests and screenshots: the same sky with another hour, weather, season, town light or moon.</summary>
        public void Preview(float hours, string weather, string season, float lightPollution, double moonAgeDays, int seed)
        {
            var p = JsonUtility.FromJson<RouteParams>(JsonUtility.ToJson(prm));
            p.timeOfDay = hours; p.weather = weather; p.season = season; p.lightPollution = lightPollution;
            Setup(p, seed);
            moonAge = moonAgeDays;
            envElevation = -999f;
            Tick(0f);
        }

        private void OnDestroy() { if (Instance == this) Instance = null; if (cookie != null) Destroy(cookie); if (mat != null) Destroy(mat); }

        private void Setup(RouteParams p, int seed)
        {
            prm = p;
            startHours = Mathf.Clamp(p.timeOfDay, 4f, 23.75f);
            startTime = Time.time;
            pollution = Mathf.Clamp01(p.lightPollution);
            var rnd = new System.Random(seed * 31 + 7);
            float R() => (float)rnd.NextDouble();
            switch (p.weather ?? "clear")
            {
                case "cloudy": cumulus = 0.45f + 0.17f * R(); darkness = 0.5f + 0.4f * R(); cirrus = 0.25f * R(); banks = 0.8f; break;
                case "rain": cumulus = 0.985f; darkness = 0.8f; cirrus = 0f; banks = 1f; break;
                case "snow": cumulus = 0.95f; darkness = 0.2f; cirrus = 0f; banks = 1f; break;
                default: cumulus = 0.07f + 0.23f * R(); darkness = 0f; cirrus = 0.15f + 0.5f * R(); banks = 0.3f + 0.5f * R(); break;
            }
            float az = R() * Mathf.PI * 2f, speed = 5f + 8f * R();
            wind1 = new Vector2(Mathf.Sin(az), Mathf.Cos(az)) * speed;
            wind2 = new Vector2(Mathf.Sin(az + 0.45f), Mathf.Cos(az + 0.45f)) * speed * 2.6f;   // high winds: faster, veered
            wind3 = wind1 * 0.6f;
            offs1 = new Vector2(R(), R()); offs2 = new Vector2(R(), R()); offs3 = new Vector2(R(), R());
            float taz = R() * Mathf.PI * 2f;
            townDir = new Vector3(Mathf.Sin(taz), 0f, Mathf.Cos(taz));
            moonAge = p.moonAge >= 0f ? p.moonAge : MoonAge(DateTime.UtcNow); // a route may fix its moon (Mondnacht)
            var a = Jogging.Core.Args.All;
            for (int i = 0; i < a.Length - 1; i++)
                if (a[i] == "-moonage") double.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out moonAge);

            var shader = Resources.Load<Shader>("JoggingSky");
            if (shader != null)
            {
                if (mat == null) mat = new Material(shader) { name = "JoggingSky" };
                mat.SetTexture("_Noise", SkyNoise.Texture);
                if (GraphicsQuality.Level == "minimal") mat.EnableKeyword("_SKY_LOW"); else mat.DisableKeyword("_SKY_LOW");
                RenderSettings.skybox = mat;
            }
            else Debug.LogWarning("[Jogging] Himmel: Shader JoggingSky fehlt – bleibt beim alten Himmel");

            lightSrc = RenderSettings.sun;
            if (lightSrc == null) foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { lightSrc = l; break; }
            if (lightSrc != null)
            {
                RenderSettings.sun = lightSrc;
                MakeCookie();
                lightSrc.cookie = cookie;
                lightData = lightSrc.GetUniversalAdditionalLightData();
                lightData.lightCookieSize = new Vector2(ShadowTile, ShadowTile);
                var ca = Jogging.Core.Args.All; // test: -cookiesize 60 → small, obvious patches
                for (int i = 0; i < ca.Length - 1; i++) if (ca[i] == "-cookiesize" && float.TryParse(ca[i + 1], out float cs)) lightData.lightCookieSize = new Vector2(cs, cs);
            }

            float haze = Mathf.Clamp01(p.haze);
            string wx = p.weather ?? "clear";
            baseFogDensity = Mathf.Lerp(0.0011f, 0.0062f, haze * haze) * (p.season == "winter" ? 1.3f : p.season == "autumn" ? 1.15f : 1f)
                           * (wx == "rain" ? 2.6f : wx == "snow" ? 2.2f : wx == "cloudy" ? 1.6f : 1f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            if (GetComponent<NightLights>() == null) gameObject.AddComponent<NightLights>();
            var mist = GetComponent<GroundMist>();
            if (mist == null) mist = gameObject.AddComponent<GroundMist>();
            mist.Setup(p, seed, wind1);
            envElevation = -999f;
            Tick(0f);
            Debug.Log($"[Jogging] Himmel: {startHours:0.00} h, Sonne {SunElevation:0}°, Mond {MoonIllumination * 100f:0} % (Alter {moonAge:0.0} d), " +
                      $"Wolken {cumulus:0.00}/{cirrus:0.00}/{banks:0.00}, dunkel {darkness:0.00}, Licht der Orte {pollution:0.00}");
        }

        // The cookie: 1 = sun, darker where a cloud of the cumulus layer is (same shapes, same coverage)
        private void MakeCookie()
        {
            const int n = 128;
            if (cookie == null) cookie = new Texture2D(n, n, TextureFormat.RGBA32, true, true) { name = "CloudShadows", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, hideFlags = HideFlags.DontSave };
            float strength = 0.78f * (1f - Mathf.InverseLerp(0.9f, 1f, cumulus)); // under a closed deck there are no patches
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = SkyNoise.Density(SkyNoise.Shape((x + 0.5f) / n, (y + 0.5f) / n), cumulus);
                    byte v = (byte)(255f * (1f - strength * Mathf.SmoothStep(0f, 1f, d)));
                    px[y * n + x] = new Color32(v, v, v, 255);
                }
            cookie.SetPixels32(px);
            cookie.Apply(true);
        }

        private void Update() => Tick(Time.deltaTime);

        private void Tick(float dt)
        {
            offs1 += wind1 * dt / CumulusTile; offs2 += wind2 * dt / CirrusTile; offs3 += wind3 * dt / BanksTile;
            var cam = Camera.main != null ? Camera.main.transform : null;
            Vector3 camPos = cam != null ? cam.position : Vector3.zero;

            // ---- sun and moon
            float decl = prm.season == "winter" ? -20f : prm.season == "autumn" ? -6f : prm.season == "spring" ? 4f : 21f;
            float noon = prm.season == "winter" ? 12.3f : 13.3f;          // solar noon on the clock (CET / CEST)
            float hourAngle = (Hours - noon) * 15f;
            Vector3 sunDir = Direction(hourAngle, decl, out float sunEl);
            SunElevation = sunEl;
            float elong = (float)(moonAge / 29.530588853 * 360.0);
            Vector3 moonDir = Direction(hourAngle - elong, decl * Mathf.Cos(elong * Mathf.Deg2Rad), out float moonEl);
            MoonIllumination = (1f - Mathf.Cos(elong * Mathf.Deg2Rad)) * 0.5f;

            string wx = prm.weather ?? "clear";
            float overcast = Mathf.InverseLerp(0.6f, 1f, cumulus);
            float low = Mathf.InverseLerp(38f, 4f, sunEl);                 // 0 high … 1 low sun
            float night = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-12f, -1f, sunEl));
            Night = night;
            float moonUp = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-1f, 4f, moonEl));

            // ---- sky colours by sun height
            Color zenith = Keys(sunEl, ZenithKeys), horizon = Keys(sunEl, HorizonKeys);
            float dayBright = Mathf.Lerp(1f, 0.02f, night);
            var grey = (wx == "snow" ? new Color(0.78f, 0.80f, 0.84f) : wx == "rain" ? new Color(0.40f, 0.43f, 0.47f) : new Color(0.58f, 0.61f, 0.65f)) * dayBright;
            zenith = Color.Lerp(zenith, grey, overcast); horizon = Color.Lerp(horizon, grey * 1.08f, overcast);
            if (prm.season == "winter") { zenith = Color.Lerp(zenith, zenith * new Color(0.92f, 0.96f, 1.05f), 0.5f); }
            // the moon lights the night sky a little
            Color moonSky = new Color(0.010f, 0.015f, 0.03f) * MoonIllumination * moonUp * night;
            zenith += moonSky; horizon += moonSky * 1.2f;

            Color sunCol = Color.Lerp(prm.season == "winter" ? new Color(0.96f, 0.97f, 1f) : new Color(1f, 0.95f, 0.87f), new Color(1f, 0.62f, 0.36f), low);
            float glowStrength = Mathf.Lerp(0.45f, 1.5f, low) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-9f, -1f, sunEl)) * (1f - overcast * 0.85f);
            Color glow = Color.Lerp(new Color(1f, 0.88f, 0.72f), new Color(1f, 0.52f, 0.26f), Mathf.Max(low, Mathf.InverseLerp(2f, -4f, sunEl)));

            // ---- haze = fog colour: the horizon, a touch bluer by day (aerial perspective)
            float haze = Mathf.Clamp01(prm.haze);
            Color fog = Color.Lerp(horizon, new Color(0.66f, 0.75f, 0.88f) * dayBright, (1f - low) * (1f - overcast) * 0.45f);
            fog = Color.Lerp(fog, Color.Lerp(horizon, new Color(0.80f, 0.82f, 0.84f) * dayBright, 0.5f), haze * 0.35f);
            RenderSettings.fogColor = fog;
            RenderSettings.fogDensity = baseFogDensity * Mathf.Lerp(1f, 0.75f, night);

            // ---- the light: the sun by day, the moon (or a faint sky light) at night
            float sunI = Mathf.Lerp(2.2f, 1.1f, low) * (prm.season == "winter" ? 0.9f : 1f)
                       * (wx == "rain" ? 0.35f : wx == "snow" ? 0.5f : wx == "cloudy" ? 0.55f : 1f)
                       * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-3f, 3f, sunEl));
            Vector3 lightDir; float lightI; Color lightCol;
            if (sunEl > -3f) { lightDir = sunDir; lightI = sunI; lightCol = sunCol; }
            else
            {
                float ramp = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-3f, -8f, sunEl));
                if (moonEl > 0f) { lightDir = moonDir; lightI = (0.04f + 0.30f * MoonIllumination) * moonUp * ramp * (1f - overcast * 0.8f); }
                else { lightDir = new Vector3(0.2f, 1f, 0.3f).normalized; lightI = 0.02f * ramp; }
                lightCol = new Color(0.62f, 0.72f, 1f);
            }
            if (lightSrc != null)
            {
                lightSrc.transform.rotation = Quaternion.LookRotation(-lightDir, Vector3.up);
                lightSrc.color = lightCol;
                lightSrc.intensity = lightI;
                lightSrc.shadowStrength = wx == "clear" ? (night > 0.5f ? 0.6f : 0.85f) : wx == "cloudy" ? 0.55f : 0.35f;
                PlaceCookie(camPos, lightDir);
            }
            // ambient comes from the sky picture; at night it is scaled to a brightness the eye adapts to:
            // the path stays visible, a full moon night is lighter than a new moon one
            float ambient = Mathf.Lerp(1.3f, 1.0f, low) * (wx == "clear" ? 1f : wx == "cloudy" ? 0.9f : 0.8f);
            Color skyAvg = zenith * 0.6f + horizon * 0.4f;
            float skyLum = Mathf.Max(0.0005f, skyAvg.r * 0.3f + skyAvg.g * 0.59f + skyAvg.b * 0.11f);
            float nightTarget = (0.03f + 0.045f * MoonIllumination * moonUp) / skyLum;
            RenderSettings.ambientIntensity = Mathf.Clamp(Mathf.Lerp(ambient, nightTarget, night), 0.5f, 9f);

            // ---- clouds lit by sun or moon; ambient from the sky
            Color cloudLit = sunCol * sunI * 0.42f + new Color(0.10f, 0.12f, 0.16f) * MoonIllumination * moonUp * night;
            Color skyMix = zenith * 0.7f + horizon * 0.4f;
            float mixLum = skyMix.r * 0.3f + skyMix.g * 0.59f + skyMix.b * 0.11f;
            Color cloudAmb = Color.Lerp(skyMix, new Color(mixLum, mixLum, mixLum), 0.45f); // shadow sides: grey with a hint of sky blue
            float stars = night * (1f - 0.55f * MoonIllumination * moonUp) * (1f - overcast);
            float milky = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-13f, -17f, sunEl)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.08f, 0.35f, pollution)))
                        * (1f - 0.9f * MoonIllumination * moonUp) * (1f - overcast);
            bool hdr = GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset u && u.supportsHDR;

            if (mat != null)
            {
                mat.SetVector(idSunDir, new Vector4(sunDir.x, sunDir.y, sunDir.z, (hdr ? 14f : 3f) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-1.2f, 0.5f, sunEl)) * (1f - overcast)));
                mat.SetVector(idSunColor, sunCol);
                float moonDay = 0.22f * (1f - overcast) * Mathf.Clamp01(MoonIllumination * 1.5f);
                mat.SetVector(idMoonDir, new Vector4(moonDir.x, moonDir.y, moonDir.z, moonEl > -1f ? Mathf.Lerp(moonDay, hdr ? 1.6f : 0.9f, night) : 0f));
                mat.SetVector(idZenith, zenith);
                mat.SetVector(idHorizon, horizon);
                mat.SetVector(idHaze, new Vector4(fog.r, fog.g, fog.b, haze));
                mat.SetVector(idGlow, new Vector4(glow.r, glow.g, glow.b, glowStrength));
                mat.SetVector(idNight, new Vector4(stars, milky, pollution, Time.time));
                mat.SetVector(idTown, new Vector4(townDir.x, 0f, townDir.z, pollution * night));
                mat.SetVector(idCumulus, new Vector4(cumulus, darkness, 1f, CumulusH));
                mat.SetVector(idCirrus, new Vector4(cirrus * (1f - overcast), 0f, 0f, CirrusH));
                mat.SetVector(idBanks, new Vector4(banks, 0f, 0f, BanksH));
                mat.SetVector(idOffs1, new Vector4(offs1.x, offs1.y, offs2.x, offs2.y));
                mat.SetVector(idOffs2, new Vector4(offs3.x, offs3.y, 0f, 0f));
                mat.SetVector(idScale, new Vector4(CumulusTile, CirrusTile, BanksTile, 38000f / (1f + haze * 2f)));
                mat.SetVector(idCloudLit, cloudLit);
                mat.SetVector(idCloudAmb, cloudAmb);
                mat.SetVector(idCam, new Vector4(camPos.x, camPos.z, 0f, 0f));
            }

            // ambient and reflections follow the sky — not every frame: when the sun moved or every 90 s
            // (real seconds apart at least: with -timescale the sun races, and the update costs a few ms)
            if (envElevation < -900f || (Time.unscaledTime - envTime > 8f && (Mathf.Abs(sunEl - envElevation) > 1.5f || Time.unscaledTime - envTime > 90f)))
            {
                envElevation = sunEl; envTime = Time.unscaledTime;
                DynamicGI.UpdateEnvironment();
            }
        }

        // Cookie offset: at the runner the cookie shows the cumulus cloud on the ray towards the light
        private void PlaceCookie(Vector3 camPos, Vector3 lightDir)
        {
            if (lightData == null) return;
            float h = Mathf.Max(lightDir.y, 0.02f);
            float t = LayerDist(h, CumulusH);
            var sky = new Vector2(camPos.x + lightDir.x * t, camPos.z + lightDir.z * t) / CumulusTile + offs1;
            Vector3 ls = lightSrc.transform.InverseTransformPoint(camPos);
            lightData.lightCookieOffset = new Vector2(ls.x, ls.y) - (sky - new Vector2(0.5f, 0.5f)) * lightData.lightCookieSize.x;
        }

        /// <summary>Cloud shadow at the runner right now: 0 = full sun … 1 = under a cloud.</summary>
        public float ShadeAtRunner()
        {
            if (lightSrc == null) return 0f;
            var cam = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            Vector3 l = -lightSrc.transform.forward;
            float t = LayerDist(Mathf.Max(l.y, 0.02f), CumulusH);
            var sky = new Vector2(cam.x + l.x * t, cam.z + l.z * t) / CumulusTile + offs1;
            return SkyNoise.Density(SkyNoise.Shape(sky.x, sky.y), cumulus);
        }

        private static float LayerDist(float h, float H)
        {
            const double R = 6371000.0;
            return (float)(Math.Sqrt(R * R * h * h + 2.0 * R * H + H * H) - R * h);
        }

        // Horizontal coordinates at the latitude: +z north, +x east (the old sun model's axes)
        private static Vector3 Direction(float hourAngleDeg, float declDeg, out float elevation)
        {
            float phi = Latitude * Mathf.Deg2Rad, dec = declDeg * Mathf.Deg2Rad, H = hourAngleDeg * Mathf.Deg2Rad;
            float sinAlt = Mathf.Sin(phi) * Mathf.Sin(dec) + Mathf.Cos(phi) * Mathf.Cos(dec) * Mathf.Cos(H);
            float alt = Mathf.Asin(Mathf.Clamp(sinAlt, -1f, 1f));
            float az = Mathf.Atan2(Mathf.Sin(H), Mathf.Cos(H) * Mathf.Sin(phi) - Mathf.Tan(dec) * Mathf.Cos(phi)) + Mathf.PI; // from north, clockwise
            elevation = alt * Mathf.Rad2Deg;
            return new Vector3(Mathf.Sin(az) * Mathf.Cos(alt), Mathf.Sin(alt), Mathf.Cos(az) * Mathf.Cos(alt));
        }

        /// <summary>Days since the last new moon (0 new … 14.8 full … 29.5).</summary>
        public static double MoonAge(DateTime utc)
        {
            double days = (utc - new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc)).TotalDays;
            double age = days % 29.530588853;
            return age < 0 ? age + 29.530588853 : age;
        }

        // sun elevation (°) → colour
        private static readonly (float e, Color c)[] ZenithKeys =
        {
            (-18f, new Color(0.004f, 0.006f, 0.016f)), (-9f, new Color(0.02f, 0.03f, 0.075f)), (-3f, new Color(0.08f, 0.12f, 0.27f)),
            (3f, new Color(0.20f, 0.31f, 0.58f)), (14f, new Color(0.19f, 0.38f, 0.78f)), (40f, new Color(0.16f, 0.36f, 0.80f)),
        };
        private static readonly (float e, Color c)[] HorizonKeys =
        {
            (-18f, new Color(0.008f, 0.011f, 0.022f)), (-9f, new Color(0.05f, 0.055f, 0.09f)), (-3f, new Color(0.34f, 0.27f, 0.30f)),
            (3f, new Color(0.82f, 0.68f, 0.58f)), (14f, new Color(0.72f, 0.79f, 0.89f)), (40f, new Color(0.70f, 0.79f, 0.90f)),
        };

        private static Color Keys(float e, (float e, Color c)[] k)
        {
            if (e <= k[0].e) return k[0].c;
            for (int i = 1; i < k.Length; i++)
                if (e <= k[i].e) return Color.Lerp(k[i - 1].c, k[i].c, Mathf.SmoothStep(0f, 1f, (e - k[i - 1].e) / (k[i].e - k[i - 1].e)));
            return k[k.Length - 1].c;
        }
    }
}
