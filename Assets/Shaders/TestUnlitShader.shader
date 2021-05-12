Shader "Custom/TestUnlitShader"
{
    Properties
    {
        _Color("Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }

        Pass
        {
            Cull Off
            CGPROGRAM

            #pragma vertex VS
            #pragma fragment PS
            #pragma multi_compile_instancing

            fixed4 _Color;

            struct Input
            {
                float3 vertex : POSITION;
                float2 uv : TEXCOORD;
            };

            struct Output
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Output VS(Input v)
            {
                Output o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 PS(Output o) : SV_Target
            {
                return fixed4(o.uv, 0, 1);
                return _Color;
            }
            ENDCG
        }
    }
}
