using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GrassFinal.PBDGrass
{
    // One uniform-wind comparison cell containing eight material types. State scales by cell/type,
    // not by the number of rendered blades. The existing 32768-blade benchmark does not use this.
    public sealed class GrassResponseProxy : IDisposable
    {
        private readonly List<ComputeBuffer> buffers=new List<ComputeBuffer>();
        private readonly ComputeShader shader;
        private readonly Material material,sourceMaterial;
        private readonly Camera mainCamera;
        private readonly ComputeBuffer state,positions,visible,args;
        private readonly GrassGridStruct[] grids;
        private readonly Bounds[] typeBounds;
        private readonly Bounds bounds;
        private readonly int advanceKernel,buildKernel,count;
        private bool disposed;
        public int StateCount => count;
        public GrassResponseProxy(GrassResponseType[] types,Vector3[] roots,ComputeShader source,Material sourceMaterial,Camera camera)
        {
            count=types.Length; if(count<1 || count>8 || roots.Length!=count)throw new ArgumentException("One to eight response types required.");
            this.sourceMaterial=sourceMaterial; mainCamera=camera;
            shader=UnityEngine.Object.Instantiate(source); material=UnityEngine.Object.Instantiate(sourceMaterial);
            try
            {
                var blades=new GrassBladeData.Blade[count*32]; var triangles=new int[count*32*39]; var uv=new Vector2[count*32*15];
                var controls=new Vector4[count*25*3]; var frames=new Vector4[count*25*7]; var segments=new Vector4[count*7];
                var dynamics=new Vector4[8]; var limits=new Vector4[8]; grids=new GrassGridStruct[count]; typeBounds=new Bounds[count];
                Bounds total=new Bounds(roots[0],Vector3.zero);
                for(int t=0;t<count;t++)
                {
                    if(!types[t].IsBaked || types[t].GridSize!=5)throw new ArgumentException("Bake the five by five response table first.");
                    var patch=GrassResponseType.CreatePatch(types[t],roots[t]);
                    try
                    {
                        var data=new GrassBladeData(patch,types[t].Physics.TipThicknessRatio);
                        Array.Copy(data.Blades,0,blades,t*32,32); Array.Copy(data.UV,0,uv,t*32*15,32*15);
                        for(int i=0;i<data.Triangles.Length;i++)triangles[t*32*39+i]=data.Triangles[i]+t*32*15;
                        float arc=0,totalLength=data.Blades[0].Length;
                        for(int s=0;s<7;s++)
                        { float start=arc/totalLength; arc+=data.Rods[s].Length; segments[t*7+s]=new Vector4(data.Rods[s].Length,data.Rods[s].HalfWidth,start,arc/totalLength); }
                        grids[t]=new GrassGridStruct { Pos=roots[t],IdOffset=t*32 }; typeBounds[t]=data.Bounds; total.Encapsulate(data.Bounds);
                    }
                    finally { GrassDemo.DestroyMesh(patch.PatchMesh); }
                    for(int s=0;s<25;s++)
                    {
                        var pose=types[t].Samples[s]; int i=(t*25+s)*3;
                        controls[i]=pose.C1; controls[i+1]=pose.C2; controls[i+2]=pose.Tip;
                        for(int j=0;j<7;j++) { var q=pose.Frames[j]; frames[(t*25+s)*7+j]=new Vector4(q.x,q.y,q.z,q.w); }
                    }
                    dynamics[t]=new Vector4(types[t].AngularFrequency.x,types[t].AngularFrequency.y,types[t].DampingRatio.x,types[t].DampingRatio.y);
                    limits[t]=new Vector4(types[t].MaxWind,0,0,0);
                }
                bounds=total; advanceKernel=shader.FindKernel("AdvanceResponse"); buildKernel=shader.FindKernel("BuildGeometry");
                if(!shader.IsSupported(advanceKernel) || !shader.IsSupported(buildKernel))throw new InvalidOperationException("Response compute shader is not supported.");
                state=NewBuffer(count,16); positions=NewBuffer(count*32*15,12); var normals=NewBuffer(count*32*15,12);
                visible=NewBuffer(count,16); args=NewBuffer(4,4,ComputeBufferType.IndirectArguments);
                var bladeBuffer=Buffer(blades,32);
                shader.SetBuffer(advanceKernel,"ResponseState",state); shader.SetBuffer(buildKernel,"ResponseState",state);
                shader.SetBuffer(buildKernel,"Controls",Buffer(controls,16)); shader.SetBuffer(buildKernel,"SampleFrames",Buffer(frames,16));
                shader.SetBuffer(buildKernel,"Segments",Buffer(segments,16)); shader.SetBuffer(buildKernel,"BladesBuffer",bladeBuffer);
                shader.SetBuffer(buildKernel,"ResultPosBuffer",positions); shader.SetBuffer(buildKernel,"NormalBuffer",normals);
                shader.SetVectorArray("Dynamics",dynamics); shader.SetVectorArray("WindLimits",limits); shader.SetInt("TypeCount",count);
                material.SetBuffer("VertexBuffer",positions); material.SetBuffer("NormalBuffer",normals);
                material.SetBuffer("TriangleBuffer",Buffer(triangles,4)); material.SetBuffer("UvBuffer",Buffer(uv,8));
                material.SetBuffer("GridsVisibleBuffer",visible); material.SetBuffer("BladesBuffer",bladeBuffer); Reset();
            }
            catch { Dispose(); throw; }
        }
        private ComputeBuffer NewBuffer(int length,int stride,ComputeBufferType type=ComputeBufferType.Default)
        { var value=new ComputeBuffer(length,stride,type); buffers.Add(value); return value; }
        private ComputeBuffer Buffer(Array values,int stride)
        { var value=NewBuffer(values.Length,stride); value.SetData(values); return value; }
        public void Reset() { state.SetData(new Vector4[count]); shader.Dispatch(buildKernel,count,1,1); }
        public void Advance(Vector3 wind,float tick)
        {
            shader.SetVector("Wind",wind); shader.SetFloat("Dt",tick);
            shader.Dispatch(advanceKernel,1,1,1); shader.Dispatch(buildKernel,count,1,1);
        }
        public void Draw(Camera target)
        {
            if(disposed)return;
            if(sourceMaterial.HasProperty("_DebugView"))material.SetFloat("_DebugView",sourceMaterial.GetFloat("_DebugView"));
            var planes=GeometryUtility.CalculateFrustumPlanes(mainCamera); var selected=new GrassGridStruct[count]; int n=0;
            for(int t=0;t<count;t++)if(GeometryUtility.TestPlanesAABB(planes,typeBounds[t]))selected[n++]=grids[t];
            if(n==0)return; visible.SetData(selected,0,0,n); args.SetData(new uint[] { 32*39,(uint)n,0,0 });
            Graphics.DrawProceduralIndirect(material,bounds,MeshTopology.Triangles,args,0,target,null,ShadowCastingMode.TwoSided,true);
        }
        // Explicit GPU diagnostics; never called by the demo's frame loop.
        public Vector3[] ReadNodes()
        {
            var vertices=new Vector3[count*32*15]; positions.GetData(vertices); var result=new Vector3[count*32*8];
            for(int b=0;b<count*32;b++)
            {
                for(int n=0;n<7;n++)result[b*8+n]=(vertices[b*15+n*2]+vertices[b*15+n*2+1])*.5f;
                result[b*8+7]=vertices[b*15+14];
            }
            return result;
        }
        public Vector4[] ReadResponse()
        { var values=new Vector4[count]; state.GetData(values); return values; }
        public void Dispose()
        {
            if(disposed)return; disposed=true; foreach(var b in buffers)b.Release(); buffers.Clear();
            foreach(var value in new UnityEngine.Object[] { shader,material })
                if(value!=null) { if(Application.isPlaying)UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value); }
        }
    }
}
