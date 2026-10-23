using System;
using System.Collections.Generic;
using UnityEngine;
namespace GrassFinal.PBDGrass
{
    // GPU state stays in stable slots. The contact path changes ownership only on coverage
    // deltas and lifecycle deadlines; the fixed path reuses its spatial distance order.
    public sealed class GrassSimulationPool
    {
        public struct ActiveGrid { public Vector3 Position; public int BladeOffset; public int PoolOffset; public int GridId, Padding0, Padding1; }
        private const byte Contact=1,Settling=2,Fading=3,Fixed=4;
        private readonly GrassGridStruct[] grids;
        private readonly GrassGridQuery query;
        private readonly int[] gridSlot,slotGrid,lastUsed,selectedIndex;
        private readonly bool[] active,reserved,interactionPriority;
        private readonly byte[] phases;
        private readonly float[] scores,nearDistanceSquared,releaseStarted;
        private readonly int[] scoreComputed;
        private readonly GrassGridSet nearby,stateChanges,pendingChanges;
        private readonly List<int> candidates=new List<int>(),nearOrder=new List<int>(),activeNear=new List<int>(),inactiveNear=new List<int>();
        private readonly List<ActiveGrid> selected=new List<ActiveGrid>();
        private readonly Comparison<int> compareRank,compareBase;
        private readonly int[] freeSlots,lruSlots;
        private int freeSlotCount,nextFreeSlot,lruSlotCount;
        private readonly Stack<int> eventFree=new Stack<int>();
        // Indexed heap: at most one pending deadline per live grass block, with O(log capacity)
        // removal on recontact. Repeated contacts cannot accumulate stale timer records.
        private readonly int[] deadlineHeap,deadlineIndex;
        private readonly float[] deadlineValues;
        private int deadlineCount;
        private float settleSeconds,fadeSeconds;
        private bool configured,inSelect,selectionDirty,scoreConfigured,nearConfigured,fixedUnsettled;
        private Vector3 scoreCenter,nearCenter;
        private int scoreRevision,scoreEpoch,nearRevision,fixedContactVersion,lastFixedTick;
        private bool scoreStable,rankStable,nearStable;
        private float rankHysteresis,nearRadius,nearHysteresis;
        public readonly List<int> InitializedSlots=new List<int>();
        public int Capacity {get;}
        public int ActiveBlades=>selected.Count*32;
        public int InitializationCount {get;private set;}
        public int BudgetRejectedGrids {get;private set;}
        public int InteractionBlades {get;private set;}
        public int SettlingBlades {get;private set;}
        public int FadingBlades {get;private set;}
        public bool ContactDriven {get;private set;}
        public int SelectionVersion {get;private set;}
        internal int StateVersion {get;private set;}
        internal IReadOnlyList<int> StateChanges=>stateChanges.Items;
        internal GrassInteractionCoverage Coverage {get;}
        public float HistoryBandWidth {get;set;}=2;
        public IReadOnlyList<ActiveGrid> Selected=>selected;
        public GrassSimulationPool(GrassGridStruct[] grids,int bladeCapacity):this(grids,bladeCapacity,new GrassGridQuery(grids)) { }
        internal GrassSimulationPool(GrassGridStruct[] grids,int bladeCapacity,GrassGridQuery query)
        {
            this.grids=grids;this.query=query;Coverage=new GrassInteractionCoverage(grids,query);
            Capacity=Mathf.Min(grids.Length,Mathf.Max(1,bladeCapacity/32));
            int count=grids.Length;
            gridSlot=new int[count];slotGrid=new int[Capacity];lastUsed=new int[Capacity];selectedIndex=new int[count];
            active=new bool[count];reserved=new bool[Capacity];interactionPriority=new bool[count];phases=new byte[count];
            scores=new float[count];nearDistanceSquared=new float[count];releaseStarted=new float[count];scoreComputed=new int[count];
            nearby=new GrassGridSet(count);stateChanges=new GrassGridSet(count);pendingChanges=new GrassGridSet(count);
            deadlineHeap=new int[Capacity];deadlineIndex=new int[count];freeSlots=new int[Capacity];lruSlots=new int[Capacity];
            deadlineValues=new float[Capacity];
            compareRank=CompareRank;compareBase=CompareBase;
            for(int g=0;g<count;g++)gridSlot[g]=selectedIndex[g]=deadlineIndex[g]=-1;
            for(int g=0;g<count;g++)releaseStarted[g]=-1;
            for(int s=0;s<Capacity;s++)slotGrid[s]=-1;
        }
        public void Select(Vector3 center,float radius,float hysteresis,int tick,Action<int,int> initialize,
            bool stableRanking=false,IReadOnlyList<Vector4> interactions=null,Vector4 interactionCoverage=default,bool radialInteractionCoverage=false,
            XPBDGrassSettings lifecycle=null,float time=0)
        {
            stateChanges.Clear();foreach(int g in pendingChanges.Items)stateChanges.Add(g);
            InitializedSlots.Clear();inSelect=true;
            Coverage.Update(interactions,interactionCoverage,radialInteractionCoverage,center,HistoryBandWidth);
            foreach(int g in Coverage.ContactRemoved)interactionPriority[g]=false;
            foreach(int g in Coverage.ContactAdded)interactionPriority[g]=true;
            bool contact=lifecycle!=null && lifecycle.ContactDrivenSimulation;
            bool entering=contact && (!configured || !ContactDriven),leaving=configured && ContactDriven && !contact;
            float settle=contact?Mathf.Max(0,lifecycle.ContactSettleSeconds):0;
            float fade=contact?Mathf.Max(.05f,lifecycle.ContactFadeSeconds):0;
            bool durationChanged=settle!=settleSeconds || fade!=fadeSeconds;
            ContactDriven=contact;settleSeconds=settle;fadeSeconds=fade;
            ConfigureScores(center,stableRanking,hysteresis);
            if(entering)
            {
                ClearDeadlines();
                foreach(var entry in selected)
                {
                    int g=entry.GridId;SetPhase(g,0);releaseStarted[g]=-1;
                    if(Coverage.ContainsContact(g)) {SetPhase(g,Contact);MarkChange(g);}
                    else BeginRelease(g,time);
                }
                BuildEventFree();
            }
            else if(leaving)
            {
                ClearDeadlines();
                foreach(var entry in selected) {releaseStarted[entry.GridId]=-1;MarkChange(entry.GridId);}
                nearConfigured=false;fixedUnsettled=true;
            }
            if(contact)SelectContacts(center,tick,initialize,time,entering,durationChanged);
            else SelectFixed(center,radius,hysteresis,tick,initialize,stableRanking,leaving || !configured);
            configured=true;pendingChanges.Clear();inSelect=false;
            if(selectionDirty) {SelectionVersion++;selectionDirty=false;}
            if(stateChanges.Items.Count>0)StateVersion++;
        }
        private void SelectContacts(Vector3 center,int tick,Action<int,int> initialize,float time,bool entering,bool durationChanged)
        {
            // Process renewed contact before timers so contact at the exact expiry time wins.
            foreach(int g in Coverage.ContactRemoved)if(active[g] && phases[g]==Contact)BeginRelease(g,time);
            foreach(int g in Coverage.ContactAdded)if(active[g])ResumeContact(g);
            if(durationChanged && !entering)
            {
                ClearDeadlines();
                foreach(var entry in selected)
                {
                    int g=entry.GridId;
                    if(phases[g]!=Settling && phases[g]!=Fading)continue;
                    SetPhase(g,time>=releaseStarted[g]+settleSeconds?Fading:Settling);MarkChange(g);Schedule(g);
                }
            }
            AdvanceDeadlines(time);
            int desired=Coverage.Contacts.Count+(SettlingBlades+FadingBlades)/32;
            bool hadRejected=BudgetRejectedGrids>0;
            BudgetRejectedGrids=Mathf.Max(0,desired-Capacity);
            if(desired>Capacity)
            {
                candidates.Clear();foreach(int g in Coverage.Contacts) {BaseScore(g);candidates.Add(g);}
                foreach(var entry in selected)if(!Coverage.ContainsContact(entry.GridId)) {BaseScore(entry.GridId);candidates.Add(entry.GridId);}
                candidates.Sort(compareRank);candidates.RemoveRange(Capacity,candidates.Count-Capacity);
                Array.Clear(reserved,0,reserved.Length);
                foreach(int g in candidates)if(gridSlot[g]>=0)reserved[gridSlot[g]]=true;
                for(int i=selected.Count-1;i>=0;i--)if(!reserved[selected[i].PoolOffset/32])RemoveActive(selected[i].GridId,true);
                foreach(int g in candidates)if(!active[g])AddContact(g,tick,initialize);
            }
            else if(entering || hadRejected)
            {
                foreach(int g in Coverage.Contacts)if(!active[g])AddContact(g,tick,initialize);
            }
            else
            {
                foreach(int g in Coverage.ContactAdded)if(!active[g])AddContact(g,tick,initialize);
                // Ring IDs can retain positive contact coverage while changing world identity.
                foreach(int g in pendingChanges.Items)if(!active[g] && Coverage.ContainsContact(g))AddContact(g,tick,initialize);
            }
        }
        private void BeginRelease(int grid,float time)
        {
            releaseStarted[grid]=time;SetPhase(grid,settleSeconds>0?Settling:Fading);MarkChange(grid);Schedule(grid);
        }
        private void ResumeContact(int grid)
        {
            if(phases[grid]==Contact && releaseStarted[grid]<0)return;
            CancelDeadline(grid);releaseStarted[grid]=-1;SetPhase(grid,Contact);MarkChange(grid);
        }
        private void AdvanceDeadlines(float time)
        {
            while(deadlineCount>0 && Deadline(deadlineHeap[0])<=time)
            {
                int g=deadlineHeap[0];CancelDeadline(g);
                // A stored float deadline can round below the original expression's
                // intermediate precision in Mono. Let that predicate decide the
                // phase; defer this single event instead of changing it one tick early.
                if((phases[g]==Settling && time<releaseStarted[g]+settleSeconds) ||
                    (phases[g]==Fading && time-releaseStarted[g]<settleSeconds+fadeSeconds))
                {
                    ScheduleAt(g,BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(time)+1));
                    continue;
                }
                if(phases[g]==Settling && time-releaseStarted[g]<settleSeconds+fadeSeconds)
                {SetPhase(g,Fading);MarkChange(g);Schedule(g);}
                else RemoveActive(g,true);
            }
        }
        private void AddContact(int grid,int tick,Action<int,int> initialize)
        {
            int slot=eventFree.Pop();gridSlot[grid]=slot;slotGrid[slot]=grid;
            initialize(slot,grid);InitializedSlots.Add(slot);InitializationCount++;
            AddSelected(grid,slot,tick);releaseStarted[grid]=-1;SetPhase(grid,Contact);
        }
        private void BuildEventFree()
        {
            eventFree.Clear();
            for(int s=Capacity-1;s>=0;s--)
            {
                int g=slotGrid[s];if(g>=0 && active[g])continue;
                if(g>=0)gridSlot[g]=-1;
                slotGrid[s]=-1;eventFree.Push(s);
            }
        }
        private void SelectFixed(Vector3 center,float radius,float hysteresis,int tick,Action<int,int> initialize,bool stable,bool force)
        {
            bool rebuild=!nearConfigured || !nearCenter.Equals(center) || nearRadius!=radius || nearHysteresis!=hysteresis || nearStable!=stable || nearRevision!=query.Revision;
            if(!force && !rebuild && !fixedUnsettled && fixedContactVersion==Coverage.ContactVersion)
            {lastFixedTick=tick;return;}
            if(rebuild)
            {
                nearConfigured=true;nearCenter=center;nearRadius=radius;nearHysteresis=hysteresis;nearStable=stable;nearRevision=query.Revision;
                nearby.Clear();query.AddBounds(center.x,center.z,radius+Mathf.Max(0,hysteresis),nearby);nearOrder.Clear();
                float maximum=Mathf.Max(radius*radius,(radius+hysteresis)*(radius+hysteresis));
                foreach(int g in nearby.Items)
                {
                    Vector3 d=grids[g].Pos-center;d.y=0;nearDistanceSquared[g]=d.sqrMagnitude;
                    if(nearDistanceSquared[g]>maximum)continue;
                    BaseScore(g);nearOrder.Add(g);
                }
                nearOrder.Sort(compareBase);
            }
            foreach(var entry in selected)lastUsed[entry.PoolOffset/32]=lastFixedTick;
            candidates.Clear();
            foreach(int g in Coverage.Contacts) {BaseScore(g);candidates.Add(g);}
            int desired=candidates.Count;
            if(candidates.Count>=Capacity)
            {
                candidates.Sort(compareRank);
                if(candidates.Count>Capacity)candidates.RemoveRange(Capacity,candidates.Count-Capacity);
                // Count rejected spatial cells too, preserving the public budget diagnostic.
                foreach(int g in nearOrder)if(!Coverage.ContainsContact(g) && EligibleNear(g,radius,hysteresis))desired++;
            }
            else
            {
                activeNear.Clear();inactiveNear.Clear();
                foreach(int g in nearOrder)
                {
                    if(Coverage.ContainsContact(g) || !EligibleNear(g,radius,hysteresis))continue;
                    if(active[g])activeNear.Add(g);else inactiveNear.Add(g);
                }
                if(stable && hysteresis>=0)
                {
                    // Compliance hysteresis clamps a small central group to score zero.
                    // Its tie order is ID order rather than unadjusted distance order.
                    int zero=0;while(zero<activeNear.Count && scores[activeNear[zero]]<=hysteresis)zero++;
                    if(zero>1)activeNear.Sort(0,zero,Comparer<int>.Default);
                }
                desired+=activeNear.Count+inactiveNear.Count;
                int a=0,b=0;
                while(candidates.Count<Capacity && (a<activeNear.Count || b<inactiveNear.Count))
                {
                    bool takeActive=b==inactiveNear.Count || (a<activeNear.Count && CompareEffective(activeNear[a],inactiveNear[b])<=0);
                    candidates.Add(takeActive?activeNear[a++]:inactiveNear[b++]);
                }
            }
            BudgetRejectedGrids=Mathf.Max(0,desired-Capacity);
            Array.Clear(reserved,0,reserved.Length);
            foreach(int g in candidates)if(gridSlot[g]>=0)reserved[gridSlot[g]]=true;
            bool membershipChanged=false;
            for(int i=selected.Count-1;i>=0;i--)if(!reserved[selected[i].PoolOffset/32])
            {RemoveActive(selected[i].GridId,false);membershipChanged=true;}
            bool built=false;
            foreach(int g in candidates)
            {
                int slot=gridSlot[g];
                if(slot<0)
                {
                    if(!built) {BuildAvailableSlots();built=true;}
                    slot=TakeAvailableSlot();if(slotGrid[slot]>=0)gridSlot[slotGrid[slot]]=-1;
                    gridSlot[g]=slot;slotGrid[slot]=g;reserved[slot]=true;
                    initialize(slot,g);InitializedSlots.Add(slot);InitializationCount++;
                }
                if(!active[g]) {AddSelected(g,slot,tick);membershipChanged=true;}
                else RefreshSelected(g);
                lastUsed[slot]=tick;releaseStarted[g]=-1;SetPhase(g,Coverage.ContainsContact(g)?Contact:Fixed);
            }
            fixedContactVersion=Coverage.ContactVersion;fixedUnsettled=membershipChanged;lastFixedTick=tick;
        }
        private bool EligibleNear(int grid,float radius,float hysteresis)
        {float reach=radius+(active[grid]?hysteresis:0);return nearDistanceSquared[grid]<=reach*reach;}
        private void ConfigureScores(Vector3 center,bool stable,float hysteresis)
        {
            rankStable=stable;rankHysteresis=hysteresis;
            if(scoreConfigured && scoreCenter.Equals(center) && scoreStable==stable && scoreRevision==query.Revision)return;
            scoreConfigured=true;scoreCenter=center;scoreStable=stable;scoreRevision=query.Revision;
            if(++scoreEpoch==0) {Array.Clear(scoreComputed,0,scoreComputed.Length);scoreEpoch=1;}
        }
        private float BaseScore(int grid)
        {
            if(scoreComputed[grid]==scoreEpoch)return scores[grid];
            Vector3 d=grids[grid].Pos-scoreCenter;if(scoreStable)d.y=0;
            scores[grid]=scoreStable?d.magnitude:d.sqrMagnitude;scoreComputed[grid]=scoreEpoch;return scores[grid];
        }
        private int CompareBase(int a,int b)
        {int c=scores[a].CompareTo(scores[b]);return c!=0?c:a.CompareTo(b);}
        private int CompareEffective(int a,int b)
        {
            float x=rankStable && active[a]?Mathf.Max(0,scores[a]-rankHysteresis):scores[a];
            float y=rankStable && active[b]?Mathf.Max(0,scores[b]-rankHysteresis):scores[b];
            int c=x.CompareTo(y);return c!=0?c:a.CompareTo(b);
        }
        private int CompareRank(int a,int b)
        {if(interactionPriority[a]!=interactionPriority[b])return interactionPriority[a]?-1:1;return CompareEffective(a,b);}
        private void AddSelected(int grid,int slot,int tick)
        {
            active[grid]=true;selectedIndex[grid]=selected.Count;lastUsed[slot]=tick;
            selected.Add(new ActiveGrid {Position=grids[grid].Pos,BladeOffset=grids[grid].IdOffset,PoolOffset=slot*32,GridId=grid});
            selectionDirty=true;MarkChange(grid);
        }
        private void RefreshSelected(int grid)
        {
            int index=selectedIndex[grid];var entry=selected[index];
            if(entry.Position.Equals(grids[grid].Pos) && entry.BladeOffset==grids[grid].IdOffset)return;
            entry.Position=grids[grid].Pos;entry.BladeOffset=grids[grid].IdOffset;selected[index]=entry;
            selectionDirty=true;MarkChange(grid);
        }
        private void RemoveActive(int grid,bool releaseSlot)
        {
            CancelDeadline(grid);int index=selectedIndex[grid];
            if(index>=0)
            {
                var last=selected[selected.Count-1];selected[index]=last;selectedIndex[last.GridId]=index;
                selected.RemoveAt(selected.Count-1);selectedIndex[grid]=-1;active[grid]=false;
                selectionDirty=true;MarkChange(grid);
            }
            SetPhase(grid,0);releaseStarted[grid]=-1;
            if(releaseSlot && gridSlot[grid]>=0)
            {int slot=gridSlot[grid];gridSlot[grid]=-1;slotGrid[slot]=-1;eventFree.Push(slot);}
        }
        private void SetPhase(int grid,byte value)
        {
            byte old=phases[grid];if(old==value)return;
            if(old==Contact)InteractionBlades-=32;else if(old==Settling)SettlingBlades-=32;else if(old==Fading)FadingBlades-=32;
            phases[grid]=value;
            if(value==Contact)InteractionBlades+=32;else if(value==Settling)SettlingBlades+=32;else if(value==Fading)FadingBlades+=32;
        }
        private void MarkChange(int grid) {if(inSelect)stateChanges.Add(grid);else pendingChanges.Add(grid);}
        private float Deadline(int grid)=>deadlineValues[gridSlot[grid]];
        private static float Expiry(float start,float lifetime)
        {
            // Preserve the original subtraction test exactly at representable float times.
            // Rounded addition alone can otherwise return a block one simulation tick early.
            float value=start+lifetime;
            if(float.IsNaN(value) || float.IsInfinity(value))return value;
            while(value-start<lifetime)value=BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(value)+1);
            for(;;)
            {
                float previous=BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(value)-1);
                if(previous<0 || previous-start<lifetime)return value;
                value=previous;
            }
        }
        private bool Earlier(int a,int b)
        {float x=Deadline(a),y=Deadline(b);return x<y || (x==y && a<b);}
        private void Schedule(int grid)
        {
            ScheduleAt(grid,phases[grid]==Fading?Expiry(releaseStarted[grid],settleSeconds+fadeSeconds):releaseStarted[grid]+settleSeconds);
        }
        private void ScheduleAt(int grid,float deadline)
        {
            deadlineValues[gridSlot[grid]]=deadline;
            int i=deadlineIndex[grid];if(i<0) {i=deadlineCount++;deadlineHeap[i]=grid;deadlineIndex[grid]=i;}
            HeapUp(i);HeapDown(deadlineIndex[grid]);
        }
        private void HeapUp(int i)
        {
            int grid=deadlineHeap[i];
            while(i>0)
            {
                int parent=(i-1)/2;if(!Earlier(grid,deadlineHeap[parent]))break;
                deadlineHeap[i]=deadlineHeap[parent];deadlineIndex[deadlineHeap[i]]=i;i=parent;
            }
            deadlineHeap[i]=grid;deadlineIndex[grid]=i;
        }
        private void HeapDown(int i)
        {
            int grid=deadlineHeap[i];
            for(int child=i*2+1;child<deadlineCount;child=i*2+1)
            {
                if(child+1<deadlineCount && Earlier(deadlineHeap[child+1],deadlineHeap[child]))child++;
                if(!Earlier(deadlineHeap[child],grid))break;
                deadlineHeap[i]=deadlineHeap[child];deadlineIndex[deadlineHeap[i]]=i;i=child;
            }
            deadlineHeap[i]=grid;deadlineIndex[grid]=i;
        }
        private void CancelDeadline(int grid)
        {
            int i=deadlineIndex[grid];if(i<0)return;deadlineIndex[grid]=-1;
            int last=deadlineHeap[--deadlineCount];if(i==deadlineCount)return;
            deadlineHeap[i]=last;deadlineIndex[last]=i;HeapUp(i);HeapDown(deadlineIndex[last]);
        }
        private void ClearDeadlines()
        {for(int i=0;i<deadlineCount;i++)deadlineIndex[deadlineHeap[i]]=-1;deadlineCount=0;}
        private bool OlderSlot(int a,int b)=>lastUsed[a]<lastUsed[b] || (lastUsed[a]==lastUsed[b] && a<b);
        private void BuildAvailableSlots()
        {
            freeSlotCount=nextFreeSlot=lruSlotCount=0;
            for(int slot=0;slot<Capacity;slot++)if(!reserved[slot])
            {if(slotGrid[slot]<0)freeSlots[freeSlotCount++]=slot;else lruSlots[lruSlotCount++]=slot;}
            for(int i=lruSlotCount/2-1;i>=0;i--)SiftOlderSlot(i);
        }
        private void SiftOlderSlot(int index)
        {
            int slot=lruSlots[index];
            for(int child=index*2+1;child<lruSlotCount;child=index*2+1)
            {
                if(child+1<lruSlotCount && OlderSlot(lruSlots[child+1],lruSlots[child]))child++;
                if(!OlderSlot(lruSlots[child],slot))break;lruSlots[index]=lruSlots[child];index=child;
            }
            lruSlots[index]=slot;
        }
        private int TakeAvailableSlot()
        {
            if(nextFreeSlot<freeSlotCount)return freeSlots[nextFreeSlot++];
            int slot=lruSlots[0];if(--lruSlotCount>0) {lruSlots[0]=lruSlots[lruSlotCount];SiftOlderSlot(0);}return slot;
        }
        public int SlotForGrid(int grid)=>gridSlot[grid];
        public bool IsActive(int grid)=>active[grid];
        public float ExitStartedAt(int grid)=>ContactDriven && releaseStarted[grid]>=0?releaseStarted[grid]+settleSeconds:-1;
        internal static bool OverlapsCoverage(Vector3 position,float padding,Vector4 coverage,bool radial)
        {
            if(coverage.z<=0)return true;
            float x=Mathf.Abs(position.x-coverage.x),z=Mathf.Abs(position.z-coverage.y),reach=coverage.z+padding;
            return radial?x*x+z*z<=reach*reach:Mathf.Max(x,z)<=reach;
        }
        public void ForgetGrids(IReadOnlyList<int> gridsToRecycle)
        {
            foreach(int grid in gridsToRecycle)
            {
                int slot=gridSlot[grid];RemoveActive(grid,ContactDriven);
                if(!ContactDriven && slot>=0) {slotGrid[slot]=-1;gridSlot[grid]=-1;lastUsed[slot]=0;reserved[slot]=false;}
                scoreComputed[grid]=0;MarkChange(grid);
            }
            nearConfigured=false;fixedUnsettled=true;
            if(selectionDirty) {SelectionVersion++;selectionDirty=false;}
        }
    }
}
