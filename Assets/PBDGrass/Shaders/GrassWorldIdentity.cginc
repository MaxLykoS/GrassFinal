#ifndef GRASS_WORLD_IDENTITY_INCLUDED
#define GRASS_WORLD_IDENTITY_INCLUDED
uint WorldHash(uint x) { x^=x>>16; x*=0x7feb352d; x^=x>>15; x*=0x846ca68b; return x^(x>>16); }
uint WorldSeed(int2 cell,uint leaf) { return WorldHash(asuint(cell.x)*73856093u ^ asuint(cell.y)*19349663u ^ leaf*83492791u); }
float WorldUnit(uint x) { return (WorldHash(x)&0xffffff)/16777216.0; }
float WorldLeafSeed(int2 cell,uint leaf) { return WorldUnit(WorldSeed(cell,leaf)+2); }
float3 WorldRoot(int2 cell,uint leaf,float ground)
{
    uint h=WorldSeed(cell,leaf),variation=WorldSeed(cell,67);
    uint x=((leaf&1)<<2)+(((leaf>>2)&1)<<1)+((leaf>>4)&1),z=(((leaf>>1)&1)<<1)+((leaf>>3)&1);
    float2 uv=(float2(x,z)+.1+.8*float2(WorldUnit(h),WorldUnit(h+1)))/float2(8,4);
    if((variation&1)!=0)uv.x=1-uv.x; if((variation&2)!=0)uv.y=1-uv.y;
    if((variation&4)!=0)uv=uv.yx;
    return float3(cell.x+uv.x,ground,cell.y+uv.y);
}
float4 WorldYaw(int2 cell) { float angle=WorldUnit(WorldSeed(cell,73))*6.28318530718*.5; return float4(0,sin(angle),0,cos(angle)); }
int WorldMod(int x,int size) { int result=x%size; return result<0?result+size:result; }
uint WorldSlot(int2 cell,int size) { return WorldMod(cell.x,size)+WorldMod(cell.y,size)*size; }
#endif
