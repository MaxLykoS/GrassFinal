"""Phase-split experiment: preserve sequential per-blade constraint order."""
def generate(source):
    begin=source.index('void SimulateBlade(')
    end=source.index('bool Occluded(',begin)
    original=source[begin:end]
    contact=original[original.index('            SegmentContact('):original.index('            BendTwist(')]
    bend=original[original.index('            BendTwist('):original.index('            Align(')]
    align=original[original.index('            Align('):original.index('\n        }',original.index('            Align('))]
    load_material='''
    Rod rod[SEGMENTS];float2 plastic[SEGMENTS];
    [unroll] for(int s=0;s<SEGMENTS;s++) {
        rod[s]=CompactRod(blade*SEGMENTS+s);
        plastic[s]=EnablePlasticity!=0?PlasticBendsBuffer[pool*SEGMENTS+s].xy:float2(0,0);
        rod[s].Relative=PlasticRest(rod[s].Relative,plastic[s]);
    }
    float4 anchor=rod[0].Rotation;
'''
    identify='''
    uint gridId=id.x/32;if(gridId>=(uint)SolverGridCount)return;
    ActiveGrid grid=ActiveGridsBuffer[gridId];uint blade=grid.BladeOffset+id.x%32,pool=grid.PoolOffset+id.x%32;
    float h=TickDt/NearSubsteps;
'''
    load_p='''
    float3 p[NODES],previous[NODES];float mass[NODES];
    [unroll] for(int n=0;n<NODES;n++) {
        p[n]=BonesBuffer[pool*NODES+n].Predicted;previous[n]=BonesBuffer[pool*NODES+n].Position;
        mass[n]=InverseMassBuffer[n]*InverseBladeMass;
    }
'''
    load_q='''
    float4 q[SEGMENTS];
    [unroll] for(int s=0;s<SEGMENTS;s++)q[s]=PredictedFrames[pool*SEGMENTS+s];
'''
    save_p='    [unroll] for(int n=0;n<NODES;n++)BonesBuffer[pool*NODES+n].Predicted=p[n];\n'
    save_q='    [unroll] for(int s=0;s<SEGMENTS;s++)PredictedFrames[pool*SEGMENTS+s]=q[s];\n'
    prediction=original[original.index('    float2 scroll='):original.index('        // Fixed indices')]
    prediction=prediction.replace('    {\n        int step=SubstepIndex;', '        int step=SubstepIndex;')
    prediction=prediction.replace('        float3 previous[NODES]; float4 previousQ[SEGMENTS];\n','')
    prediction=prediction.replace('        float stretchLambda[SEGMENTS]; float3 bendLambda[SEGMENTS],alignLambda[SEGMENTS];\n','')
    prediction=prediction.replace('            previous[node]=p[node];\n','')
    prediction=prediction.replace('previousQ[segment]=q[segment];','')
    prediction=prediction.replace('            stretchLambda[segment]=0; bendLambda[segment]=0; alignLambda[segment]=0;','')
    final=original[original.index('        [unroll] for(int velocity='):original.index('\n[numthreads')]
    final=final.replace('\n    }\n    [unroll] for(int save=', '\n    [unroll] for(int save=',1)
    result='''
RWStructuredBuffer<float4> PredictedFrames;
RWStructuredBuffer<float> SolverLambdas;
int SolverGridCount;
[numthreads(128,1,1)]
void XPBDSolverLOD0(uint3 id:SV_DispatchThreadID)
{
'''+identify+'''
    float3 p[NODES],v[NODES];float4 q[SEGMENTS];float3 omega[SEGMENTS];
    float3 root=BladesBuffer[blade].Root;
    [unroll] for(int n=0;n<NODES;n++){p[n]=BonesBuffer[pool*NODES+n].Position;v[n]=BonesBuffer[pool*NODES+n].Velocities;}
    [unroll] for(int s=0;s<SEGMENTS;s++){q[s]=FramesBuffer[pool*SEGMENTS+s].Rotation;omega[s]=FramesBuffer[pool*SEGMENTS+s].AngularVelocity;}
'''+prediction+save_p+save_q+'''
    [unroll] for(int k=0;k<49;k++)SolverLambdas[pool*49+k]=0;
}
[numthreads(128,1,1)]
void ContactGrass(uint3 id:SV_DispatchThreadID)
{
'''+identify+load_p+'''
    float3 root=BladesBuffer[blade].Root;
    float fraction=(SubstepIndex+1.0)/NearSubsteps,oldFraction=SubstepIndex/(float)NearSubsteps;
'''+contact+save_p+'''
}
[numthreads(128,1,1)]
void BendGrass(uint3 id:SV_DispatchThreadID)
{
'''+identify+load_q+load_material+'''
    float3 bendLambda[SEGMENTS];
    [unroll] for(int s=0;s<SEGMENTS;s++)bendLambda[s]=float3(SolverLambdas[pool*49+s*7],SolverLambdas[pool*49+s*7+1],SolverLambdas[pool*49+s*7+2]);
'''+bend+save_q+'''
    [unroll] for(int s=0;s<SEGMENTS;s++){
        SolverLambdas[pool*49+s*7]=bendLambda[s].x;SolverLambdas[pool*49+s*7+1]=bendLambda[s].y;SolverLambdas[pool*49+s*7+2]=bendLambda[s].z;
    }
}
[numthreads(128,1,1)]
void AlignGrass(uint3 id:SV_DispatchThreadID)
{
'''+identify+load_p+load_q+load_material+'''
    float3 alignLambda[SEGMENTS];float stretchLambda[SEGMENTS];
    [unroll] for(int s=0;s<SEGMENTS;s++){
        alignLambda[s]=float3(SolverLambdas[pool*49+s*7+3],SolverLambdas[pool*49+s*7+4],SolverLambdas[pool*49+s*7+5]);
        stretchLambda[s]=SolverLambdas[pool*49+s*7+6];
    }
'''+align+save_p+save_q+'''
    [unroll] for(int s=0;s<SEGMENTS;s++){
        SolverLambdas[pool*49+s*7+3]=alignLambda[s].x;SolverLambdas[pool*49+s*7+4]=alignLambda[s].y;SolverLambdas[pool*49+s*7+5]=alignLambda[s].z;
        SolverLambdas[pool*49+s*7+6]=stretchLambda[s];
    }
}
[numthreads(128,1,1)]
void FinishGrass(uint3 id:SV_DispatchThreadID)
{
'''+identify+load_p+load_q+load_material+'''
    float3 v[NODES],omega[SEGMENTS];v[0]=0;float4 previousQ[SEGMENTS];
    [unroll] for(int s=0;s<SEGMENTS;s++)previousQ[s]=FramesBuffer[pool*SEGMENTS+s].Rotation;
'''+final
    return source[:begin]+result+source[end:]

