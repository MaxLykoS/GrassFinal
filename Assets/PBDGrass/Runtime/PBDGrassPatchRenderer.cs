using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace GrassFinal.PBDGrass
{
    public sealed class PBDGrassPatchRenderer : IDisposable
    {
        public const int BladesPerGrid = 32, NodesPerBlade = GrassBladeData.Nodes;
        public struct ColliderMotion
        {
            public Vector3 A; public float Radius;
            public Vector3 B; public float Friction;
            public Vector3 PreviousA; public float Padding0;
            public Vector3 PreviousB; public float Padding1;
        }
        private struct Initialization { public int Slot, Grid; }
        private readonly List<ComputeBuffer> buffers = new List<ComputeBuffer>();
        private readonly ComputeShader shader;
        private readonly Material material;
        private readonly Material sourceMaterial;
        private readonly Camera camera;
        private readonly XPBDGrassSettings settings;
        private readonly IList<Transform> colliders;
        private readonly DepthTexMaker depthSource;
        private readonly int solveKernel, initializeKernel, cullKernel;
        private readonly ComputeBuffer bones, frames, plasticBends, contacts, activeBuffer, initializeBuffer, drawArgs;
        private readonly ColliderMotion[] shapes, targets;
        private readonly Vector3[] motionStartA, motionStartB;
        private readonly GrassSimulationPool.ActiveGrid[] active;
        private readonly Initialization[] initialization;
        private readonly uint[] emptyDraw = { BladesPerGrid * GrassBladeData.Indices, 0, 0, 0 };
        private readonly Vector4[] frustum = new Vector4[6];
        private readonly GrassBladeData data;
        private double accumulator;
        private bool disposed;
        public Vector3 SimulationCenter { get; set; }
        public float SimulationTime { get; private set; }
        public int SimulationTicks { get; private set; }
        public float DroppedTime { get; private set; }
        public int BladeCount => data.Blades.Length;
        public int ActiveBladeCount => Pool.ActiveBlades;
        public GrassSimulationPool Pool { get; }
        public GrassBladeData Data => data;
        public PBDGrassPatchRenderer(PBDGrassPatch patch, ComputeShader sourceShader, Material sourceMaterial,
            Camera camera, Texture2D windNoise, XPBDGrassSettings settings, IList<Transform> colliders)
        {
            this.camera = camera; this.settings = settings; this.sourceMaterial = sourceMaterial;
            this.colliders = colliders ?? new List<Transform>();
            data = new GrassBladeData(patch, Mathf.Clamp(settings.TipThicknessRatio,.1f,1));
            SimulationCenter = patch.Root;
            Pool = new GrassSimulationPool(data.Grids, settings.MaxActiveBlades);
            active = new GrassSimulationPool.ActiveGrid[Pool.Capacity]; initialization = new Initialization[Pool.Capacity];
            depthSource = camera.GetComponent<DepthTexMaker>();
            shader = UnityEngine.Object.Instantiate(sourceShader); material = UnityEngine.Object.Instantiate(sourceMaterial);
            try
            {
                solveKernel = shader.FindKernel("XPBDSolverLOD0"); initializeKernel = shader.FindKernel("InitializePool");
                cullKernel = shader.FindKernel("GridCulling");
                bones = NewBuffer(Pool.Capacity * 32 * NodesPerBlade, 48);
                frames = NewBuffer(Pool.Capacity * 32 * GrassBladeData.Segments, 32);
                plasticBends = Buffer(new Vector4[data.Blades.Length * GrassBladeData.Segments], 16);
                ComputeBuffer rest = Buffer(data.RestNodes, 48), rods = Buffer(data.Rods, 64), blades = Buffer(data.Blades, 32);
                ComputeBuffer masses = Buffer(data.InverseMass,4);
                ComputeBuffer positions = Buffer(data.VerticesData,12), normals = Buffer(data.Normals,12);
                ComputeBuffer triangles = Buffer(data.Triangles,4), uvs = Buffer(data.UV,8);
                activeBuffer = NewBuffer(Pool.Capacity,32); initializeBuffer = NewBuffer(Pool.Capacity,8);
                ComputeBuffer visible = NewBuffer(Pool.Capacity,16);
                drawArgs = Buffer(emptyDraw,4,ComputeBufferType.IndirectArguments);
                shapes = new ColliderMotion[this.colliders.Count]; targets = new ColliderMotion[shapes.Length];
                motionStartA = new Vector3[shapes.Length]; motionStartB = new Vector3[shapes.Length];
                contacts = NewBuffer(Mathf.Max(1,shapes.Length),64);
                for (int i=0; i<shapes.Length; i++)
                {
                    ReadShape(i, out shapes[i].A, out shapes[i].B, out shapes[i].Radius, out shapes[i].Friction);
                    shapes[i].PreviousA = shapes[i].A; shapes[i].PreviousB = shapes[i].B;
                }
                foreach (int kernel in new[] { solveKernel, initializeKernel })
                {
                    shader.SetBuffer(kernel,"BonesBuffer",bones); shader.SetBuffer(kernel,"FramesBuffer",frames);
                    shader.SetBuffer(kernel,"PlasticBendsBuffer",plasticBends);
                    shader.SetBuffer(kernel,"RestNodesBuffer",rest); shader.SetBuffer(kernel,"RodsBuffer",rods);
                    shader.SetBuffer(kernel,"BladesBuffer",blades);
                    shader.SetBuffer(kernel,"ResultPosBuffer",positions); shader.SetBuffer(kernel,"NormalBuffer",normals);
                }
                shader.SetBuffer(initializeKernel,"InitializationBuffer",initializeBuffer);
                shader.SetBuffer(solveKernel,"InverseMassBuffer",masses);
                shader.SetBuffer(solveKernel,"ActiveGridsBuffer",activeBuffer);
                shader.SetBuffer(solveKernel,"ColliderBuffer",contacts);
                shader.SetTexture(solveKernel,"WindForceMap",windNoise);
                shader.SetInt("ColliderCount",shapes.Length);
                shader.SetBuffer(cullKernel,"ActiveGridsBuffer",activeBuffer);
                shader.SetBuffer(cullKernel,"GridsVisibleBuffer",visible);
                shader.SetBuffer(cullKernel,"bufferWithArgsDrawIndirect",drawArgs);
                material.SetBuffer("VertexBuffer",positions); material.SetBuffer("TriangleBuffer",triangles);
                material.SetBuffer("NormalBuffer",normals); material.SetBuffer("UvBuffer",uvs);
                material.SetBuffer("GridsVisibleBuffer",visible);
                material.SetBuffer("BladesBuffer",blades);
                shader.SetInt("EnablePlasticity",settings.EnablePlasticity?1:0);
                shader.SetFloat("GrassReach",data.MaximumBladeLength*1.1f+.1f);
                SelectPool();
            }
            catch { Dispose(); throw; }
        }
        private ComputeBuffer NewBuffer(int count,int stride,ComputeBufferType type=ComputeBufferType.Default)
        { var b = new ComputeBuffer(count,stride,type); buffers.Add(b); return b; }
        private ComputeBuffer Buffer(Array values,int stride,ComputeBufferType type=ComputeBufferType.Default)
        { var b=NewBuffer(values.Length,stride,type); b.SetData(values); return b; }
        private void SelectPool()
        {
            int count=0;
            Pool.Select(SimulationCenter,Mathf.Max(1,settings.NearDistance),Mathf.Max(0,settings.PoolHysteresis),SimulationTicks,
                (slot,grid)=> { initialization[count++] = new Initialization { Slot=slot, Grid=data.Grids[grid].IdOffset }; });
            for (int i=0; i<Pool.Selected.Count; i++) active[i]=Pool.Selected[i];
            if (Pool.Selected.Count>0) activeBuffer.SetData(active,0,0,Pool.Selected.Count);
            shader.SetInt("ActiveGridCount",Pool.Selected.Count);
            if (count>0)
            {
                initializeBuffer.SetData(initialization,0,0,count); shader.SetInt("InitializationCount",count);
                shader.Dispatch(initializeKernel,(count*32+63)/64,1,1);
            }
        }
        private void ReadShape(int i,out Vector3 a,out Vector3 b,out float radius,out float friction)
        {
            Transform tr=i<colliders.Count?colliders[i]:null; a=b=Vector3.zero; radius=0; friction=settings.Friction;
            if (tr==null || !tr.gameObject.activeInHierarchy) return;
            var shape=tr.GetComponent<GrassInteractionCollider>();
            if (shape!=null)
            { if(!shape.isActiveAndEnabled)return; shape.Endpoints(out a,out b); radius=shape.WorldRadius; friction=shape.Friction; }
            else { a=b=tr.position; radius=settings.ColliderRadius; }
        }
        public void SetWind(Vector3 acceleration,float frequency,Vector4 tileAndOffset)
        { shader.SetVector("WindForce",acceleration); shader.SetFloat("_WindFrequency",frequency); shader.SetVector("_WindForceMap_ST",tileAndOffset); }
        public void Advance(float elapsedSeconds)
        {
            if (disposed || elapsedSeconds<=0 || float.IsNaN(elapsedSeconds) || float.IsInfinity(elapsedSeconds)) return;
            double tick=1.0/Mathf.Clamp(settings.SimulationRate,30,120);
            double maximum=tick*Mathf.Clamp(settings.MaxCatchUpSteps,1,8);
            accumulator+=elapsedSeconds;
            if (accumulator>maximum) { DroppedTime+=(float)(accumulator-maximum); accumulator=maximum; }
            int steps=(int)Math.Floor((accumulator+1e-8)/tick);
            if(steps<=0)return;
            for(int i=0;i<shapes.Length;i++)
            {
                motionStartA[i]=shapes[i].A; motionStartB[i]=shapes[i].B;
                ReadShape(i,out targets[i].A,out targets[i].B,out targets[i].Radius,out targets[i].Friction);
            }
            for(int step=0;step<steps;step++) { Tick((float)tick,(step+1f)/steps); accumulator-=tick; }
        }
        // Contact projection is nonlinear near grazing events. Quantize resampled inputs to 10 micrometers
        // so float roundoff in 30/60/120 FPS interpolation cannot choose different contact branches.
        private static Vector3 ContactPosition(Vector3 p) => new Vector3(
            Mathf.Round(p.x*100000)*.00001f,Mathf.Round(p.y*100000)*.00001f,Mathf.Round(p.z*100000)*.00001f);
        private void Tick(float tick,float motionFraction)
        {
            shader.SetInt("EnablePlasticity",settings.EnablePlasticity?1:0);
            SelectPool();
            for (int i=0;i<shapes.Length;i++)
            {
                shapes[i].PreviousA=shapes[i].A; shapes[i].PreviousB=shapes[i].B;
                shapes[i].A=ContactPosition(Vector3.Lerp(motionStartA[i],targets[i].A,motionFraction));
                shapes[i].B=ContactPosition(Vector3.Lerp(motionStartB[i],targets[i].B,motionFraction));
                shapes[i].Radius=targets[i].Radius; shapes[i].Friction=targets[i].Friction;
            }
            if (shapes.Length>0) contacts.SetData(shapes);
            shader.SetFloat("TickDt",tick); shader.SetFloat("SimulationTime",SimulationTime);
            shader.SetVector("Gravity",settings.Gravity);
            shader.SetFloat("StretchCompliance",Mathf.Max(0,settings.StretchCompliance));
            shader.SetFloat("AlignmentCompliance",Mathf.Max(0,settings.AlignmentCompliance));
            shader.SetFloat("SoftBendCompliance",Mathf.Max(0,settings.SoftBendCompliance));
            shader.SetFloat("HardBendCompliance",Mathf.Max(0,settings.HardBendCompliance));
            shader.SetFloat("TwistCompliance",Mathf.Max(0,settings.TwistCompliance));
            shader.SetFloat("BaseBendCompliance",Mathf.Max(0,settings.BaseBendCompliance));
            shader.SetFloat("InverseBladeMass",1/Mathf.Max(.0001f,settings.BladeMass));
            shader.SetFloat("VelocityDamping",Mathf.Max(0,settings.VelocityDamping));
            shader.SetFloat("AngularDamping",Mathf.Max(0,settings.AngularDamping));
            shader.SetFloat("CollisionMargin",Mathf.Max(0,settings.CollisionMargin));
            shader.SetFloat("GroundFriction",Mathf.Max(0,settings.Friction));
            shader.SetFloat("PlasticYieldCurvature",Mathf.Max(.01f,settings.PlasticYieldCurvature));
            shader.SetFloat("PlasticFlowRate",Mathf.Max(0,settings.PlasticFlowRate));
            shader.SetFloat("PlasticMaxCurvature",Mathf.Max(0,settings.PlasticMaxCurvature));
            shader.SetFloat("BaseYieldAngle",Mathf.Clamp(settings.BaseYieldDegrees,1,89)*Mathf.Deg2Rad);
            shader.SetFloat("BaseMaxPlasticAngle",Mathf.Clamp(settings.BaseMaxPlasticDegrees,0,85)*Mathf.Deg2Rad);
            shader.SetInt("GroundCollision",settings.GroundCollision?1:0);
            shader.SetInt("NearSubsteps",Mathf.Clamp(settings.NearSubsteps,1,16));
            shader.SetInt("ConstraintIterations",Mathf.Clamp(settings.ConstraintIterations,1,6));
            if (Pool.Selected.Count>0) for(int substep=0;substep<Mathf.Clamp(settings.NearSubsteps,1,16);substep++)
            { shader.SetInt("SubstepIndex",substep); shader.Dispatch(solveKernel,Pool.Selected.Count,1,1); }
            SimulationTime+=tick; SimulationTicks++;
        }
        // Editor preview updates residency without advancing the fixed simulation clock.
        public void RefreshSimulationSelection()
        {
            if (!disposed) SelectPool();
        }
        public void Draw(Camera renderCamera = null)
        {
            if (disposed) return;
            // Debug selection stays live without recreating the simulation or its GPU buffers.
            if (sourceMaterial != null && sourceMaterial.HasProperty("_DebugView"))
                material.SetFloat("_DebugView",sourceMaterial.GetFloat("_DebugView"));
            Plane[] planes=GeometryUtility.CalculateFrustumPlanes(camera);
            for(int i=0;i<6;i++) frustum[i]=new Vector4(planes[i].normal.x,planes[i].normal.y,planes[i].normal.z,planes[i].distance);
            shader.SetVectorArray("FrustumPlanes",frustum); shader.SetVector("camPos",camera.transform.position);
            shader.SetVector("camDir",camera.transform.forward); shader.SetFloat("MaxDistance",Mathf.Max(1,settings.MaxDistance));
            shader.SetMatrix("_Matrix_VP",GL.GetGPUProjectionMatrix(camera.projectionMatrix,true)*camera.worldToCameraMatrix);
            bool occlusion=settings.UseOcclusionCulling && depthSource!=null && depthSource.CanCull(camera);
            shader.SetInt("EnableOcclusionCulling",occlusion?1:0); shader.SetInt("ReversedZ",SystemInfo.usesReversedZBuffer?1:0);
            shader.SetTexture(cullKernel,"_DepthTex",occlusion?(Texture)depthSource.DepthTexture:Texture2D.blackTexture);
            drawArgs.SetData(emptyDraw);
            if(Pool.Selected.Count>0) shader.Dispatch(cullKernel,(Pool.Selected.Count+31)/32,1,1);
            // Only the render target changes. Frustum, distance, projection and Hi-Z above
            // always come from the designated main camera, including Scene view renders.
            Graphics.DrawProceduralIndirect(material,data.Bounds,MeshTopology.Triangles,drawArgs,0,
                renderCamera != null ? renderCamera : camera,null,ShadowCastingMode.TwoSided,true);
        }
        // Synchronous reads are diagnostics only, never used by the normal frame loop or pool.
        public BoneInfo[] ReadNodes()
        {
            var packed=new BoneInfo[Pool.Capacity*32*NodesPerBlade]; bones.GetData(packed);
            var result=(BoneInfo[])data.RestNodes.Clone();
            for(int g=0;g<data.Grids.Length;g++)
            {
                int slot=Pool.SlotForGrid(g); if(slot<0) continue;
                Array.Copy(packed,slot*32*NodesPerBlade,result,data.Grids[g].IdOffset*NodesPerBlade,32*NodesPerBlade);
            }
            for(int b=0;b<BladeCount;b++) for(int n=0;n<NodesPerBlade;n++)
            { result[b*NodesPerBlade+n].Position+=data.Blades[b].Root; result[b*NodesPerBlade+n].Predicted+=data.Blades[b].Root; }
            return result;
        }
        public GrassBladeData.Frame[] ReadFrames()
        {
            var packed=new GrassBladeData.Frame[Pool.Capacity*32*GrassBladeData.Segments]; frames.GetData(packed);
            var result=new GrassBladeData.Frame[BladeCount*GrassBladeData.Segments];
            for(int i=0;i<result.Length;i++) result[i].Rotation=data.Rods[i].Rotation;
            for(int g=0;g<data.Grids.Length;g++)
            {
                int slot=Pool.SlotForGrid(g); if(slot>=0) Array.Copy(packed,slot*32*GrassBladeData.Segments,result,
                    data.Grids[g].IdOffset*GrassBladeData.Segments,32*GrassBladeData.Segments);
            }
            return result;
        }
        // Diagnostic read only; normal simulation and residency never read plastic state back.
        public Vector4[] ReadPlasticBends()
        {
            var result=new Vector4[BladeCount*GrassBladeData.Segments]; plasticBends.GetData(result); return result;
        }
        public void ResetDeformation()
        {
            if(disposed)return;
            plasticBends.SetData(new Vector4[BladeCount*GrassBladeData.Segments]);
            shader.SetInt("EnablePlasticity",settings.EnablePlasticity?1:0);
            int count=0;
            for(int g=0;g<data.Grids.Length;g++)
            {
                int slot=Pool.SlotForGrid(g);
                if(slot>=0)initialization[count++]=new Initialization { Slot=slot,Grid=data.Grids[g].IdOffset };
            }
            if(count==0)return;
            initializeBuffer.SetData(initialization,0,0,count); shader.SetInt("InitializationCount",count);
            shader.Dispatch(initializeKernel,(count*32+63)/64,1,1);
        }
        public void ImpulseBlade(int blade,Vector3 velocity,Vector3 angularVelocity)
        {
            if(blade<0 || blade>=BladeCount) throw new ArgumentOutOfRangeException(nameof(blade));
            int slot=Pool.SlotForGrid(blade/32); if(slot<0) throw new InvalidOperationException("Blade is not resident.");
            var n=new BoneInfo[NodesPerBlade]; int offset=(slot*32+blade%32)*NodesPerBlade;
            bones.GetData(n,0,offset,n.Length);
            for(int i=1;i<n.Length;i++) n[i].Velocities+=velocity; bones.SetData(n,0,offset,n.Length);
            var f=new GrassBladeData.Frame[GrassBladeData.Segments]; offset=(slot*32+blade%32)*GrassBladeData.Segments;
            frames.GetData(f,0,offset,f.Length);
            for(int i=0;i<f.Length;i++) f[i].AngularVelocity+=angularVelocity; frames.SetData(f,0,offset,f.Length);
        }
        public uint VisibleGridCount() { var a=new uint[4]; drawArgs.GetData(a); return a[1]; }
        public void Dispose()
        {
            if(disposed)return; disposed=true;
            foreach(var b in buffers)b.Release(); buffers.Clear(); DestroyOwned(shader); DestroyOwned(material);
        }
        private static void DestroyOwned(UnityEngine.Object value)
        { if(value==null)return; if(Application.isPlaying)UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value); }
    }
}
