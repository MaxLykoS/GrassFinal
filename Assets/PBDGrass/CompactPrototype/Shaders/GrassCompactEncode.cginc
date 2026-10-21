void WriteCompactPose(uint blade,float3 p[8],float4 q[7])
{
    float coordinates[22];
    [unroll] for(uint n=1;n<8;n++) { coordinates[(n-1)*3]=p[n].x;coordinates[(n-1)*3+1]=p[n].y;coordinates[(n-1)*3+2]=p[n].z; }
    coordinates[21]=0;
    [unroll] for(uint pair=0;pair<11;pair++)
    {
        uint a=pair*2,b=a+1;
        float va=coordinates[a],vb=coordinates[b];
        CompactPose[blade*20+pair]=f32tof16(va)|(f32tof16(vb)<<16);
    }
    [unroll] for(uint s=0;s<7;s++)
    {
        float3 t=SafeNormal(p[s+1]-p[s],float3(0,1,0)),w=QRotate(q[s],float3(1,0,0));
        w=SafeNormal(w-t*dot(w,t),float3(1,0,0));
        CompactPose[blade*20+11+s]=CompactPackDirection(w);
    }
    CompactPose[blade*20+18]=0;CompactPose[blade*20+19]=0;
}
