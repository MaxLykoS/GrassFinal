using System;
using System.Collections.Generic;
using UnityEngine;

namespace GrassFinal.PBDGrass
{
    // Separate opt-in representation. GPU buffers remain fixed during streaming.
    // The active XPBD plastic buffer remains float32; a packed cache is updated at tick end.
    public sealed class GrassCompactStorage : IDisposable
    {
        public const int PoseBytesPerBlade=80, PlasticBytesPerBlade=32;
        public readonly ComputeBuffer Pose, Plastic, Yaw, Anchors;
        private readonly List<ComputeBuffer> buffers=new List<ComputeBuffer>();
        public long AllocatedGpuBytes { get {long n=0;foreach(var b in buffers)n+=(long)b.count*b.stride;return n;} }
        public GrassCompactStorage(int blades,Quaternion[] yaw,GrassBladeData.Rod[] rods)
            :this(blades,yaw,MakeAnchors(yaw,rods,Quaternion.identity)) { }
        public GrassCompactStorage(int blades,Quaternion[] yaw,Quaternion templateAnchor)
            :this(blades,yaw,MakeAnchors(yaw,null,templateAnchor)) { }
        private static Quaternion[] MakeAnchors(Quaternion[] yaw,GrassBladeData.Rod[] rods,Quaternion template)
        {
            var anchors=new Quaternion[yaw.Length];
            for(int g=0;g<anchors.Length;g++)anchors[g]=rods!=null?rods[g*32*7].Rotation:yaw[g]*template;
            return anchors;
        }
        private GrassCompactStorage(int blades,Quaternion[] yaw,Quaternion[] anchors)
        {
            Pose=New(blades*20,4); Plastic=New(blades*8,4); Yaw=New(yaw.Length,16);
            // Preserve the accepted CPU-created basal frames bit for bit. Re-evaluating
            // yaw * template on the GPU changes rounding before the contact solve.
            Anchors=New(yaw.Length,16);
            Anchors.SetData(anchors);
            Yaw.SetData(yaw); ClearPlastic();
        }
        private ComputeBuffer New(int count,int stride)
        {var b=new ComputeBuffer(count,stride);buffers.Add(b);return b;}
        public void Bind(ComputeShader shader,int kernel)
        {
            shader.SetBuffer(kernel,"CompactPose",Pose);
            shader.SetBuffer(kernel,"CompactPlastic",Plastic);
            shader.SetBuffer(kernel,"CompactYaw",Yaw);
            shader.SetBuffer(kernel,"CompactAnchors",Anchors);
        }
        public void ClearPlastic()
        {
            // Bounded staging, including reset: no world-sized temporary CPU array.
            var zeros=new uint[Math.Min(262144,Plastic.count)];
            for(int start=0;start<Plastic.count;start+=zeros.Length)
                Plastic.SetData(zeros,0,start,Math.Min(zeros.Length,Plastic.count-start));
        }
        public Vector4[] ReadPlastic()
        {
            var raw=new uint[Plastic.count];Plastic.GetData(raw);
            var result=new Vector4[raw.Length/8*7];
            for(int b=0;b<raw.Length/8;b++)for(int s=0;s<7;s++)
            {
                uint word=raw[b*8+s];
                result[b*7+s]=new Vector4(unchecked((short)(word&65535))*(2f/32767),unchecked((short)(word>>16))*(2f/32767),0,0);
            }
            return result;
        }
        public void Dispose() {foreach(var b in buffers)b.Release();buffers.Clear();}
    }
}
