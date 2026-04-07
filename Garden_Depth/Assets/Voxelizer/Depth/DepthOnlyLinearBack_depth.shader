Shader "Hidden/DepthOnlyLinearBack_depth"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }

        Pass
        {
            Cull Front
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float3 _VolumeMinWS;
            float3 _VolumeSizeWS;
            int _SliceAxis;

            float AxisValue(float3 v, int axis)
            {
                return (axis == 0) ? v.x : ((axis == 1) ? v.y : v.z);
            }

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 ws  : TEXCOORD0;
            };

            v2f vert(appdata_full v)
            {
                v2f o;
                float4 w = mul(unity_ObjectToWorld, v.vertex);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.ws = w.xyz;
                return o;
            }

            float frag(v2f i) : SV_Target
            {
                float axisMin  = AxisValue(_VolumeMinWS, _SliceAxis);
                float axisSize = AxisValue(_VolumeSizeWS, _SliceAxis);
                float axisPos  = AxisValue(i.ws, _SliceAxis);

                return saturate((axisPos - axisMin) / max(axisSize, 1e-6));
            }
            ENDCG
        }
    }
}