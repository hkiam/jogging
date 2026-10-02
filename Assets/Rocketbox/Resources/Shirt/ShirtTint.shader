// Shirt colour (World/RealFigure): the body texture with its shirt area (mask R) in a new colour, keeping the
// folds (mask G: 0.5 + G = brightness). Used once per figure with Graphics.Blit into a render texture.
Shader "Hidden/Jogging/ShirtTint"
{
    Properties
    {
        _MainTex ("Body", 2D) = "white" {}
        _Mask ("Shirt mask", 2D) = "black" {}
        _Tint ("Colour", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex, _Mask;
            float4 _Tint;
            float4 frag(v2f_img i) : SV_Target
            {
                float4 c = tex2D(_MainTex, i.uv);
                float4 m = tex2D(_Mask, i.uv);
                c.rgb = lerp(c.rgb, _Tint.rgb * (0.5 + m.g), m.r);
                return c;
            }
            ENDCG
        }
    }
}
