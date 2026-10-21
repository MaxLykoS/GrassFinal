"""Generate isolated storage variants from the accepted physics/handoff equations.
Run with Python 3. Only reads/writes Assets/PBDGrass. Never touches Library.
"""
from pathlib import Path
import re

root = Path(__file__).resolve().parents[2]
dest = root / "CompactPrototype/Shaders"

def replace(text, old, new):
    assert old in text, old[:100]
    return text.replace(old, new)

def templates(text, marker):
    text = re.sub(r"RodsBuffer\[([^\]]+)\]", r"CompactRod(\1)", text)
    text = re.sub(r"RestNodesBuffer\[([^\]]+)\]", r"CompactRest(\1)", text)
    for name in ("InverseMassBuffer", "BakedInverseMass"):
        text = re.sub(name+r"\[([^\]]+)\]", name+r"[(\1)%8]", text)
    return replace(text, marker, '#include "GrassCompactTemplates.cginc"\n'+marker)

banner = "// Generated storage variant; equations come from the accepted source.\n// Regenerate with CompactPrototype/Editor/generate_compact_shaders.py.\n"
solver = (root/"Shaders/PBDSolverCS.compute").read_text(encoding="utf-8-sig")
solver = templates(solver, "float3 Axis(int k)")
solver = replace(solver, "// World blade ID, not simulation slot: plastic material history survives pool eviction.",
                 "// Full precision active plastic belongs to the simulation slot; packed history uses world identity.")
solver = replace(solver, "RWStructuredBuffer<float4> PlasticBendsBuffer;",
                 'RWStructuredBuffer<float4> PlasticBendsBuffer;\nRWStructuredBuffer<uint> CompactPlastic;\n#include "GrassCompactPacking.cginc"')
solver = replace(solver, "void WriteGeometry(uint blade,float3 p[NODES],float4 q[SEGMENTS])",
                 "void WriteGeometry(uint blade,uint pool,float3 p[NODES],float4 q[SEGMENTS])")
solver = replace(solver, "WriteGeometry(blade,p,q)", "WriteGeometry(blade,pool,p,q)")
solver = solver.replace("blade*VERTICES", "pool*VERTICES")
solver = re.sub(r"PlasticBendsBuffer\[blade\*SEGMENTS", "PlasticBendsBuffer[pool*SEGMENTS", solver)
solver = replace(solver, "if(SubstepIndex==NearSubsteps-1)WriteGeometry(blade,pool,p,q);", """
    if(SubstepIndex==NearSubsteps-1)
    {
        WriteGeometry(blade,pool,p,q);
        // Cache only. Simulation always reloads float32 active plastic, never this quantized copy.
        [unroll] for(int s=0;s<7;s++)CompactPlastic[blade*8+s]=CompactPackPlastic(PlasticBendsBuffer[pool*7+s].xy);
    }""")
(dest/"CompactGrassSolver.compute").write_text(banner+solver, encoding="utf-8")

handoff = (root/"Shaders/TieredGrassHandoff.compute").read_text(encoding="utf-8-sig")
handoff=replace(handoff,"#pragma kernel InitializeHistory",
                "#pragma kernel InitializeHistory\n#pragma kernel InitializeNearHistory COMPACT_NEAR_BIRTH")
