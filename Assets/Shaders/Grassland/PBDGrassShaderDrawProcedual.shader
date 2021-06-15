Shader "Custom/PBDGrassShaderDrawProcedual"
{
    Properties
    {
        _Color("Color", Color) = (1, 1, 1, 1)
    }

        SubShader
    {
        Tags { "RenderType" = "Opaque" }

        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;
            StructuredBuffer<float3> VertexBuffer;
            StructuredBuffer<int> TriangleBuffer;

            struct v2f
            {
                float4 vertex : SV_POSITION;
            };  

            v2f vert(uint vertex_id : SV_VertexID)
            {
                v2f o;

                int positionIndex = TriangleBuffer[vertex_id];
                float3 position = VertexBuffer[positionIndex];

                o.vertex = mul(UNITY_MATRIX_VP, float4(position, 1.0f));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }
}
