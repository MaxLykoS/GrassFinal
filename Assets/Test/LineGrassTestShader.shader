Shader "Custom/LineGrassTestShader"
{
    Properties
	{
		[Header(Shading)]
		_TopColor("Top Color", Color) = (1,1,1,1)
		_BottomColor("Bottom Color", Color) = (1,1,1,1)
		_TessellationUniform("Tessellation Uniform", Range(1, 64)) = 1
		[Header(Blades)]
		_BladeWidth("Blade Width", Float) = 0.05
		[Header(Wind)]
		_WindDistortionMap("Wind Distortion Map", 2D) = "white"{}
		_WindFrequency("Wind Frequency", Vector) = (0.05, 0.05, 0, 0)
		_WindStrength("Wind Strength", Float) = 1
		[Header(BackLightSSS)]
		_InteriorColor("Interior Color", Color) = (1, 1, 1, 1)
		_BackSubsurfaceDistortion("Back Subsurface Distortion", Range(0, 1)) = 0.5
		_EdgeLitRate("Edge Light Rate", range(0, 2)) = 0.3
		[Header(Trample)]
		_EffectTopOffset("Effect Top Offset", float) = 2
		_EffectBottomOffset("Effect Bottom Offset", float) = -1
		_EffectRadius("Effect Radius", float) = 2
		_OffsetMultiplier("Offset Multiplier", range(0.1,200)) = 2
	}

	CGINCLUDE
	#include "UnityCG.cginc"
	#include "Autolight.cginc"

	#define BLADE_SEGMENTS 3

	float _BendRotationRandom;
	float _BladeHeight;
	float _BladeHeightRandom;
	float _BladeWidth;
	float _BladeWidthRandom;
	float _TessellationUniform;
	sampler2D _WindDistortionMap;
	float4 _WindDistortionMap_ST;
	float2 _WindFrequency;
	float _WindStrength;
	float _BladeForward;
	float _BladeCurve;
	float _EffectTopOffset;
	float _EffectBottomOffset;
	float _EffectRadius;
	float _OffsetMultiplier;

	// hidden parameters
	float _PositionArrayLen;
	float3 _ObstaclePositions[100];
	// hidden parameters

	float rand(float3 co)
	{
		return frac(sin(dot(co.xyz, float3(12.9898, 78.233, 53.539))) * 43758.5453);
	}

	float3x3 AngleAxis3x3(float angle, float3 axis)
	{
		float c, s;
		sincos(angle, s, c);

		float t = 1 - c;
		float x = axis.x;
		float y = axis.y;
		float z = axis.z;

		return float3x3(
			t * x * x + c, t * x * y - s * z, t * x * z + s * y,
			t * x * y + s * z, t * y * y + c, t * y * z - s * x,
			t * x * z - s * y, t * y * z + s * x, t * z * z + c
		);
	}

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

	struct g2f
	{
		float4 pos : SV_POSITION;
		float2 uv : TEXCOORD0;
		float3 normal : NORMAL;
		float3 viewDir : TEXCOORD1;
		float3 lightDir : TEXCOORD2;
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
		o.vertex = v.vertex;
		o.normal = v.normal;
		o.tangent = v.tangent;

		return o;
	}

	g2f GenV(float3 vertexPosition, float width, float height, float forward, float2 uv, float3x3 transformMatrix)
	{
		g2f o;
		float3 tangentPoint = float3(width, forward, height);
		float3 tangentNormal = normalize(float3(0, -1, forward));
		float3 localNormal = mul(transformMatrix, tangentNormal);
		float3 localPosition = vertexPosition + mul(transformMatrix, tangentPoint);

		o.pos = UnityObjectToClipPos(localPosition);
		o.uv = uv;
		o.normal = normalize(UnityObjectToWorldNormal(localNormal));
		o.viewDir = normalize(_WorldSpaceCameraPos.xyz - mul(unity_ObjectToWorld, localPosition).xyz);
		o.lightDir = normalize(_WorldSpaceLightPos0.xyz);
		return o;
	}

	g2f GenVTrample(float3 vertexPosition, float width, float height, float forward, float2 uv, float3x3 transformMatrix)
	{
		g2f o;
		float3 tangentPoint = float3(width, forward, height);
		float3 tangentNormal = normalize(float3(0, -1, forward));
		float3 localNormal = mul(transformMatrix, tangentNormal);
		float3 localPosition = vertexPosition + mul(transformMatrix, tangentPoint);
		float3 worldPosition = mul(unity_ObjectToWorld, float4(localPosition, 1));	

		for (int i = 0; i < _PositionArrayLen; i++)
		{
			// get char top and bottom pos as effect range in Y.
			float charTopY = _ObstaclePositions[i].y + _EffectTopOffset;
			float charBottomY = _ObstaclePositions[i].y - _EffectBottomOffset;
			float charY = clamp(worldPosition.y, charBottomY, charTopY); // when Y is within the Range (charTopY to charBottomY)

			// get bend force by distance --------------------------
			float dist = distance(float3(_ObstaclePositions[i].x, charY, _ObstaclePositions[i].z), worldPosition); // get distance of char and vertices
			float effectRate = clamp(_EffectRadius - dist, 0, _EffectRadius); // get bend rate within 0 ~ max
			float3 bendDir = normalize(worldPosition - float3 (_ObstaclePositions[i].x, worldPosition.y, _ObstaclePositions[i].z)); // get bend dir
			float3 bendForce = bendDir * effectRate;

			float gravityDistRate = float2 (worldPosition.x - _ObstaclePositions[i].x, worldPosition.z - _ObstaclePositions[i].z) * effectRate * forward;

			// get final bend force
			float3 finalBendForce = bendForce * _OffsetMultiplier;

			// set bend force to vertices offset ================================================
			worldPosition += finalBendForce;
		}

		o.pos = mul(UNITY_MATRIX_VP, float4(worldPosition, 1));
		o.uv = uv;
		o.normal = normalize(UnityObjectToWorldNormal(localNormal));
		o.viewDir = normalize(_WorldSpaceCameraPos.xyz - worldPosition);
		o.lightDir = normalize(_WorldSpaceLightPos0.xyz);
		return o;
	}

	[maxvertexcount(BLADE_SEGMENTS * 2 + 1)]
	void GS(triangle VertexOutput IN[3] : SV_POSITION, inout TriangleStream<g2f> triStream)
	{
		VertexOutput root = IN[0];

		float3x3 facingRotationMatrix = AngleAxis3x3(rand(root.vertex) * UNITY_TWO_PI, float3(0, 0, 1));
		float3x3 bendRotationMatrix = AngleAxis3x3(rand(root.vertex.zzx) * _BendRotationRandom * UNITY_PI * 0.5, float3(-1, 0, 0));

		float2 windUV = root.vertex.xz * _WindDistortionMap_ST.xy + _WindDistortionMap_ST.zw + _WindFrequency * _Time.y;
		float2 windSample = (tex2Dlod(_WindDistortionMap, float4(windUV, 0, 0)).xy * 2 - 1) * _WindStrength;
		float3 wind = normalize(float3(windSample.x, windSample.y, 0));
		float3x3 windRotation = AngleAxis3x3(UNITY_PI * windSample, wind);

		float3 bitangent = cross(root.normal, root.tangent) * root.tangent.w;
		float3x3 tangentToLocal = float3x3(
			root.tangent.x, bitangent.x, root.normal.x,
			root.tangent.y, bitangent.y, root.normal.y,
			root.tangent.z, bitangent.z, root.normal.z
			);
	
		float3x3 transformationMatrix = mul(mul(mul(tangentToLocal, windRotation), facingRotationMatrix), bendRotationMatrix);
		float3x3 transformationMatrixFacing = mul(tangentToLocal, facingRotationMatrix);

		float height = (rand(root.vertex.zyx) * 2 - 1) * _BladeHeightRandom + _BladeHeight;
		float width = (rand(root.vertex.xzy) * 2 - 1) * _BladeWidthRandom + _BladeWidth;
		float forward = rand(root.vertex.yyz) * _BladeForward;

		for (int i = 0; i < BLADE_SEGMENTS; i++)
		{
			float t = i / (float)BLADE_SEGMENTS;

			float segmentHeight = height * t;
			float segmentWidth = width * (1 - t);
			float segmentForward = pow(t, _BladeCurve) * forward;

			if (i == 0) //2 roots
			{
				triStream.Append(GenV(root.vertex, segmentWidth, segmentHeight, segmentForward, float2(0, t), transformationMatrixFacing));
				triStream.Append(GenV(root.vertex, -segmentWidth, segmentHeight, segmentForward, float2(1, t), transformationMatrixFacing));
			}
			else //vertices on top
			{
				triStream.Append(GenVTrample(root.vertex, segmentWidth, segmentHeight, segmentForward, float2(0, t), transformationMatrix));
				triStream.Append(GenVTrample(root.vertex, -segmentWidth, segmentHeight, segmentForward, float2(1, t), transformationMatrix));
			}
		}
		triStream.Append(GenVTrample(root.vertex, 0, height, forward, float2(0.5, 1), transformationMatrix));
	}
	ENDCG

	SubShader
	{
		Cull Off

		Pass
		{
			Tags
			{
				"RenderType" = "Opaque"
				"LightMode" = "ForwardBase"
			}

			CGPROGRAM
			#pragma vertex VS
			#pragma hull HS
			#pragma domain DS
			#pragma geometry GS
			#pragma fragment PS
			#pragma target 4.6

			#include "Lighting.cginc"

			float4 _TopColor;
			float4 _BottomColor;
			float _TranslucentGain;
			float _BackSubsurfaceDistortion;
			float _EdgeLitRate;
			float4 _InteriorColor;

			fixed4 PS(g2f o, fixed facing : VFACE) : SV_Target
			{
				o.normal = facing > 0 ? o.normal : -o.normal;

				float4 texColor = lerp(_BottomColor, _TopColor, o.uv.y);
				float NdotL = saturate(saturate(dot(o.normal, _WorldSpaceLightPos0)) + _TranslucentGain);

				// back light sss
				float3 backLitDir = o.normal * _BackSubsurfaceDistortion + o.lightDir;
				float backSSS = saturate(dot(o.viewDir, -backLitDir));
				backSSS = saturate(pow(backSSS, 3));
				fixed3 edgeCol = backSSS * _EdgeLitRate * _InteriorColor * texColor.rgb; //***
				edgeCol += backSSS * _InteriorColor;

				float3 ambient = ShadeSH9(float4(o.normal, 1));
				float4 lightIntensity = NdotL * _LightColor0 + float4(ambient, 1);
				float4 col = lerp(_BottomColor, _TopColor * lightIntensity, o.uv.y);
				return col + fixed4(edgeCol, 1);
			}
			ENDCG
		}
	}
}
