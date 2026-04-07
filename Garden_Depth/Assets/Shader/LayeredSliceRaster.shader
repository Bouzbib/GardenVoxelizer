Shader "Hidden/LayeredSliceRaster"
{
    Properties
    {
        _Color("Color", Color) = (1,1,1,1)
        _MainTex("Main Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend One One

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;
            float4 _ObjectColor;
            float4 _FallbackSliceColor;
            float _SlicePos;
            float _SliceHalfThickness;
            int _SliceAxis;
            float _TargetVoxelType;
            float _ObjectVoxelType;
            float _ShellThicknessNorm;
            float3 _VolumeMinWS;
            float3 _VolumeMaxWS;

            sampler2D _MainTex;
            float4 _MainTex_ST;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float4 w = mul(unity_ObjectToWorld, v.vertex);
                o.worldPos = w.xyz;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }


            fixed4 frag(v2f i) : SV_Target
            {
                float axisValue =
                    (_SliceAxis == 0) ? i.worldPos.x :
                    (_SliceAxis == 1) ? i.worldPos.y :
                                        i.worldPos.z;

                float dist = abs(axisValue - _SlicePos);

                if (dist > _SliceHalfThickness)
                    discard;

                if (abs(_ObjectVoxelType - _TargetVoxelType) > 0.25)
                    discard;

                if (i.worldPos.x < _VolumeMinWS.x || i.worldPos.x > _VolumeMaxWS.x ||
                    i.worldPos.y < _VolumeMinWS.y || i.worldPos.y > _VolumeMaxWS.y ||
                    i.worldPos.z < _VolumeMinWS.z || i.worldPos.z > _VolumeMaxWS.z)
                    discard;

                float2 uv;

                if (_SliceAxis == 0)        // X → ZY
                    uv = (i.worldPos.zy - _VolumeMinWS.zy) / (_VolumeMaxWS.zy - _VolumeMinWS.zy);
                else if (_SliceAxis == 1)   // Y → XZ
                    uv = (i.worldPos.xz - _VolumeMinWS.xz) / (_VolumeMaxWS.xz - _VolumeMinWS.xz);
                else                        // Z → XY
                    uv = (i.worldPos.xy - _VolumeMinWS.xy) / (_VolumeMaxWS.xy - _VolumeMinWS.xy);

                float4 texCol = tex2D(_MainTex, i.uv);

                // base tint fallback
                float3 baseColor = _ObjectColor.rgb;
                if (baseColor.r <= 0.001 && baseColor.g <= 0.001 && baseColor.b <= 0.001)
                    baseColor = _Color.rgb;
                if (baseColor.r <= 0.001 && baseColor.g <= 0.001 && baseColor.b <= 0.001)
                    baseColor = _FallbackSliceColor.rgb;

                // combine texture and tint
                float3 c = texCol.rgb * baseColor;

                // keep your current binary-per-channel packing logic
                c = step(0.33, c);

                return float4(c, saturate(_ShellThicknessNorm));
            }
            ENDHLSL
        }
    }
}
