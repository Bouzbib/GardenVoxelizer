Shader "Unlit/DebugSliceArray"
{
    Properties
    {
        _SliceArray ("Slice Array", 2DArray) = "" {}
        _SliceIndex ("Slice Index", Int) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            UNITY_DECLARE_TEX2DARRAY(_SliceArray);
            int _SliceIndex;

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
                return UNITY_SAMPLE_TEX2DARRAY(_SliceArray, float3(i.uv, _SliceIndex));
            }
            ENDHLSL
        }
    }
}