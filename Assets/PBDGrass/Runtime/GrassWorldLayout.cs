using System;
using System.Collections.Generic;
using UnityEngine;

namespace GrassFinal.PBDGrass
{
    // A fixed 1m-cell ring. World coordinates own identity; overlapping cells keep
    // the same resident indices, simulation slots and handoff history.
    public sealed class GrassWorldLayout
    {
        public readonly int Size;
        public readonly float Ground;
        public readonly Vector2Int[] Cells;
        public readonly Quaternion[] Yaws;
        public readonly List<int> Changed=new List<int>();
        public readonly List<Vector4Int> Replacements=new List<Vector4Int>();
        public Vector3 Center { get; private set; }
        public int Version { get; private set; }
        public int LastLoadedCells { get; private set; }
        public long TotalLoadedCells { get; private set; }
        public BoneInfo[] TemplateNodes { get; private set; }
        public GrassBladeData.Rod[] TemplateRods { get; private set; }
        public GrassBladeData.Blade TemplateBlade { get; private set; }
        private readonly BoneInfo[] rotatedNodes=new BoneInfo[8];
        private readonly GrassBladeData.Rod[] rotatedRods=new GrassBladeData.Rod[7];
        private float[] templateMass;
        private readonly bool[] cpuDirty;
        private int dirtyCount;
        public GrassWorldLayout(int size,Vector3 focus,float ground)
        {
            if(size<64 || size%16!=0)throw new ArgumentException("Streaming core size must be at least 64m and a multiple of 16m.");
            Size=size; Ground=ground; Cells=new Vector2Int[size*size]; Yaws=new Quaternion[Cells.Length];cpuDirty=new bool[Cells.Length];
            Center=Anchor(focus); RecenterCells(true);
        }
        private Vector3 Anchor(Vector3 focus) => new Vector3(Mathf.Floor(focus.x/8)*8,Ground,Mathf.Floor(focus.z/8)*8);
        private void RecenterCells(bool initial)
        {
            Changed.Clear(); Replacements.Clear(); int minX=(int)Center.x-Size/2,minZ=(int)Center.z-Size/2;
            for(int slot=0;slot<Cells.Length;slot++)
            {
                var cell=new Vector2Int(minX+GrassWorldIdentity.Mod(slot%Size-minX,Size),minZ+GrassWorldIdentity.Mod(slot/Size-minZ,Size));
                if(!initial && cell==Cells[slot])continue;
                Cells[slot]=cell; Yaws[slot]=GrassWorldIdentity.Yaw(cell); Changed.Add(slot);
                Replacements.Add(new Vector4Int(slot,cell.x,cell.y,0));
            }
            LastLoadedCells=Changed.Count; TotalLoadedCells+=Changed.Count;
        }
        public PBDGrassPatch CreatePatch(GrassResponseType type)
        {
            var random=UnityEngine.Random.state; PBDGrassPatch patch;
            try { UnityEngine.Random.InitState(12345); patch=new PBDGrassPatch(Center,Size,32,type.Shape); }
            finally { UnityEngine.Random.state=random; }
            var uv=new Vector2[patch.Bodies.Length*15]; var indices=new int[patch.Bodies.Length*39];
            for(int g=0;g<Cells.Length;g++)
            {
                patch.grids[g].Pos=new Vector3(Cells[g].x+.5f,Ground,Cells[g].y+.5f);
                for(int leaf=0;leaf<32;leaf++)
                {
                    int b=g*32+leaf;
                    var body=new PBDGrassBody(ref patch.vertices,ref uv,ref indices,b*15,b*39,Vector3.zero,7,type.Shape.Height,type.Shape.HalfWidth,0,1,type.Shape);
                    body.Transform(Matrix4x4.TRS(GrassWorldIdentity.Root(Cells[g],leaf,Ground),Yaws[g],Vector3.one)); patch.Bodies[b]=body;
                }
            }
            patch.UpdateMesh();patch.PatchMesh.uv=uv;patch.PatchMesh.triangles=indices;patch.PatchMesh.RecalculateNormals();return patch;
        }
        public void Attach(GrassBladeData data)
        {
            TemplateNodes=new BoneInfo[8]; TemplateRods=new GrassBladeData.Rod[7]; templateMass=new float[8];
            Quaternion inverse=Quaternion.Inverse(Yaws[0]);
            for(int n=0;n<8;n++) { var node=data.RestNodes[n]; node.Position=inverse*node.Position;node.Predicted=node.Position;node.OriginPos=node.Position;node.Velocities=Vector3.zero;TemplateNodes[n]=node;templateMass[n]=data.InverseMass[n]; }
            for(int s=0;s<7;s++) { var rod=data.Rods[s];rod.Rotation=inverse*rod.Rotation;TemplateRods[s]=rod; }
            TemplateBlade=data.Blades[0];
            foreach(int g in Changed)Fill(data,g);
            data.MoveWorldBounds(Center);
            data.RefreshCpuWorld=()=>RefreshCpu(data);
        }
        public bool Move(Vector3 focus,GrassBladeData data)
        {
            Vector3 next=Anchor(focus); if(next==Center)return false;
            Center=next; RecenterCells(false);
            foreach(int g in Changed)
            {
                data.Grids[g].Pos=new Vector3(Cells[g].x+.5f,Ground,Cells[g].y+.5f);
                if(!cpuDirty[g]) {cpuDirty[g]=true;dirtyCount++;}
            }
            data.MoveWorldBounds(Center); Version++; return true;
        }
        private void RefreshCpu(GrassBladeData data)
        {
            if(dirtyCount==0)return;
            for(int g=0;g<cpuDirty.Length;g++)if(cpuDirty[g]) {Fill(data,g);cpuDirty[g]=false;}
            dirtyCount=0;
        }
        private void Fill(GrassBladeData data,int g)
        {
            Quaternion yaw=Yaws[g]; var cell=Cells[g];
            data.Grids[g].Pos=new Vector3(cell.x+.5f,Ground,cell.y+.5f);
            for(int n=0;n<8;n++) { var node=TemplateNodes[n];node.Position=yaw*node.Position;node.Predicted=node.Position;node.OriginPos=node.Position;rotatedNodes[n]=node; }
            for(int s=0;s<7;s++) { var rod=TemplateRods[s];rod.Rotation=yaw*rod.Rotation;rotatedRods[s]=rod; }
            for(int leaf=0;leaf<32;leaf++)
            {
                int b=g*32+leaf; var blade=TemplateBlade;blade.Id=(uint)b;blade.Root=GrassWorldIdentity.Root(cell,leaf,Ground);blade.Seed=GrassWorldIdentity.LeafSeed(cell,leaf);data.BladeStorage[b]=blade;
                Array.Copy(rotatedNodes,0,data.RestStorage,b*8,8);Array.Copy(rotatedRods,0,data.RodStorage,b*7,7);Array.Copy(templateMass,0,data.InverseMass,b*8,8);
                for(int n=0;n<8;n++)data.RestStorage[b*8+n].OriginPos+=blade.Root;
            }
        }
    }
    // Unity has no Vector4Int; the GPU consumes this 16-byte world assignment.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct Vector4Int
    { public int Slot,X,Z,Padding; public Vector4Int(int slot,int x,int z,int padding) {Slot=slot;X=x;Z=z;Padding=padding;} }
}
