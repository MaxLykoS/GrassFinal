Shader "Custom/ArcheryEmissive" {
     Properties {
         _Color ("Color", Color) = (1,1,1,1)
         _MainTex ("Albedo (RGB)", 2D) = "white" {}
         _Glossiness ("Smoothness", Range(0,1)) = 0.5
         _Metallic ("Metallic", 2D) = "black" {}
         _NormalMap ("Normals", 2D) = "bump"{}
         _AOMap("Ambient Occlusion",2D) = "white"{}
 
         _Cutoff ("Alpha Cutoff", Range (0.0, 1.0)) = 0.5
         
         //Adding the Emissive maps
         _Emissive1 ("Primary Emissive", 2D) = "black"{}
         _EmissionIntensity ("Intensity", Range(0,3)) = 1.0
         _Emissive2 ("Secondary Emissive", 2D) = "black"{}
         _Tween ("Transition", Range(0,1)) = 0.0
 
                 // Blending state
         [HideInInspector] _Mode ("__mode", Float) = 0.0
         [HideInInspector] _SrcBlend ("__src", Float) = 1.0
         [HideInInspector] _DstBlend ("__dst", Float) = 0.0
         [HideInInspector] _ZWrite ("__zw", Float) = 1.0
 
 
     }
     SubShader {
         Tags { "RenderType"="Opaque" }
         LOD 300
 
         Blend [_SrcBlend] [_DstBlend]
         ZWrite [_ZWrite]
         
         CGPROGRAM
         // Physically based Standard lighting model, and enable shadows on all light types
         #pragma surface surf Standard fullforwardshadows
 
         #pragma shader_feature _ _ALPHATEST_ON _ALPHABLEND_ON _ALPHAPREMULTPIY_ON
 
         // Use shader model 3.0 target, to get nicer looking lighting
         #pragma target 3.0
 
         sampler2D _MainTex;
         sampler2D _Metallic;
         sampler2D _NormalMap;
         sampler2D _AOMap;
         sampler2D _Emissive1;
         sampler2D _Emissive2;
 
         struct Input {
             float2 uv_MainTex;
             float2 uv_Metallic;
             float2 uv_NormalMap;
             float2 uv_AOMap;
             float2 uv_Emissive1;
             float2 uv_Emissive2;
         };
 
         half _Glossiness;
         fixed4 _Color;
         fixed _Cutoff;
         float _EmissionIntensity;
         float _Tween;
 
 
 
         void surf (Input IN, inout SurfaceOutputStandard o) {
             // Albedo comes from a texture tinted by color
             fixed4 c = tex2D (_MainTex, IN.uv_MainTex) * _Color;
             o.Albedo = c.rgb;
             // Metallic and smoothness come from slider variables
             o.Metallic = tex2D (_Metallic, IN.uv_Metallic);
             fixed smooth = _Glossiness * tex2D(_Metallic, IN.uv_Metallic).a;
             o.Smoothness = smooth;
             
             //Adding normal maps
             o.Normal = UnpackNormal (tex2D(_NormalMap, IN.uv_NormalMap));
 
             //Ambient Occlusion
             o.Occlusion = tex2D(_AOMap, IN.uv_AOMap);
 
             //emission mapping and the transition between two maps
             float3 Em1 = tex2D(_Emissive1, IN.uv_Emissive1).rgb;
             float3 Em2 = tex2D(_Emissive2, IN.uv_Emissive2).rgb;
             o.Emission = c.rgb * lerp(Em1, Em2, _Tween) * _EmissionIntensity;
 
             o.Alpha = c.a;
         }
         ENDCG
     }
     FallBack "VertexLit"
     CustomEditor "CustomShaderGUI"
 }