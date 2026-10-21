using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace GrassFinal.PBDGrass.Editor
{
    // Offline acceptance readbacks. Never called by the game/runtime simulation.
    internal static class GrassCompactNearAudit
    {
        [Serializable] public sealed class Comparison
        {
            public string phase;
            public float position,velocity,rotation,angular,plastic,contacts;
            public int mappingMismatches,nodeMismatches,frameMismatches,worstWorldBlade;
        }
        public static readonly List<Comparison> Comparisons=new List<Comparison>();
        private sealed class Snapshot
        {
            public BoneInfo[] nodes;
            public GrassBladeData.Frame[] frames;
            public Vector4[] plastic;
            public PBDGrassPatchRenderer.ColliderMotion[] contacts;
            public int[] mapping;
        }
        private static readonly Dictionary<string,Snapshot> baseline=new Dictionary<string,Snapshot>();
        private static T Field<T>(object owner,string name)=>(T)owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner);
        public static void Begin() {baseline.Clear();Comparisons.Clear();}
        public static void AssertEquivalent()
        {
            if(Comparisons.Count!=9)throw new InvalidOperationException("Missing independent near-physics checkpoints.");
            foreach(var c in Comparisons)
                if(c.nodeMismatches!=0 || c.frameMismatches!=0 || c.plastic!=0 || c.contacts!=0 || c.mappingMismatches!=0)
                    throw new InvalidOperationException("Independent near physics diverged: "+JsonUtility.ToJson(c));
        }
        public static void Check(PBDGrassPatchRenderer r,string phase,bool compact)
        {
            var s=new Snapshot {nodes=new BoneInfo[r.ActiveBladeCount*8],frames=new GrassBladeData.Frame[r.ActiveBladeCount*7],
                mapping=new int[r.ActiveBladeCount/32],contacts=(PBDGrassPatchRenderer.ColliderMotion[])Field<PBDGrassPatchRenderer.ColliderMotion[]>(r,"shapes").Clone(),
                plastic=new Vector4[r.ActiveBladeCount*7]};
            Field<ComputeBuffer>(r,"bones").GetData(s.nodes);Field<ComputeBuffer>(r,"frames").GetData(s.frames);
            var plastic=r.ReadPlasticBends();
            foreach(var g in r.Pool.Selected)
            {
                s.mapping[g.PoolOffset/32]=g.GridId;
                Array.Copy(plastic,g.BladeOffset*7,s.plastic,g.PoolOffset*7,32*7);
            }
            if(!compact) {baseline[phase]=s;return;}
            var b=baseline[phase];
            float position=0,velocity=0,rotation=0,angular=0,bend=0,contact=0;
            int nodeMismatches=0,frameMismatches=0,mapping=0,worstNode=0;
            for(int i=0;i<s.nodes.Length;i++)
            {
                float d=Vector3.Distance(s.nodes[i].Position,b.nodes[i].Position);
                if(d>position) {position=d;worstNode=i;}
                velocity=Mathf.Max(velocity,Vector3.Distance(s.nodes[i].Velocities,b.nodes[i].Velocities));
                if(!s.nodes[i].Position.Equals(b.nodes[i].Position) || !s.nodes[i].Predicted.Equals(b.nodes[i].Predicted) ||
                   !s.nodes[i].Velocities.Equals(b.nodes[i].Velocities))nodeMismatches++;
            }
            for(int i=0;i<s.frames.Length;i++)
            {
                var a=s.frames[i];var bb=b.frames[i];
                rotation=Mathf.Max(rotation,Vector4.Distance(new Vector4(a.Rotation.x,a.Rotation.y,a.Rotation.z,a.Rotation.w),new Vector4(bb.Rotation.x,bb.Rotation.y,bb.Rotation.z,bb.Rotation.w)));
                angular=Mathf.Max(angular,Vector3.Distance(a.AngularVelocity,bb.AngularVelocity));
                bend=Mathf.Max(bend,Vector4.Distance(s.plastic[i],b.plastic[i]));
                if(!a.Rotation.Equals(bb.Rotation) || !a.AngularVelocity.Equals(bb.AngularVelocity))frameMismatches++;
            }
            for(int i=0;i<s.mapping.Length;i++)if(s.mapping[i]!=b.mapping[i])mapping++;
            for(int i=0;i<s.contacts.Length;i++)
            {
                var a=s.contacts[i];var bb=b.contacts[i];
                contact=Mathf.Max(contact,Vector3.Distance(a.A,bb.A),Vector3.Distance(a.B,bb.B),Vector3.Distance(a.PreviousA,bb.PreviousA),Vector3.Distance(a.PreviousB,bb.PreviousB),Mathf.Abs(a.Radius-bb.Radius),Mathf.Abs(a.Friction-bb.Friction));
            }
            var comparison=new Comparison {phase=phase,position=position,velocity=velocity,rotation=rotation,angular=angular,plastic=bend,contacts=contact,
                mappingMismatches=mapping,nodeMismatches=nodeMismatches,frameMismatches=frameMismatches,
                worstWorldBlade=s.mapping[worstNode/256]*32+(worstNode/8)%32};
            Comparisons.Add(comparison);
            Debug.Log("NEAR_AUDIT "+JsonUtility.ToJson(comparison));
            baseline.Remove(phase);
        }
    }
}