def renderer(text):
    text=text.replace('private readonly int solveKernel, initializeKernel, cullKernel;',
        'private readonly int solveKernel, initializeKernel, cullKernel;\n        private int contactPhase=-1,bendPhase,alignPhase,finishPhase;')
    needle='                foreach (int kernel in new[] { solveKernel, initializeKernel })'
    text=text.replace(needle,'''                int[] bindKernels=new[]{solveKernel,initializeKernel};
                if(shader.HasKernel("ContactGrass"))
                {
                    contactPhase=shader.FindKernel("ContactGrass");bendPhase=shader.FindKernel("BendGrass");
                    alignPhase=shader.FindKernel("AlignGrass");finishPhase=shader.FindKernel("FinishGrass");
                    var predictions=NewBuffer(Pool.Capacity*32*7,16);var lambdas=NewBuffer(Pool.Capacity*32*49,4);
                    bindKernels=new[]{solveKernel,initializeKernel,contactPhase,bendPhase,alignPhase,finishPhase};
                    foreach(int k in bindKernels){shader.SetBuffer(k,"PredictedFrames",predictions);shader.SetBuffer(k,"SolverLambdas",lambdas);
                        shader.SetBuffer(k,"InverseMassBuffer",masses);shader.SetBuffer(k,"ActiveGridsBuffer",activeBuffer);
                        shader.SetBuffer(k,"ColliderBuffer",contacts);shader.SetTexture(k,"WindForceMap",windNoise);}
                }
                foreach (int kernel in bindKernels)''')
    needle='            { shader.SetInt("SubstepIndex",substep); shader.Dispatch(solveKernel,Pool.Selected.Count,1,1); }'
    assert needle in text
    text=text.replace(needle,'''            {
                shader.SetInt("SubstepIndex",substep);
                if(contactPhase<0)shader.Dispatch(solveKernel,Pool.Selected.Count,1,1);
                else {
                    int groups=(Pool.Selected.Count+3)/4;shader.SetInt("SolverGridCount",Pool.Selected.Count);
                    shader.Dispatch(solveKernel,groups,1,1);
                    for(int iteration=0;iteration<Mathf.Clamp(settings.ConstraintIterations,1,6);iteration++){
                        shader.Dispatch(contactPhase,groups,1,1);shader.Dispatch(bendPhase,groups,1,1);shader.Dispatch(alignPhase,groups,1,1);}
                    shader.Dispatch(contactPhase,groups,1,1);shader.Dispatch(finishPhase,groups,1,1);
                }
            }''')
    return text