handoff = templates(handoff, "float4 AlignTangent(")
handoff = replace(handoff, "StructuredBuffer<float4> PlasticBendsBuffer;", """
RWStructuredBuffer<float4> PlasticBendsBuffer;
StructuredBuffer<uint> CompactPlastic;
#define COMPACT_POSE_WRITABLE
#include "GrassCompactPacking.cginc"
float4 CompactBend(uint index) { return float4(CompactUnpackPlastic(CompactPlastic[(index/7)*8+index%7]),0,0); }
""")
handoff = re.sub(r"PlasticBendsBuffer\[([^\]]+)\]", r"CompactBend(\1)", handoff)
start=handoff.index("void WriteGeometry(")
end=handoff.index("float4 FrameFromBasis",start)
old_write=handoff[start:end]
near_write=old_write.replace("void WriteGeometry(uint blade,", "void WriteNearGeometry(uint blade,uint pool,").replace("blade*15","pool*15")
pack = """
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
void WriteGeometry(uint blade,float3 p[8],float4 q[7])
{
    int pool=GridStates[blade/32].PoolOffset;
    if(pool>=0)WriteNearGeometry(blade,pool+blade%32,p,q);
    else WriteCompactPose(blade,p,q);
}
"""
handoff=handoff[:start]+near_write+pack+handoff[end:]
start=handoff.index("void ReadGeometry(")
end=handoff.index("float PlasticAmount",start)
near_read=handoff[start:end].replace("void ReadGeometry(uint blade,","void ReadNearGeometry(uint blade,uint pool,").replace("blade*15","pool*15")
handoff=handoff[:start]+near_read+"""
int CompactInitialSeeding;
void ReadGeometry(uint blade,out float3 p[8],out float4 q[7])
{
    #if defined(COMPACT_NEAR_BIRTH)
        // Keep the accepted full-float basis reconstruction in a separate kernel.
        // Do not merge its arithmetic with the packed direction decoder.
        int pool=GridStates[blade/32].PoolOffset;
        ReadNearGeometry(blade,pool+blade%32,p,q);
    #else
    [unroll] for(uint n=0;n<8;n++)p[n]=CompactPosition(blade,n);
    [unroll] for(uint s=0;s<7;s++)q[s]=FrameFromBasis(CompactWidth(blade,s),SafeNormal(p[s+1]-p[s],float3(0,1,0)));
    #endif
}
"""+handoff[end:]
handoff=replace(handoff,"void InitializeHistory(uint3 id:SV_DispatchThreadID)","""#if defined(COMPACT_NEAR_BIRTH)
void InitializeNearHistory(uint3 id:SV_DispatchThreadID)
#else
void InitializeHistory(uint3 id:SV_DispatchThreadID)
#endif""")
handoff=replace(handoff,"    if(entry.z>=0)\n", """    #if defined(COMPACT_NEAR_BIRTH)
        if(GridStates[blade/32].PoolOffset<0)return;
    #else
        if(CompactInitialSeeding!=0 && GridStates[blade/32].PoolOffset>=0)return;
    #endif
    if(entry.z>=0)
""")
handoff=replace(handoff,
    "for(int segment11=0;segment11<7;segment11++)FramesBuffer[pool*7+segment11]=HistoryFrames[cache*7+segment11];",
    """for(int segment11=0;segment11<7;segment11++)
    {
        FramesBuffer[pool*7+segment11]=HistoryFrames[cache*7+segment11];
        PlasticBendsBuffer[pool*7+segment11]=CompactBend(blade*7+segment11);
    }
    // Actual LOD entries need geometry before the first tick; birth/reset already
    // wrote the accepted float32 mesh. Do not reconstruct it a second time.
    if(CompactInitialSeeding==0)
    {
        float3 p[8];float4 q[7];
        [unroll] for(int n=0;n<8;n++)p[n]=HistoryNodes[cache*8+n].Position;
        [unroll] for(int s=0;s<7;s++)q[s]=HistoryFrames[cache*7+s].Rotation;
        WriteNearGeometry(blade,pool,p,q);
    }""")
handoff=replace(handoff, "void CaptureTransition(uint3 id:SV_DispatchThreadID)", "void CaptureTransition(uint3 id:SV_DispatchThreadID)")
needle="float3 p[8]; float4 q[7]; EvaluateCurve(blade,PoseTime,false,p,q);\n    [unroll] for(int node8"
handoff=replace(handoff,needle,"""// Preserve the outgoing visible pose before physical slots are reused.
    if(GridStates[blade/32].PoolOffset<0)
    {
        float3 oldP[8];float4 oldQ[7];
        [unroll] for(int n=0;n<8;n++)oldP[n]=HistoryNodes[cache*8+n].Position;
        [unroll] for(int s=0;s<7;s++)oldQ[s]=HistoryFrames[cache*7+s].Rotation;
        WriteCompactPose(blade,oldP,oldQ);
    }
    float3 p[8]; float4 q[7]; EvaluateCurve(blade,PoseTime,false,p,q);
    [unroll] for(int node8""")
# Display reads these four buffers. SRV aliases keep the D3D11 UAV count <=8;
# mutation kernels continue to use the original UAV names in separate dispatches.
handoff=replace(handoff,"RWStructuredBuffer<float3> ResultPosBuffer, NormalBuffer;","""RWStructuredBuffer<float3> ResultPosBuffer, NormalBuffer;
StructuredBuffer<BoneInfo> CompactBonesRead;
StructuredBuffer<Frame> CompactFramesRead;
StructuredBuffer<float3> CompactOffsetsRead;
StructuredBuffer<float4> CompactRotationsRead;""")
begin=handoff.index("void UpdateTieredDisplay(")
end=handoff.index("// Diagnostics alone",begin)
display=handoff[begin:end]
for old,new in (("BonesBuffer[","CompactBonesRead["),("FramesBuffer[","CompactFramesRead["),
                ("TransitionOffsets[","CompactOffsetsRead["),("TransitionRotations[","CompactRotationsRead[")):
    display=display.replace(old,new)
