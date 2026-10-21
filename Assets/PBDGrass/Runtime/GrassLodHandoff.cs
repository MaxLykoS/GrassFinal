using System;
using System.Collections.Generic;
using UnityEngine;

namespace GrassFinal.PBDGrass
{
    // Small-patch contract prototype. Global pose history is deliberately not a world streaming cache.
    public sealed class GrassLodHandoff : IDisposable
    {
        private readonly GrassTieredHandoff tiered;
        private struct GridState { public int PoolOffset, Changed; public float Started, Padding; }
        private struct Initialization { public int Slot, BladeOffset; }
        private readonly GrassBladeData data;
        private readonly XPBDGrassSettings physics;
        private readonly GrassHandoffSettings settings;
        private readonly ComputeShader shader;
        private readonly List<ComputeBuffer> buffers = new List<ComputeBuffer>();
        private readonly ComputeBuffer states, entries, displayNodes, displayFrames;
        private readonly GridState[] gridStates;
        private readonly bool[] selected;
        private readonly Initialization[] initializations;
        private readonly int resetKernel, captureKernel, seedKernel, displayKernel;
        private readonly int windKernel;
        private readonly ComputeBuffer windState;
        public ComputeBuffer CullingGrids { get; }
        private int entryInitializations,simulatedBlades;
        public int EntryInitializations => tiered!=null?tiered.EntryInitializations:entryInitializations;
        public int SimulatedBlades => tiered!=null?tiered.SimulatedBlades:simulatedBlades;
        public int HistoryCapacityBlades => tiered!=null?tiered.HistoryCapacityBlades:data.BladeCount;
        public int HistoryResidentBlades => tiered!=null?tiered.HistoryResidentBlades:data.BladeCount;
        public int TransitionBlades => tiered!=null?tiered.TransitionBlades:0;
        public float WarmLookAheadMeters => tiered!=null?tiered.WarmLookAheadMeters:0;
        public long HistoryBytes => (long)HistoryCapacityBlades*816;
        public long AllocatedGpuBytes
        {
            get { if(tiered!=null)return tiered.AllocatedGpuBytes;
                long bytes=0; foreach(var buffer in buffers)bytes+=(long)buffer.count*buffer.stride; return bytes; }
        }

