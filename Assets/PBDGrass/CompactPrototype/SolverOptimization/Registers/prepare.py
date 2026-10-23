"""Generate register-lifetime experiments in the isolated project, not live shaders."""
from pathlib import Path
import runpy
import re
import uuid
import subprocess

here=Path(__file__).resolve().parent
support=here.parent
grass=support.parents[1]
project=Path('D:/CodexTemp/codex-grass-contact-profile')
runpy.run_path(str(support/'prepare_solver.py'))
temporary=project/'Assets/PBDGrass/ContactProfileTemporary'
# Freeze the renderer used by these experiments so future production changes do
# not silently alter the rejected variants' dispatch or allocation behavior.
target=project/'Assets/PBDGrass'
(target/'Runtime/PBDGrassPatchRenderer.cs').write_bytes((here/'ReferenceRenderer.cs.txt').read_bytes())
(target/'Runtime/GrassTieredHandoff.cs').write_bytes((grass/'Runtime/GrassTieredHandoff.cs').read_bytes())
instrument=(grass/'CompactPrototype/Showcase/Performance/prepare_profile.py').read_text(encoding='utf-8-sig')
exec(compile(instrument[instrument.index('def replace(text, before, after):'):],str(here/'prepare.py'),'exec'),{'target':target})
source=subprocess.check_output(['git','show','6197058:Assets/PBDGrass/CompactPrototype/Shaders/CompactGrassSolver.compute'],cwd=grass.parents[1]).decode('utf-8-sig')

def material(text):
    before,body=text.split('void SimulateBlade(uint blade,uint pool)',1)
    body=body.replace(' Rod rod[SEGMENTS];','').replace('    float2 plastic[SEGMENTS];\n','')
    body=body.replace(' omega[loadFrame]=frame.AngularVelocity; rod[loadFrame]=CompactRod(blade*SEGMENTS+loadFrame);\n'
        '        plastic[loadFrame]=EnablePlasticity!=0?PlasticBendsBuffer[pool*SEGMENTS+loadFrame].xy:float2(0,0);\n'
        '        rod[loadFrame].Relative=PlasticRest(rod[loadFrame].Relative,plastic[loadFrame]);',
        ' omega[loadFrame]=frame.AngularVelocity;')
    body=body.replace('anchor=rod[0].Rotation','anchor=CompactAnchors[blade/32]')
    # Fetch homogeneous template scalars where they are consumed instead of
    # keeping seven complete material records through the constraint loop.
    body=re.sub(r'rod\[([^\]]+)\]\.(Length|InverseInertia)',r'RodsBuffer[\1].\2',body)
    body=re.sub(r'BendTwist\(([^\n]+?),rod\[([0-6])\],',
                r'BendTwist(\1,LiveRestRod(pool,\2),',body)
    body=re.sub(r'plastic\[([^\]]+)\]',r'PlasticBendsBuffer[pool*SEGMENTS+\1].xy',body)
    helper='''Rod LiveRestRod(uint pool,uint segment)
{
    Rod rod=RodsBuffer[segment];
    float2 plastic=EnablePlasticity!=0?PlasticBendsBuffer[pool*SEGMENTS+segment].xy:float2(0,0);
    rod.Relative=PlasticRest(rod.Relative,plastic);
    return rod;
}
'''
    return before+helper+'void SimulateBlade(uint blade,uint pool)'+body

def previous_frames(text):
    text=text.replace(' float4 previousQ[SEGMENTS];','')
    text=text.replace('previousQ[segment]=q[segment];','')
    return text.replace('QInv(previousQ[angular])','QInv(FramesBuffer[pool*SEGMENTS+angular].Rotation)')

def shared_material(text):
    text=material(text)
    text=text.replace('Rod LiveRestRod(uint pool,uint segment)','groupshared float4 SharedRest[7][32];\nRod LiveRestRod(uint pool,uint segment)')
    text=text.replace('    float2 plastic=EnablePlasticity!=0?PlasticBendsBuffer[pool*SEGMENTS+segment].xy:float2(0,0);\n'
                      '    rod.Relative=PlasticRest(rod.Relative,plastic);','    rod.Relative=SharedRest[segment][pool%32];')
    needle='    float h=TickDt/NearSubsteps;'
    text=text.replace(needle,'''    // Each lane owns its column; there are no cross-lane dependencies.
    [unroll] for(int rest=0;rest<SEGMENTS;rest++)
    {
        float2 bend=EnablePlasticity!=0?PlasticBendsBuffer[pool*SEGMENTS+rest].xy:float2(0,0);
        SharedRest[rest][pool%32]=PlasticRest(RodsBuffer[rest].Relative,bend);
    }
'''+needle)
    return text

