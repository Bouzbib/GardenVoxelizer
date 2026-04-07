Shader "Unlit/PackBitDebugInterlaced"
{
    Properties
    {
        _MainTex ("Packed", 2D) = "black" {}
        _Channel ("Channel (0=R,1=G,2=B)", Float) = 0
        _Bit ("Bit (0-7)", Float) = 0
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
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Channel;
            float _Bit;
            float _Invert;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float4 c = tex2D(_MainTex, i.uv);

                float channelValue =
                    (_Channel < 0.5) ? c.r :
                    (_Channel < 1.5) ? c.g :
                                       c.b;

                float byteValue = floor(channelValue * 255.0 + 0.5);
                float bitMask = pow(2.0, _Bit);
                float bit = fmod(floor(byteValue / bitMask), 2.0);

                if (_Invert > 0.5)
                    bit = 1.0 - bit;

                return float4(bit, bit, bit, 1);
            }
            ENDCG
        }
    }
}