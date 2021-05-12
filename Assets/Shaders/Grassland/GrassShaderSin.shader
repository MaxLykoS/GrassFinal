// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "Custom/GrassShaderSin" 
{
	Properties
	{
		_MainTex("Albedo (RGB)", 2D) = "white" {}
		_AlphaTex("Alpha (A)", 2D) = "white" {}
		_Height("Grass Height", float) = 3
		_Width("Grass Width", range(0, 0.1)) = 0.05
		_LOD("Vertex Count", int) = 12
	}
	SubShader
	{
		Cull off
		Tags{ "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" "IgnoreProjector" = "True" "DisableBatching" = "True"}

		CGINCLUDE
		#include "UnityCG.cginc" 
		#include "UnityLightingCommon.cginc" // 用来处理光照的一些效果

		sampler2D _MainTex;
		sampler2D _AlphaTex;

		float _Height;//草的高度
		float _Width;//草的宽度

		int _LOD;

		struct a2v
		{
			float4 vertex : POSITION;
			float3 normal : NORMAL;
			float2 texcoord : TEXCOORD;
		};

		struct v2g
		{
			float4 pos : SV_POSITION;
			float3 normal : NORMAL;
			float2 uv : TEXCOORD0;
		};

		struct g2f
		{
			float4 pos : SV_POSITION;
			float3 normal : NORMAL;
			float2 uv : TEXCOORD0;
		};

		static const float vibeDelta = 0.05;

		v2g VS(a2v v)
		{
			v2g o;
			o.pos = v.vertex;
			o.normal = v.normal;
			o.uv = v.texcoord;
			return o;
		}

		g2f createGSOut()
		{
			g2f output;

			output.pos = float4(0, 0, 0, 1);
			output.normal = float3(0, 0, 0);
			output.uv = float2(0, 0);

			return output;
		}

		void GS(point v2g points[1],in uint LOD, inout TriangleStream<g2f> triStream)
		{
			float4 root = points[0].pos;

			const int vertexCount = LOD;

			float random = sin(UNITY_HALF_PI * frac(root.x) + UNITY_HALF_PI * frac(root.z));
			float randomRotation = 0;

			float4x4 rotationYMatrix = (cos(randomRotation), 0, sin(randomRotation), 0,
										0,					 1.0f, 0,                0,
									   -sin(randomRotation), 0, cos(randomRotation), 0,
										0,                   0,     0,               1.0f);

			_Width += random / 50;
			_Height += random / 5;

			g2f v[12] =
			{
				createGSOut() ,createGSOut() ,createGSOut() ,createGSOut() ,
				createGSOut() ,createGSOut() ,createGSOut() ,createGSOut() ,
				createGSOut() ,createGSOut() ,createGSOut() ,createGSOut()
			};

			// UV
			float currentV = 0;
			float offsetV = 1.0f / (vertexCount/2 - 1);
			
			//	Height
			float currentHeightOffset = 0;
			float currentVertexHeight = 0;

			// Wind
			float windCoEff = 0;

			for (int i = 0; i < vertexCount; i++)
			{
				v[i].normal = float3(0, 0, 1);

				if (fmod(i, 2) == 0)
				{
					v[i].pos = float4(root.x - _Width, root.y + currentVertexHeight, root.z, 1);
					v[i].uv = float2(0, currentV);
				}
				else
				{
					v[i].pos = float4(root.x + _Width, root.y + currentVertexHeight, root.z, 1);
					v[i].uv = float2(1, currentV);

					currentV += offsetV;
					currentVertexHeight = currentV * _Height;
				}

				// First rotate (translate to origin)
				//v[i].pos = float4(v[i].pos.x - root.x, v[i].pos.y - root.y, v[i].pos.z - root.z, 1.0f);
				//v[i].pos = mul(rotationYMatrix, v[i].pos);
				//v[i].pos = float4(v[i].pos.x + root.x, v[i].pos.y + root.y, v[i].pos.z + root.z, 1.0f);

				// WIND
				float2 wind = float2(sin(_Time.x * UNITY_PI * 5), sin(_Time.x * UNITY_PI * 5));
				wind.x += (sin(_Time.x + root.x / 25) + sin((_Time.x + root.x / 15) + 50)) * 0.5;
				wind.y += cos(_Time.x + root.z / 80);
				wind *= lerp(0.7, 1.0, 1.0 - random);

				float vibeStrength = 2.5f;  // 震荡强度
				float sinSkewCoeff = random; // 倾斜系数
				float lerpCoeff = (sin(vibeStrength * _Time.x + sinSkewCoeff) + 1.0) / 2;
				float2 leftWindBound = wind * (1.0 - vibeDelta);
				float2 rightWindBound = wind * (1.0 + vibeDelta);

				wind = lerp(leftWindBound, rightWindBound, lerpCoeff);

				float randomAngle = lerp(-UNITY_PI, UNITY_PI, random);
				float randomMagnitude = lerp(0, 1, random);
				float2 randomWindDir = float2(sin(randomAngle), cos(randomAngle));
				wind += randomWindDir * randomMagnitude;

				v[i].pos.xz += wind.xy * windCoEff;
				v[i].pos.y -= length(wind) * windCoEff * 0.8;

				v[i].pos = UnityObjectToClipPos(v[i].pos);

				if (fmod(i, 2) == 1)
				{
					windCoEff += offsetV;
				}
			}

			for (int p = 0; p < (vertexCount - 2); p++)
			{
				triStream.Append(v[p]);
				triStream.Append(v[p + 2]);
				triStream.Append(v[p + 1]);
			}
		}

		half4 PS(g2f IN) : COLOR
		{
			fixed4 color = tex2D(_MainTex, IN.uv);
			fixed4 alpha = tex2D(_AlphaTex, IN.uv);

			return fixed4(color.rgb, alpha.g);
		}
		ENDCG

		Pass
		{
			Cull OFF
			Tags{ "LightMode" = "ForwardBase" }
			AlphaToMask On

			CGPROGRAM

			#pragma vertex VS
			#pragma geometry GS_LOD
			#pragma fragment PS
			
			#pragma target 4.0
			
			[maxvertexcount(30)]
			void GS_LOD(point v2g points[1], inout TriangleStream<g2f> triStream)
			{
				GS(points, _LOD, triStream);
			}		

			ENDCG
		}
	}
}