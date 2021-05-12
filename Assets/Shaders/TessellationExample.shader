Shader "Roystan/Tessellation Example"
{
	Properties
	{
		_Color("Color", Color) = (1,1,1,1)
		_TessellationUniform ("Tessellation Uniform", Range(1, 64)) = 1
	}
	SubShader
	{
		Pass
		{
			CGPROGRAM
			#pragma vertex VS
			#pragma fragment PS
			#pragma hull HS
			#pragma domain DS
			#pragma target 4.6
			
			#include "UnityCG.cginc"

			float _TessellationUniform;
			float4 _Color;

			struct VertexInput
			{
				float4 vertex : POSITION;
				float3 normal : NORMAL;
				float4 tangent : TANGENT;
			};

			struct VertexOutput
			{
				float4 vertex : SV_POSITION;
				float3 normal : NORMAL;
				float4 tangent : TANGENT;
			};

			VertexInput VS(VertexInput v)
			{
				return v;
			}

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};
			TessellationFactors patchConstantFunction(InputPatch<VertexInput, 3> patch)
			{
				TessellationFactors f;
				f.edge[0] = _TessellationUniform;
				f.edge[1] = _TessellationUniform;
				f.edge[2] = _TessellationUniform;
				f.inside = _TessellationUniform;
				return f;
			}
			[UNITY_domain("tri")]
			[UNITY_outputcontrolpoints(3)]
			[UNITY_outputtopology("triangle_cw")]
			[UNITY_partitioning("integer")]
			[UNITY_patchconstantfunc("patchConstantFunction")]
			VertexInput HS(InputPatch<VertexInput, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[UNITY_domain("tri")]
			VertexOutput DS(TessellationFactors factors, OutputPatch<VertexInput, 3> patch, float3 barycentricCoordinates : SV_DomainLocation)
			{
				VertexOutput v;

				#define MY_DOMAIN_PROGRAM_INTERPOLATE(fieldName) v.fieldName = \
					patch[0].fieldName * barycentricCoordinates.x + \
					patch[1].fieldName * barycentricCoordinates.y + \
					patch[2].fieldName * barycentricCoordinates.z;

				MY_DOMAIN_PROGRAM_INTERPOLATE(vertex)
				MY_DOMAIN_PROGRAM_INTERPOLATE(normal)
				MY_DOMAIN_PROGRAM_INTERPOLATE(tangent)

				VertexOutput o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.normal = v.normal;
				o.tangent = v.tangent;

				return o;
			}
			
			float4 PS (VertexOutput o) : SV_Target
			{
				return _Color;
			}
			ENDCG
		}
	}
}
