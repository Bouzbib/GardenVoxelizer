Shader "Unlit/VoxelSliceDebug"
{
    Properties
    {
        _VoxelTex ("Voxel Texture", 3D) = "" {}
        _Slice01 ("Slice 0..1", Range(0,1)) = 0
        _Axis ("Axis (0=Z only for now)", Float) = 0
        _Threshold ("Threshold", Range(0,1)) = 0.5
        _Invert ("Invert", Float) = 0
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
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler3D _VoxelTex;
            float _Slice01;
            float _Threshold;
            float _Invert;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
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
                float3 coord = float3(i.uv.x, i.uv.y, _Slice01);
                float v = tex3D(_VoxelTex, coord).r;
                v = (v > _Threshold) ? 1.0 : 0.0;

                if (_Invert > 0.5)
                    v = 1.0 - v;

                return float4(v, v, v, 1);
            }
            ENDCG
        }
    }
}