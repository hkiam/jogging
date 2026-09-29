// The trail surface (World/TrailRibbon + World/TrailSurface): up to five surfaces blended along the
// way by weights in the vertex (colour rgba + uv2.x): asphalt, gravel, earth, forest floor, meadow path.
// Where two meet, the texture's own relief decides which shows (height blend, no smear). Unpaved paths
// get a ragged edge into the grass, asphalt a straight one. Lit like URP Lit (shadows, cloud-shadow
// cookie, fog, additional lights); winter lays snow on it, rain makes it darker and shinier.
Shader "Jogging/TrailSurface"
{
    Properties
    {
        _T0 ("Asphalt", 2D) = "grey" {}   _N0 ("Asphalt N", 2D) = "bump" {}
        _T1 ("Gravel", 2D) = "grey" {}    _N1 ("Gravel N", 2D) = "bump" {}
        _T2 ("Earth", 2D) = "grey" {}     _N2 ("Earth N", 2D) = "bump" {}
        _T3 ("Forest", 2D) = "grey" {}    _N3 ("Forest N", 2D) = "bump" {}
        _T4 ("Meadow", 2D) = "grey" {}    _N4 ("Meadow N", 2D) = "bump" {}
        _Noise ("Noise", 2D) = "grey" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_T0); TEXTURE2D(_T1); TEXTURE2D(_T2); TEXTURE2D(_T3); TEXTURE2D(_T4);
            TEXTURE2D(_N0); TEXTURE2D(_N1); TEXTURE2D(_N2); TEXTURE2D(_N3); TEXTURE2D(_N4);
            SAMPLER(sampler_T0); SAMPLER(sampler_N0);
            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);
            CBUFFER_START(UnityPerMaterial)
                float4 _Tile;      // metres per texture tile along the way: asphalt, gravel, earth, forest (meadow in _Tile2.x)
                float4 _Tile2;
                float4 _Tint0, _Tint1, _Tint2, _Tint3, _Tint4; // brightness per surface (rgb), w smoothness
                float4 _Ragged;    // edge raggedness: asphalt, gravel, earth, forest (meadow in _Ragged2.x)
                float4 _Ragged2;
                float4 _Weather;   // x snow 0..1, y wet 0..1
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;   // x across (0 and 1 = path edges), y = metres along
                float2 uv2 : TEXCOORD1;  // x = meadow weight
                float4 color : COLOR;    // weights asphalt, gravel, earth, forest
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 tangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                float4 w0 : TEXCOORD4;
                float w4 : TEXCOORD5;
                float fog : TEXCOORD6;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(i.normalOS, i.tangentOS);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.normalWS = n.normalWS; o.tangentWS = float4(n.tangentWS, i.tangentOS.w * GetOddNegativeScale());
                o.uv = i.uv; o.w0 = i.color; o.w4 = i.uv2.x;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            float4 frag(Varyings i) : SV_Target
            {
                float w[5] = { i.w0.x, i.w0.y, i.w0.z, i.w0.w, i.w4 };
                float tile[5] = { _Tile.x, _Tile.y, _Tile.z, _Tile.w, _Tile2.x };
                float ragged = _Ragged.x * w[0] + _Ragged.y * w[1] + _Ragged.z * w[2] + _Ragged.w * w[3] + _Ragged2.x * w[4];

                // ragged edge: the path frays into the grass (clip), asphalt keeps its line
                float n1 = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, i.positionWS.xz * 0.23).r;
                float n2 = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, i.positionWS.xz * 0.91).g;
                float edge = abs(i.uv.x - 0.5) * 2.0;                  // 0 centre … 1 path edge (skirt beyond)
                clip(1.0 + ragged * ((n1 * 0.7 + n2 * 0.3) - 0.55) * 0.9 - edge);

                // height blend: each surface's brightness is its relief; the higher one wins where they meet
                float3 alb[5]; float3 nrm[5]; float hgt[5]; float best = -1.0;
                // derivatives outside the branch: sampling inside it with implicit ones is undefined where weights differ
                float2 uvm = float2(i.uv.x * 2.6, i.uv.y), dx = ddx(uvm), dy = ddy(uvm);
                [unroll] for (int k = 0; k < 5; k++)
                {
                    alb[k] = 0; nrm[k] = float3(0, 0, 1); hgt[k] = -1.0;
                    [branch] if (w[k] > 0.004)
                    {
                        float2 uv = uvm / tile[k], gx = dx / tile[k], gy = dy / tile[k];
                        float4 c = k == 0 ? SAMPLE_TEXTURE2D_GRAD(_T0, sampler_T0, uv, gx, gy) : k == 1 ? SAMPLE_TEXTURE2D_GRAD(_T1, sampler_T0, uv, gx, gy) : k == 2 ? SAMPLE_TEXTURE2D_GRAD(_T2, sampler_T0, uv, gx, gy) : k == 3 ? SAMPLE_TEXTURE2D_GRAD(_T3, sampler_T0, uv, gx, gy) : SAMPLE_TEXTURE2D_GRAD(_T4, sampler_T0, uv, gx, gy);
                        float4 nn = k == 0 ? SAMPLE_TEXTURE2D_GRAD(_N0, sampler_N0, uv, gx, gy) : k == 1 ? SAMPLE_TEXTURE2D_GRAD(_N1, sampler_N0, uv, gx, gy) : k == 2 ? SAMPLE_TEXTURE2D_GRAD(_N2, sampler_N0, uv, gx, gy) : k == 3 ? SAMPLE_TEXTURE2D_GRAD(_N3, sampler_N0, uv, gx, gy) : SAMPLE_TEXTURE2D_GRAD(_N4, sampler_N0, uv, gx, gy);
                        alb[k] = c.rgb; nrm[k] = UnpackNormal(nn);
                        hgt[k] = dot(c.rgb, float3(0.3, 0.59, 0.11)) * 0.35 + w[k];
                        best = max(best, hgt[k]);
                    }
                }
                float3 albedo = 0, normalTS = 0; float smooth = 0, sum = 0;
                float4 tint[5] = { _Tint0, _Tint1, _Tint2, _Tint3, _Tint4 };
                [unroll] for (int j = 0; j < 5; j++)
                {
                    float b = max(hgt[j] - (best - 0.12), 0.0);
                    albedo += alb[j] * tint[j].rgb * b; normalTS += nrm[j] * b; smooth += tint[j].w * b; sum += b;
                }
                albedo /= max(sum, 1e-4); smooth /= max(sum, 1e-4);
                normalTS = normalize(float3(normalTS.xy / max(sum, 1e-4), 1.0));

                // weather: rain darkens and wets, winter snow lies in patches (less on asphalt, more at the edges)
                albedo *= lerp(1.0, 0.72, _Weather.y);
                smooth = lerp(smooth, 0.55, _Weather.y);
                float snowN = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, i.positionWS.xz * 0.11).r;
                float snow = saturate((snowN + edge * 0.35 - (1.0 - _Weather.x * (1.0 - w[0] * 0.6))) * 4.0) * saturate(_Weather.x * 20.0); // none without snow
                albedo = lerp(albedo, float3(0.86, 0.88, 0.92), snow);
                smooth = lerp(smooth, 0.25, snow);
                normalTS = normalize(lerp(normalTS, float3(0, 0, 1), snow * 0.7));

                float3 bit = cross(i.normalWS, i.tangentWS.xyz) * i.tangentWS.w;
                float3 nWS = normalize(TransformTangentToWorld(normalTS, float3x3(i.tangentWS.xyz, bit, i.normalWS)));

                InputData id = (InputData)0;
                id.positionWS = i.positionWS;
                id.positionCS = i.positionCS;
                id.normalWS = nWS;
                id.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                id.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                id.fogCoord = i.fog;
                id.bakedGI = SampleSH(nWS);
                id.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                id.shadowMask = half4(1, 1, 1, 1);

                SurfaceData sd = (SurfaceData)0;
                sd.albedo = albedo; sd.metallic = 0; sd.specular = 0; sd.smoothness = smooth;
                sd.normalTS = normalTS; sd.occlusion = 1; sd.emission = 0; sd.alpha = 1;
                half4 col = UniversalFragmentPBR(id, sd);
                col.rgb = MixFog(col.rgb, i.fog);
                return col;
            }
            ENDHLSL
        }

    }
    Fallback Off
}