handoff=handoff[:begin]+display+handoff[end:]
begin=handoff.index("void WriteCompactPose(")
end=handoff.index("void WriteGeometry(",begin)
(dest/"GrassCompactEncode.cginc").write_text(handoff[begin:end],encoding="utf-8")
handoff=handoff[:begin]+'#include "GrassCompactEncode.cginc"\n'+handoff[end:]
# Contact-driven lifecycle transforms keep regeneration reproducible.
handoff=replace(handoff,'''struct Rod { float4 Rotation, Relative; float Length, HalfWidth, InverseInertia, SoftScale; float HardScale, TwistScale, Padding0, Padding1; };
struct Frame { float4 Rotation; float3 AngularVelocity; float Padding; };
struct GridState { int PoolOffset, Changed; float Started, Padding; };
struct ActiveGrid { float3 Position; int BladeOffset,PoolOffset,GridId,Padding0,Padding1; };
StructuredBuffer<Blade> BladesBuffer;
StructuredBuffer<Rod> RodsBuffer;
''','''struct Rod { float4 Rotation, Relative; float Length, HalfWidth, InverseInertia, SoftScale; float HardScale, TwistScale, Padding0, Padding1; };
struct Frame { float4 Rotation; float3 AngularVelocity; float Padding; };
struct GridState { int PoolOffset, Changed; float Started, Padding; };
float ContactFadeSeconds;
struct ActiveGrid { float3 Position; int BladeOffset,PoolOffset,GridId,Padding0,Padding1; };
StructuredBuffer<Blade> BladesBuffer;
StructuredBuffer<Rod> RodsBuffer;
''')
handoff=replace(handoff,'''    uint blade=id.x; if(blade>=(uint)BladeCount)return;
    GridState state=GridStates[blade/32]; int slot=HistoryMap[blade/32].x; uint cache=max(slot,0)*32+blade%32;
    uint pool=max(state.PoolOffset,0)+blade%32;
    float t=saturate((PoseTime-state.Started)/max(TransitionSeconds,.05)); t=t*t*(3-2*t);
    bool transitioning=state.Padding>0 && t<1;
    if(state.PoolOffset>=0 && !transitioning)
    {
        // The last XPBD substep already wrote the final visible geometry. No Bezier
''','''    uint blade=id.x; if(blade>=(uint)BladeCount)return;
    GridState state=GridStates[blade/32]; int slot=HistoryMap[blade/32].x; uint cache=max(slot,0)*32+blade%32;
    uint pool=max(state.PoolOffset,0)+blade%32;
    bool liveExit=state.Padding==2 && state.PoolOffset>=0;
    float t=saturate((PoseTime-state.Started)/max(liveExit?ContactFadeSeconds:TransitionSeconds,.05)); t=t*t*(3-2*t);
    bool transitioning=state.Padding!=0 && (t<1 || liveExit);
    if(state.PoolOffset>=0 && !transitioning)
    {
        // The last XPBD substep already wrote the final visible geometry. No Bezier
''')
handoff=replace(handoff,'''        return;
    }
    float3 p[8]; float4 q[7]; EvaluateCurve(blade,PoseTime,false,p,q);
    if(transitioning && slot>=0)
    {
        [unroll] for(int node14=0;node14<8;node14++)
        { float3 source=p[node14]+CompactOffsetsRead[cache*8+node14],target=p[node14]; if(state.PoolOffset>=0)target=CompactBonesRead[pool*8+node14].Position; p[node14]=lerp(source,target,t); }
''','''        return;
    }
    float3 p[8]; float4 q[7]; EvaluateCurve(blade,PoseTime,false,p,q);
    if(liveExit)
    {
        // The solver keeps advancing independently; only the displayed ribbon fades.
        [unroll] for(int n=0;n<8;n++)p[n]=lerp(CompactBonesRead[pool*8+n].Position,p[n],t);
        p[0]=0;
        [unroll] for(int s=0;s<7;s++)
        {
            float3 direction=SafeNormal(p[s+1]-p[s],QRotate(q[s],float3(0,0,1)));
            p[s+1]=p[s]+direction*CompactRod(blade*7+s).Length;
            q[s]=AlignTangent(QBlend(CompactFramesRead[pool*7+s].Rotation,q[s],t),direction);
        }
    }
    else if(transitioning && slot>=0)
    {
        [unroll] for(int node14=0;node14<8;node14++)
        { float3 source=p[node14]+CompactOffsetsRead[cache*8+node14],target=p[node14]; if(state.PoolOffset>=0)target=CompactBonesRead[pool*8+node14].Position; p[node14]=lerp(source,target,t); }
''')
(dest/"CompactGrassHandoff.compute").write_text(banner+handoff,encoding="utf-8")

