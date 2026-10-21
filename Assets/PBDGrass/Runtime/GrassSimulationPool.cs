using System;
using System.Collections.Generic;
using UnityEngine;
namespace GrassFinal.PBDGrass
{
    // Grid-sized slots bound allocations. Retained slots preserve GPU position/velocity/frame state.
    // Dormant slots form an LRU cache; only an evicted slot needs initialization on re-entry.
    public sealed class GrassSimulationPool
    {
        public struct ActiveGrid { public Vector3 Position; public int BladeOffset; public int PoolOffset; public int GridId, Padding0, Padding1; }
        private readonly GrassGridStruct[] grids;
        private readonly int[] gridSlot, slotGrid, lastUsed;
        private readonly bool[] active, reserved;
        private readonly float[] scores;
        private float[] releaseStarted;
        private float settleSeconds;
        public bool ContactDriven { get; private set; }
        public int SettlingBlades { get; private set; }
        public int FadingBlades { get; private set; }
        private readonly bool[] interactionPriority;
        private readonly GrassGridQuery query;
        private readonly GrassGridSet nearby,regionGrids,priorityGrids;
        public int InteractionBlades { get; private set; }
        private readonly List<int> candidates = new List<int>();
        private readonly List<ActiveGrid> selected = new List<ActiveGrid>();
        public readonly List<int> InitializedSlots = new List<int>();
        public int Capacity { get; }
        public int ActiveBlades => selected.Count * 32;
        public int InitializationCount { get; private set; }
        public int BudgetRejectedGrids { get; private set; }
        public IReadOnlyList<ActiveGrid> Selected => selected;
        public GrassSimulationPool(GrassGridStruct[] grids, int bladeCapacity)
            :this(grids,bladeCapacity,new GrassGridQuery(grids)) { }
        internal GrassSimulationPool(GrassGridStruct[] grids,int bladeCapacity,GrassGridQuery query)
        {
            this.query=query;
            nearby=new GrassGridSet(grids.Length);regionGrids=new GrassGridSet(grids.Length);priorityGrids=new GrassGridSet(grids.Length);
            this.grids = grids; Capacity = Mathf.Min(grids.Length, Mathf.Max(1, bladeCapacity / 32));
            gridSlot = new int[grids.Length]; slotGrid = new int[Capacity]; lastUsed = new int[Capacity];
            active = new bool[grids.Length]; reserved = new bool[Capacity];
            scores=new float[grids.Length]; interactionPriority=new bool[grids.Length];
            for (int i = 0; i < grids.Length; i++) gridSlot[i] = -1;
            for (int i = 0; i < Capacity; i++) slotGrid[i] = -1;
        }
        public void Select(Vector3 center, float radius, float hysteresis, int tick, Action<int,int> initialize,
            bool stableRanking=false,IReadOnlyList<Vector4> interactions=null,Vector4 interactionCoverage=default,bool radialInteractionCoverage=false,
            XPBDGrassSettings lifecycle=null,float time=0)
        {
            ContactDriven=lifecycle!=null && lifecycle.ContactDrivenSimulation;
            settleSeconds=ContactDriven?Mathf.Max(0,lifecycle.ContactSettleSeconds):0;
            float lifetime=settleSeconds+(ContactDriven?Mathf.Max(.05f,lifecycle.ContactFadeSeconds):0);
            if(ContactDriven && releaseStarted==null) {releaseStarted=new float[grids.Length];for(int g=0;g<grids.Length;g++)releaseStarted[g]=-1;}
            candidates.Clear(); InitializedSlots.Clear(); Array.Clear(reserved, 0, reserved.Length);
            nearby.Clear();if(!ContactDriven)query.AddBounds(center.x,center.z,radius+Mathf.Max(0,hysteresis),nearby);
            foreach(int g in priorityGrids.Items)interactionPriority[g]=false;
            priorityGrids.Clear();
            if(interactions!=null)for(int i=0;i<interactions.Count;i++)
            {
                var region=interactions[i];regionGrids.Clear();
                query.AddBounds(region.x,region.y,Mathf.Abs(region.z),regionGrids);
                foreach(int g in regionGrids.Items)
                {
                    // One simulation grid contains 32 roots. Include boundary grids
                    // that overlap the compatible field, without promoting the outer field.
                    if(!OverlapsCoverage(grids[g].Pos,.71f,interactionCoverage,radialInteractionCoverage))continue;
                    float dx=grids[g].Pos.x-region.x,dz=grids[g].Pos.z-region.y;
                    if(dx*dx+dz*dz>region.z*region.z)continue;
                    interactionPriority[g]=true;priorityGrids.Add(g);nearby.Add(g);
                    if(ContactDriven)releaseStarted[g]=-1;
                }
            }
            if(ContactDriven)foreach(var previous in selected)
            {
                int g=previous.GridId;if(!active[g] || interactionPriority[g])continue;
                if(releaseStarted[g]<0)releaseStarted[g]=time;
                if(time-releaseStarted[g]<lifetime)nearby.Add(g);
            }
            foreach(int g in nearby.Items)
            {
                Vector3 d = grids[g].Pos - center; d.y = 0;
                float reach = radius + (active[g] ? hysteresis : 0);
                if(!ContactDriven && !interactionPriority[g] && d.sqrMagnitude>reach*reach)continue;
                scores[g]=stableRanking?(active[g]?Mathf.Max(0,d.magnitude-hysteresis):d.magnitude):(grids[g].Pos-center).sqrMagnitude;
                candidates.Add(g);
            }
            candidates.Sort((a,b) => { if(interactionPriority[a]!=interactionPriority[b])return interactionPriority[a]?-1:1;
                int compare = scores[a].CompareTo(scores[b]); return compare != 0 ? compare : a.CompareTo(b); });
            BudgetRejectedGrids = Mathf.Max(0, candidates.Count - Capacity);
            if (candidates.Count > Capacity) candidates.RemoveRange(Capacity, candidates.Count-Capacity);
            foreach (int g in candidates) if (gridSlot[g] >= 0) reserved[gridSlot[g]] = true;
            foreach(var grid in selected)
            {
                int g=grid.GridId;active[g]=false;
                if(ContactDriven && gridSlot[g]>=0 && !reserved[gridSlot[g]])
                {slotGrid[gridSlot[g]]=-1;gridSlot[g]=-1;releaseStarted[g]=-1;}
            }
            selected.Clear();
            InteractionBlades=SettlingBlades=FadingBlades=0;
            foreach (int g in candidates)
            {
                int slot = gridSlot[g];
                if (slot < 0)
                {
                    slot = -1;
                    for (int s = 0; s < Capacity; s++) if (!reserved[s] && (slot < 0 || slotGrid[s] < 0 || lastUsed[s] < lastUsed[slot]))
                    { slot = s; if (slotGrid[s] < 0) break; }
                    if (slotGrid[slot] >= 0) gridSlot[slotGrid[slot]] = -1;
                    gridSlot[g] = slot; slotGrid[slot] = g; reserved[slot] = true;
                    initialize(slot, g); InitializedSlots.Add(slot); InitializationCount++;
                }
                active[g] = true; lastUsed[slot] = tick;
                if(interactionPriority[g])InteractionBlades+=32;
                else if(ContactDriven) {if(time>=releaseStarted[g]+settleSeconds)FadingBlades+=32;else SettlingBlades+=32;}
                selected.Add(new ActiveGrid { Position = grids[g].Pos, BladeOffset = grids[g].IdOffset,
                    PoolOffset = slot*32, GridId = g });
            }
        }
        public int SlotForGrid(int grid) => gridSlot[grid];
        public bool IsActive(int grid)=>active[grid];
        public float ExitStartedAt(int grid)=>ContactDriven && releaseStarted[grid]>=0?releaseStarted[grid]+settleSeconds:-1;
        internal static bool OverlapsCoverage(Vector3 position,float padding,Vector4 coverage,bool radial)
        {
            if(coverage.z<=0)return true; // No outer representation: the resident field is compatible.
            float x=Mathf.Abs(position.x-coverage.x),z=Mathf.Abs(position.z-coverage.y),reach=coverage.z+padding;
            return radial?x*x+z*z<=reach*reach:Mathf.Max(x,z)<=reach;
        }
        public void ForgetGrids(IReadOnlyList<int> gridsToRecycle)
        {
            foreach(int grid in gridsToRecycle)
            {
                int slot=gridSlot[grid]; if(slot>=0) {slotGrid[slot]=-1;lastUsed[slot]=0;reserved[slot]=false;}
                gridSlot[grid]=-1;active[grid]=false;
                if(releaseStarted!=null)releaseStarted[grid]=-1;
            }
        }
    }
}