def shared_lambda(text):
    before,body=text.split('void SimulateBlade(uint blade,uint pool)',1)
    body=body.replace('        float stretchLambda[SEGMENTS]; float3 bendLambda[SEGMENTS],alignLambda[SEGMENTS];\n','')
    body=re.sub(r'(stretchLambda|bendLambda|alignLambda)\[([^\]]+)\]',r'\1[\2][pool%32]',body)
    return before+'''// Constraint multipliers are private to a lane; no barrier is needed.
groupshared float stretchLambda[7][32];
groupshared float3 bendLambda[7][32],alignLambda[7][32];
'''+ 'void SimulateBlade(uint blade,uint pool)'+body

def constant_material(text):
    text=shared_material(text)
    text=text.replace('groupshared float4 SharedRest', 'float4 SolverShape[7],SolverStiffness[7];\ngroupshared float4 SharedRest')
    text=text.replace('    rod.Relative=SharedRest[segment][pool%32];', '''    rod.Relative=SharedRest[segment][pool%32];
    rod.SoftScale=SolverShape[segment].w;
    rod.HardScale=SolverStiffness[segment].x;rod.TwistScale=SolverStiffness[segment].y;''')
    text=re.sub(r'RodsBuffer\[([^\]]+)\]\.Length',r'SolverShape[\1].x',text)
    text=re.sub(r'RodsBuffer\[([^\]]+)\]\.InverseInertia',r'SolverShape[\1].z',text)
    return text

variants={'material':material(source),'frames':previous_frames(source),
          'both':previous_frames(material(source)), 'shared':shared_material(source),
          'lambda':shared_lambda(source),
          'combo':previous_frames(shared_lambda(shared_material(source))),
          'constant':constant_material(source)}
split=runpy.run_path(str(here/'split_variant.py'))
variants['loop']=runpy.run_path(str(here/'loop_variant.py'))['generate'](source)
special=source.replace('#pragma kernel XPBDSolverLOD0',
    '#pragma kernel XPBDSolverLOD0 XPBD_INTERMEDIATE\n#pragma kernel XPBDSolverFinal')
special=special.replace('BonesBuffer[n].Position=p[save]; BonesBuffer[n].Predicted=p[save]; BonesBuffer[n].Velocities=',
    'BonesBuffer[n].Position=p[save];\n#ifndef XPBD_INTERMEDIATE\n        BonesBuffer[n].Predicted=p[save];\n#endif\n        BonesBuffer[n].Velocities=')
special=special.replace('    if(SubstepIndex==NearSubsteps-1)\n', '#ifndef XPBD_INTERMEDIATE\n    if(SubstepIndex==NearSubsteps-1)\n')
special=special.replace('}\n[numthreads(32,1,1)]\nvoid XPBDSolverLOD0', '#endif\n}\n[numthreads(32,1,1)]\nvoid XPBDSolverLOD0')
special=special.replace('bool Occluded(', '''[numthreads(32,1,1)]
void XPBDSolverFinal(uint3 group:SV_GroupID,uint3 lane:SV_GroupThreadID)
{
    ActiveGrid grid=ActiveGridsBuffer[group.x];SimulateBlade(grid.BladeOffset+lane.x,grid.PoolOffset+lane.x);
}
bool Occluded(''')
variants['special']=special
soa=runpy.run_path(str(here/'soa_variant.py'))
variants['soa']=soa['generate'](source)
structure=runpy.run_path(str(here/'structure_variant.py'))
variants['structure']=structure['generate'](source)
for name in ['soa','structure']:
    variant=variants[name]
    a=variant.index('[numthreads(128,1,1)]\nvoid XPBDSolverLOD0(')
    b=variant.index('\n}',a)+2
    variants[name+'32']=variant[:a]+'''[numthreads(32,1,1)]
void XPBDSolverLOD0(uint3 group:SV_GroupID,uint3 lane:SV_GroupThreadID)
{
    ActiveGrid grid=ActiveGridsBuffer[group.x];SimulateBlade(grid.BladeOffset+lane.x,grid.PoolOffset+lane.x);
}'''+variant[b:]
variants['split']=split['generate'](source).replace('#pragma kernel XPBDSolverLOD0',
    '#pragma kernel XPBDSolverLOD0\n#pragma kernel ContactGrass\n#pragma kernel BendGrass\n#pragma kernel AlignGrass\n#pragma kernel FinishGrass')
for name,text in variants.items():
    (here/(name+'.compute.txt')).write_text(text,encoding='utf-8')
    (temporary/('CompactRegister_'+name+'.compute')).write_text(text.replace('#include "GrassCompact',
        '#include "../CompactPrototype/Shaders/GrassCompact'),encoding='utf-8')
