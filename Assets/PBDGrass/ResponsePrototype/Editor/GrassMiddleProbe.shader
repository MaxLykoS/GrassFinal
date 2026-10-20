Shader "Hidden/PBDGrass/MiddlePresentationProbe"
{
    SubShader
    {
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "../../Shaders/GrassMiddlePresentation.cginc"
            StructuredBuffer<float3> ProbePositions;
            int ProbeBlade;
            float3 ProbeRoot;
            float ProbeSeed,ProbeCoverage;
            float4 frag(v2f_img input):SV_Target
            {
                uint vertex=min((uint)(input.uv.x*15),14);
                float3 target,normal,width;MiddleOuterTarget(ProbeRoot,vertex,target,normal,width);
                float weight=MiddlePresentationWeight(ProbeCoverage,ProbeSeed);
                return float4(lerp(ProbePositions[ProbeBlade*15+vertex],target,weight),weight);
            }
            ENDCG
        }
    }
}
