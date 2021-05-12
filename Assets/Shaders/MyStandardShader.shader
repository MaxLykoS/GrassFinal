Shader "Custom/MyStandardShader" 
{
	Properties
	{
		_MainTex("Main Tex", 2D) = "white" {}
		_BumpTex("Bump Tex", 2D) = "bump"{}
		_Specular("Specular", Color) = (1, 1, 1, 1)
		_Gloss("Gloss", Range(8.0, 256)) = 20
		_GlossScale("Gloss Scale", Range(0.0,1.0)) = 1.0
		_BumpScale("Bump Scale", Range(0,2)) = 1
	}
	SubShader
	{
		Tags { "RenderType" = "Opaque" }
		// Directional light caster
		Pass 
		{
			Tags { "LightMode" = "ForwardBase" }

			CGPROGRAM
			#pragma multi_compile_fwdbase	

			#pragma vertex VS
			#pragma fragment PS

			#include "Lighting.cginc"
			#include "AutoLight.cginc"

			sampler2D _MainTex;
			float4 _MainTex_ST;
			sampler2D _BumpTex;
			float4 _BumpTex_ST;
			fixed4 _Specular;
			float _Gloss;
			float _BumpScale;
			float _GlossScale;

			struct Input 
			{
				float4 pos : POSITION;
				float3 normal : NORMAL;
				float4 tan : TANGENT;
				float2 uv : TEXCOORD0;
			};

			struct Output 
			{
				float4 pos : SV_POSITION;
				float4 uv : TEXCOORD0;
				float3 posW : TEXCOORD1;
				float3 tanW : TEXCOORD2;
				float3 bitanW : TEXCOORD3;
				float3 normalW : TEXCOORD4;
				SHADOW_COORDS(5)
			};

			Output VS(Input v) 
			{
				v.normal = normalize(v.normal);

				Output o;

				o.pos = UnityObjectToClipPos(v.pos);

				o.uv.xy = v.uv.xy * _MainTex_ST.xy + _MainTex_ST.zw;
				o.uv.zw = v.uv.xy * _BumpTex_ST.xy + _BumpTex_ST.zw;

				o.posW = mul(unity_ObjectToWorld, v.pos).xyz;
				o.normalW = normalize(mul((float3x3)unity_WorldToObject, v.normal));
				o.tanW = normalize(mul((float3x3)UNITY_MATRIX_M, v.tan.xyz));
				o.bitanW = normalize(cross(o.normalW, o.tanW) * v.tan.w);

				TRANSFER_SHADOW(o);

				return o;
			}

			float4 PS(Output o) : SV_Target 
			{
				o.normalW = normalize(o.normalW);
				o.tanW = normalize(o.tanW - dot(o.tanW,o.normalW) * o.normalW);
				o.bitanW = normalize(o.bitanW);
				float3x3 t2w = float3x3(o.tanW, o.bitanW, o.normalW);
				float3 bump = UnpackNormal(tex2D(_BumpTex, o.uv.zw));
				bump.xy *= _BumpScale;
				bump.z = sqrt(1.0 - saturate(dot(bump.xy, bump.xy)));
				bump = normalize(mul(bump, t2w));

				fixed3 f2Light = normalize(_WorldSpaceLightPos0.xyz);
				fixed3 f2Cam = normalize(_WorldSpaceCameraPos.xyz - o.posW.xyz);
				fixed3 albedo = tex2D(_MainTex, o.uv.xy);
				fixed3 refDir = normalize(reflect(-f2Light,bump));

				fixed3 ambient = UNITY_LIGHTMODEL_AMBIENT.xyz * albedo.rgb;
				fixed3 diffuse = _LightColor0.rgb * albedo.rgb * max(0, dot(bump, f2Light));
				fixed3 specular = _LightColor0.rgb * _Specular.rgb * pow(max(0, dot(f2Cam, refDir)), _Gloss) * _GlossScale;

				UNITY_LIGHT_ATTENUATION(atten, o, o.posW);

				return float4(ambient + (diffuse + specular) * atten, 1.0);
			}

			ENDCG
		}
	
		// Other light source caster
		Pass 
		{
			Tags { "LightMode" = "ForwardAdd" }

			Blend One One

			CGPROGRAM
			#pragma multi_compile_fwdadd_fullshadows

			#pragma vertex VS
			#pragma fragment PS

			#include "Lighting.cginc"
			#include "AutoLight.cginc"
			#include "UnityCG.cginc"

			sampler2D _MainTex;
			float4 _MainTex_ST;
			sampler2D _BumpTex;
			float4 _BumpTex_ST;
			fixed4 _Specular;
			float _Gloss;
			float _BumpScale;
			float _GlossScale;

			struct Input 
			{
				float4 vertex : POSITION;
				float3 normal : NORMAL;
				float2 uv : TEXCOORD0;
				float4 tan : TANGENT;
			};

			struct Output 
			{
				float4 pos : SV_POSITION;
				float4 uv : TEXCOORD0;
				float3 posW : TEXCOORD1;
				float3 tanW : TEXCOORD2;
				float3 bitanW : TEXCOORD3;
				float3 normalW : TEXCOORD4;
				SHADOW_COORDS(5)
			};

			Output VS(Input v) 
			{
				v.normal = normalize(v.normal);

				Output o;
				
				o.uv.xy = v.uv * _MainTex_ST.xy + _MainTex_ST.zw;
				o.uv.zw = v.uv * _BumpTex_ST.xy + _BumpTex_ST.zw;

				o.pos = UnityObjectToClipPos(v.vertex);
				o.posW = mul(unity_ObjectToWorld, v.vertex);
				
				o.tanW = normalize(mul((float3x3)UNITY_MATRIX_M, v.tan.xyz));
				o.normalW = normalize(mul((float3x3)unity_WorldToObject,v.normal));
				o.bitanW = normalize(cross(o.tanW, o.normalW) * v.tan.w);
				
				TRANSFER_SHADOW(o);

				return o;
			}

			fixed4 PS(Output o) : SV_Target 
			{
				o.normalW = normalize(o.normalW);
				o.tanW = normalize(o.tanW - dot(o.normalW, o.tanW) * o.normalW);
				o.bitanW = normalize(o.bitanW);
				float3x3 t2w = float3x3(o.tanW, o.bitanW, o.normalW);
				float3 bump = UnpackNormal(tex2D(_BumpTex, o.uv.zw));
				bump.xy *= _BumpScale;
				bump.z = sqrt(1.0 - saturate(dot(bump.xy, bump.xy)));
				bump = normalize(mul(bump, t2w));

				#ifdef USING_DIRECTIONAL_LIGHT
				fixed3 f2Light = normalize(_WorldSpaceLightPos0.xyz);
				#else
				fixed3 f2Light = normalize(_WorldSpaceLightPos0.xyz - o.posW.xyz);
				#endif

				fixed3 diffuse = _LightColor0.rgb * tex2D(_MainTex, o.uv.xy).rgb * max(0, dot(bump, f2Light));

				fixed3 f2Cam = normalize(_WorldSpaceCameraPos.xyz - o.posW.xyz);
				fixed3 refDir = normalize(reflect(-f2Light, bump));
				fixed3 specular = _LightColor0.rgb * _Specular.rgb * pow(max(0, dot(f2Cam, refDir)), _Gloss) * _GlossScale;

				UNITY_LIGHT_ATTENUATION(atten, o, o.posW);

				return fixed4((diffuse + specular) * atten, 1.0);
			}

			ENDCG
		}

		// ShadowMapCaster
		Pass
		{
			Tags{"LightMode" = "ShadowCaster"}

			ZWrite On
			ZTest LEqual
			Cull Off

			CGPROGRAM

			#pragma vertex VS
			#pragma fragment PS
			#pragma multi-compile_shadowcaster

			#include "UnityCG.cginc"

			struct Output
			{
				V2F_SHADOW_CASTER;
				//float4 pos : SV_POSITION;
			};

			Output VS(appdata_base v)
			{
				Output o;
				TRANSFER_SHADOW_CASTER_NORMALOFFSET(o);
				return o;
			}

			fixed4 PS(Output o) : COLOR
			{
				SHADOW_CASTER_FRAGMENT(o);
			}

			ENDCG
		}
	}
}