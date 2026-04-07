Shader "Unlit/PackedRGBDebug"
{
    Properties
    {
        _MainTex ("Packed Tex", 2D) = "black" {}
        _Gain ("Gain", Float) = 255
        _ShowR ("Show R", Float) = 1
        _ShowG ("Show G", Float) = 1
        _ShowB ("Show B", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Gain;
            float _ShowR, _ShowG, _ShowB;

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

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float4 c = tex2D(_MainTex, i.uv);
                float3 rgb = float3(c.r * _ShowR, c.g * _ShowG, c.b * _ShowB) * _Gain;
                return float4(saturate(rgb), 1.0);
            }
            ENDCG
        }
    }
}