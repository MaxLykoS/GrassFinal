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
        private readonly Material material;
        private readonly GrassSimulationPool simulationPool;
        private readonly List<ComputeBuffer> buffers=new List<ComputeBuffer>();
        private readonly ComputeBuffer states,mapping,initializations,changes,entries,windState,previousWind;
        private readonly ComputeBuffer worldYawBuffer,recycledGrids;
        private readonly ComputeBuffer bakedControlsBuffer,bakedFramesBuffer;
        private GrassResponseType bakedResponse;
        private readonly GrassSimulationPool.ActiveGrid[] renderGrids;
        private readonly int worldWindKernel,worldGeometryKernel;
        private readonly GridState[] gridStates;
        private readonly Vector2Int[] historyMap,changedEntries,seedEntries;
        private readonly Initialization[] newSlots;
        private readonly int[] slotGrid,previousPool;
        private readonly bool[] active,wanted;
        private readonly Stack<int> freeSlots=new Stack<int>();
        private readonly List<int> boundary=new List<int>();
        private readonly GrassGridQuery gridQuery;
        private readonly GrassGridSet work,warm,queryGrids,stateDirty,mapDirty;
        private GrassGridSet frontierWarm,eventNextFrontier;
        private readonly List<int> activeIds=new List<int>(),wantedIds=new List<int>(),transitions=new List<int>();
        private readonly int[] selectedOffsets;
        private readonly float[] boundaryScores;
        private readonly Comparison<int> compareBoundary;
        private readonly int[] recycledGridIds;
        private readonly int[] boundaryScoreEpoch,sortScratch,sortCounts=new int[256];
        private readonly List<int> cachedBoundaryInput=new List<int>(),cachedBoundaryOrder=new List<int>();
        private bool frontierCached,scoreCached,cachedAdaptive;
        private Vector3 cachedFrontierCenter,cachedFrontierVelocity,scoreCenter;
        private float cachedInner,cachedOuter,cachedDuration,scoreFrontier;
        private int worldRevision,cachedFrontierRevision,scoreRevision,scoreEpoch,sortedBoundaryEpoch=-1;
        private readonly GrassHandoffHeap eventDeadlinesHeap,eventFrontierHeap,eventResidents,eventWaiting;
        private readonly GrassHandoffSet eventRequired,eventWarm;
        private readonly float[] eventDeadlines,eventFrontierDistances;
        private readonly int[] eventActiveIndices;
        private readonly List<int> eventClearChanged=new List<int>(),eventRequiredAllocations=new List<int>();
        private readonly List<int> eventWarmSnapshot;
        private bool eventInitialized,eventNeedsSync,eventRankAdaptive,eventHadSelection;
        private int eventStateVersion=-1,eventWarmVersion=-1,eventQueryRevision=-1;
        private Vector3 eventFrontierCenter;
        private float eventNear,eventHysteresis,eventDuration,eventFade;
        private bool eventPromote,eventContactDriven;
        private readonly int resetKernel,initializeKernel,nearInitializeKernel,captureKernel,seedKernel,displayKernel,windKernel,snapshotKernel;
        private readonly int[] kernels;
        private Vector3 center;
        private float poseTime,poseDt=1f/60;
        private Vector3 previousFocus,focusVelocity;
        private float previousFocusTime=-1;
        private bool configured,windConfigured;
        private float configuredTime,configuredDt,configuredContactFade,configuredTransition,configuredWindDisplacement,
            configuredMargin,configuredLodgedSpeed,configuredMass,configuredWindFrequency;
        private int configuredPlasticity,configuredGround;
        private Vector3 configuredGravity,configuredWind;
        private Vector4 configuredWindTile;
        private static class Uniform
        {
            public static readonly int PoseTime=Shader.PropertyToID("PoseTime"),PoseDt=Shader.PropertyToID("PoseDt"),
                ContactFade=Shader.PropertyToID("ContactFadeSeconds"),Transition=Shader.PropertyToID("TransitionSeconds"),
                WindDisplacement=Shader.PropertyToID("WindDisplacement"),Plasticity=Shader.PropertyToID("EnablePlasticity"),
                Ground=Shader.PropertyToID("GroundCollision"),Margin=Shader.PropertyToID("CollisionMargin"),
                LodgedSpeed=Shader.PropertyToID("LodgedAngularSpeed"),Gravity=Shader.PropertyToID("BakedGravity"),
                Mass=Shader.PropertyToID("BakedBladeMass"),LifecycleTime=Shader.PropertyToID("GrassLifecycleTime"),
                LifecycleEntry=Shader.PropertyToID("GrassLifecycleEntrySeconds"),LifecycleExit=Shader.PropertyToID("GrassLifecycleExitSeconds"),
                Wind=Shader.PropertyToID("WindForce"),WindFrequency=Shader.PropertyToID("WindFrequency"),WindTile=Shader.PropertyToID("WindTileOffset");
        }
        public float WarmLookAheadMeters { get; private set; }
        private bool disposed;
        private bool initializingNear;
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
            ComputeBuffer positions,ComputeBuffer normals,GrassCompactStorage compact=null,GrassGridQuery gridQuery=null,GrassSimulationPool simulationPool=null)
        {
            this.data=data; this.physics=physics; this.settings=settings;
            this.material=material;this.simulationPool=simulationPool;
            this.gridQuery=gridQuery??new GrassGridQuery(data.Grids);
            int count=data.Grids.Length;
            work=new GrassGridSet(count);warm=new GrassGridSet(count);queryGrids=new GrassGridSet(count);
            stateDirty=new GrassGridSet(count);mapDirty=new GrassGridSet(count);
            frontierWarm=new GrassGridSet(count);
            selectedOffsets=new int[count];boundaryScores=new float[count];
            boundaryScoreEpoch=new int[count];sortScratch=new int[count];
            compareBoundary=(a,b)=>boundaryScores[a].CompareTo(boundaryScores[b]);
            recycledGridIds=data.WorldYaws!=null?new int[count]:null;
            if(simulationPool!=null)
            {
                eventNextFrontier=new GrassGridSet(count);
                eventDeadlines=new float[count];eventFrontierDistances=new float[count];eventActiveIndices=new int[count];
                eventDeadlinesHeap=new GrassHandoffHeap(count,(a,b)=>CompareKey(eventDeadlines[a],eventDeadlines[b],a,b));
                eventFrontierHeap=new GrassHandoffHeap(count,(a,b)=>CompareKey(eventFrontierDistances[b],eventFrontierDistances[a],a,b));
                eventResidents=new GrassHandoffHeap(count,(a,b)=>CompareOptional(b,a));
                eventWaiting=new GrassHandoffHeap(count,CompareOptional);
                eventRequired=new GrassHandoffSet(count);eventWarm=new GrassHandoffSet(count);eventWarmSnapshot=new List<int>(count);
                eventClearChanged.Capacity=eventRequiredAllocations.Capacity=Mathf.Min(count,Mathf.Max(1,settings.MaxHistoryBlades/32));
            }
            shader=UnityEngine.Object.Instantiate(source);
            try
            {
                resetKernel=shader.FindKernel("ResetGeometry"); initializeKernel=shader.FindKernel("InitializeHistory");
                nearInitializeKernel=compact!=null?shader.FindKernel("InitializeNearHistory"):initializeKernel;
                captureKernel=shader.FindKernel("CaptureTransition"); seedKernel=shader.FindKernel("SeedSimulation");
                displayKernel=shader.FindKernel("UpdateTieredDisplay"); windKernel=shader.FindKernel("AdvanceBakedWind");
                snapshotKernel=shader.FindKernel("DiagnosticSnapshot");
                kernels=compact!=null?new[] { resetKernel,initializeKernel,nearInitializeKernel,captureKernel,seedKernel,displayKernel,snapshotKernel }
                    :new[] { resetKernel,initializeKernel,captureKernel,seedKernel,displayKernel,snapshotKernel };
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
                bakedResponse=response;
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
                var controlsBuffer=NewBuffer(75,16); controlsBuffer.SetData(controls); bakedControlsBuffer=controlsBuffer;
                var framesBuffer=NewBuffer(175,16); framesBuffer.SetData(bakedFrames); bakedFramesBuffer=framesBuffer;
                var yawBuffer=NewBuffer(grids,16); yawBuffer.SetData(yaw);worldYawBuffer=yawBuffer;
                shader.SetInt("EnableBakedResponse",response!=null?1:0); shader.SetInt("GridCount",grids); shader.SetInt("BladeCount",data.BladeCount);
                foreach(int k in kernels)
                {
                    compact?.Bind(shader,k);
                    if(compact!=null)
                    {
                        shader.SetBuffer(k,"CompactBonesRead",bones);shader.SetBuffer(k,"CompactFramesRead",frames);
                        shader.SetBuffer(k,"CompactOffsetsRead",offsets);shader.SetBuffer(k,"CompactRotationsRead",rotations);
                    }
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
                if(compact!=null)shader.SetBuffer(nearInitializeKernel,"HistoryInitializations",initializations);
                shader.SetBuffer(captureKernel,"ChangedEntries",changes); shader.SetBuffer(seedKernel,"Entries",entries);
                shader.SetBuffer(windKernel,"BakedWindState",windState); shader.SetBuffer(windKernel,"PreviousWindState",previousWind);
                shader.SetBuffer(windKernel,"BakedGrids",CullingGrids); shader.SetBuffer(windKernel,"BakedYaw",yawBuffer); shader.SetTexture(windKernel,"WindForceMap",wind);
                material.SetBuffer("_GrassLodGridStates",states);
                if(data.WorldYaws!=null)
                {
                    worldWindKernel=shader.FindKernel("ResetWorldWind");worldGeometryKernel=shader.FindKernel("ResetWorldGeometry");recycledGrids=NewBuffer(grids,4);
                    foreach(int k in new[] {worldWindKernel,worldGeometryKernel})
                    {
                        compact?.Bind(shader,k);
                        if(compact!=null)shader.SetBuffer(k,"GridStates",states);
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
        internal void SetBakedResponse(GrassResponseType response)
        {
            if(response==bakedResponse)return;
            GrassLodHandoff.ValidateResponseReplacement(bakedResponse,response);
            var controls=new Vector4[75]; var frames=new Vector4[175];
            for(int sample=0;sample<25;sample++)
            {
                controls[sample*3]=response.Samples[sample].C1;
                controls[sample*3+1]=response.Samples[sample].C2;
                controls[sample*3+2]=response.Samples[sample].Tip;
                for(int segment=0;segment<7;segment++)
                { var q=response.Samples[sample].Frames[segment];frames[sample*7+segment]=new Vector4(q.x,q.y,q.z,q.w); }
            }
            bakedControlsBuffer.SetData(controls); bakedFramesBuffer.SetData(frames);
            shader.SetVector("BakedDynamics",new Vector4(response.AngularFrequency.x,response.AngularFrequency.y,response.DampingRatio.x,response.DampingRatio.y));
            shader.SetFloat("BakedMaxWind",response.MaxWind);
            shader.SetVectorArray("BakedMomentCompliance",response.MomentCompliance(data));
            bakedResponse=response; settings.BakedResponse=response;
        }
        private void Configure(float time)
        {
            poseTime=time;
            // Select and UpdateDisplay normally configure the same tick. Retain live
            // Inspector changes while avoiding duplicate native calls for stable uniforms.
            if(!configured || configuredTime!=time)
            { shader.SetFloat(Uniform.PoseTime,time);material.SetFloat(Uniform.LifecycleTime,time);configuredTime=time; }
            SetFloat(Uniform.PoseDt,poseDt,ref configuredDt);
            float fade=Mathf.Max(.05f,physics.ContactFadeSeconds),transition=Mathf.Max(.05f,settings.TransitionSeconds);
            if(!configured || configuredContactFade!=fade)
            { shader.SetFloat(Uniform.ContactFade,fade);material.SetFloat(Uniform.LifecycleExit,fade);configuredContactFade=fade; }
            if(!configured || configuredTransition!=transition)
            { shader.SetFloat(Uniform.Transition,transition);material.SetFloat(Uniform.LifecycleEntry,transition);configuredTransition=transition; }
            SetFloat(Uniform.WindDisplacement,Mathf.Max(0,settings.WindDisplacement),ref configuredWindDisplacement);
            int plasticity=physics.EnablePlasticity?1:0,ground=physics.GroundCollision?1:0;
            if(!configured || configuredPlasticity!=plasticity) {shader.SetInt(Uniform.Plasticity,plasticity);configuredPlasticity=plasticity;}
            if(!configured || configuredGround!=ground) {shader.SetInt(Uniform.Ground,ground);configuredGround=ground;}
            SetFloat(Uniform.Margin,Mathf.Max(0,physics.CollisionMargin),ref configuredMargin);
            SetFloat(Uniform.LodgedSpeed,Mathf.Max(.1f,settings.LodgedAngularSpeed),ref configuredLodgedSpeed);
            if(!configured || !configuredGravity.Equals(physics.Gravity))
            { shader.SetVector(Uniform.Gravity,physics.Gravity);configuredGravity=physics.Gravity; }
            SetFloat(Uniform.Mass,physics.BladeMass,ref configuredMass);
            configured=true;
        }
        private void SetFloat(int property,float value,ref float previous)
        { if(!configured || previous!=value) {shader.SetFloat(property,value);previous=value;} }
        public void SetWind(Vector3 acceleration,float frequency,Vector4 tileAndOffset)
        {
            if(!windConfigured || !configuredWind.Equals(acceleration)) {shader.SetVector(Uniform.Wind,acceleration);configuredWind=acceleration;}
            if(!windConfigured || configuredWindFrequency!=frequency) {shader.SetFloat(Uniform.WindFrequency,frequency);configuredWindFrequency=frequency;}
            if(!windConfigured || !configuredWindTile.Equals(tileAndOffset)) {shader.SetVector(Uniform.WindTile,tileAndOffset);configuredWindTile=tileAndOffset;}
            windConfigured=true;
        }
        public void Select(IReadOnlyList<GrassSimulationPool.ActiveGrid> selected,float time,Vector3 focus,IReadOnlyList<Vector4> interactions=null)
        {
            if(simulationPool!=null) {SelectEvents(selected,time,focus);return;}
            SelectFallback(selected,time,focus,interactions);
        }
        private void SelectFallback(IReadOnlyList<GrassSimulationPool.ActiveGrid> selected,float time,Vector3 focus,IReadOnlyList<Vector4> interactions)
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
            center=focus; Configure(time);
            work.Clear();warm.Clear();stateDirty.Clear();mapDirty.Clear();
            // Old near entries and unfinished transitions must survive a move/teleport
            // even when they are outside the new spatial query.
            foreach(int g in activeIds) {work.Add(g);active[g]=false;}
            activeIds.Clear();
            foreach(int g in transitions)work.Add(g);
            foreach(int g in wantedIds)wanted[g]=false;
            wantedIds.Clear();
            for(int i=0;i<selected.Count;i++)
            {
                var grid=selected[i];int g=grid.GridId;
                active[g]=true;selectedOffsets[g]=grid.PoolOffset;activeIds.Add(g);work.Add(g);
            }
            float frontier=0;
            for(int i=0;i<selected.Count;i++) {var d=selected[i].Position-center;d.y=0;
                if(!physics.PromoteInteractions || d.magnitude<=physics.NearDistance+physics.PoolHysteresis)frontier=Mathf.Max(frontier,d.sqrMagnitude);}
            frontier=Mathf.Sqrt(frontier);boundary.Clear();transitions.Clear();int required=0;TransitionBlades=0;
            float duration=Mathf.Max(.05f,settings.TransitionSeconds);
            float band=Mathf.Max(.1f,settings.HistoryBandWidth),inner=Mathf.Max(0,frontier-band),outer=frontier+band;
            float innerSquared=inner*inner,outerSquared=outer*outer;
            if(!scoreCached || !scoreCenter.Equals(center) || scoreFrontier!=frontier || scoreRevision!=worldRevision)
            {
                scoreCached=true;scoreCenter=center;scoreFrontier=frontier;scoreRevision=worldRevision;
                if(++scoreEpoch==0) {Array.Clear(boundaryScoreEpoch,0,boundaryScoreEpoch.Length);scoreEpoch=1;}
            }
            if(selected.Count>0)
            {
                if(!physics.ContactDrivenSimulation)
                {
                    if(!frontierCached || !cachedFrontierCenter.Equals(center) || !cachedFrontierVelocity.Equals(focusVelocity) ||
                        cachedInner!=inner || cachedOuter!=outer || cachedDuration!=duration || cachedAdaptive!=settings.AdaptiveHistory || cachedFrontierRevision!=worldRevision)
                    {
                        frontierCached=true;cachedFrontierCenter=center;cachedFrontierVelocity=focusVelocity;
                        cachedInner=inner;cachedOuter=outer;cachedDuration=duration;cachedAdaptive=settings.AdaptiveHistory;cachedFrontierRevision=worldRevision;
                        frontierWarm.Clear();queryGrids.Clear();gridQuery.AddBounds(center.x,center.z,outer+WarmLookAheadMeters,queryGrids);
                        bool moving=focusVelocity.sqrMagnitude>0;
                        foreach(int g in queryGrids.Items)
                        {
                            var d=data.Grids[g].Pos-center;d.y=0;float distance=d.sqrMagnitude;
                            bool keep=distance>=innerSquared && distance<=outerSquared;
                            if(settings.AdaptiveHistory && moving && distance>=innerSquared)
                            {
                                float extra=Mathf.Max(0,Vector3.Dot(d.normalized,focusVelocity))*duration;
                                float reach=outer+Mathf.Min(20,extra);keep|=distance<=reach*reach;
                            }
                            if(keep)frontierWarm.Add(g);
                        }
                    }
                    foreach(int g in frontierWarm.Items)warm.Add(g);
                }
                if(interactions!=null)for(int i=0;i<interactions.Count;i++)
                {
                    var region=interactions[i];float reach=region.z+band;
                    queryGrids.Clear();gridQuery.AddBounds(region.x,region.y,Mathf.Abs(reach),queryGrids);
                    foreach(int g in queryGrids.Items)
                    {
                        var d=data.Grids[g].Pos-center;
                        float dx=d.x+center.x-region.x,dz=d.z+center.z-region.y;
                        if(dx*dx+dz*dz<=reach*reach)warm.Add(g);
                    }
                }
            }
            foreach(int g in warm.Items)work.Add(g);
            // Preserve the reference's ascending input order, including equal-distance
            // ties in its optional-history sort and free-slot allocation order.
            SortWork();
            foreach(int g in work.Items)
            {
                var before=gridStates[g];var state=before;previousPool[g]=before.PoolOffset;
                // Most queried warm cells are settled Bezier cells, and retained near
                // cells often have no lifecycle event. Avoid rewriting/classifying
                // their unchanged state; entry/exit/interrupt paths remain below.
                if(before.Changed==0 && before.Transitioning==0 && before.PoolOffset==(active[g]?selectedOffsets[g]:-1) &&
                    (!physics.ContactDrivenSimulation || !active[g] || simulationPool.ExitStartedAt(g)<0 || time<simulationPool.ExitStartedAt(g)))
                {
                    if(warm.Contains(g))AddBoundary(g,frontier);
                    continue;
                }
                state.Changed=active[g]!=(before.PoolOffset>=0)?1:0;
                if(state.Changed!=0) {state.Started=time;state.Transitioning=1;}
                if(state.Transitioning!=2 && time-state.Started>=duration)state.Transitioning=0;
                if(physics.ContactDrivenSimulation && active[g])
                {
                    float exit=simulationPool.ExitStartedAt(g);
                    if(exit>=0 && time>=exit) {state.Started=exit;state.Transitioning=2;}
                    else if(before.Transitioning==2)
                    {
                        // Resume from the displayed pose without seeding or resetting the live solver.
                        float t=Mathf.Clamp01((time-before.Started)/Mathf.Max(.05f,physics.ContactFadeSeconds));
                        float weight=1-t*t*(3-2*t);
                        state.Started=time;state.Transitioning=-1-weight;state.Changed=1;
                    }
                }
                if(!active[g] && before.Transitioning==2 && time>=before.Started+Mathf.Max(.05f,physics.ContactFadeSeconds))
                    state.Transitioning=0; // Already faded while still simulated; do not start another exit.
                state.PoolOffset=active[g]?selectedOffsets[g]:-1;
                gridStates[g]=state;
                if(state.PoolOffset!=before.PoolOffset || state.Changed!=before.Changed ||
                    state.Started!=before.Started || state.Transitioning!=before.Transitioning)stateDirty.Add(g);
                if(state.Transitioning!=0 || state.Changed!=0)
                {wanted[g]=true;wantedIds.Add(g);transitions.Add(g);required++;TransitionBlades+=32;}
                else if(warm.Contains(g))
                {
                    AddBoundary(g,frontier);
                }
            }
            if(required>slotGrid.Length)throw new InvalidOperationException("Handoff history pool exhausted by interrupted transitions. Increase MaxHistoryBlades (required "+required*32+").");
            if(settings.AdaptiveHistory)SortBoundary();
            foreach(int g in boundary)if(required<slotGrid.Length) {wanted[g]=true;wantedIds.Add(g);required++;}
            for(int slot=0;slot<slotGrid.Length;slot++)
            {
                int g=slotGrid[slot];if(g<0 || wanted[g])continue;
                historyMap[g]=new Vector2Int(-1,0);slotGrid[slot]=-1;freeSlots.Push(slot);mapDirty.Add(g);
            }
            int created=0,changed=0,seeds=0;
            foreach(int g in work.Items)
            {
                if(wanted[g] && historyMap[g].x<0)
                {
                    int slot=freeSlots.Pop();slotGrid[slot]=g;historyMap[g]=new Vector2Int(slot,0);mapDirty.Add(g);
                    newSlots[created++]=new Initialization {Slot=slot,BladeOffset=data.Grids[g].IdOffset,PreviousPool=previousPool[g]};
                }
                if(gridStates[g].Changed!=0)
                {
                    changedEntries[changed++]=new Vector2Int(historyMap[g].x,data.Grids[g].IdOffset);
                    if(active[g] && previousPool[g]<0)seedEntries[seeds++]=new Vector2Int(gridStates[g].PoolOffset/32,data.Grids[g].IdOffset);
                }
            }
            HistoryResidentBlades=(slotGrid.Length-freeSlots.Count)*32;SimulatedBlades=selected.Count*32;
            UploadDirty(states,gridStates,stateDirty);UploadDirty(mapping,historyMap,mapDirty);
            if(created>0)
            {
                initializations.SetData(newSlots,0,0,created); shader.SetInt("HistoryInitializationCount",created);
                shader.Dispatch(initializeKernel,(created*32+63)/64,1,1);
                if(initializingNear)shader.Dispatch(nearInitializeKernel,(created*32+63)/64,1,1);
            }
            if(changed>0)
            { changes.SetData(changedEntries,0,0,changed); shader.SetInt("ChangedCount",changed); shader.Dispatch(captureKernel,(changed*32+63)/64,1,1); }
            if(seeds>0)
            { entries.SetData(seedEntries,0,0,seeds); shader.SetInt("EntryCount",seeds); shader.Dispatch(seedKernel,(seeds*32+63)/64,1,1); EntryInitializations+=seeds; }
        }
        private static int CompareKey(float a,float b,int idA,int idB)
        {int result=a.CompareTo(b);return result!=0?result:idA.CompareTo(idB);}
        private int CompareOptional(int a,int b)
        {return eventRankAdaptive?CompareKey(boundaryScores[a],boundaryScores[b],a,b):a.CompareTo(b);}
        private void SyncEventActive(int grid)
        {
            bool selected=simulationPool.IsActive(grid);
            if(selected)
            {
                selectedOffsets[grid]=simulationPool.SlotForGrid(grid)*32;
                if(!active[grid]) {eventActiveIndices[grid]=activeIds.Count;activeIds.Add(grid);active[grid]=true;}
            }
            else RemoveEventActive(grid);
        }
        private void RemoveEventActive(int grid)
        {
            if(!active[grid])return;
            int index=eventActiveIndices[grid],last=activeIds[activeIds.Count-1];
            activeIds[index]=last;eventActiveIndices[last]=index;activeIds.RemoveAt(activeIds.Count-1);active[grid]=false;
        }
        private void UpdateEventFrontier(int grid)
        {
            eventFrontierHeap.Remove(grid);
            if(!active[grid])return;
            var delta=data.Grids[grid].Pos-center;delta.y=0;
            if(physics.PromoteInteractions && delta.magnitude>physics.NearDistance+physics.PoolHysteresis)return;
            eventFrontierDistances[grid]=delta.sqrMagnitude;eventFrontierHeap.Add(grid);
        }
        private void SelectEvents(IReadOnlyList<GrassSimulationPool.ActiveGrid> selected,float time,Vector3 focus)
        {
            if(settings.AdaptiveHistory)
            {
                float dt=time-previousFocusTime;Vector3 delta=focus-previousFocus;delta.y=0;
                if(previousFocusTime<0 || delta.magnitude>physics.NearDistance)focusVelocity=Vector3.zero;
                else if(dt>1e-5f)focusVelocity=Vector3.ClampMagnitude(delta/dt,60);
                if(dt>1e-5f || previousFocusTime<0) {previousFocus=focus;previousFocusTime=time;}
            }
            WarmLookAheadMeters=settings.AdaptiveHistory?Mathf.Min(20,focusVelocity.magnitude*Mathf.Max(.05f,settings.TransitionSeconds)):0;
            center=focus;Configure(time);work.Clear();stateDirty.Clear();mapDirty.Clear();eventRequiredAllocations.Clear();
            int stateVersion=simulationPool.StateVersion;
            bool snapshot=!eventInitialized || eventNeedsSync || (stateVersion!=eventStateVersion && stateVersion!=eventStateVersion+1);
            if(snapshot)
            {
                foreach(int grid in activeIds)work.Add(grid);
                for(int i=0;i<selected.Count;i++)work.Add(selected[i].GridId);
            }
            else if(stateVersion!=eventStateVersion)
                for(int i=0;i<simulationPool.StateChanges.Count;i++)work.Add(simulationPool.StateChanges[i]);
            foreach(int grid in eventClearChanged)work.Add(grid);
            eventClearChanged.Clear();
            // Pop before classifying, then reschedule at most one deadline per grid.
            // A small early tolerance lets the original float subtraction decide
            // the exact boundary instead of a rounded addition delaying it a tick.
            float deadlineTolerance=Mathf.Max(1e-6f,Mathf.Abs(time)*1.2e-7f);
            while(eventDeadlinesHeap.Count>0 && eventDeadlines[eventDeadlinesHeap.First]<=time+deadlineTolerance)
                work.Add(eventDeadlinesHeap.Pop());
            float duration=Mathf.Max(.05f,settings.TransitionSeconds),fade=Mathf.Max(.05f,physics.ContactFadeSeconds);
            if(!eventInitialized || duration!=eventDuration || fade!=eventFade || physics.ContactDrivenSimulation!=eventContactDriven)
            {
                foreach(int grid in eventRequired.Items)work.Add(grid);
                foreach(int grid in activeIds)work.Add(grid);
            }
            foreach(int grid in work.Items)SyncEventActive(grid);
            bool moved=!eventInitialized || !eventFrontierCenter.Equals(center) || eventNear!=physics.NearDistance ||
                eventHysteresis!=physics.PoolHysteresis || eventPromote!=physics.PromoteInteractions || eventQueryRevision!=gridQuery.Revision;
            if(moved)
            {
                eventFrontierHeap.Clear();foreach(int grid in activeIds)UpdateEventFrontier(grid);
            }
            else foreach(int grid in work.Items)UpdateEventFrontier(grid);
            float frontier=eventFrontierHeap.Count>0?Mathf.Sqrt(eventFrontierDistances[eventFrontierHeap.First]):0;
            // The comparator is backed by the old scores until all keys below are
            // replaced, allowing normal indexed removals during classification.
            foreach(int grid in work.Items)ClassifyEvent(grid,time,duration,fade);
            UpdateEventWarm(selected.Count>0,frontier,duration,snapshot);
            EnsureEventRanking(frontier);
            if(eventRequired.Count>slotGrid.Length)
                throw new InvalidOperationException("Handoff history pool exhausted by interrupted transitions. Increase MaxHistoryBlades (required "+eventRequired.Count*32+").");
            int created=0;
            foreach(int grid in eventRequiredAllocations)
            {
                if(historyMap[grid].x>=0)continue;
                if(freeSlots.Count==0)
                {
                    if(eventResidents.Count==0)throw new InvalidOperationException("Required handoff history has no available slot.");
                    int evicted=eventResidents.Pop();ReleaseEventHistory(evicted);eventWaiting.Add(evicted);
                }
                AllocateEventHistory(grid,ref created);
            }
            // Keep the nearest optional history without sorting/rebuilding its
            // entire warm union. Unchanged contacts cause no heap or slot work.
            while(eventWaiting.Count>0 && freeSlots.Count>0)
            {int grid=eventWaiting.Pop();AllocateEventHistory(grid,ref created);eventResidents.Add(grid);}
            while(eventWaiting.Count>0 && eventResidents.Count>0 && CompareOptional(eventWaiting.First,eventResidents.First)<0)
            {
                int evicted=eventResidents.Pop(),incoming=eventWaiting.Pop();
                ReleaseEventHistory(evicted);eventWaiting.Add(evicted);
                AllocateEventHistory(incoming,ref created);eventResidents.Add(incoming);
            }
            int changed=0,seeds=0;
            foreach(int grid in work.Items)if(gridStates[grid].Changed!=0)
            {
                changedEntries[changed++]=new Vector2Int(historyMap[grid].x,data.Grids[grid].IdOffset);
                if(active[grid] && previousPool[grid]<0)seedEntries[seeds++]=new Vector2Int(gridStates[grid].PoolOffset/32,data.Grids[grid].IdOffset);
            }
            HistoryResidentBlades=(slotGrid.Length-freeSlots.Count)*32;SimulatedBlades=selected.Count*32;TransitionBlades=eventRequired.Count*32;
            UploadDirty(states,gridStates,stateDirty);UploadDirty(mapping,historyMap,mapDirty);
            if(created>0)
            {
                initializations.SetData(newSlots,0,0,created);shader.SetInt("HistoryInitializationCount",created);
                shader.Dispatch(initializeKernel,(created*32+63)/64,1,1);
                if(initializingNear)shader.Dispatch(nearInitializeKernel,(created*32+63)/64,1,1);
            }
            if(changed>0)
            {changes.SetData(changedEntries,0,0,changed);shader.SetInt("ChangedCount",changed);shader.Dispatch(captureKernel,(changed*32+63)/64,1,1);}
            if(seeds>0)
            {entries.SetData(seedEntries,0,0,seeds);shader.SetInt("EntryCount",seeds);shader.Dispatch(seedKernel,(seeds*32+63)/64,1,1);EntryInitializations+=seeds;}
            eventInitialized=true;eventNeedsSync=false;eventStateVersion=stateVersion;eventQueryRevision=gridQuery.Revision;
            eventFrontierCenter=center;eventNear=physics.NearDistance;eventHysteresis=physics.PoolHysteresis;eventPromote=physics.PromoteInteractions;
            eventDuration=duration;eventFade=fade;eventContactDriven=physics.ContactDrivenSimulation;
        }
        private void ClassifyEvent(int grid,float time,float duration,float fade)
        {
            var before=gridStates[grid];var state=before;previousPool[grid]=before.PoolOffset;
            state.Changed=active[grid]!=(before.PoolOffset>=0)?1:0;
            if(state.Changed!=0) {state.Started=time;state.Transitioning=1;}
            if(state.Transitioning!=2 && time-state.Started>=duration)state.Transitioning=0;
            if(physics.ContactDrivenSimulation && active[grid])
            {
                float exit=simulationPool.ExitStartedAt(grid);
                if(exit>=0 && time>=exit) {state.Started=exit;state.Transitioning=2;}
                else if(before.Transitioning==2)
                {
                    float t=Mathf.Clamp01((time-before.Started)/fade),weight=1-t*t*(3-2*t);
                    state.Started=time;state.Transitioning=-1-weight;state.Changed=1;
                }
            }
            else if(active[grid] && before.Transitioning==2)
            {
                // Switching back to fixed ownership cancels a live exit just like
                // recontact. Capture the displayed blend, keep its running XPBD
                // slot, then retire the required history after the resume fade.
                float t=Mathf.Clamp01((time-before.Started)/fade),weight=1-t*t*(3-2*t);
                state.Started=time;state.Transitioning=-1-weight;state.Changed=1;
            }
            if(!active[grid] && before.Transitioning==2 && time>=before.Started+fade)state.Transitioning=0;
            state.PoolOffset=active[grid]?selectedOffsets[grid]:-1;gridStates[grid]=state;
            if(state.PoolOffset!=before.PoolOffset || state.Changed!=before.Changed || state.Started!=before.Started || state.Transitioning!=before.Transitioning)stateDirty.Add(grid);
            bool required=state.Transitioning!=0 || state.Changed!=0;
            if(required)
            {
                eventResidents.Remove(grid);eventWaiting.Remove(grid);eventRequired.Add(grid);wanted[grid]=true;
                if(historyMap[grid].x<0)eventRequiredAllocations.Add(grid);
            }
            else if(eventRequired.Contains(grid))
            {
                eventRequired.Remove(grid);
                if(eventWarm.Contains(grid))
                {if(historyMap[grid].x>=0)eventResidents.Add(grid);else eventWaiting.Add(grid);}
                else ReleaseEventHistory(grid);
            }
            if(state.Changed!=0)eventClearChanged.Add(grid);
            eventDeadlinesHeap.Remove(grid);
            float deadline=float.PositiveInfinity;
            if(state.Transitioning!=0 && state.Transitioning!=2)deadline=state.Started+duration;
            if(physics.ContactDrivenSimulation && active[grid] && state.Transitioning!=2)
            {float exit=simulationPool.ExitStartedAt(grid);if(exit>=0)deadline=Mathf.Min(deadline,exit);}
            if(!float.IsPositiveInfinity(deadline)) {eventDeadlines[grid]=deadline;eventDeadlinesHeap.Add(grid);}
        }
        private void SetEventWarm(int grid,bool contains,float frontier)
        {
            if(contains==eventWarm.Contains(grid))return;
            if(contains)
            {
                eventWarm.Add(grid);boundaryScores[grid]=Mathf.Abs((data.Grids[grid].Pos-center).magnitude-frontier);
                if(!eventRequired.Contains(grid))
                {if(historyMap[grid].x>=0)eventResidents.Add(grid);else eventWaiting.Add(grid);}
            }
            else
            {
                eventWarm.Remove(grid);eventWaiting.Remove(grid);eventResidents.Remove(grid);
                if(!eventRequired.Contains(grid))ReleaseEventHistory(grid);
            }
        }
        private void EnsureEventRanking(float frontier)
        {
            // New warm entries may receive keys for an intermediate focus while
            // capacity is available. Remember that dirtiness even if the focus
            // later returns to the last full rebuild's position.
            if(scoreCached && (!scoreCenter.Equals(center) || scoreFrontier!=frontier || scoreRevision!=worldRevision ||
                eventRankAdaptive!=settings.AdaptiveHistory))scoreCached=false;
            // When every requested cell fits, all optional history is retained:
            // its distance order cannot affect residency. Keep the old numerical
            // heap keys until capacity actually requires choosing a victim/entry.
            // This guard runs before required evictions and limited optional pops.
            if(eventRequired.Count+eventResidents.Count+eventWaiting.Count<=slotGrid.Length)return;
            if(scoreCached)return;
            foreach(int grid in eventWarm.Items)boundaryScores[grid]=Mathf.Abs((data.Grids[grid].Pos-center).magnitude-frontier);
            eventRankAdaptive=settings.AdaptiveHistory;eventResidents.Rebuild();eventWaiting.Rebuild();
            // These are the last full rebuild's keys, not the last Select's keys.
            scoreCached=true;scoreCenter=center;scoreFrontier=frontier;scoreRevision=worldRevision;
        }
        private void UpdateEventWarm(bool selected,float frontier,float duration,bool snapshot)
        {
            var coverage=simulationPool.Coverage;
            float band=Mathf.Max(.1f,settings.HistoryBandWidth),inner=Mathf.Max(0,frontier-band),outer=frontier+band;
            bool ringChanged=!frontierCached || !cachedFrontierCenter.Equals(center) || !cachedFrontierVelocity.Equals(focusVelocity) ||
                cachedInner!=inner || cachedOuter!=outer || cachedDuration!=duration || cachedAdaptive!=settings.AdaptiveHistory ||
                cachedFrontierRevision!=worldRevision || eventContactDriven!=physics.ContactDrivenSimulation || eventHadSelection!=selected;
            if(ringChanged)
            {
                eventNextFrontier.Clear();
                if(selected && !physics.ContactDrivenSimulation)
                {
                    queryGrids.Clear();gridQuery.AddBounds(center.x,center.z,outer+WarmLookAheadMeters,queryGrids);
                    float innerSquared=inner*inner,outerSquared=outer*outer;bool moving=focusVelocity.sqrMagnitude>0;
                    foreach(int grid in queryGrids.Items)
                    {
                        var delta=data.Grids[grid].Pos-center;delta.y=0;float distance=delta.sqrMagnitude;
                        bool keep=distance>=innerSquared && distance<=outerSquared;
                        if(settings.AdaptiveHistory && moving && distance>=innerSquared)
                        {float extra=Mathf.Max(0,Vector3.Dot(delta.normalized,focusVelocity))*duration,reach=outer+Mathf.Min(20,extra);keep|=distance<=reach*reach;}
                        if(keep)eventNextFrontier.Add(grid);
                    }
                }
                var old=frontierWarm;frontierWarm=eventNextFrontier;eventNextFrontier=old;
                foreach(int grid in eventNextFrontier.Items)
                    if(!frontierWarm.Contains(grid))SetEventWarm(grid,selected && coverage.ContainsWarm(grid),frontier);
                foreach(int grid in frontierWarm.Items)SetEventWarm(grid,true,frontier);
                frontierCached=true;cachedFrontierCenter=center;cachedFrontierVelocity=focusVelocity;cachedInner=inner;cachedOuter=outer;
                cachedDuration=duration;cachedAdaptive=settings.AdaptiveHistory;cachedFrontierRevision=worldRevision;
            }
            int version=coverage.WarmVersion;
            if(snapshot || !eventInitialized || selected!=eventHadSelection || (version!=eventWarmVersion && version!=eventWarmVersion+1))
            {
                eventWarmSnapshot.Clear();eventWarmSnapshot.AddRange(eventWarm.Items);
                foreach(int grid in eventWarmSnapshot)SetEventWarm(grid,selected && (coverage.ContainsWarm(grid) || frontierWarm.Contains(grid)),frontier);
                if(selected)for(int i=0;i<coverage.Warm.Count;i++)SetEventWarm(coverage.Warm[i],true,frontier);
            }
            else if(version!=eventWarmVersion)
            {
                for(int i=0;i<coverage.WarmRemoved.Count;i++)
                {int grid=coverage.WarmRemoved[i];SetEventWarm(grid,selected && frontierWarm.Contains(grid),frontier);}
                if(selected)for(int i=0;i<coverage.WarmAdded.Count;i++)SetEventWarm(coverage.WarmAdded[i],true,frontier);
            }
            eventWarmVersion=version;eventHadSelection=selected;
        }
        private void ReleaseEventHistory(int grid)
        {
            int slot=historyMap[grid].x;if(slot<0)return;
            historyMap[grid]=new Vector2Int(-1,0);slotGrid[slot]=-1;freeSlots.Push(slot);wanted[grid]=false;mapDirty.Add(grid);
        }
        private void AllocateEventHistory(int grid,ref int created)
        {
            int slot=freeSlots.Pop();slotGrid[slot]=grid;historyMap[grid]=new Vector2Int(slot,0);wanted[grid]=true;mapDirty.Add(grid);
            int previous=work.Contains(grid)?previousPool[grid]:gridStates[grid].PoolOffset;
            newSlots[created++]=new Initialization {Slot=slot,BladeOffset=data.Grids[grid].IdOffset,PreviousPool=previous};
        }
        private void AddBoundary(int grid,float frontier)
        {
            boundary.Add(grid);
            if(boundaryScoreEpoch[grid]==scoreEpoch)return;
            boundaryScoreEpoch[grid]=scoreEpoch;
            boundaryScores[grid]=Mathf.Abs((data.Grids[grid].Pos-center).magnitude-frontier);
        }
        private void SortBoundary()
        {
            bool reuse=sortedBoundaryEpoch==scoreEpoch && cachedBoundaryInput.Count==boundary.Count;
            if(reuse)for(int i=0;i<boundary.Count;i++)if(cachedBoundaryInput[i]!=boundary[i]) {reuse=false;break;}
            if(reuse) {for(int i=0;i<boundary.Count;i++)boundary[i]=cachedBoundaryOrder[i];return;}
            // Preserve List.Sort's exact equal-score order: reuse only for identical
            // ascending input and identical scores, rather than merge equal keys.
            cachedBoundaryInput.Clear();cachedBoundaryInput.AddRange(boundary);
            boundary.Sort(compareBoundary);
            cachedBoundaryOrder.Clear();cachedBoundaryOrder.AddRange(boundary);sortedBoundaryEpoch=scoreEpoch;
        }
        private void SortWork()
        {
            var ids=work.Items;int count=ids.Count;
            if(count<64) {ids.Sort();return;}
            // Grid IDs are unique/nonnegative. Radix sorting yields the same ascending
            // order with linear work, avoiding comparison sorting every local union.
            int passes=sortScratch.Length<=65536?2:4;
            for(int pass=0;pass<passes;pass++)
            {
                int shift=pass*8;bool fromList=(pass&1)==0;Array.Clear(sortCounts,0,sortCounts.Length);
                for(int i=0;i<count;i++)sortCounts[((fromList?ids[i]:sortScratch[i])>>shift)&255]++;
                int offset=0;for(int bucket=0;bucket<sortCounts.Length;bucket++) {int length=sortCounts[bucket];sortCounts[bucket]=offset;offset+=length;}
                if(fromList)for(int i=0;i<count;i++) {int id=ids[i];sortScratch[sortCounts[(id>>shift)&255]++]=id;}
                else for(int i=0;i<count;i++) {int id=sortScratch[i];ids[sortCounts[(id>>shift)&255]++]=id;}
            }
        }
        private static void UploadDirty(ComputeBuffer buffer,Array values,GrassGridSet dirty)
        {
            var ids=dirty.Items;if(ids.Count==0)return;
            ids.Sort();int first=ids[0],last=first,ranges=1;
            for(int i=1;i<ids.Count;i++)if(ids[i]-ids[i-1]>16)ranges++;
            // Bound driver calls on fragmented transitions; dense changes are cheaper
            // as one contiguous transfer. Ordinary stationary frames upload nothing.
            if(ranges>32) {buffer.SetData(values,first,first,ids[ids.Count-1]-first+1);return;}
            for(int i=1;i<ids.Count;i++)
            {
                int next=ids[i];
                if(next-last>16) {buffer.SetData(values,first,first,last-first+1);first=next;}
                last=next;
            }
            buffer.SetData(values,first,first,last-first+1);
        }
        public void UpdateDisplay(float time,float tick)
        { poseDt=tick; Configure(time); if(settings.BakedResponse!=null)shader.Dispatch(windKernel,(data.Grids.Length+63)/64,1,1);
            // Reading Blades refreshes streamed CPU diagnostic mirrors. Dispatch only
            // needs the count; newly loaded geometry is already generated on the GPU.
            shader.Dispatch(displayKernel,(data.BladeCount+63)/64,1,1); }
        public void RecycleWorldGrids(IReadOnlyList<int> grids,float time)
        {
            if(recycledGrids==null)throw new InvalidOperationException("Not a streamed field.");
            worldRevision++;
            if(simulationPool!=null)eventNeedsSync=true;
            var ids=recycledGridIds;int i=0;stateDirty.Clear();mapDirty.Clear();
            foreach(int g in grids)
            {
                if(simulationPool!=null)
                {
                    RemoveEventActive(g);eventDeadlinesHeap.Remove(g);eventFrontierHeap.Remove(g);
                    eventRequired.Remove(g);eventWarm.Remove(g);eventResidents.Remove(g);eventWaiting.Remove(g);
                }
                ids[i++]=g;int slot=historyMap[g].x;
                if(slot>=0) {slotGrid[slot]=-1;freeSlots.Push(slot);}
                historyMap[g]=new Vector2Int(-1,0);gridStates[g]=new GridState { PoolOffset=-1 };previousPool[g]=-1;active[g]=wanted[g]=false;
                renderGrids[g].Position=data.Grids[g].Pos;stateDirty.Add(g);mapDirty.Add(g);
            }
            UploadDirty(states,gridStates,stateDirty);UploadDirty(mapping,historyMap,mapDirty);
            UploadDirty(CullingGrids,renderGrids,stateDirty);UploadDirty(worldYawBuffer,data.WorldYaws,stateDirty);
            recycledGrids.SetData(ids,0,0,grids.Count);shader.SetInt("RecycledCount",grids.Count);Configure(time);
            shader.Dispatch(worldWindKernel,(grids.Count+63)/64,1,1);
            shader.Dispatch(worldGeometryKernel,(grids.Count*32+63)/64,1,1);
        }
        public void Reset(IReadOnlyList<GrassSimulationPool.ActiveGrid> selected,float time)
        {
            if(simulationPool!=null)
            {
                eventDeadlinesHeap.Clear();eventFrontierHeap.Clear();eventResidents.Clear();eventWaiting.Clear();
                eventRequired.Clear();eventWarm.Clear();eventClearChanged.Clear();eventRequiredAllocations.Clear();eventWarmSnapshot.Clear();
                eventNextFrontier.Clear();frontierWarm.Clear();frontierCached=scoreCached=eventInitialized=false;eventNeedsSync=true;
                eventStateVersion=eventWarmVersion=eventQueryRevision=-1;eventHadSelection=false;
            }
            previousFocusTime=-1;focusVelocity=Vector3.zero;WarmLookAheadMeters=0;
            activeIds.Clear();wantedIds.Clear();transitions.Clear();
            Array.Clear(active,0,active.Length);Array.Clear(wanted,0,wanted.Length);
            freeSlots.Clear(); for(int s=slotGrid.Length-1;s>=0;s--) { slotGrid[s]=-1; freeSlots.Push(s); }
            for(int g=0;g<gridStates.Length;g++) { gridStates[g]=new GridState { PoolOffset=-1 }; historyMap[g]=new Vector2Int(-1,0); }
            states.SetData(gridStates); mapping.SetData(historyMap); windState.SetData(new Vector4[gridStates.Length]); previousWind.SetData(new Vector4[gridStates.Length]);
            Configure(time);
            if(settings.UseCompactMiddle)
            {
                // Birth/reset of already-near grass uses its full precision render cache.
                // Quantization is confined to the middle representation and actual LOD entries.
                foreach(var grid in selected)gridStates[grid.GridId]=new GridState {PoolOffset=grid.PoolOffset};
                states.SetData(gridStates);
            }
            shader.Dispatch(resetKernel,(data.BladeCount+63)/64,1,1);
            if(settings.UseCompactMiddle)
            {
                foreach(var grid in selected)gridStates[grid.GridId]=new GridState {PoolOffset=-1};
                states.SetData(gridStates);shader.SetInt("CompactInitialSeeding",1);initializingNear=true;
            }
            Select(selected,time,center);
            if(settings.UseCompactMiddle) {shader.SetInt("CompactInitialSeeding",0);initializingNear=false;}
        }
        private void Snapshot(out BoneInfo[] nodes,out GrassBladeData.Frame[] frames,bool readNodes)
        {
            Configure(poseTime); nodes=null; frames=null;
            using(var nodeBuffer=new ComputeBuffer(data.BladeCount*8,48))using(var frameBuffer=new ComputeBuffer(data.BladeCount*7,32))
            {
                shader.SetBuffer(snapshotKernel,"DiagnosticNodes",nodeBuffer); shader.SetBuffer(snapshotKernel,"DiagnosticFrames",frameBuffer);
                shader.Dispatch(snapshotKernel,(data.BladeCount+63)/64,1,1);
                if(readNodes) { nodes=new BoneInfo[data.BladeCount*8]; nodeBuffer.GetData(nodes);
                    for(int b=0;b<data.BladeCount;b++)for(int n=0;n<8;n++)nodes[b*8+n].Position+=data.GetBlade(b).Root; }
                else { frames=new GrassBladeData.Frame[data.BladeCount*7]; frameBuffer.GetData(frames); }
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
