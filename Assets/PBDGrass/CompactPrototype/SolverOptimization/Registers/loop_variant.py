"""Shorter instruction stream; explicit copy-in/out preserves sequential constraints."""
import re
def generate(source):
    contact='''            [loop] for(int s=0;s<SEGMENTS;s++)
            {
                float3 a=p[s],b=p[s+1];
                SegmentContact(a,b,previous[s],previous[s+1],mass[s],mass[s+1],root,fraction,oldFraction);
                p[s]=a;p[s+1]=b;
            }
            [loop] for(int n=1;n<NODES;n++)
            {float3 a=p[n];GroundContact(a,previous[n]);p[n]=a;}
'''
    begin=source.index('void SimulateBlade(');end=source.index('bool Occluded(',begin)
    body=source[begin:end]
    first=body.index('            SegmentContact(');bend=body.index('            BendTwist(')
    contact_original=body[first:bend]
    # The second contact pass is indented differently; replace by exact bounds.
    tail=body.index('            SegmentContact(',bend)
    velocity=body.index('        [unroll] for(int velocity=',tail)
    tail_contact=re.sub(r'\bs\b','tailContact',re.sub(r'\bn\b','tailGround',contact))
    iter_contact=re.sub(r'\bs\b','iterContact',re.sub(r'\bn\b','iterGround',contact))
    body=body[:tail]+tail_contact+body[velocity:]
    body=body.replace(contact_original,iter_contact)
    begin_bend=body.index('            BendTwist(');begin_align=body.index('            Align(')
    body=body[:begin_bend]+'''            BendTwist(anchor,q[0],rod[0],0,rod[0].InverseInertia*InverseBladeMass,true,h,bendLambda[0]);
            [loop] for(int bendIndex=1;bendIndex<SEGMENTS;bendIndex++)
            {
                float4 parent=q[bendIndex-1],child=q[bendIndex];float3 lambda=bendLambda[bendIndex];
                BendTwist(parent,child,rod[bendIndex],rod[bendIndex-1].InverseInertia*InverseBladeMass,
                    rod[bendIndex].InverseInertia*InverseBladeMass,false,h,lambda);
                q[bendIndex-1]=parent;q[bendIndex]=child;bendLambda[bendIndex]=lambda;
            }
'''+body[begin_align:]
    begin_align=body.index('            Align(');end_align=body.index('\n        }',begin_align)
    body=body[:begin_align]+'''            [loop] for(int alignIndex=0;alignIndex<SEGMENTS;alignIndex++)
            {
                float3 a=p[alignIndex],b=p[alignIndex+1],lambda=alignLambda[alignIndex];float4 rotation=q[alignIndex];float stretch=stretchLambda[alignIndex];
                Align(a,b,rotation,mass[alignIndex],mass[alignIndex+1],rod[alignIndex].InverseInertia*InverseBladeMass,h,lambda);
                Stretch(a,b,rod[alignIndex].Length,mass[alignIndex],mass[alignIndex+1],h,stretch);
                p[alignIndex]=a;p[alignIndex+1]=b;q[alignIndex]=rotation;alignLambda[alignIndex]=lambda;stretchLambda[alignIndex]=stretch;
            }'''+body[end_align:]
    # The tail contact replacement must retain the closing scope after velocity update.
    old='''[numthreads(32,1,1)]
void XPBDSolverLOD0(uint3 group:SV_GroupID,uint3 lane:SV_GroupThreadID)
{
    ActiveGrid grid=ActiveGridsBuffer[group.x]; SimulateBlade(grid.BladeOffset+lane.x,grid.PoolOffset+lane.x);
}'''
    new='''int SolverGridCount;
[numthreads(128,1,1)]
void XPBDSolverLOD0(uint3 id:SV_DispatchThreadID)
{
    if(id.x/32>=(uint)SolverGridCount)return;
    ActiveGrid grid=ActiveGridsBuffer[id.x/32];SimulateBlade(grid.BladeOffset+id.x%32,grid.PoolOffset+id.x%32);
}'''
    assert old in body
    body=body.replace(old,new)
    return source[:begin]+body+source[end:]
