// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'

// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "Custom/Terrain"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }

        Pass
        {
            CGPROGRAM

            #pragma vertex VS
            #pragma fragment PS

            fixed4 _Color;

            struct Input
            {
                float4 vertex : POSITION;
            };

            struct Output
            {
                float4 pos : SV_POSITION;
            };

            Output VS(Input v)
            {
                Output o;

                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 PS(Output o) : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }
}