        internal GrassLodHandoff(GrassBladeData data, XPBDGrassSettings physics, GrassHandoffSettings settings,
            ComputeShader source, Material material, ComputeBuffer bones, ComputeBuffer frames,
            ComputeBuffer plastic, ComputeBuffer rest, ComputeBuffer rods, ComputeBuffer blades,ComputeBuffer masses, Texture wind,
            ComputeBuffer physicsPositions,ComputeBuffer physicsNormals,GrassCompactStorage compact=null,GrassGridQuery gridQuery=null,GrassSimulationPool simulationPool=null)
        {
            this.data=data; this.physics=physics; this.settings=settings;
            if(settings.UseTieredState)
            {
                tiered=new GrassTieredHandoff(data,physics,settings,source,material,bones,frames,plastic,rest,rods,blades,masses,wind,physicsPositions,physicsNormals,compact,gridQuery,simulationPool);
                CullingGrids=tiered.CullingGrids; return;
            }
            shader=UnityEngine.Object.Instantiate(source);
            try
            {
                resetKernel=shader.FindKernel("ResetDisplay"); captureKernel=shader.FindKernel("CaptureTransition");
                seedKernel=shader.FindKernel("SeedSimulation"); displayKernel=shader.FindKernel("UpdateDisplay");
                windKernel=shader.FindKernel("AdvanceBakedWind");
                int count=data.BladeCount;
                displayNodes=NewBuffer(count*8,48); displayFrames=NewBuffer(count*7,32);
                var offsets=NewBuffer(count*8,12); var rotations=NewBuffer(count*7,16);
                states=NewBuffer(data.Grids.Length,16); entries=NewBuffer(data.Grids.Length,8);
                material.SetBuffer("_GrassLodGridStates",states);
                ConfigureDebugColors(material);
                gridStates=new GridState[data.Grids.Length]; selected=new bool[gridStates.Length];
                initializations=new Initialization[gridStates.Length];
                var grids=new GrassSimulationPool.ActiveGrid[gridStates.Length];
                for(int g=0;g<grids.Length;g++)
                {
                    gridStates[g].PoolOffset=-1;
                    grids[g]=new GrassSimulationPool.ActiveGrid { Position=data.Grids[g].Pos,
                        BladeOffset=data.Grids[g].IdOffset, GridId=g, PoolOffset=-1 };
                }
                CullingGrids=NewBuffer(grids.Length,32); CullingGrids.SetData(grids);
                windState=NewBuffer(grids.Length,16);
                var controls=new Vector4[75]; var bakedFrames=new Vector4[175]; var yaw=new Quaternion[grids.Length];
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
                    shader.SetFloat("BakedMaxWind",response.MaxWind);
                    shader.SetVectorArray("BakedMomentCompliance",response.MomentCompliance(data));
                    for(int g=0;g<yaw.Length;g++)yaw[g]=GrassResponseType.HandoffYaw(g);
                }
                else for(int g=0;g<yaw.Length;g++)yaw[g]=Quaternion.identity;
                var controlsBuffer=NewBuffer(75,16); controlsBuffer.SetData(controls);
                var bakedFramesBuffer=NewBuffer(175,16); bakedFramesBuffer.SetData(bakedFrames);
                var yawBuffer=NewBuffer(yaw.Length,16); yawBuffer.SetData(yaw);
                shader.SetInt("EnableBakedResponse",response!=null?1:0); shader.SetInt("GridCount",grids.Length);
                foreach(int k in new[] { resetKernel,captureKernel,seedKernel,displayKernel })
                {
                    shader.SetBuffer(k,"BladesBuffer",blades); shader.SetBuffer(k,"RodsBuffer",rods);
                    shader.SetBuffer(k,"RestNodesBuffer",rest); shader.SetBuffer(k,"PlasticBendsBuffer",plastic);
                    shader.SetBuffer(k,"DisplayNodes",displayNodes); shader.SetBuffer(k,"DisplayFrames",displayFrames);
                    shader.SetBuffer(k,"TransitionOffsets",offsets); shader.SetBuffer(k,"TransitionRotations",rotations);
                    shader.SetBuffer(k,"GridStates",states); shader.SetTexture(k,"WindForceMap",wind);
                    shader.SetBuffer(k,"BakedWindRead",windState); shader.SetBuffer(k,"BakedControls",controlsBuffer);
                    shader.SetBuffer(k,"BakedFrames",bakedFramesBuffer); shader.SetBuffer(k,"BakedYaw",yawBuffer);
                    shader.SetBuffer(k,"BakedInverseMass",masses);
                }
                shader.SetBuffer(windKernel,"BakedWindState",windState);
                shader.SetBuffer(windKernel,"BakedGrids",CullingGrids); shader.SetBuffer(windKernel,"BakedYaw",yawBuffer);
                shader.SetTexture(windKernel,"WindForceMap",wind);
                shader.SetBuffer(seedKernel,"BonesBuffer",bones); shader.SetBuffer(seedKernel,"FramesBuffer",frames);
                shader.SetBuffer(seedKernel,"Entries",entries);
                shader.SetBuffer(displayKernel,"BonesBuffer",bones); shader.SetBuffer(displayKernel,"FramesBuffer",frames);
                var positions=NewBuffer(count*15,12); var normals=NewBuffer(count*15,12);
                foreach(int k in new[] { resetKernel,displayKernel })
                { shader.SetBuffer(k,"ResultPosBuffer",positions); shader.SetBuffer(k,"NormalBuffer",normals); }
                material.SetBuffer("VertexBuffer",positions); material.SetBuffer("NormalBuffer",normals);
                shader.SetInt("BladeCount",count);
                Reset(Array.Empty<GrassSimulationPool.ActiveGrid>(),0);
            }
            catch { Dispose(); throw; }
        }
        private ComputeBuffer NewBuffer(int count,int stride)
        { var b=new ComputeBuffer(count,stride); buffers.Add(b); return b; }
        // Read-only current representation mask, shared with optional outer coverage.
        public ComputeBuffer ClassificationStates => tiered!=null?tiered.ClassificationStates:states;
        public void RecycleWorldGrids(IReadOnlyList<int> grids,float time)
        {
            if(tiered==null)throw new InvalidOperationException("World streaming requires tiered state.");
            tiered.RecycleWorldGrids(grids,time);
        }
        internal void ConfigureDebugColors(Material material)
        {
            const string keyword="GRASS_LOD_BASE_COLORS";
            if(material.IsKeywordEnabled(keyword)==settings.DebugLodBaseColors)return;
            if(settings.DebugLodBaseColors)material.EnableKeyword(keyword);
            else material.DisableKeyword(keyword);
        }
        private void Configure(float time)
        {
            shader.SetFloat("PoseTime",time); shader.SetFloat("TransitionSeconds",Mathf.Max(.05f,settings.TransitionSeconds));
            shader.SetFloat("WindDisplacement",Mathf.Max(0,settings.WindDisplacement));
            shader.SetInt("EnablePlasticity",physics.EnablePlasticity?1:0);
            shader.SetInt("GroundCollision",physics.GroundCollision?1:0);
            shader.SetFloat("CollisionMargin",Mathf.Max(0,physics.CollisionMargin));
            shader.SetFloat("LodgedAngularSpeed",Mathf.Max(.1f,settings.LodgedAngularSpeed));
            shader.SetVector("BakedGravity",physics.Gravity); shader.SetFloat("BakedBladeMass",physics.BladeMass);
        }
        public void SetWind(Vector3 acceleration,float frequency,Vector4 tileAndOffset)
        { if(tiered!=null) { tiered.SetWind(acceleration,frequency,tileAndOffset); return; }
            shader.SetVector("WindForce",acceleration); shader.SetFloat("WindFrequency",frequency); shader.SetVector("WindTileOffset",tileAndOffset); }
        internal void Select(IReadOnlyList<GrassSimulationPool.ActiveGrid> active,float time,Vector3 center,IReadOnlyList<Vector4> interactions=null)
        {
            if(tiered!=null) { tiered.Select(active,time,center,interactions); return; }
            Configure(time); Array.Clear(selected,0,selected.Length);
            foreach(var grid in active) selected[grid.GridId]=true;
            int count=0; simulatedBlades=active.Count*32;
            for(int g=0;g<gridStates.Length;g++)
            {
                bool changed=selected[g]!=(gridStates[g].PoolOffset>=0);
                gridStates[g].Changed=changed?1:0;
                if(changed) gridStates[g].Started=time;
                if(!selected[g]) gridStates[g].PoolOffset=-1;
            }
            foreach(var grid in active)
            {
                gridStates[grid.GridId].PoolOffset=grid.PoolOffset;
                // Reinitialize dormant cached slots too: their old motion is no longer the displayed pose.
                if(gridStates[grid.GridId].Changed!=0)
                    initializations[count++]=new Initialization { Slot=grid.PoolOffset/32, BladeOffset=grid.BladeOffset };
            }
            states.SetData(gridStates);
            bool anyChange=false; foreach(var state in gridStates) anyChange|=state.Changed!=0;
            if(anyChange) shader.Dispatch(captureKernel,(data.BladeCount+63)/64,1,1);
            if(count==0)return;
            entries.SetData(initializations,0,0,count); shader.SetInt("EntryCount",count);
            shader.Dispatch(seedKernel,(count*32+63)/64,1,1); entryInitializations+=count;
        }
        internal void UpdateDisplay(float time,float tick)
        {
            if(tiered!=null) { tiered.UpdateDisplay(time,tick); return; }
            Configure(time); shader.SetFloat("PoseDt",tick);
            if(settings.BakedResponse!=null)shader.Dispatch(windKernel,(data.Grids.Length+63)/64,1,1);
            shader.Dispatch(displayKernel,(data.BladeCount+63)/64,1,1);
        }
        internal void Reset(IReadOnlyList<GrassSimulationPool.ActiveGrid> active,float time)
        {
            if(tiered!=null) { tiered.Reset(active,time); return; }
            for(int g=0;g<gridStates.Length;g++) gridStates[g]=new GridState { PoolOffset=-1, Started=time };
            states.SetData(gridStates); Configure(time);
            windState.SetData(new Vector4[gridStates.Length]);
            shader.Dispatch(resetKernel,(data.BladeCount+63)/64,1,1);
            Select(active,time,Vector3.zero);
        }
        // Explicit validation reads only. Runtime handoff never reads GPU simulation state back.
        public BoneInfo[] ReadDisplayNodes()
        {
            if(tiered!=null)return tiered.ReadDisplayNodes();
            var values=new BoneInfo[data.BladeCount*8]; displayNodes.GetData(values);
            for(int b=0;b<data.BladeCount;b++) for(int n=0;n<8;n++) values[b*8+n].Position+=data.GetBlade(b).Root;
            return values;
        }
        public GrassBladeData.Frame[] ReadDisplayFrames()
        { if(tiered!=null)return tiered.ReadDisplayFrames();
            var values=new GrassBladeData.Frame[data.BladeCount*7]; displayFrames.GetData(values); return values; }
        public Vector4[] ReadBakedWindState()
        { if(tiered!=null)return tiered.ReadBakedWindState();
            var values=new Vector4[data.Grids.Length]; windState.GetData(values); return values; }
        public void Dispose()
        {
            if(tiered!=null) { tiered.Dispose(); return; }
            foreach(var b in buffers)b.Release(); buffers.Clear();
            if(shader!=null) { if(Application.isPlaying)UnityEngine.Object.Destroy(shader); else UnityEngine.Object.DestroyImmediate(shader); }
        }
    }
}
