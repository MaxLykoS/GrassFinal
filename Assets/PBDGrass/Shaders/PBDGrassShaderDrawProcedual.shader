Shader "PBDGrass/Workspace/PBDGrassShaderDrawProcedual"
{
    Properties
    {
        [Header(Debug)]
        [Enum(Lit,0,SimulationNormals,1,ShadingNormals,2,SpecularNormals,3,FacingNormals,4,SpecularOnly,5)] _DebugView("Debug View", Float) = 0
        [Header(Leaf Color)]
        _TopColor("Tip Color", Color) = (0.58,0.72,0.30,1)
        _BottomColor("Root Color", Color) = (0.12,0.25,0.055,1)
        _AlternateColor("Alternate Leaf Tint", Color) = (1.12,0.96,0.72,1)
        _ColorMap("Leaf Color Variation (RGB)", 2D) = "white" {}
        _VeinMap("Vein Profile (1D)", 2D) = "white" {}
        _ColorVariation("Alternate Leaf Amount", Range(0,1)) = 0.35
        [Header(Leaf Surface)]
        _GlossMap("Gloss Profile (1D)", 2D) = "gray" {}
        _Roughness("Roughness", Range(0.18,1)) = 0.48
        _SpecularStrength("Dielectric Specular Strength", Range(0,1)) = 0.7
        _NormalCurvature("Rounded Cross Section", Range(0,1)) = 0.65
        _SpecularAA("Specular Anti Aliasing", Range(0,2)) = 1
        _ClumpSize("Shading Clump Size (m)", Range(0.25,4)) = 1.5
        [Header(Thin Leaf Transmission)]
        _Transmission("Transmission", Range(0,1)) = 0.55
        _TransmissionColor("Transmission Tint", Color) = (1,0.94,0.56,1)
        _TransmissionPower("Forward Scattering Power", Range(1,12)) = 4
        _RootOcclusion("Root Ambient Occlusion", Range(0,1)) = 0.65
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "IgnoreProjector"="True" }
        Cull Off
        CGINCLUDE
        #include "UnityCG.cginc"
        StructuredBuffer<float3> VertexBuffer;
        StructuredBuffer<int> TriangleBuffer;
        StructuredBuffer<float3> NormalBuffer;
        StructuredBuffer<float2> UvBuffer;
        struct PBDGrid { float3 pos; int idOffset; };
        struct GrassBlade { float3 root; float length; uint id; float width; float height; float seed; };
        StructuredBuffer<PBDGrid> GridsVisibleBuffer;
        StructuredBuffer<GrassBlade> BladesBuffer;
        float _NormalCurvature, _ClumpSize;
        float3 SafeNormal(float3 v) { return v * rsqrt(max(dot(v,v),1e-8)); }
        uint LeafIndex(uint vertexID, uint instanceID)
        { return TriangleBuffer[GridsVisibleBuffer[instanceID].idOffset * 39 + vertexID]; }
        float HashClump(float2 p)
        { float3 h=frac(float3(p.xyx)*0.1031); h+=dot(h,h.yzx+33.33); return frac((h.x+h.y)*h.z); }
        ENDCG
        Pass
        {
            Name "LEAF_FORWARD"
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight
            #pragma multi_compile_fog
            #include "Lighting.cginc"
            #include "AutoLight.cginc"
            #include "UnityStandardBRDF.cginc"
            sampler2D _ColorMap, _VeinMap, _GlossMap;
            float4 _TopColor, _BottomColor, _AlternateColor, _TransmissionColor;
            float _ColorVariation, _Roughness, _SpecularStrength, _SpecularAA;
            float _Transmission, _TransmissionPower, _RootOcclusion;
            float _DebugView;
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 world : TEXCOORD1;
                float3 normal : TEXCOORD2;
                float3 width : TEXCOORD3;
                float3 clumpNormal : TEXCOORD4;
                nointerpolation float seed : TEXCOORD5;
                SHADOW_COORDS(6)
                UNITY_FOG_COORDS(7)
            };
            v2f vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                v2f o;
                uint index=LeafIndex(vertexID,instanceID), blade=index/15;
                uint pair=blade*15+min((index%15)/2,6)*2;
                GrassBlade leaf=BladesBuffer[blade];
                o.world=VertexBuffer[index];
                o.pos=UnityWorldToClipPos(o.world);
                o.uv=UvBuffer[index];
                o.normal=SafeNormal(NormalBuffer[index]);
                o.width=SafeNormal(VertexBuffer[pair]-VertexBuffer[pair+1]);
                // Coherent clump normals suppress independent leaf glints at small footprints.
                float angle=HashClump(floor(leaf.root.xz/max(_ClumpSize,0.25)))*6.2831853;
                o.clumpNormal=float3(cos(angle)*0.35,0.93675,sin(angle)*0.35);
                o.seed=leaf.seed;
                TRANSFER_SHADOW_WPOS(o,o.world);
                UNITY_TRANSFER_FOG(o,o.pos);
                return o;
            }
            float3 LeafSpecular(float3 n, float3 v, float3 l, float roughness)
            {
                float3 h=Unity_SafeNormalize(v+l);
                // Use the same two-sided normal-map convention and GGX helpers as Unity Standard.
                float nv=abs(dot(n,v)), nl=saturate(dot(n,l));
                float nh=saturate(dot(n,h)), lh=saturate(dot(l,h));
                float a=max(PerceptualRoughnessToRoughness(roughness),0.002);
                float distribution=GGXTerm(nh,a);
                float visibility=SmithJointGGXVisibilityTerm(nl,nv,a);
                float specularTerm=distribution*visibility*UNITY_PI;
                #ifdef UNITY_COLORSPACE_GAMMA
                    specularTerm=sqrt(max(1e-4,specularTerm));
                #endif
                // Gamma uses 0.220916301 here; Linear uses 0.04. Do not mix the conventions.
                return max(0,specularTerm*nl)*FresnelTerm(unity_ColorSpaceDielectricSpec.rgb,lh)*_SpecularStrength;
            }
            half4 frag(v2f i, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                float3 v=SafeNormal(_WorldSpaceCameraPos.xyz-i.world);
                float3 l=SafeNormal(UnityWorldSpaceLightDir(i.world));
                float3 width=SafeNormal(i.width);
                float3 flat=SafeNormal(i.normal);
                // World-space normals encoded directly; debug output bypasses lighting and fog.
                if(_DebugView>0.5 && _DebugView<1.5) return half4(flat*0.5+0.5,1);
                // Canonical front-surface normals stay in world space, independent of the view.
                float3 n=SafeNormal(flat+width*((1-2*i.uv.x)*_NormalCurvature));
                if(_DebugView>1.5 && _DebugView<2.5) return half4(n*0.5+0.5,1);
                float footprint=length(float2(ddx(i.uv.x),ddy(i.uv.x)));
                // Preserve resolved leaf highlights; clump filtering starts near a one-pixel leaf width.
                float filter=saturate((footprint-0.75)*0.8)*saturate(_SpecularAA);
                // Align the clump to the leaf's hemisphere, never to the camera's hemisphere.
                float3 clump=i.clumpNormal*(dot(i.clumpNormal,flat)>=0?1:-1);
                float3 specNormal=SafeNormal(lerp(n,clump,filter*0.6));
                if(_DebugView>2.5 && _DebugView<3.5) return half4(specNormal*0.5+0.5,1);
                float variance=dot(ddx(specNormal),ddx(specNormal))+dot(ddy(specNormal),ddy(specNormal));
                // Two-sided BRDF orientation is separate from canonical normal reconstruction.
                float side=isFrontFace?1:-1;
                n*=side; specNormal*=side;
                if(_DebugView>3.5 && _DebugView<4.5) return half4(specNormal*0.5+0.5,1);
                float gloss=tex2D(_GlossMap,float2(i.uv.x,0.5)).r;
                float roughness=clamp(_Roughness*lerp(1.18,0.78,gloss),0.18,1);
                roughness=sqrt(saturate(roughness*roughness+min(variance*_SpecularAA*0.25,0.06)+filter*0.04));
                UNITY_LIGHT_ATTENUATION(attenuation,i,i.world);
                float3 specular=LeafSpecular(specNormal,v,l,roughness)*_LightColor0.rgb*attenuation;
                // The exact direct-specular contribution at unit gain; no diffuse, transmission, ambient or fog.
                if(_DebugView>4.5) return half4(specular,1);
                float3 albedo=lerp(_BottomColor.rgb,_TopColor.rgb,smoothstep(0,1,i.uv.y));
                albedo*=tex2D(_ColorMap,float2(i.seed,i.uv.y)).rgb;
                albedo*=tex2D(_VeinMap,float2(i.uv.x,0.5)).r;
                albedo*=lerp(1,_AlternateColor.rgb,smoothstep(0.35,0.85,i.seed)*_ColorVariation);
                float nl=dot(n,l);
                float3 diffuse=albedo*(unity_ColorSpaceDielectricSpec.a*(1-_Transmission*0.35)*saturate(nl)/UNITY_PI);
                // Transmission follows the light's color, intensity and shadow attenuation.
                float forward=pow(saturate(dot(v,-l)),_TransmissionPower);
                float transmitted=saturate(-nl+0.25)/1.25;
                float3 transmission=albedo*_TransmissionColor.rgb*(_Transmission*transmitted*(0.6+1.4*forward)/UNITY_PI);
                // Built-in light intensities use Lambert units (the same pi compensation as Unity Standard).
                float3 direct=(diffuse+transmission)*UNITY_PI*_LightColor0.rgb*attenuation+specular;
                float ao=lerp(1-_RootOcclusion,1,smoothstep(0,0.65,i.uv.y));
                float3 ambient=max(0,ShadeSH9(float4(n,1)))*albedo*ao;
                half4 color=half4(max(0,direct+ambient),1);
                UNITY_APPLY_FOG(i.fogCoord,color);
                return color;
            }
            ENDCG
        }
        Pass
        {
            Name "LEAF_SHADOW"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_shadowcaster
            struct shadowV2F { V2F_SHADOW_CASTER; };
            shadowV2F shadowVert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                shadowV2F o;
                uint index=LeafIndex(vertexID,instanceID);
                float3 world=VertexBuffer[index], normal=SafeNormal(NormalBuffer[index]);
                float cosine=dot(normal,SafeNormal(UnityWorldSpaceLightDir(world)));
                normal*=cosine>=0?1:-1;
                world-=normal*(unity_LightShadowBias.z*sqrt(saturate(1-cosine*cosine)));
                o.pos=UnityApplyLinearShadowBias(UnityWorldToClipPos(world));
                #if defined(SHADOWS_CUBE) && !defined(SHADOWS_CUBE_IN_DEPTH_TEX)
                    o.vec=world-_LightPositionRange.xyz;
                #endif
                return o;
            }
            float4 shadowFrag(shadowV2F i) : SV_Target { SHADOW_CASTER_FRAGMENT(i) }
            ENDCG
        }
    }
    FallBack Off
}
