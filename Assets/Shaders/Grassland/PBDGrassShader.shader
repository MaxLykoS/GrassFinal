Shader "Custom/PBDGrassShader"
{
    Properties
    {
        [Header(Shading)]
        _TopColor("Top Color", Color) = (1,1,1,1)
        _BottomColor("Bottom Color", Color) = (1,1,1,1)
        _TranslucentGain("Translucent Gain", Range(0,1)) = 0.5

        [Header(Tessellation)]
        _TessellationUniform("Tessellation Uniform", Range(1, 64)) = 1
        
        [Header(BackLightSSS)]
        _InteriorColor("Interior Color", Color) = (1, 1, 1, 1)
        _BackSubsurfaceDistortion("Back Subsurface Distortion", Range(0, 1)) = 0.5
        _EdgeLitRate("Edge Light Rate", range(0, 2)) = 0.3
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }

        Cull Off

        Pass
        {
            CGPROGRAM

            #pragma vertex vert
            #pragma hull HS
            #pragma domain DS
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            #define PI 3.14159

            struct a2v
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2h
            {
                float4 posW : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float3 normalW : NORMAL;
                float3 viewDir : TEXCOORD2;
                float3 lightDir : TEXCOORD3;
            };

            struct h2d
            {
                float4 posW : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float3 normalW : NORMAL;
                float3 viewDir : TEXCOORD2;
                float3 lightDir : TEXCOORD3;
                float bend : TEXCOORD4;
            };

            struct d2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalW : NORMAL;
                float3 viewDir : TEXCOORD1;
                float3 lightDir : TEXCOORD2;
            };

            v2h vert(a2v v)
            {
                v2h o;

                o.posW = mul(unity_ObjectToWorld, v.vertex);
                o.uv = v.uv;

                o.normalW = normalize(UnityObjectToWorldNormal(v.normal));
                o.normalW *= o.normalW.y >= 0 ? 1 : -1;

                o.viewDir = normalize(_WorldSpaceCameraPos.xyz - o.posW.xyz);
                o.lightDir = normalize(_WorldSpaceLightPos0.xyz);
                return o;
            }

            float _TessellationUniform;

            struct TessellationFactors
            {
                float edge[3] : SV_TessFactor;
                float inside : SV_InsideTessFactor;
            };
            TessellationFactors patchConstantFunction(InputPatch<v2h, 3> patch)
            {
                float factor = _TessellationUniform;
                if (patch[0].uv.y < 0.34f || patch[0].uv.y > 0.67f)
                    factor = 1;
                TessellationFactors f;
                f.edge[0] = factor;
                f.edge[1] = factor;
                f.edge[2] = factor;
                f.inside = factor;
                return f;
            }
            [UNITY_domain("tri")]
            [UNITY_outputcontrolpoints(3)]
            [UNITY_outputtopology("triangle_cw")]
            [UNITY_partitioning("integer")]
            [UNITY_patchconstantfunc("patchConstantFunction")]
            h2d HS(InputPatch<v2h, 3> patch, uint id : SV_OutputControlPointID)
            {
                h2d o;

                o.posW = patch[id].posW;
                o.uv = patch[id].uv;
                o.normalW = patch[id].normalW;
                o.viewDir = patch[id].viewDir;
                o.lightDir = patch[id].lightDir;

                float bladeYDif = abs(patch[0].posW.y - patch[2].posW.y);
                o.bend = bladeYDif * 0.5f;

                return o;
            }

            [UNITY_domain("tri")]
            d2f DS(TessellationFactors factors, OutputPatch<h2d, 3> patch, float3 barycentricCoordinates : SV_DomainLocation)
            {
                h2d v;

                #define MY_DOMAIN_PROGRAM_INTERPOLATE(fieldName) v.fieldName = \
					patch[0].fieldName * barycentricCoordinates.x + \
					patch[1].fieldName * barycentricCoordinates.y + \
					patch[2].fieldName * barycentricCoordinates.z;

                MY_DOMAIN_PROGRAM_INTERPOLATE(posW)
                MY_DOMAIN_PROGRAM_INTERPOLATE(uv)
                MY_DOMAIN_PROGRAM_INTERPOLATE(normalW)
                MY_DOMAIN_PROGRAM_INTERPOLATE(viewDir)
                MY_DOMAIN_PROGRAM_INTERPOLATE(lightDir)
                MY_DOMAIN_PROGRAM_INTERPOLATE(bend)

                v.normalW = normalize(v.normalW);

                float displacement = (sin(v.uv.y * PI) - sin(0.333f * PI)) * v.bend;
                displacement = max(0, displacement);

                v.posW += float4(v.normalW * displacement, 0);

                d2f o;
                o.pos = mul(UNITY_MATRIX_VP, v.posW);
                o.uv = v.uv;
                o.normalW = v.normalW;
                o.viewDir = v.viewDir;
                o.lightDir = v.lightDir;
                return o;
            }

            float4 _TopColor;
            float4 _BottomColor;
            float _TranslucentGain;
            float _BackSubsurfaceDistortion;
            float _EdgeLitRate;
            float4 _InteriorColor;

            fixed4 frag(d2f o, fixed facing : VFACE) : SV_Target
            {
                o.normalW = facing > 0 ? o.normalW : -o.normalW;

                float4 texColor = lerp(_BottomColor, _TopColor, o.uv.y);
                float NdotL = saturate(saturate(dot(o.normalW, _WorldSpaceLightPos0)) + _TranslucentGain);

                // back light sss
                float3 backLitDir = o.normalW * _BackSubsurfaceDistortion + o.lightDir;
                float backSSS = saturate(dot(o.viewDir, -backLitDir));
                backSSS = saturate(pow(backSSS, 3));
                fixed3 edgeCol = backSSS * _EdgeLitRate * _InteriorColor * texColor.rgb;
                edgeCol += backSSS * _InteriorColor;

                float3 ambient = ShadeSH9(float4(o.normalW, 1));
                float4 lightIntensity = NdotL * _LightColor0 + float4(ambient, 1);
                float4 col = lerp(_BottomColor, _TopColor * lightIntensity, o.uv.y);
                return col + fixed4(edgeCol, 1);
            }
            ENDCG
        }
    }
}
