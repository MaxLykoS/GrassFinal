Shader "PBDGrass/Workspace/StatelessOuterCoverage"
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
        struct OuterInstance { float3 Root; float Seed; float2 Facing; float WidthScale,Fade; };
        StructuredBuffer<OuterInstance> OuterInstances;
        StructuredBuffer<float4> OuterControls;
        sampler2D OuterWindNoise;
        float3 OuterWind;
        float OuterTime,OuterMaxWind,OuterHalfWidth;
        float _NormalCurvature,_ClumpSize;
        float3 SafeNormal(float3 v) { return v*rsqrt(max(dot(v,v),1e-8)); }
        float HashClump(float2 p) { float3 h=frac(float3(p.xyx)*0.1031); h+=dot(h,h.yzx+33.33); return frac((h.x+h.y)*h.z); }
        float3 RotateYaw(float3 v,float2 facing) { return float3(facing.x*v.x-facing.y*v.z,v.y,facing.y*v.x+facing.x*v.z); }
        float3 Control(uint control,int2 cell,float2 f)
        {
            uint index=(cell.y*5+cell.x)*3+control;
            return lerp(lerp(OuterControls[index].xyz,OuterControls[index+3].xyz,f.x),lerp(OuterControls[index+15].xyz,OuterControls[index+18].xyz,f.x),f.y);
        }
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
            #pragma multi_compile_local __ GRASS_LOD_BASE_COLORS
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
                #if defined(GRASS_LOD_BASE_COLORS)
                    nointerpolation float xpbd : TEXCOORD8;
                #endif
            };
            v2f vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                v2f o;
                OuterInstance leaf=OuterInstances[instanceID];
                // Five triangles reference a seven-vertex ribbon; no stored vertex buffer.
                uint vertex=vertexID==0?0:vertexID==1?1:vertexID==2?2:vertexID==3?1:vertexID==4?3:vertexID==5?2:
                    vertexID==6?2:vertexID==7?3:vertexID==8?4:vertexID==9?3:vertexID==10?5:vertexID==11?4:vertexID==12?4:vertexID==13?5:6;
                float t=vertex==6?1:(vertex/2)/3.0,u=1-t;
                float2 scroll=length(OuterWind.xz)>.0001?normalize(OuterWind.xz):float2(1,0);
                float2 noise=tex2Dlod(OuterWindNoise,float4(leaf.Root.xz*.01+scroll*OuterTime*.1,0,0)).rg;
                float3 wind=OuterWind*lerp(.55,1.45,noise.x)+cross(float3(0,1,0),OuterWind)*(noise.y*2-1)*.2;
                float3 localWind=RotateYaw(wind,float2(leaf.Facing.x,-leaf.Facing.y));
                float2 uv=(clamp(localWind.xz/max(OuterMaxWind,.01),-1,1)+1)*2;
                int2 cell=min((int2)floor(uv),3); float2 f=uv-cell;
                float3 c1=RotateYaw(Control(0,cell,f),leaf.Facing),c2=RotateYaw(Control(1,cell,f),leaf.Facing),tip=RotateYaw(Control(2,cell,f),leaf.Facing);
                float3 center=(3*u*u*t*c1+3*u*t*t*c2+t*t*t*tip)*leaf.Fade;
                float3 tangent=SafeNormal(3*u*u*c1+6*u*t*(c2-c1)+3*t*t*(tip-c2));
                float3 across=RotateYaw(float3(1,0,0),leaf.Facing); across=SafeNormal(across-tangent*dot(across,tangent));
                float profile=(.72+.28*sin(UNITY_PI*t))*pow(u,.75);
                float side=vertex==6?0:(vertex%2==0?-1:1);
                float width=OuterHalfWidth*profile*leaf.WidthScale*leaf.Fade;
                // Modest edge-on thickening, bounded to preserve silhouettes and avoid billboard rotation.
                float3 view=SafeNormal(_WorldSpaceCameraPos.xyz-leaf.Root-center);
                float edge=abs(dot(across,view)); float thicken=lerp(1,1.6,smoothstep(.55,.95,edge));
                o.world=leaf.Root+center+across*(side*width*thicken);
                o.pos=UnityWorldToClipPos(o.world); o.uv=float2(vertex==6?.5:(vertex%2),t);
                o.normal=SafeNormal(cross(across,tangent)); o.width=-across;
                float angle=HashClump(floor(leaf.Root.xz/max(_ClumpSize,.25)))*6.2831853;
                o.clumpNormal=float3(cos(angle)*.35,.93675,sin(angle)*.35); o.seed=leaf.Seed;
                #if defined(GRASS_LOD_BASE_COLORS)
                    o.xpbd=-1;
                #endif
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
                #if defined(GRASS_LOD_BASE_COLORS)
                    // Replace only the BRDF base color. Normal, GGX, transmission, AO,
                    // light/shadow attenuation and fog continue through the same path.
                    albedo=float3(1,.45,0);
                #endif
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
    }
    Fallback Off
}
