"""Transpose active states for coalesced neighboring-lane access; preserve external buffers."""
def generate(source):
    begin=source.index('void SimulateBlade(');end=source.index('bool Occluded(',begin)
    body=source[begin:end]
    body=body.replace('BoneInfo bone=BonesBuffer[pool*NODES+load];',
        'BoneInfo bone;bone.Position=SolverPositions[load*SolverSlotCount+pool];bone.Velocities=SolverVelocities[load*SolverSlotCount+pool];')
    body=body.replace('Frame frame=FramesBuffer[pool*SEGMENTS+loadFrame];',
        'Frame frame;frame.Rotation=SolverRotations[loadFrame*SolverSlotCount+pool];frame.AngularVelocity=SolverAngular[loadFrame*SolverSlotCount+pool];')
    body=body.replace('pool*SEGMENTS+loadFrame','loadFrame*SolverSlotCount+pool')
    body=body.replace('uint n=pool*NODES+save;', 'uint n=save*SolverSlotCount+pool;')
    body=body.replace('BonesBuffer[n].Position=p[save]; BonesBuffer[n].Predicted=p[save]; BonesBuffer[n].Velocities=',
                      'SolverPositions[n]=p[save];SolverVelocities[n]=')
    body=body.replace('uint s=pool*SEGMENTS+saveFrame;', 'uint s=saveFrame*SolverSlotCount+pool;')
    body=body.replace('FramesBuffer[s].Rotation=', 'SolverRotations[s]=').replace('FramesBuffer[s].AngularVelocity=', 'SolverAngular[s]=')
    body=body.replace('PlasticBendsBuffer[pool*SEGMENTS+joint]', 'PlasticBendsBuffer[joint*SolverSlotCount+pool]')
    body=body.replace('PlasticBendsBuffer[pool*SEGMENTS]', 'PlasticBendsBuffer[pool]')
    body=body.replace('PlasticBendsBuffer[pool*7+s]', 'PlasticBendsBuffer[s*SolverSlotCount+pool]')
    body=body.replace('PlasticBendsBuffer[','SolverPlastic[')
    body=body.replace('[numthreads(32,1,1)]\nvoid XPBDSolverLOD0(uint3 group:SV_GroupID,uint3 lane:SV_GroupThreadID)',
        '[numthreads(128,1,1)]\nvoid XPBDSolverLOD0(uint3 id:SV_DispatchThreadID)')
    body=body.replace('ActiveGrid grid=ActiveGridsBuffer[group.x]; SimulateBlade(grid.BladeOffset+lane.x,grid.PoolOffset+lane.x);',
        'if(id.x/32>=(uint)SolverGridCount)return;ActiveGrid grid=ActiveGridsBuffer[id.x/32];SimulateBlade(grid.BladeOffset+id.x%32,grid.PoolOffset+id.x%32);')
    declarations='''RWStructuredBuffer<float3> SolverPositions,SolverVelocities,SolverAngular;
RWStructuredBuffer<float4> SolverRotations,SolverPlastic;
int SolverSlotCount,SolverGridCount;
'''
    staging='''
[numthreads(128,1,1)]
void StageSolver(uint3 id:SV_DispatchThreadID)
{
    if(id.x/32>=(uint)SolverGridCount)return;ActiveGrid grid=ActiveGridsBuffer[id.x/32];uint pool=grid.PoolOffset+id.x%32;
    [unroll] for(int n=0;n<NODES;n++){
        uint target=n*SolverSlotCount+pool;BoneInfo bone=BonesBuffer[pool*NODES+n];
        SolverPositions[target]=bone.Position;SolverVelocities[target]=bone.Velocities;
    }
    [unroll] for(int s=0;s<SEGMENTS;s++){
        uint target=s*SolverSlotCount+pool;Frame frame=FramesBuffer[pool*SEGMENTS+s];
        SolverRotations[target]=frame.Rotation;SolverAngular[target]=frame.AngularVelocity;
        SolverPlastic[target]=PlasticBendsBuffer[pool*SEGMENTS+s];
    }
}
[numthreads(128,1,1)]
void UnstageSolver(uint3 id:SV_DispatchThreadID)
{
    if(id.x/32>=(uint)SolverGridCount)return;ActiveGrid grid=ActiveGridsBuffer[id.x/32];uint pool=grid.PoolOffset+id.x%32;
    [unroll] for(int n=0;n<NODES;n++){
        uint source=n*SolverSlotCount+pool;
        BonesBuffer[pool*NODES+n].Position=SolverPositions[source];BonesBuffer[pool*NODES+n].Predicted=SolverPositions[source];
        BonesBuffer[pool*NODES+n].Velocities=SolverVelocities[source];
    }
    [unroll] for(int s=0;s<SEGMENTS;s++){
        uint source=s*SolverSlotCount+pool;
        FramesBuffer[pool*SEGMENTS+s].Rotation=SolverRotations[source];FramesBuffer[pool*SEGMENTS+s].AngularVelocity=SolverAngular[source];
        PlasticBendsBuffer[pool*SEGMENTS+s]=SolverPlastic[source];
    }
}
'''
    result=source[:begin]+declarations+body+staging+source[end:]
    return result.replace('#pragma kernel XPBDSolverLOD0',
        '#pragma kernel XPBDSolverLOD0\n#pragma kernel StageSolver\n#pragma kernel UnstageSolver')

def renderer(text):
    text=text.replace('private int finalKernel=-1;', 'private int finalKernel=-1,stageKernel=-1,unstageKernel;')
    needle='                foreach (int kernel in bindKernels)'
    text=text.replace(needle,'''                if(shader.HasKernel("StageSolver"))
                {
                    stageKernel=shader.FindKernel("StageSolver");unstageKernel=shader.FindKernel("UnstageSolver");
                    int slots=Pool.Capacity*32;shader.SetInt("SolverSlotCount",slots);
                    var sp=NewBuffer(slots*8,12);var sv=NewBuffer(slots*8,12);var sq=NewBuffer(slots*7,16);
                    var sw=NewBuffer(slots*7,12);var sb=NewBuffer(slots*7,16);
                    bindKernels=new[]{solveKernel,initializeKernel,stageKernel,unstageKernel};
                    foreach(int k in bindKernels){shader.SetBuffer(k,"SolverPositions",sp);shader.SetBuffer(k,"SolverVelocities",sv);
                        shader.SetBuffer(k,"SolverRotations",sq);shader.SetBuffer(k,"SolverAngular",sw);shader.SetBuffer(k,"SolverPlastic",sb);
                        shader.SetBuffer(k,"ActiveGridsBuffer",activeBuffer);}
                }
'''+needle)
    needle='            if (Pool.Selected.Count>0) for(int substep'
    text=text.replace(needle,'''            if(stageKernel>=0 && Pool.Selected.Count>0){shader.SetInt("SolverGridCount",Pool.Selected.Count);shader.Dispatch(stageKernel,(Pool.Selected.Count+3)/4,1,1);}
'''+needle)
    needle='            SimulationTime+=tick; SimulationTicks++;'
    text=text.replace(needle,'''            if(stageKernel>=0 && Pool.Selected.Count>0)shader.Dispatch(unstageKernel,(Pool.Selected.Count+3)/4,1,1);
'''+needle)
    return text
