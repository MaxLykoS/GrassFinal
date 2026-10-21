#ifndef GRASS_COMPACT_PACKING_INCLUDED
#define GRASS_COMPACT_PACKING_INCLUDED
// 20 uints / blade: 21 half coordinates in words 0..10, 7 octahedral
// width directions in 11..17, and two reserved words. Root is implicit zero.
#if defined(COMPACT_POSE_WRITABLE)
RWStructuredBuffer<uint> CompactPose;
#else
StructuredBuffer<uint> CompactPose;
#endif
float2 CompactSign(float2 v) { return float2(v.x>=0?1:-1,v.y>=0?1:-1); }
uint CompactPackDirection(float3 v)
{
    v/=max(abs(v.x)+abs(v.y)+abs(v.z),1e-10);
    float2 oct=v.z>=0?v.xy:(1-abs(v.yx))*CompactSign(v.xy);
    uint2 packed=(uint2)round(saturate(oct*.5+.5)*65535);
    return packed.x|(packed.y<<16);
}
float3 CompactUnpackDirection(uint bits)
{
    float2 f=float2(bits&65535,bits>>16)*(2.0/65535)-1;
    float3 v=float3(f,1-abs(f.x)-abs(f.y));
    if(v.z<0)v.xy=(1-abs(v.yx))*CompactSign(v.xy);
    return normalize(v);
}
float CompactCoordinate(uint blade,uint coordinate)
{
    uint bits=CompactPose[blade*20+coordinate/2];
    return f16tof32((bits>>((coordinate&1)*16))&65535);
}
float3 CompactPosition(uint blade,uint node)
{
    if(node==0)return 0;
    uint i=(node-1)*3;
    return float3(CompactCoordinate(blade,i),CompactCoordinate(blade,i+1),CompactCoordinate(blade,i+2));
}
float3 CompactWidth(uint blade,uint segment) { return CompactUnpackDirection(CompactPose[blade*20+11+segment]); }
// Plastic bending is bounded by 85 degrees at the root and 1.2 rad elsewhere.
// Signed 16-bit fixed point over [-2,2] keeps exact zero and has <=3.06e-5 rad error.
uint CompactPackPlastic(float2 p)
{
    int2 q=(int2)round(clamp(p,-2,2)*(32767.0/2));
    return (asuint(q.x)&65535)|((asuint(q.y)&65535)<<16);
}
float2 CompactUnpackPlastic(uint bits)
{
    int x=(int)(bits<<16)>>16,y=(int)bits>>16;
    return float2(x,y)*(2.0/32767);
}
#endif
