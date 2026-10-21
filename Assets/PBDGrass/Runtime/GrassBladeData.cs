using System;
using UnityEngine;
namespace GrassFinal.PBDGrass
{
    // Immutable identity/rest shape shared by generation, physics and rendering.
    // A later representation can sample these nodes without creating another population.
    public sealed class GrassBladeData
    {
        public const int Nodes = 8, Segments = 7, Vertices = 15, Indices = 39;
        public struct Blade { public Vector3 Root; public float Length; public uint Id; public float Width, Height, Seed; }
        public struct Rod
        {
            public Quaternion Rotation, Relative;
            public float Length, HalfWidth, InverseInertia, SoftScale;
            public float HardScale, TwistScale, Padding0, Padding1;
        }
        public struct Frame { public Quaternion Rotation; public Vector3 AngularVelocity; public float Padding; }
        private readonly Blade[] blades;
        private readonly BoneInfo[] restNodes;
        private readonly Rod[] rods;
        private readonly GrassWorldLayout compactWorld;
        private readonly int bladeCount;
        public bool UsesCompactTemplate => compactWorld!=null;
        internal Action RefreshCpuWorld;
        internal Blade[] BladeStorage => blades;
        internal BoneInfo[] RestStorage => restNodes;
        internal Rod[] RodStorage => rods;
        // Streamed static metadata is generated on the GPU. Refresh CPU mirrors only
        // when a diagnostic/editor consumer explicitly asks for the arrays.
        // Compact array access is an explicit diagnostic snapshot, owned by the caller.
        // Use the indexed accessors for probes; normal frames never expand these arrays.
        public Blade[] Blades { get {if(UsesCompactTemplate)return SnapshotBlades();RefreshCpuWorld?.Invoke();return blades;} }
        public BoneInfo[] RestNodes { get {if(UsesCompactTemplate)return SnapshotRestNodes();RefreshCpuWorld?.Invoke();return restNodes;} }
        public Rod[] Rods { get {if(UsesCompactTemplate)return SnapshotRods();RefreshCpuWorld?.Invoke();return rods;} }
        public int BladeCount => bladeCount;
        // Legacy expanded storage. Compact fields keep these mesh/mass arrays empty;
        // use GetInverseMass for physics probes and the GPU diagnostic APIs for geometry.
        public readonly float[] InverseMass;
        public readonly GrassGridStruct[] Grids;
        public readonly Vector3[] VerticesData, Normals;
        public readonly int[] Triangles;
        public readonly Vector2[] UV;
        public Bounds Bounds { get; private set; }
        public readonly Quaternion[] WorldYaws;
        private readonly int fieldSize;
        public float MaximumBladeLength { get; private set; }
        public GrassBladeData(PBDGrassPatch patch, float tipThickness,Quaternion[] worldYaws=null)
        {
            WorldYaws=worldYaws; fieldSize=patch.Width;
            if (patch.SEGMENTS != Segments || patch.grids == null || patch.Bodies.Length != patch.grids.Length * 32)
                throw new ArgumentException("Near ribbon requires 8 nodes and 32 blades per grid.");
            blades = new Blade[patch.Bodies.Length]; restNodes = patch.GenBoneInfoArray();
            bladeCount=blades.Length;
            rods = new Rod[Blades.Length * Segments]; InverseMass = new float[RestNodes.Length];
            Grids = patch.grids; VerticesData = patch.vertices; Normals = patch.PatchMesh.normals;
            Triangles = patch.PatchMesh.triangles; UV = patch.PatchMesh.uv;
            for (int b = 0; b < Blades.Length; b++)
            {
                var body = patch.Bodies[b]; Vector3 root = body.OriginPos[0];
                float total = 0; float[] weights = new float[Nodes];
                Quaternion previous = Quaternion.identity;
                float bladeLength = 0;
                float maximumWidth = 1e-6f;
                foreach (Vector3 offset in body.Offset) maximumWidth = Mathf.Max(maximumWidth, offset.magnitude);
                for (int s = 0; s < Segments; s++)
                {
                    Vector3 tangent = (body.OriginPos[s+1] - body.OriginPos[s]).normalized;
                    Vector3 width = Vector3.ProjectOnPlane(body.Offset[s], tangent).normalized;
                    Quaternion q = Quaternion.LookRotation(tangent, Vector3.Cross(tangent, width).normalized);
                    float len = Vector3.Distance(body.OriginPos[s], body.OriginPos[s+1]);
                    float u = (s + .5f) / Segments;
                    float w = Mathf.Max(.08f, 1 - u), t = Mathf.Lerp(1, tipThickness, u);
                    if (body.UsesBezierShape)
                    {
                        float nextWidth = s + 1 < Segments ? body.Offset[s + 1].magnitude : 0;
                        w = Mathf.Max(.08f, .5f * (body.Offset[s].magnitude + nextWidth) / maximumWidth);
                    }
                    float mass = len * w * t;
                    weights[s] += mass*.5f; weights[s+1] += mass*.5f; total += mass;
                    float thin = len / .1f / (w*t*t*t);
                    Rods[b*Segments+s] = new Rod { Rotation = q,
                        Relative = s == 0 ? Quaternion.identity : Quaternion.Inverse(previous)*q,
                        Length = len, HalfWidth = body.Offset[s].magnitude,
                        InverseInertia = 1 / Mathf.Max(1e-8f, mass * len*len / 12),
                        SoftScale = thin, HardScale = len / .1f / (t*w*w*w), TwistScale = thin };
                    previous = q; bladeLength += len;
                }
                for (int n = 0; n < Nodes; n++)
                {
                    InverseMass[b*Nodes+n] = n == 0 ? 0 : total / weights[n];
                    RestNodes[b*Nodes+n].Position -= root; RestNodes[b*Nodes+n].Predicted -= root;
                }
                for (int s = 0; s < Segments; s++)
                {
                    Rod r = Rods[b*Segments+s]; r.InverseInertia *= total; Rods[b*Segments+s] = r;
                }
                Blades[b] = new Blade { Root = root, Length = bladeLength, Id = (uint)b,
                    Width = body.Offset[0].magnitude*2, Height = body.OriginPos[Nodes-1].y-root.y,
                    Seed = Mathf.Repeat(b * .61803398875f, 1) };
                MaximumBladeLength = Mathf.Max(MaximumBladeLength, bladeLength);
            }
            float reach = MaximumBladeLength * 1.1f + .1f;
            Bounds = new Bounds(patch.Root, new Vector3(patch.Width + 2 * reach, 2 * reach, patch.Length + 2 * reach));
        }
        internal GrassBladeData(GrassWorldLayout world,float maximumLength)
        {
            compactWorld=world;fieldSize=world.Size;WorldYaws=world.Yaws;
            Grids=new GrassGridStruct[world.Cells.Length];bladeCount=Grids.Length*32;
            for(int g=0;g<Grids.Length;g++)Grids[g]=new GrassGridStruct {
                Pos=new Vector3(world.Cells[g].x+.5f,world.Ground,world.Cells[g].y+.5f),IdOffset=g*32};
            // Compact rendering owns no full CPU mesh or per-node mass array.
            InverseMass=Array.Empty<float>();VerticesData=Normals=Array.Empty<Vector3>();
            Triangles=Array.Empty<int>();UV=Array.Empty<Vector2>();
            MaximumBladeLength=maximumLength;MoveWorldBounds(world.Center);
        }
        public Blade GetBlade(int index)
        {
            if(!UsesCompactTemplate) {RefreshCpuWorld?.Invoke();return blades[index];}
            if((uint)index>=(uint)bladeCount)throw new ArgumentOutOfRangeException(nameof(index));
            var b=compactWorld.TemplateBlade;var cell=compactWorld.Cells[index/32];
            b.Id=(uint)index;b.Root=GrassWorldIdentity.Root(cell,index%32,compactWorld.Ground);
            b.Seed=GrassWorldIdentity.LeafSeed(cell,index%32);return b;
        }
        public Rod GetRod(int index)
        {
            if(!UsesCompactTemplate) {RefreshCpuWorld?.Invoke();return rods[index];}
            if((uint)index>=(uint)(bladeCount*Segments))throw new ArgumentOutOfRangeException(nameof(index));
            var r=compactWorld.TemplateRods[index%Segments];r.Rotation=WorldYaws[index/(32*Segments)]*r.Rotation;return r;
        }
        public BoneInfo GetRestNode(int index)
        {
            if(!UsesCompactTemplate) {RefreshCpuWorld?.Invoke();return restNodes[index];}
            if((uint)index>=(uint)(bladeCount*Nodes))throw new ArgumentOutOfRangeException(nameof(index));
            var n=compactWorld.TemplateNodes[index%Nodes];Quaternion yaw=WorldYaws[index/(32*Nodes)];
            n.Position=yaw*n.Position;n.Predicted=n.Position;n.OriginPos=n.Position+GetBlade(index/Nodes).Root;return n;
        }
        public float GetInverseMass(int index)
        {
            if(!UsesCompactTemplate)return InverseMass[index];
            if((uint)index>=(uint)(bladeCount*Nodes))throw new ArgumentOutOfRangeException(nameof(index));
            return compactWorld.TemplateMass[index%Nodes];
        }
        private Blade[] SnapshotBlades()
        { var result=new Blade[bladeCount];for(int b=0;b<result.Length;b++)result[b]=GetBlade(b);return result; }
        private BoneInfo[] SnapshotRestNodes()
        { var result=new BoneInfo[bladeCount*Nodes];for(int n=0;n<result.Length;n++)result[n]=GetRestNode(n);return result; }
        private Rod[] SnapshotRods()
        { var result=new Rod[bladeCount*Segments];for(int s=0;s<result.Length;s++)result[s]=GetRod(s);return result; }
        public void MoveWorldBounds(Vector3 center)
        {
            float reach=MaximumBladeLength*1.1f+.1f;
            Bounds=new Bounds(center,new Vector3(fieldSize+2*reach,2*reach,fieldSize+2*reach));
        }
        public Quaternion GridYaw(int grid) => WorldYaws!=null?WorldYaws[grid]:GrassResponseType.HandoffYaw(grid);
    }
}