stream=(root/"Shaders/GrassStreaming.compute").read_text(encoding="utf-8-sig")
stream=replace(stream,'"GrassWorldIdentity.cginc"','"../../Shaders/GrassWorldIdentity.cginc"')
stream=replace(stream,"RWStructuredBuffer<float4> WorldPlastic;","RWStructuredBuffer<uint> WorldPlastic;")
stream=replace(stream,"float WorldGround;","RWStructuredBuffer<float4> WorldAnchors;\nfloat WorldGround;")
start=stream.index("    [unroll] for(uint n=0;n<8;n++)")
stream=stream[:start]+"""    // Same operation and GPU identity as the accepted streaming path.
    if(lane==0)WorldAnchors[assignment.x]=Multiply(yaw,WorldTemplateRods[0].Rotation);
    [unroll] for(uint s=0;s<8;s++)WorldPlastic[b*8+s]=0;
}
"""
(dest/"CompactGrassStreaming.compute").write_text(banner+stream,encoding="utf-8")

draw=(root/"Shaders/PBDGrassShaderDrawProcedual.shader").read_text(encoding="utf-8-sig")
draw=replace(draw,'Shader "PBDGrass/Workspace/PBDGrassShaderDrawProcedual"','Shader "PBDGrass/CompactPrototype/Grass"')
draw=replace(draw,'"GrassMiddlePresentation.cginc"','"../../Shaders/GrassMiddlePresentation.cginc"')
draw=replace(draw,'#if defined(GRASS_LOD_BASE_COLORS) || defined(GRASS_OUTER_COVERAGE)','#if 1')
draw=replace(draw,'StructuredBuffer<float2> UvBuffer;',"""StructuredBuffer<float2> UvBuffer;
        #include "GrassCompactPacking.cginc"
        StructuredBuffer<float> CompactHalfWidths;""")
draw=replace(draw,'{ return TriangleBuffer[GridsVisibleBuffer[instanceID].idOffset * 39 + vertexID]; }',
    """{ uint blade=GridsVisibleBuffer[instanceID].idOffset+vertexID/39;
          return blade*15+TriangleBuffer[vertexID%39]; }
        void CompactVertex(uint index,out float3 world,out float3 normal,out float3 width)
        {
            uint blade=index/15,local=index%15,node=local==14?7:local/2,segment=min(node,6);
            int poolOffset=_GrassLodGridStates[blade/32].poolOffset;
            if(poolOffset>=0)
            {
                uint address=(poolOffset+blade%32)*15;
                world=VertexBuffer[address+local];normal=SafeNormal(NormalBuffer[address+local]);
                width=SafeNormal(VertexBuffer[address+segment*2]-VertexBuffer[address+segment*2+1]);
                return;
            }
            float3 p=CompactPosition(blade,node);
            float3 t=SafeNormal(CompactPosition(blade,segment+1)-CompactPosition(blade,segment));
            width=CompactWidth(blade,segment);width=SafeNormal(width-t*dot(width,t));
            normal=SafeNormal(cross(width,t));
            world=BladesBuffer[blade].root+p;
            if(local!=14)world+=width*CompactHalfWidths[segment]*(local%2==0?1:-1);
        }""")
draw=replace(draw,"o.world=VertexBuffer[index];", "CompactVertex(index,o.world,o.normal,o.width);")
draw=replace(draw,"o.uv=UvBuffer[index];","o.uv=UvBuffer[index%15];")
draw=replace(draw,"o.normal=SafeNormal(NormalBuffer[index]);","")
draw=replace(draw,"o.width=SafeNormal(VertexBuffer[pair]-VertexBuffer[pair+1]);","")
# ShadowCaster uses local variables rather than v2f geometry members.
draw=replace(draw,"float3 world=VertexBuffer[index], normal=SafeNormal(NormalBuffer[index]);",
                 "float3 world,normal,width;CompactVertex(index,world,normal,width);")
