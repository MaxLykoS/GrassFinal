// Presentation target matches GrassOuterCoverage.shader. It never writes simulation,
// plasticity, history or final compute geometry. Root-space wind and density IDs stay shared.
#include "GrassWorldIdentity.cginc"
StructuredBuffer<float4> OuterControls;
sampler2D OuterWindNoise;
float3 OuterWind;
float OuterTime,OuterMaxWind,OuterHalfWidth;
float MiddlePresentationWeight(float coverage,float seed)
{float t=saturate((1-coverage)/max(1-seed,1e-6));return t*t*(3-2*t);}
float3 MiddleSafe(float3 v) {return v*rsqrt(max(dot(v,v),1e-8));}
float3 MiddleYaw(float3 v,float2 facing) {return float3(facing.x*v.x-facing.y*v.z,v.y,facing.y*v.x+facing.x*v.z);}
float3 MiddleControl(uint control,int2 cell,float2 f)
{
    uint index=(cell.y*5+cell.x)*3+control;
    return lerp(lerp(OuterControls[index].xyz,OuterControls[index+3].xyz,f.x),lerp(OuterControls[index+15].xyz,OuterControls[index+18].xyz,f.x),f.y);
}
void MiddleOuterVertex(float3 root,float2 facing,float3 c1,float3 c2,float3 tip,float t,float side,out float3 world,out float3 normal,out float3 width)
{
    t=saturate(t);float u=max(0,1-t);
    float3 center=3*u*u*t*c1+3*u*t*t*c2+t*t*t*tip;
    float3 tangent=MiddleSafe(3*u*u*c1+6*u*t*(c2-c1)+3*t*t*(tip-c2));
    float3 across=MiddleYaw(float3(1,0,0),facing);across=MiddleSafe(across-tangent*dot(across,tangent));
    // FXC's dynamic pow(0, .75) can produce a non-finite tip. Evaluate a positive
    // base and set the exact tip width explicitly, including interpolated samples.
    float profile=t>=1?0:(.72+.28*sin(UNITY_PI*t))*pow(max(u,1e-8),.75);
    float edge=abs(dot(across,MiddleSafe(_WorldSpaceCameraPos.xyz-root-center)));
    world=root+center+across*(side*OuterHalfWidth*profile*lerp(1,1.6,smoothstep(.55,.95,edge)));
    normal=MiddleSafe(cross(across,tangent));width=across;
}
void MiddleOuterTarget(float3 root,uint vertex,out float3 world,out float3 normal,out float3 width)
{
    int2 worldCell=(int2)floor(root.xz);float angle=WorldUnit(WorldSeed(worldCell,73))*6.28318530718;
    float2 facing=float2(cos(angle),-sin(angle));
    float2 scroll=length(OuterWind.xz)>.0001?normalize(OuterWind.xz):float2(1,0);
    float2 noise=tex2Dlod(OuterWindNoise,float4(root.xz*.01+scroll*OuterTime*.1,0,0)).rg;
    float3 wind=OuterWind*lerp(.55,1.45,noise.x)+cross(float3(0,1,0),OuterWind)*(noise.y*2-1)*.2;
    float3 localWind=MiddleYaw(wind,float2(facing.x,-facing.y));
    float2 uv=(clamp(localWind.xz/max(OuterMaxWind,.01),-1,1)+1)*2;
    int2 cell=min((int2)floor(uv),3);float2 f=uv-cell;
    float3 c1=MiddleYaw(MiddleControl(0,cell,f),facing),c2=MiddleYaw(MiddleControl(1,cell,f),facing),tip=MiddleYaw(MiddleControl(2,cell,f),facing);
    uint node=vertex==14?7:vertex/2;
    // Include every knot of the seven-vertex outer mesh so its straight edges are reproduced.
    float t=node<=4?node/6.0:node==5?5.0/6:node==6?11.0/12:1;
    float scaled=t*3;float segment=min(floor(scaled),2),blend=scaled-segment;
    // Core and outer strips use opposite even/odd sides and opposite triangle order.
    // Keep the core winding, front normal and existing UV orientation consistent.
    float side=vertex==14?0:(vertex%2==0?1:-1);
    float3 a,na,wa,b,nb,wb;
    MiddleOuterVertex(root,facing,c1,c2,tip,segment/3,side,a,na,wa);
    MiddleOuterVertex(root,facing,c1,c2,tip,segment>=2?1:(segment+1)/3,side,b,nb,wb);
    world=lerp(a,b,blend);normal=MiddleSafe(lerp(na,nb,blend));width=MiddleSafe(lerp(wa,wb,blend));
}