benchmark=temporary/'Editor/ContactGpuBenchmark.cs'
text=benchmark.read_text(encoding='utf-8')
text=text.replace('ContactGpuHooks.ReferenceContacts=name.StartsWith("reference");','ContactGpuHooks.ReferenceContacts=false;')
text=text.replace('if(ContactGpuHooks.ReferenceContacts)demo.Solver=', 'if(name.StartsWith("reference"))demo.Solver=')
needle='            demo.PatchSize=name.StartsWith("small")?96:144;'
text=text.replace(needle,'''            string variant=name.Split('_')[0];
            if(variant!="reference")demo.Solver=AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/PBDGrass/ContactProfileTemporary/CompactRegister_"+variant+".compute");
'''+needle)
benchmark.write_text(text,encoding='utf-8')
parity=temporary/'Editor/ContactSolverParity.cs'
text=parity.read_text(encoding='utf-8')
text=text.replace('ContactGpuHooks.ReferenceContacts=baseline;','ContactGpuHooks.ReferenceContacts=false;')
needle='        demo.Physics.ContactDrivenSimulation=false;'
text=text.replace(needle,'''        if(!baseline)demo.Solver=AssetDatabase.LoadAssetAtPath<ComputeShader>(
            "Assets/PBDGrass/ContactProfileTemporary/CompactRegister_"+Environment.GetEnvironmentVariable("GRASS_REGISTER_VARIANT")+".compute");
'''+needle)
parity.write_text(text,encoding='utf-8')
renderer=project/'Assets/PBDGrass/Runtime/PBDGrassPatchRenderer.cs'
text=renderer.read_text(encoding='utf-8')
needle='                if(compact)Compact=new GrassCompactStorage'
assert needle in text
text=text.replace(needle,'''                if(compact)
                {
                    var shape=new Vector4[7];var stiffness=new Vector4[7];
                    for(int j=0;j<7;j++){var r=world.TemplateRods[j];
                        shape[j]=new Vector4(r.Length,r.HalfWidth,r.InverseInertia,r.SoftScale);
                        stiffness[j]=new Vector4(r.HardScale,r.TwistScale,0,0);}
                    shader.SetVectorArray("SolverShape",shape);shader.SetVectorArray("SolverStiffness",stiffness);
                }
'''+needle)
renderer.write_text(text,encoding='utf-8')
text=split['renderer'](text)
text=text.replace('private int contactPhase=-1,bendPhase,alignPhase,finishPhase;',
    'private int contactPhase=-1,bendPhase,alignPhase,finishPhase; private uint solverWidth;')
text=text.replace('solveKernel = shader.FindKernel("XPBDSolverLOD0");',
    'solveKernel = shader.FindKernel("XPBDSolverLOD0"); shader.GetKernelThreadGroupSizes(solveKernel,out solverWidth,out _,out _);')
text=text.replace('if(contactPhase<0)shader.Dispatch(solveKernel,Pool.Selected.Count,1,1);',
    'if(contactPhase<0){shader.SetInt("SolverGridCount",Pool.Selected.Count);int batch=(int)solverWidth/32;shader.Dispatch(solveKernel,(Pool.Selected.Count+batch-1)/batch,1,1);}')
text=text.replace('private uint solverWidth;', 'private uint solverWidth;private int finalKernel=-1;')
needle='                foreach (int kernel in bindKernels)'
text=text.replace(needle,'''                if(shader.HasKernel("XPBDSolverFinal"))
                {
                    finalKernel=shader.FindKernel("XPBDSolverFinal");bindKernels=new[]{solveKernel,initializeKernel,finalKernel};
                    shader.SetBuffer(finalKernel,"InverseMassBuffer",masses);shader.SetBuffer(finalKernel,"ActiveGridsBuffer",activeBuffer);
                    shader.SetBuffer(finalKernel,"ColliderBuffer",contacts);shader.SetTexture(finalKernel,"WindForceMap",windNoise);
                }
'''+needle)
text=text.replace('shader.Dispatch(solveKernel,(Pool.Selected.Count+batch-1)/batch,1,1);',
    'shader.Dispatch(finalKernel>=0 && substep==Mathf.Clamp(settings.NearSubsteps,1,16)-1?finalKernel:solveKernel,(Pool.Selected.Count+batch-1)/batch,1,1);')
renderer.write_text(text,encoding='utf-8')
text=structure['renderer'](soa['renderer'](text))
# The end marker must follow optional unpacking as well as the eight substeps.
text=text.replace('            ContactGpuHooks.Event(4);\n','')
text=text.replace('            SimulationTime+=tick; SimulationTicks++;',
    '            ContactGpuHooks.Event(4);\n            SimulationTime+=tick; SimulationTicks++;')
renderer.write_text(text,encoding='utf-8')
for path in [here,*here.rglob('*')]:
    if path.suffix=='.meta' or '__pycache__' in path.parts:continue
    meta=Path(str(path)+'.meta')
    if not meta.exists():meta.write_text('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\n'+
        ('folderAsset: yes\n' if path.is_dir() else '')+'DefaultImporter:\n  externalObjects: {}\n',encoding='utf-8')
print('Prepared material, frames, both variants; production shader unchanged.')
