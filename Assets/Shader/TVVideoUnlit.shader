// Unlit shader for video Quads, with a _White parameter that pushes the image to pure white.
// Works in Built-in and URP.
Shader "Custom/TVVideoUnlit"
{
    Properties
    {
        _MainTex ("Video Texture", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _White ("White Amount", Range(0,1)) = 0
        _WhiteIntensity ("White Intensity (>1 glows with Bloom)", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            Cull Off
            ZWrite On

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float _White;
            float _WhiteIntensity;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float4 col = tex2D(_MainTex, i.uv) * _Color;
                col.rgb = lerp(col.rgb, float3(1, 1, 1) * _WhiteIntensity, saturate(_White));
                return col;
            }
            ENDCG
        }
    }
}
