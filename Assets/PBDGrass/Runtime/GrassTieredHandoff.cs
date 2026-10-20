using System;
using System.Collections.Generic;
using UnityEngine;

namespace GrassFinal.PBDGrass
{
    // Full temporal state belongs to boundary/transition slots, not world blade IDs.
    // Static mid-field blades use the existing render geometry as their prior visible pose.
    internal sealed class GrassTieredHandoff : IDisposable
    {
        private struct GridState { public int PoolOffset,Changed; public float Started,Transitioning; }
        private struct Initialization { public int Slot,BladeOffset,PreviousPool,Padding; }
        private readonly GrassBladeData data;
        private readonly XPBDGrassSettings physics;
        private readonly GrassHandoffSettings settings;
        private readonly ComputeShader shader;
        private readonly List<ComputeBuffer> buffers=new List<ComputeBuffer>();
        private readonly ComputeBuffer states,mapping,initializations,changes,entries,windState,previousWind;
        private readonly ComputeBuffer worldYawBuffer,recycledGrids;
        private readonly GrassSimulationPool.ActiveGrid[] renderGrids;
        private readonly int worldWindKernel,worldGeometryKernel;
        private readonly GridState[] gridStates;
        private readonly Vector2Int[] historyMap,changedEntries,seedEntries;
        private readonly Initialization[] newSlots;
        private readonly int[] slotGrid,previousPool;
        private readonly bool[] active,wanted;
        private readonly Stack<int> freeSlots=new Stack<int>();
        private readonly List<int> boundary=new List<int>();
        private readonly int resetKernel,initializeKernel,captureKernel,seedKernel,displayKernel,windKernel,snapshotKernel;
        private readonly int[] kernels;
        private Vector3 center;
        private float poseTime,poseDt=1f/60;
        private Vector3 previousFocus,focusVelocity;
        private float previousFocusTime=-1;
        public float WarmLookAheadMeters { get; private set; }
        private bool disposed;
        public ComputeBuffer CullingGrids { get; }
        public ComputeBuffer ClassificationStates => states;
        public int EntryInitializations { get; private set; }
        public int SimulatedBlades { get; private set; }
        public int HistoryCapacityBlades => slotGrid.Length*32;
        public int HistoryResidentBlades { get; private set; }
        public int TransitionBlades { get; private set; }
        public long AllocatedGpuBytes
        { get { long bytes=0; foreach(var b in buffers)bytes+=(long)b.count*b.stride; return bytes; } }
        public GrassTieredHandoff(GrassBladeData data,XPBDGrassSettings physics,GrassHandoffSettings settings,
            ComputeShader source,Material material,ComputeBuffer bones,ComputeBuffer frames,ComputeBuffer plastic,
            ComputeBuffer rest,ComputeBuffer rods,ComputeBuffer blades,ComputeBuffer masses,Texture wind,
            ComputeBuffer positions,ComputeBuffer normals)
        {
            this.data=data; this.physics=physics; this.settings=settings;
            shader=UnityEngine.Object.Instantiate(source);
            try
            {
                resetKernel=shader.FindKernel("ResetGeometry"); initializeKernel=shader.FindKernel("InitializeHistory");
                captureKernel=shader.FindKernel("CaptureTransition"); seedKernel=shader.FindKernel("SeedSimulation");
                displayKernel=shader.FindKernel("UpdateTieredDisplay"); windKernel=shader.FindKernel("AdvanceBakedWind");
                snapshotKernel=shader.FindKernel("DiagnosticSnapshot");
                kernels=new[] { resetKernel,initializeKernel,captureKernel,seedKernel,displayKernel,snapshotKernel };
                int grids=data.Grids.Length,capacity=Mathf.Min(grids,Mathf.Max(1,settings.MaxHistoryBlades/32));
                gridStates=new GridState[grids]; historyMap=new Vector2Int[grids]; previousPool=new int[grids];
                active=new bool[grids]; wanted=new bool[grids]; slotGrid=new int[capacity];
                newSlots=new Initialization[capacity]; changedEntries=new Vector2Int[grids]; seedEntries=new Vector2Int[grids];
                states=NewBuffer(grids,16); mapping=NewBuffer(grids,8);
                initializations=NewBuffer(capacity,16); changes=NewBuffer(grids,8); entries=NewBuffer(grids,8);
                var nodes=NewBuffer(capacity*32*8,48); var directions=NewBuffer(capacity*32*7,32);
                var offsets=NewBuffer(capacity*32*8,12); var rotations=NewBuffer(capacity*32*7,16);
                windState=NewBuffer(grids,16); previousWind=NewBuffer(grids,16);
                var allGrids=new GrassSimulationPool.ActiveGrid[grids]; renderGrids=allGrids;
                var yaw=new Quaternion[grids]; var controls=new Vector4[75]; var bakedFrames=new Vector4[175];
                for(int g=0;g<grids;g++)
                {
                    allGrids[g]=new GrassSimulationPool.ActiveGrid { Position=data.Grids[g].Pos,BladeOffset=data.Grids[g].IdOffset,GridId=g,PoolOffset=-1 };
                    yaw[g]=settings.BakedResponse!=null?data.GridYaw(g):Quaternion.identity;
                }
                CullingGrids=NewBuffer(grids,32); CullingGrids.SetData(allGrids);
                var response=settings.BakedResponse;
                if(response!=null)
                {
                    if(!response.IsBaked || response.GridSize!=5)throw new ArgumentException("Bake the response type first.");
                    for(int s=0;s<25;s++)
                    {
                        controls[s*3]=response.Samples[s].C1; controls[s*3+1]=response.Samples[s].C2; controls[s*3+2]=response.Samples[s].Tip;
                        for(int j=0;j<7;j++) { var q=response.Samples[s].Frames[j]; bakedFrames[s*7+j]=new Vector4(q.x,q.y,q.z,q.w); }
                    }
                    shader.SetVector("BakedDynamics",new Vector4(response.AngularFrequency.x,response.AngularFrequency.y,response.DampingRatio.x,response.DampingRatio.y));
                    shader.SetFloat("BakedMaxWind",response.MaxWind); shader.SetVectorArray("BakedMomentCompliance",response.MomentCompliance(data));
                }
                var controlsBuffer=NewBuffer(75,16); controlsBuffer.SetData(controls);
                var framesBuffer=NewBuffer(175,16); framesBuffer.SetData(bakedFrames);
                var yawBuffer=NewBuffer(grids,16); yawBuffer.SetData(yaw);worldYawBuffer=yawBuffer;
                shader.SetInt("EnableBakedResponse",response!=null?1:0); shader.SetInt("GridCount",grids); shader.SetInt("BladeCount",data.Blades.Length);
                foreach(int k in kernels)
                {
                    shader.SetBuffer(k,"BladesBuffer",blades); shader.SetBuffer(k,"RodsBuffer",rods); shader.SetBuffer(k,"RestNodesBuffer",rest);
                    shader.SetBuffer(k,"PlasticBendsBuffer",plastic); shader.SetBuffer(k,"GridStates",states); shader.SetBuffer(k,"HistoryMap",mapping);
                    shader.SetBuffer(k,"HistoryNodes",nodes); shader.SetBuffer(k,"HistoryFrames",directions);
                    shader.SetBuffer(k,"TransitionOffsets",offsets); shader.SetBuffer(k,"TransitionRotations",rotations);
                    shader.SetBuffer(k,"BonesBuffer",bones); shader.SetBuffer(k,"FramesBuffer",frames);
                    shader.SetBuffer(k,"ResultPosBuffer",positions); shader.SetBuffer(k,"NormalBuffer",normals);
                    shader.SetBuffer(k,"BakedWindRead",windState); shader.SetBuffer(k,"BakedWindPrevious",previousWind);
                    shader.SetBuffer(k,"BakedControls",controlsBuffer); shader.SetBuffer(k,"BakedFrames",framesBuffer);
                    shader.SetBuffer(k,"BakedYaw",yawBuffer); shader.SetBuffer(k,"BakedInverseMass",masses); shader.SetTexture(k,"WindForceMap",wind);
                }
                shader.SetBuffer(initializeKernel,"HistoryInitializations",initializations);
                shader.SetBuffer(captureKernel,"ChangedEntries",changes); shader.SetBuffer(seedKernel,"Entries",entries);
                shader.SetBuffer(windKernel,"BakedWindState",windState); shader.SetBuffer(windKernel,"PreviousWindState",previousWind);
                shader.SetBuffer(windKernel,"BakedGrids",CullingGrids); shader.SetBuffer(windKernel,"BakedYaw",yawBuffer); shader.SetTexture(windKernel,"WindForceMap",wind);
                material.SetBuffer("_GrassLodGridStates",states);
                if(data.WorldYaws!=null)
                {
                    worldWindKernel=shader.FindKernel("ResetWorldWind");worldGeometryKernel=shader.FindKernel("ResetWorldGeometry");recycledGrids=NewBuffer(grids,4);
                    foreach(int k in new[] {worldWindKernel,worldGeometryKernel})
                    {
                        shader.SetBuffer(k,"RecycledGrids",recycledGrids);shader.SetBuffer(k,"BakedGrids",CullingGrids);shader.SetBuffer(k,"BakedYaw",yawBuffer);
                        shader.SetBuffer(k,"BakedWindState",windState);shader.SetBuffer(k,"PreviousWindState",previousWind);
                        shader.SetBuffer(k,"BakedWindRead",windState);shader.SetBuffer(k,"BakedWindPrevious",previousWind);
                        shader.SetBuffer(k,"BladesBuffer",blades);shader.SetBuffer(k,"RodsBuffer",rods);shader.SetBuffer(k,"RestNodesBuffer",rest);
                        shader.SetBuffer(k,"PlasticBendsBuffer",plastic);shader.SetBuffer(k,"BakedControls",controlsBuffer);shader.SetBuffer(k,"BakedFrames",framesBuffer);
                        shader.SetBuffer(k,"BakedInverseMass",masses);shader.SetBuffer(k,"ResultPosBuffer",positions);shader.SetBuffer(k,"NormalBuffer",normals);shader.SetTexture(k,"WindForceMap",wind);
                    }
                }
                Reset(Array.Empty<GrassSimulationPool.ActiveGrid>(),0);
            }
            catch { Dispose(); throw; }
        }
        private ComputeBuffer NewBuffer(int count,int stride)
        { var b=new ComputeBuffer(count,stride); buffers.Add(b); return b; }
        private void Configure(float time)
        {
            poseTime=time; shader.SetFloat("PoseTime",time); shader.SetFloat("PoseDt",poseDt);
            shader.SetFloat("TransitionSeconds",Mathf.Max(.05f,settings.TransitionSeconds)); shader.SetFloat("WindDisplacement",Mathf.Max(0,settings.WindDisplacement));
            shader.SetInt("EnablePlasticity",physics.EnablePlasticity?1:0); shader.SetInt("GroundCollision",physics.GroundCollision?1:0);
            shader.SetFloat("CollisionMargin",Mathf.Max(0,physics.CollisionMargin)); shader.SetFloat("LodgedAngularSpeed",Mathf.Max(.1f,settings.LodgedAngularSpeed));
            shader.SetVector("BakedGravity",physics.Gravity); shader.SetFloat("BakedBladeMass",physics.BladeMass);
        }
        public void SetWind(Vector3 acceleration,float frequency,Vector4 tileAndOffset)
        { shader.SetVector("WindForce",acceleration); shader.SetFloat("WindFrequency",frequency); shader.SetVector("WindTileOffset",tileAndOffset); }
        public void Select(IReadOnlyList<GrassSimulationPool.ActiveGrid> selected,float time,Vector3 focus,IReadOnlyList<Vector4> interactions=null)
        {
            if(settings.AdaptiveHistory)
            {
                float dt=time-previousFocusTime;Vector3 delta=focus-previousFocus;delta.y=0;
                if(previousFocusTime<0 || delta.magnitude>physics.NearDistance)focusVelocity=Vector3.zero;
                else if(dt>1e-5f)focusVelocity=Vector3.ClampMagnitude(delta/dt,60);
                // Multiple classifications at one simulation time must not consume the motion sample.
                if(dt>1e-5f || previousFocusTime<0) {previousFocus=focus;previousFocusTime=time;}
            }
            WarmLookAheadMeters=settings.AdaptiveHistory?Mathf.Min(20,focusVelocity.magnitude*Mathf.Max(.05f,settings.TransitionSeconds)):0;
            center=focus; Configure(time); Array.Clear(active,0,active.Length); Array.Clear(wanted,0,wanted.Length);
            foreach(var grid in selected)active[grid.GridId]=true;
            float frontier=0;
            foreach(var grid in selected) { var d=grid.Position-center; d.y=0;
                if(!physics.PromoteInteractions || d.magnitude<=physics.NearDistance+physics.PoolHysteresis)frontier=Mathf.Max(frontier,d.sqrMagnitude); }
            frontier=Mathf.Sqrt(frontier); boundary.Clear(); int required=0; TransitionBlades=0;
            float band=Mathf.Max(.1f,settings.HistoryBandWidth),inner=Mathf.Max(0,frontier-band),outer=frontier+band;
            float innerSquared=inner*inner,outerSquared=outer*outer;
            for(int g=0;g<gridStates.Length;g++)
            {
                var state=gridStates[g]; previousPool[g]=state.PoolOffset;
                state.Changed=active[g]!=(state.PoolOffset>=0)?1:0;
                if(state.Changed!=0) { state.Started=time; state.Transitioning=1; }
                if(time-state.Started>=Mathf.Max(.05f,settings.TransitionSeconds))state.Transitioning=0;
                if(!active[g])state.PoolOffset=-1;
                gridStates[g]=state;
                if(state.Transitioning!=0) { wanted[g]=true; required++; TransitionBlades+=32; }
                else if(selected.Count>0)
                {
                    var d=data.Grids[g].Pos-center; d.y=0;
                    float distance=d.sqrMagnitude;
                    bool warm=distance>=innerSquared && distance<=outerSquared;
                    if(settings.AdaptiveHistory && distance>=innerSquared)
                    {
                        float extra=Mathf.Max(0,Vector3.Dot(d.normalized,focusVelocity))*Mathf.Max(.05f,settings.TransitionSeconds);
                        float reach=outer+Mathf.Min(20,extra);warm|=distance<=reach*reach;
                    }
                    if(interactions!=null)foreach(var region in interactions)
                    {float dx=d.x+center.x-region.x,dz=d.z+center.z-region.y,reach=region.z+band;warm|=dx*dx+dz*dz<=reach*reach;}
                    if(warm)boundary.Add(g);
                }
            }
            if(required>slotGrid.Length)throw new InvalidOperationException("Handoff history pool exhausted by interrupted transitions. Increase MaxHistoryBlades (required "+required*32+").");
            if(settings.AdaptiveHistory)boundary.Sort((a,b)=>
            {
                // Keep the nearest approaching boundary first when optional warming reaches its budget.
                float da=(data.Grids[a].Pos-center).magnitude,db=(data.Grids[b].Pos-center).magnitude;
                return Mathf.Abs(da-frontier).CompareTo(Mathf.Abs(db-frontier));
            });
            // Transition states are mandatory. Warm boundary history uses the remaining budget.
            foreach(int g in boundary)if(required<slotGrid.Length) { wanted[g]=true; required++; }
            foreach(var grid in selected) { var state=gridStates[grid.GridId]; state.PoolOffset=grid.PoolOffset; gridStates[grid.GridId]=state; }
            bool mappingChanged=false;
            for(int slot=0;slot<slotGrid.Length;slot++)
            {
                int g=slotGrid[slot]; if(g<0 || wanted[g])continue;
                historyMap[g]=new Vector2Int(-1,0); slotGrid[slot]=-1; freeSlots.Push(slot); mappingChanged=true;
            }
            int created=0,changed=0,seeds=0;
            for(int g=0;g<wanted.Length;g++)
            {
                if(wanted[g] && historyMap[g].x<0)
                {
                    int slot=freeSlots.Pop(); slotGrid[slot]=g; historyMap[g]=new Vector2Int(slot,0);
                    mappingChanged=true;
                    newSlots[created++]=new Initialization { Slot=slot,BladeOffset=data.Grids[g].IdOffset,PreviousPool=previousPool[g] };
                }
                if(gridStates[g].Changed!=0)
                {
                    changedEntries[changed++]=new Vector2Int(historyMap[g].x,data.Grids[g].IdOffset);
                    if(active[g])seedEntries[seeds++]=new Vector2Int(gridStates[g].PoolOffset/32,data.Grids[g].IdOffset);
                }
            }
            HistoryResidentBlades=(slotGrid.Length-freeSlots.Count)*32; SimulatedBlades=selected.Count*32;
            states.SetData(gridStates); if(mappingChanged)mapping.SetData(historyMap);
            if(created>0)
            {
                initializations.SetData(newSlots,0,0,created); shader.SetInt("HistoryInitializationCount",created);
                shader.Dispatch(initializeKernel,(created*32+63)/64,1,1);
            }
            if(changed>0)
            { changes.SetData(changedEntries,0,0,changed); shader.SetInt("ChangedCount",changed); shader.Dispatch(captureKernel,(changed*32+63)/64,1,1); }
            if(seeds>0)
            { entries.SetData(seedEntries,0,0,seeds); shader.SetInt("EntryCount",seeds); shader.Dispatch(seedKernel,(seeds*32+63)/64,1,1); EntryInitializations+=seeds; }
        }
        public void UpdateDisplay(float time,float tick)
        { poseDt=tick; Configure(time); if(settings.BakedResponse!=null)shader.Dispatch(windKernel,(data.Grids.Length+63)/64,1,1);
            shader.Dispatch(displayKernel,(data.Blades.Length+63)/64,1,1); }
        public void RecycleWorldGrids(IReadOnlyList<int> grids,float time)
        {
            if(recycledGrids==null)throw new InvalidOperationException("Not a streamed field.");
            var ids=new int[grids.Count];int i=0;
            foreach(int g in grids)
            {
                ids[i++]=g;int slot=historyMap[g].x;
                if(slot>=0) {slotGrid[slot]=-1;freeSlots.Push(slot);}
                historyMap[g]=new Vector2Int(-1,0);gridStates[g]=new GridState { PoolOffset=-1 };previousPool[g]=-1;active[g]=wanted[g]=false;
                renderGrids[g].Position=data.Grids[g].Pos;
            }
            states.SetData(gridStates);mapping.SetData(historyMap);CullingGrids.SetData(renderGrids);worldYawBuffer.SetData(data.WorldYaws);
            recycledGrids.SetData(ids);shader.SetInt("RecycledCount",ids.Length);Configure(time);
            shader.Dispatch(worldWindKernel,(ids.Length+63)/64,1,1);
            shader.Dispatch(worldGeometryKernel,(ids.Length*32+63)/64,1,1);
        }
        public void Reset(IReadOnlyList<GrassSimulationPool.ActiveGrid> selected,float time)
        {
            previousFocusTime=-1;focusVelocity=Vector3.zero;WarmLookAheadMeters=0;
            freeSlots.Clear(); for(int s=slotGrid.Length-1;s>=0;s--) { slotGrid[s]=-1; freeSlots.Push(s); }
            for(int g=0;g<gridStates.Length;g++) { gridStates[g]=new GridState { PoolOffset=-1 }; historyMap[g]=new Vector2Int(-1,0); }
            states.SetData(gridStates); mapping.SetData(historyMap); windState.SetData(new Vector4[gridStates.Length]); previousWind.SetData(new Vector4[gridStates.Length]);
            Configure(time); shader.Dispatch(resetKernel,(data.Blades.Length+63)/64,1,1); Select(selected,time,center);
        }
        private void Snapshot(out BoneInfo[] nodes,out GrassBladeData.Frame[] frames,bool readNodes)
        {
            Configure(poseTime); nodes=null; frames=null;
            using(var nodeBuffer=new ComputeBuffer(data.Blades.Length*8,48))using(var frameBuffer=new ComputeBuffer(data.Blades.Length*7,32))
            {
                shader.SetBuffer(snapshotKernel,"DiagnosticNodes",nodeBuffer); shader.SetBuffer(snapshotKernel,"DiagnosticFrames",frameBuffer);
                shader.Dispatch(snapshotKernel,(data.Blades.Length+63)/64,1,1);
                if(readNodes) { nodes=new BoneInfo[data.Blades.Length*8]; nodeBuffer.GetData(nodes);
                    for(int b=0;b<data.Blades.Length;b++)for(int n=0;n<8;n++)nodes[b*8+n].Position+=data.Blades[b].Root; }
                else { frames=new GrassBladeData.Frame[data.Blades.Length*7]; frameBuffer.GetData(frames); }
            }
        }
        public BoneInfo[] ReadDisplayNodes() { Snapshot(out var nodes,out var frames,true); return nodes; }
        public GrassBladeData.Frame[] ReadDisplayFrames() { Snapshot(out var nodes,out var frames,false); return frames; }
        public Vector4[] ReadBakedWindState() { var values=new Vector4[data.Grids.Length]; windState.GetData(values); return values; }
        public void Dispose()
        { if(disposed)return; disposed=true; foreach(var b in buffers)b.Release(); buffers.Clear();
            if(shader!=null) { if(Application.isPlaying)UnityEngine.Object.Destroy(shader); else UnityEngine.Object.DestroyImmediate(shader); } }
    }
}
