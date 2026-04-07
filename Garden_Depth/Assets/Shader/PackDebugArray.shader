Shader "Unlit/PackDebugArray"
{
    Properties
    {
        _Frames ("Frames", 2DArray) = "" {}
        _PackIndex ("Pack Index", Int) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            ZWrite Off Cull Off

            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            UNITY_DECLARE_TEX2DARRAY(_Frames);
            int _PackIndex;

            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };

            v2f vert(appdata v){ v2f o; o.pos=UnityObjectToClipPos(v.vertex); o.uv=v.uv; return o; }

            float4 frag(v2f i) : SV_Target
            {
                return UNITY_SAMPLE_TEX2DARRAY(_Frames, float3(i.uv, _PackIndex));
            }
            ENDHLSL
        }
    }
}