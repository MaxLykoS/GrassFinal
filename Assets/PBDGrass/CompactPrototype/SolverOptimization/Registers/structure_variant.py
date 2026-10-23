"""Transpose the original full structs without changing the shader load expressions."""
def generate(source):
    begin=source.index('void SimulateBlade(');end=source.index('bool Occluded(',begin)
    body=source[begin:end]
    for before,after in [('pool*NODES+load','load*SolverSlotCount+pool'),('pool*NODES+save','save*SolverSlotCount+pool'),
        ('pool*SEGMENTS+loadFrame','loadFrame*SolverSlotCount+pool'),('pool*SEGMENTS+saveFrame','saveFrame*SolverSlotCount+pool'),
        ('pool*SEGMENTS+joint','joint*SolverSlotCount+pool'),('PlasticBendsBuffer[pool*SEGMENTS]','PlasticBendsBuffer[pool]'),
        ('pool*7+s','s*SolverSlotCount+pool')]:body=body.replace(before,after)
    body=body.replace('BonesBuffer[','SolverBones[').replace('FramesBuffer[','SolverFrames[').replace('PlasticBendsBuffer[','SolverPlastic[')
    body=body.replace('[numthreads(32,1,1)]\nvoid XPBDSolverLOD0(uint3 group:SV_GroupID,uint3 lane:SV_GroupThreadID)',
        '[numthreads(128,1,1)]\nvoid XPBDSolverLOD0(uint3 id:SV_DispatchThreadID)')
    body=body.replace('ActiveGrid grid=ActiveGridsBuffer[group.x]; SimulateBlade(grid.BladeOffset+lane.x,grid.PoolOffset+lane.x);',
        'if(id.x/32>=(uint)SolverGridCount)return;ActiveGrid grid=ActiveGridsBuffer[id.x/32];SimulateBlade(grid.BladeOffset+id.x%32,grid.PoolOffset+id.x%32);')
    declarations='''RWStructuredBuffer<BoneInfo> SolverBones;
RWStructuredBuffer<Frame> SolverFrames;
RWStructuredBuffer<float4> SolverPlastic;
int SolverSlotCount,SolverGridCount;
'''
    stage='''
[numthreads(128,1,1)]
void StageStruct(uint3 id:SV_DispatchThreadID)
{
    if(id.x/32>=(uint)SolverGridCount)return;ActiveGrid grid=ActiveGridsBuffer[id.x/32];uint pool=grid.PoolOffset+id.x%32;
    [unroll] for(int n=0;n<NODES;n++)SolverBones[n*SolverSlotCount+pool]=BonesBuffer[pool*NODES+n];
    [unroll] for(int s=0;s<SEGMENTS;s++){
        SolverFrames[s*SolverSlotCount+pool]=FramesBuffer[pool*SEGMENTS+s];
        SolverPlastic[s*SolverSlotCount+pool]=PlasticBendsBuffer[pool*SEGMENTS+s];
    }
}
[numthreads(128,1,1)]
void UnstageStruct(uint3 id:SV_DispatchThreadID)
{
    if(id.x/32>=(uint)SolverGridCount)return;ActiveGrid grid=ActiveGridsBuffer[id.x/32];uint pool=grid.PoolOffset+id.x%32;
    [unroll] for(int n=0;n<NODES;n++)BonesBuffer[pool*NODES+n]=SolverBones[n*SolverSlotCount+pool];
    [unroll] for(int s=0;s<SEGMENTS;s++){
        FramesBuffer[pool*SEGMENTS+s]=SolverFrames[s*SolverSlotCount+pool];
        PlasticBendsBuffer[pool*SEGMENTS+s]=SolverPlastic[s*SolverSlotCount+pool];
    }
}
'''
    return (source[:begin]+declarations+body+stage+source[end:]).replace('#pragma kernel XPBDSolverLOD0',
        '#pragma kernel XPBDSolverLOD0\n#pragma kernel StageStruct\n#pragma kernel UnstageStruct')

def renderer(text):
    needle='                foreach (int kernel in bindKernels)'
    assert needle in text
    return text.replace(needle,'''                if(shader.HasKernel("StageStruct"))
                {
                    stageKernel=shader.FindKernel("StageStruct");unstageKernel=shader.FindKernel("UnstageStruct");
                    int slots=Pool.Capacity*32;shader.SetInt("SolverSlotCount",slots);
                    var sn=NewBuffer(slots*8,48);var sf=NewBuffer(slots*7,32);var sb=NewBuffer(slots*7,16);
                    bindKernels=new[]{solveKernel,initializeKernel,stageKernel,unstageKernel};
                    foreach(int k in bindKernels){shader.SetBuffer(k,"SolverBones",sn);shader.SetBuffer(k,"SolverFrames",sf);
                        shader.SetBuffer(k,"SolverPlastic",sb);shader.SetBuffer(k,"ActiveGridsBuffer",activeBuffer);}
                }
'''+needle)
