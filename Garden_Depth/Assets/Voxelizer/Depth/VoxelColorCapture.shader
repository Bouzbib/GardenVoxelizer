Shader "Hidden/VoxelColorCapture"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            ZWrite On
            ZTest LEqual
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _BaseMap;
            float4 _BaseMap_ST;
            float4 _Color;
            float4 _BaseColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uvMain : TEXCOORD0;
                float2 uvBase : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uvMain = TRANSFORM_TEX(v.uv, _MainTex);
                o.uvBase = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 texA = tex2D(_MainTex, i.uvMain);
                fixed4 texB = tex2D(_BaseMap, i.uvBase);

                fixed4 colA = texA * _Color;
                fixed4 colB = texB * _BaseColor;

                fixed3 rgb = colA.rgb;
                if (length(colB.rgb) > 0.0001) rgb = colB.rgb;

                return fixed4(rgb, 1);
            }
            ENDCG
        }
    }
}