draw=replace(draw,"float3 width=SafeNormal(VertexBuffer[pair]-VertexBuffer[pair+1]);","")
# One decoder is shared by forward/shadow draws and the diagnostic probe.
begin=draw.index("        void CompactVertex(")
end=draw.index("        float HashClump(",begin)
(dest/"GrassCompactVertex.cginc").write_text(draw[begin:end],encoding="utf-8")
draw=draw[:begin]+'        #include "GrassCompactVertex.cginc"\n'+draw[end:]
# Contact-driven lifecycle transforms keep regeneration reproducible.
draw=replace(draw,'''        #endif
        float4 _GrassCoverageTransition;
        int _GrassRadialCoverage;
        #if defined(GRASS_RADIAL_MIDDLE)
            #include "../../Shaders/GrassMiddlePresentation.cginc"
        #endif
''','''        #endif
        float4 _GrassCoverageTransition;
        int _GrassRadialCoverage;
        #include "../../Shaders/GrassInteractionBlend.cginc"
        float PhysicalCoverage(uint blade)
        {GrassLodGridState s=_GrassLodGridStates[blade/32];return GrassPhysicalWeight(s.poolOffset,s.started,s.padding);}
        #if defined(GRASS_RADIAL_MIDDLE)
            #include "../../Shaders/GrassMiddlePresentation.cginc"
        #endif
''')
draw=replace(draw,'''        {
            #if defined(GRASS_OUTER_COVERAGE) && defined(GRASS_RADIAL_MIDDLE)
                uint blade=index/15;
                if(_GrassRadialCoverage==0 || _GrassLodGridStates[blade/32].poolOffset>=0)return;
                GrassBlade leaf=BladesBuffer[blade];float coverage=CoreCoverage(leaf.root);
                if(coverage<leaf.seed)return;
                float t=MiddlePresentationWeight(coverage,leaf.seed);
                if(t<=0)return;
''','''        {
            #if defined(GRASS_OUTER_COVERAGE) && defined(GRASS_RADIAL_MIDDLE)
                uint blade=index/15;
                if(_GrassRadialCoverage==0)return;
                GrassBlade leaf=BladesBuffer[blade];float coverage=lerp(CoreCoverage(leaf.root),1,PhysicalCoverage(blade));
                if(coverage<leaf.seed)return;
                float t=MiddlePresentationWeight(coverage,leaf.seed);
                if(t<=0)return;
''')
draw=replace(draw,'''                o.clumpNormal=float3(cos(angle)*0.35,0.93675,sin(angle)*0.35);
                o.seed=leaf.seed;
                #if defined(GRASS_OUTER_COVERAGE)
                    o.coverage=_GrassLodGridStates[blade/32].poolOffset>=0?1:CoreCoverage(leaf.root)-leaf.seed;
                #endif
                #if defined(GRASS_LOD_BASE_COLORS)
                    o.xpbd=_GrassLodGridStates[blade/32].poolOffset>=0?1:0;
''','''                o.clumpNormal=float3(cos(angle)*0.35,0.93675,sin(angle)*0.35);
                o.seed=leaf.seed;
                #if defined(GRASS_OUTER_COVERAGE)
                    o.coverage=lerp(CoreCoverage(leaf.root),1,PhysicalCoverage(blade))-leaf.seed;
                #endif
                #if defined(GRASS_LOD_BASE_COLORS)
                    o.xpbd=_GrassLodGridStates[blade/32].poolOffset>=0?1:0;
''')
draw=replace(draw,'''                shadowV2F o;
                uint index=LeafIndex(vertexID,instanceID);
                #if defined(GRASS_OUTER_COVERAGE)
                    o.coverage=_GrassLodGridStates[index/(15*32)].poolOffset>=0?1:CoreCoverage(BladesBuffer[index/15].root)-BladesBuffer[index/15].seed;
                #endif
                float3 world,normal,width;CompactVertex(index,world,normal,width);
                uint pair=(index/15)*15+min((index%15)/2,6)*2;
''','''                shadowV2F o;
                uint index=LeafIndex(vertexID,instanceID);
                #if defined(GRASS_OUTER_COVERAGE)
                    o.coverage=lerp(CoreCoverage(BladesBuffer[index/15].root),1,PhysicalCoverage(index/15))-BladesBuffer[index/15].seed;
                #endif
                float3 world,normal,width;CompactVertex(index,world,normal,width);
                uint pair=(index/15)*15+min((index%15)/2,6)*2;
''')
(dest/"CompactGrass.shader").write_text(banner+draw,encoding="utf-8")
print("Generated isolated compact solver, handoff, streaming and draw variants.")
