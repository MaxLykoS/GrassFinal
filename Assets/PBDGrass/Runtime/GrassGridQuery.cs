using System;
using System.Collections.Generic;
using UnityEngine;

namespace GrassFinal.PBDGrass
{
    // Scratch membership with work proportional to the previous set, not the resident world.
    internal sealed class GrassGridSet
    {
        private readonly bool[] contains;
        public readonly List<int> Items=new List<int>();
        public GrassGridSet(int count) { contains=new bool[count]; }
        public bool Contains(int grid) => contains[grid];
        public void Add(int grid)
        {
            if(contains[grid])return;
            contains[grid]=true;Items.Add(grid);
        }
        public void Clear()
        {
            for(int i=0;i<Items.Count;i++)contains[Items[i]]=false;
            Items.Clear();
        }
    }

    // Broad phase only: callers retain their original exact distance/ranking rules.
    // Streamed cells have a direct ring address; static patches use immutable buckets.
    internal sealed class GrassGridQuery
    {
        private readonly GrassWorldLayout world;
        private readonly Dictionary<Vector2Int,List<int>> buckets;
        private readonly int minX,minZ,maxX,maxZ;
        public int Revision => world!=null?world.Version:0;
        public GrassGridQuery(GrassGridStruct[] grids,GrassWorldLayout world=null)
        {
            this.world=world;
            if(world!=null)return;
            buckets=new Dictionary<Vector2Int,List<int>>();
            minX=minZ=int.MaxValue;maxX=maxZ=int.MinValue;
            for(int g=0;g<grids.Length;g++)
            {
                var cell=new Vector2Int(Mathf.FloorToInt(grids[g].Pos.x),Mathf.FloorToInt(grids[g].Pos.z));
                minX=Math.Min(minX,cell.x);minZ=Math.Min(minZ,cell.y);
                maxX=Math.Max(maxX,cell.x);maxZ=Math.Max(maxZ,cell.y);
                if(!buckets.TryGetValue(cell,out var ids))buckets.Add(cell,ids=new List<int>());
                ids.Add(g);
            }
        }
        public void AddBounds(float x,float z,float radius,GrassGridSet result)
        {
            // One extra cell protects the broad phase from float rounding at a boundary.
            int left=Mathf.FloorToInt(x-radius)-1,right=Mathf.FloorToInt(x+radius)+1;
            int bottom=Mathf.FloorToInt(z-radius)-1,top=Mathf.FloorToInt(z+radius)+1;
            int x0=world!=null?(int)world.Center.x-world.Size/2:minX;
            int z0=world!=null?(int)world.Center.z-world.Size/2:minZ;
            left=Math.Max(left,x0);bottom=Math.Max(bottom,z0);
            right=Math.Min(right,world!=null?x0+world.Size-1:maxX);
            top=Math.Min(top,world!=null?z0+world.Size-1:maxZ);
            for(int cz=bottom;cz<=top;cz++)for(int cx=left;cx<=right;cx++)
            {
                if(world!=null)
                {
                    int grid=GrassWorldIdentity.Mod(cx,world.Size)+GrassWorldIdentity.Mod(cz,world.Size)*world.Size;
                    result.Add(grid);
                }
                else if(buckets.TryGetValue(new Vector2Int(cx,cz),out var ids))
                    for(int i=0;i<ids.Count;i++)result.Add(ids[i]);
            }
        }
    }
}
