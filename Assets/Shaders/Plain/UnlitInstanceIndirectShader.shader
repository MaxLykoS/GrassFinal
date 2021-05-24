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

            struct MeshProperties 
            {
                float4x4 mat; // world matrix
                float4 color;
            };
            StructuredBuffer<MeshProperties> _Properties;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;   
                float4 color : COLOR;
            };

            v2f vert(appdata v, uint instanceID : SV_InstanceID)
            {
                v2f o;
                o.vertex = mul(_Properties[instanceID].mat, v.vertex);
                o.vertex = mul(UNITY_MATRIX_VP, o.vertex);
                o.color = _Properties[instanceID].color;
                return o;
            }

            fixed4 frag(v2f o) : SV_Target
            {
                return o.color;
            }
            ENDCG
        }
    }
}
