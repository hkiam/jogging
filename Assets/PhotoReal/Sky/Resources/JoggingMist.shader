// Ground fog (World/GroundMist.cs): thin horizontal sheets a few metres above valley floors and
// fields, stacked, with drifting noise and a soft edge. Seen flat from above they are faint; seen at a
// grazing angle (from the trail) the path through them is long, so they read as a fog layer. Near the
// camera and at the camera's own height they fade out, so no sheet cuts through the view.
Shader "Jogging/Mist"
{
    Properties
    {
        _Noise ("Noise", 2D) = "gray" {}
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);
            CBUFFER_START(UnityPerMaterial)
                float4 _Noise_ST;
            CBUFFER_END
            float4 _MistColor;   // rgb lit mist, w global amount
            float4 _MistDrift;   // xy drift (m), z time
            float _Alpha;        // per patch (property block): fade in/out × layer

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float fog : TEXCOORD2;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = i.uv;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float4 frag(Varyings i) : SV_Target
            {
                float2 c = i.uv * 2.0 - 1.0;
                float radial = 1.0 - smoothstep(0.35, 1.0, length(c));
                float2 w = (i.positionWS.xz + _MistDrift.xy) / 70.0;
                float n = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, w).r * 0.6 + SAMPLE_TEXTURE2D(_Noise, sampler_Noise, w * 2.7 + _MistDrift.z * 0.004).g * 0.4;
                float a = radial * smoothstep(0.3, 0.8, n) * _Alpha * _MistColor.w;

                float3 v = i.positionWS - _WorldSpaceCameraPos;
                float dist = length(v);
                float path = min(1.0 / (abs(v.y) / max(dist, 0.001) + 0.08), 5.0) / 5.0; // longer path through a flat layer at a grazing view
                a *= path;
                a *= smoothstep(6.0, 30.0, dist) * smoothstep(0.3, 2.0, abs(v.y) + dist * 0.02);
                float3 col = MixFog(_MistColor.rgb, i.fog);
                return float4(col, saturate(a));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
