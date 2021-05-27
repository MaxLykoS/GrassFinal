Shader "Custom/UnlitInstanceIndirectShader"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing nolightmap nodirlightmap nodynlightmap novertexlight
            
            #include "UnityCG.cginc"

            StructuredBuffer<int3> posVisibleBuffer;
            float3 camPos;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;   
                float3 worldPos : TEXCOORD0;
            };

            v2f vert(appdata v, uint instanceID : SV_InstanceID)
            {
                v2f o;
                o.worldPos = posVisibleBuffer[instanceID] + v.vertex;
                o.vertex = mul(UNITY_MATRIX_VP, float4(o.worldPos, 1));
                return o;
            }

            fixed4 frag(v2f o) : SV_Target
            {
                float dist = distance(camPos, o.worldPos);
                float4 color = float4(0, 0, 0, 1);
                if (dist <= 10)
                    color.r = 1;
                else if (dist <= 20)
                    color.g = 1;
                else
                    color.b = 1;
                return color;
            }
            ENDCG
        }
    }
}
