// The sky of the run (World/Sky.cs sets every value): gradient with haze at the horizon, sun disc
// without lens flare, moon with its phase (lit by the real sun direction), stars by brightness
// (fewer near towns) and the Milky Way only under a truly dark sky; clouds in four layers at
// different heights and speeds — horizon banks, cumulus, high thin cirrus, and a closed deck when
// overcast, some of it darker (rain). All cloud layers are planes over a curved earth, so they
// flatten and thin out into the haze towards the horizon.
Shader "Jogging/Sky"
{
    Properties
    {
        _Noise ("Noise", 2D) = "gray" {}
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ _SKY_LOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);

            float4 _SunDir;      // xyz towards the sun, w disc brightness (0 below the horizon)
            float4 _SunColor;    // rgb light colour of the sun
            float4 _MoonDir;     // xyz towards the moon, w brightness (phase, day/night)
            float4 _Zenith;      // rgb
            float4 _Horizon;     // rgb
            float4 _HazeCol;     // rgb = fog colour, w = haze 0..1
            float4 _Glow;        // rgb glow around the sun, w strength
            float4 _Night;       // x stars (0..1), y Milky Way, z light pollution, w time (s)
            float4 _Town;        // xyz direction of the nearest town (horizontal), w glow
            float4 _Cumulus;     // x coverage, y darkness (rain), z amount (0 = off), w height (m)
            float4 _Cirrus;      // x amount, w height
            float4 _Banks;       // x amount, w height
            float4 _Offs1;       // xy cumulus offset (tiles), zw cirrus offset
            float4 _Offs2;       // xy banks offset
            float4 _Scale;       // x cumulus tile (m), y cirrus tile, z banks tile, w fade distance (m)
            float4 _CloudLit;    // rgb light on clouds (sun or moon)
            float4 _CloudAmb;    // rgb ambient of clouds
            float4 _CamXZ;       // xy camera position (m) — clouds stay put in the world

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.dir = i.positionOS.xyz;
                return o;
            }

            float3 Hash33(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            // Distance along a ray (elevation sine h) to a layer H metres above ground, earth curved
            float LayerDist(float h, float H)
            {
                const float R = 6371000.0;
                return sqrt(R * R * h * h + 2.0 * R * H + H * H) - R * h;
            }

            float CumulusDensity(float2 uv, float cover, bool detail)
            {
                float4 t = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, uv);
                float b = t.r;
                #ifndef _SKY_LOW
                if (detail)
                    b += (SAMPLE_TEXTURE2D(_Noise, sampler_Noise, uv * 4.3 + 0.37).g - 0.5) * 0.22
                       + (SAMPLE_TEXTURE2D(_Noise, sampler_Noise, uv * 9.7 + 0.71).r - 0.5) * 0.08;
                #endif
                return saturate((b - (1.0 - cover)) / 0.24);
            }

            float3 Stars(float3 d)
            {
                float3 p = d * 230.0;
                float3 c = floor(p);
                float3 h = Hash33(c);
                float3 s = Hash33(c + 17.1);
                float dist = length(p - c - h);
                // magnitude: many faint, few bright; light pollution and haze drown the faint ones
                float bright = pow(s.x, 6.0) * 7.0;
                float limit = 0.05 + 5.0 * pow(_Night.z, 1.5) + _HazeCol.w * 0.4;
                float v = saturate(bright - limit);
                float twinkle = 0.75 + 0.25 * sin(_Night.w * (2.0 + s.y * 5.0) + s.z * 40.0);
                float3 col = lerp(float3(0.72, 0.82, 1.0), float3(1.0, 0.84, 0.62), s.z);
                float shape = smoothstep(0.2, 0.03, dist) + smoothstep(0.5, 0.0, dist) * 0.12 * saturate(bright - 1.0); // bright ones glow a little
                return col * v * shape * twinkle * smoothstep(0.0, 0.18, d.y);
            }

            float3 MilkyWay(float3 d)
            {
                float3 n = normalize(float3(0.78, 0.28, -0.56));   // pole of the band: it arcs high across the sky
                float3 a = normalize(cross(n, float3(0, 1, 0))), b = cross(n, a);
                float g = dot(d, n);
                float ang = atan2(dot(d, b), dot(d, a));           // along the band
                float band = exp(-g * g / 0.06);
                // one whole texture turn around the band (no seam where atan2 wraps), sampled at the top mip level
                // (the wrap would give a derivative spike)
                float2 uv = float2(ang * (1.0 / 6.2831853), g * 0.9);
                float neb = SAMPLE_TEXTURE2D_LOD(_Noise, sampler_Noise, uv, 0).r;                          // soft star clouds
                float clump = SAMPLE_TEXTURE2D_LOD(_Noise, sampler_Noise, uv * float2(3.0, 3.1) + 0.2, 0).g; // grain
                float dust = smoothstep(0.62, 0.9, SAMPLE_TEXTURE2D_LOD(_Noise, sampler_Noise, uv * float2(1.0, 1.3) + 0.3, 0).a) * exp(-g * g / 0.012);
                float core = 0.5 + 0.5 * cos(ang - 1.1);          // brighter towards the galactic centre
                float v = band * (0.25 + 0.55 * neb + 0.2 * clump) * core * (1.0 - dust * 0.6);
                float3 tint = lerp(float3(0.66, 0.72, 0.9), float3(0.95, 0.86, 0.72), core * 0.5);
                return tint * v * 0.11 * smoothstep(0.0, 0.3, d.y);
            }

            float3 Moon(float3 d, out float disc)
            {
                float3 m = normalize(_MoonDir.xyz);
                const float r = 0.0078;                  // a bit larger than the real 0.26°: readable on a tablet
                float cm = dot(d, m);
                disc = 0.0;
                if (cm < cos(r * 1.2)) return 0;
                float3 right = normalize(cross(float3(0, 1, 0), m));
                float3 up = cross(m, right);
                float3 off = d - m * cm;
                float2 q = float2(dot(off, right), dot(off, up)) / r;
                float l = length(q);
                disc = smoothstep(1.0, 0.9, l);
                float z = sqrt(saturate(1.0 - l * l));
                float3 nrm = q.x * right + q.y * up - z * m;   // surface facing us
                float lit = smoothstep(-0.03, 0.12, dot(nrm, _SunDir.xyz));
                float maria = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, q * 0.22 + 0.5).a;
                float albedo = lerp(0.62, 1.0, smoothstep(0.35, 0.75, maria));
                return float3(1.0, 0.97, 0.9) * (lit * albedo + 0.025) * disc;
            }

            float4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = max(d.y, 0.0);
                float mu = dot(d, _SunDir.xyz);

                // ---- clear sky
                float3 sky = lerp(_Horizon.rgb, _Zenith.rgb, pow(h, 0.5));
                float muP = saturate(mu);
                sky += _Glow.rgb * _Glow.w * (pow(muP, 5.0) * 0.45 + pow(muP, 48.0) * 1.1) * (0.6 + 0.4 * exp(-h * 4.0));
                // light-polluted night sky: a dim orange-grey dome, brightest towards the town
                float3 hd = normalize(float3(d.x, 0.001, d.z));
                float townSide = pow(saturate(dot(hd, _Town.xyz) * 0.5 + 0.5), 3.0);
                sky += _Town.w * float3(0.9, 0.55, 0.3) * (0.02 * exp(-h * 3.0) + 0.06 * townSide * exp(-h * 10.0));

                // ---- sun, moon, stars (all behind the clouds)
                float sunDisc = smoothstep(cos(0.0092), cos(0.0080), mu);
                float limb = 1.0 - 0.45 * (1.0 - smoothstep(cos(0.0092), 1.0, mu));
                sky += _SunColor.rgb * _SunDir.w * sunDisc * limb;
                float moonDisc;
                float3 moon = Moon(d, moonDisc);
                // by day the moon is a pale disc: it only brightens, it doesn't cover the blue
                sky = lerp(sky, sky * (1.0 - moonDisc * 0.55 * _Night.x) + moon * _MoonDir.w, moonDisc > 0 ? 1.0 : 0.0);
                if (_Night.x > 0.001)
                {
                    sky += Stars(d) * _Night.x * (1.0 - moonDisc);
                    #ifndef _SKY_LOW
                    if (_Night.y > 0.001) sky += MilkyWay(d) * _Night.y;
                    #endif
                }

                // ---- clouds, far to near: cirrus (high, fast), banks (horizon), cumulus (low)
                float3 col = sky;
                float fadeDist = _Scale.w;
                if (d.y > -0.02)
                {
                    float hy = max(d.y, 0.002);
                    if (_Cirrus.x > 0.001)
                    {
                        float t = LayerDist(hy, _Cirrus.w);
                        float2 uv = (_CamXZ.xy + d.xz * t) / _Scale.y;
                        uv = float2(uv.x * 0.8 + uv.y * 0.3, uv.y * 0.9 - uv.x * 0.25) + _Offs1.zw; // streaks slanted to the wind
                        float c = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, uv).b * 0.65 + SAMPLE_TEXTURE2D(_Noise, sampler_Noise, uv * 2.3 + 0.5).b * 0.35;
                        float a = saturate((c - (1.0 - _Cirrus.x * 0.8)) * 1.6) * 0.32 * exp(-t / (fadeDist * 2.5)) * smoothstep(0.0, 0.12, hy);
                        float3 cc = _CloudLit.rgb * (1.0 + pow(muP, 6.0) * 1.5) + _CloudAmb.rgb;
                        col = lerp(col, cc, a);
                    }
                    if (_Banks.x > 0.001)
                    {
                        float t = LayerDist(hy, _Banks.w);
                        float2 uv = (_CamXZ.xy + d.xz * t) / _Scale.z + _Offs2.xy;
                        float cover = saturate(_Banks.x * exp(-hy * 9.0) + _Cumulus.x * 0.6);
                        float den = CumulusDensity(uv, cover, false);
                        float top = CumulusDensity(uv + _SunDir.xz * 0.03, cover, false);
                        float3 bright = _CloudLit.rgb + _CloudAmb.rgb, base = _CloudLit.rgb * 0.15 + _CloudAmb.rgb * 0.65;
                        float3 cc = lerp(bright, base, saturate(top * 0.7 + den * 0.3)) * lerp(1.0, 0.55, _Cumulus.y * den);
                        cc += _CloudLit.rgb * pow(muP, 24.0) * (1.0 - den) * 1.2;
                        float fog = 1.0 - exp(-t / fadeDist);
                        cc = lerp(cc, _HazeCol.rgb, fog * 0.85);
                        col = lerp(col, cc, den * (1.0 - fog * 0.6));
                    }
                    if (_Cumulus.z > 0.001)
                    {
                        float t = LayerDist(hy, _Cumulus.w);
                        float2 uv = (_CamXZ.xy + d.xz * t) / _Scale.x + _Offs1.xy;
                        float den = CumulusDensity(uv, _Cumulus.x, true);
                        float shade = den * 0.45;                                  // thick middles are grey underneath
                        #ifndef _SKY_LOW
                        shade = saturate(CumulusDensity(uv + _SunDir.xz * 0.018, _Cumulus.x, false) * 0.55 + den * 0.3);
                        #endif
                        // some clouds are darker than the rest (rain showers)
                        float dark = _Cumulus.y * smoothstep(0.35, 0.75, SAMPLE_TEXTURE2D(_Noise, sampler_Noise, uv * 0.37 + 0.19).a) * saturate(den * 1.5);
                        float3 bright = _CloudLit.rgb + _CloudAmb.rgb, base = _CloudLit.rgb * 0.12 + _CloudAmb.rgb * 0.62;
                        float3 cc = lerp(bright, base, shade) * lerp(1.0, 0.42, dark);
                        cc += _CloudLit.rgb * pow(muP, 24.0) * (1.0 - den) * 1.4; // silver lining right around the sun
                        // a lit town paints the bottoms of night clouds
                        cc += _Town.w * float3(0.9, 0.55, 0.3) * 0.05 * (0.4 + townSide) * den;
                        float fog = 1.0 - exp(-t / fadeDist);
                        cc = lerp(cc, _HazeCol.rgb, fog * 0.9);
                        col = lerp(col, cc, smoothstep(0.0, 0.55, den) * _Cumulus.z * (1.0 - fog * 0.7));
                    }
                }

                // ---- haze at the horizon: the fog colour of the landscape, so hills melt into the sky
                float hazeBand = exp(-h * lerp(16.0, 4.5, _HazeCol.w)) * lerp(0.45, 0.95, _HazeCol.w);
                col = lerp(col, _HazeCol.rgb, hazeBand);
                if (d.y < 0.0) col = lerp(col, _HazeCol.rgb, saturate(-d.y * 30.0));
                return float4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
