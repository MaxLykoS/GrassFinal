using System;
using System.Collections.Generic;
using UnityEngine;

namespace GrassFinal.PBDGrass
{
    // One shared broad phase for simulation ownership and display-history warming.
    // Moving actors update their own cached footprint. Reference counts turn overlaps
    // into one set of entering/leaving world cells, without rebuilding either union.
    internal sealed class GrassInteractionCoverage
    {
        private sealed class Region
        {
            public bool Valid;
            public Vector4 Shape;
            public readonly List<int> Contact=new List<int>(),Warm=new List<int>();
        }
        private readonly GrassGridStruct[] grids;
        private readonly GrassGridQuery query;
        private readonly GrassGridSet scratch;
        private readonly List<Region> regions=new List<Region>();
        private readonly int[] contactReferences,warmReferences,contactIndex,warmIndex;
        private readonly bool[] dirty,wasContact,wasWarm;
        private readonly List<int> dirtyIds=new List<int>(),contacts=new List<int>(),warm=new List<int>();
        private readonly List<int> contactAdded=new List<int>(),contactRemoved=new List<int>(),warmAdded=new List<int>(),warmRemoved=new List<int>();
        private bool configured,previousRadial;
        private int previousRevision;
        private Vector4 previousCoverage;
        private Vector3 previousCenter;
        private float previousBand;
        public IReadOnlyList<int> Contacts=>contacts;
        public IReadOnlyList<int> Warm=>warm;
        public IReadOnlyList<int> ContactAdded=>contactAdded;
        public IReadOnlyList<int> ContactRemoved=>contactRemoved;
        public IReadOnlyList<int> WarmAdded=>warmAdded;
        public IReadOnlyList<int> WarmRemoved=>warmRemoved;
        public int ContactVersion {get;private set;}
        public int WarmVersion {get;private set;}
        public bool ContainsContact(int grid)=>contactReferences[grid]>0;
        public bool ContainsWarm(int grid)=>warmReferences[grid]>0;
        public GrassInteractionCoverage(GrassGridStruct[] grids,GrassGridQuery query)
        {
            this.grids=grids;this.query=query;scratch=new GrassGridSet(grids.Length);
            contactReferences=new int[grids.Length];warmReferences=new int[grids.Length];
            contactIndex=new int[grids.Length];warmIndex=new int[grids.Length];
            dirty=new bool[grids.Length];wasContact=new bool[grids.Length];wasWarm=new bool[grids.Length];
            for(int g=0;g<grids.Length;g++)contactIndex[g]=warmIndex[g]=-1;
        }
        private void Touch(int grid)
        {
            if(dirty[grid])return;
            dirty[grid]=true;dirtyIds.Add(grid);
            wasContact[grid]=contactReferences[grid]>0;wasWarm[grid]=warmReferences[grid]>0;
        }
        private void Remove(Region region)
        {
            foreach(int g in region.Contact){Touch(g);contactReferences[g]--;}
            foreach(int g in region.Warm){Touch(g);warmReferences[g]--;}
            region.Contact.Clear();region.Warm.Clear();region.Valid=false;
        }
        private void Add(Region region,Vector4 shape,Vector4 coverage,bool radial,Vector3 center,float band)
        {
            region.Shape=shape;region.Valid=true;
            float reach=shape.z+band;
            scratch.Clear();query.AddBounds(shape.x,shape.y,Mathf.Max(Mathf.Abs(shape.z),Mathf.Abs(reach)),scratch);
            foreach(int g in scratch.Items)
            {
                var p=grids[g].Pos;
                float dx=p.x-shape.x,dz=p.z-shape.y;
                if(dx*dx+dz*dz<=shape.z*shape.z && GrassSimulationPool.OverlapsCoverage(p,.71f,coverage,radial))
                {Touch(g);contactReferences[g]++;region.Contact.Add(g);}
                // Retain the original display query's arithmetic at distant world origins.
                dx=(p.x-center.x)+center.x-shape.x;dz=(p.z-center.z)+center.z-shape.y;
                if(dx*dx+dz*dz<=reach*reach)
                {Touch(g);warmReferences[g]++;region.Warm.Add(g);}
            }
        }
        private static void AddDense(List<int> values,int[] indices,int grid)
        {indices[grid]=values.Count;values.Add(grid);}
        private static void RemoveDense(List<int> values,int[] indices,int grid)
        {
            int index=indices[grid],last=values[values.Count-1];
            values[index]=last;indices[last]=index;values.RemoveAt(values.Count-1);indices[grid]=-1;
        }
        public void Update(IReadOnlyList<Vector4> input,Vector4 coverage,bool radial,Vector3 center,float band)
        {
            contactAdded.Clear();contactRemoved.Clear();warmAdded.Clear();warmRemoved.Clear();dirtyIds.Clear();
            bool geometryChanged=!configured || previousRevision!=query.Revision || !previousCoverage.Equals(coverage) ||
                previousRadial!=radial || previousBand!=band || !previousCenter.Equals(center);
            int count=input!=null?input.Count:0;
            while(regions.Count<count)regions.Add(new Region());
            for(int i=0;i<regions.Count;i++)
            {
                var region=regions[i];
                if(i>=count){if(region.Valid)Remove(region);continue;}
                var shape=input[i];
                if(region.Valid && !geometryChanged && region.Shape.Equals(shape))continue;
                if(region.Valid)Remove(region);
                Add(region,shape,coverage,radial,center,band);
            }
            foreach(int g in dirtyIds)
            {
                bool contact=contactReferences[g]>0,warmed=warmReferences[g]>0;
                if(contact!=wasContact[g])
                {
                    if(contact){AddDense(contacts,contactIndex,g);contactAdded.Add(g);}
                    else {RemoveDense(contacts,contactIndex,g);contactRemoved.Add(g);}
                }
                if(warmed!=wasWarm[g])
                {
                    if(warmed){AddDense(warm,warmIndex,g);warmAdded.Add(g);}
                    else {RemoveDense(warm,warmIndex,g);warmRemoved.Add(g);}
                }
                dirty[g]=false;
            }
            if(contactAdded.Count+contactRemoved.Count>0)ContactVersion++;
            if(warmAdded.Count+warmRemoved.Count>0)WarmVersion++;
            previousRevision=query.Revision;previousCoverage=coverage;previousRadial=radial;previousCenter=center;previousBand=band;configured=true;
        }
        public void Reset()
        {
            dirtyIds.Clear();
            foreach(var region in regions)if(region.Valid)Remove(region);
            foreach(int g in contacts)contactIndex[g]=-1;
            foreach(int g in warm)warmIndex[g]=-1;
            foreach(int g in dirtyIds)dirty[g]=false;
            dirtyIds.Clear();contacts.Clear();warm.Clear();contactAdded.Clear();contactRemoved.Clear();warmAdded.Clear();warmRemoved.Clear();
            configured=false;ContactVersion++;WarmVersion++;
        }
    }
}
