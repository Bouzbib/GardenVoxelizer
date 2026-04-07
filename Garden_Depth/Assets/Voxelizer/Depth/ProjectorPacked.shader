Shader "Hidden/ProjectorPacked"
{
    Properties
    {
        _PackedTex ("Packed Texture", 2D) = "black" {}
        _Exposure ("Exposure", Float) = 1
    }

    SubShader
    {
        Tags { "Queue"="Overlay" }
        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off
            Blend Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _PackedTex;
            float _Exposure;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            v2f vert(appdata_full v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float4 p = tex2D(_PackedTex, i.uv);
                return float4(p.rgb * _Exposure, 1.0);
            }
            ENDCG
        }
    }
}