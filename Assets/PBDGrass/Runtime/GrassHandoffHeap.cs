using System;
using System.Collections.Generic;

namespace GrassFinal.PBDGrass
{
    // One entry per world-grid ID. Updating/removing a key never leaves stale
    // records behind, so long running contact/streaming sessions stay bounded.
    internal sealed class GrassHandoffHeap
    {
        private readonly int[] items,indices;
        private readonly Comparison<int> compare;
        public int Count {get;private set;}
        public int First=>items[0];
        public GrassHandoffHeap(int capacity,Comparison<int> compare)
        {
            items=new int[capacity];indices=new int[capacity];this.compare=compare;
            for(int i=0;i<indices.Length;i++)indices[i]=-1;
        }
        public bool Contains(int id)=>indices[id]>=0;
        private void Place(int index,int id) {items[index]=id;indices[id]=index;}
        private void Up(int index)
        {
            int id=items[index];
            while(index>0)
            {
                int parent=(index-1)/2;
                if(compare(items[parent],id)<=0)break;
                Place(index,items[parent]);index=parent;
            }
            Place(index,id);
        }
        private void Down(int index)
        {
            int id=items[index];
            while(index*2+1<Count)
            {
                int child=index*2+1;
                if(child+1<Count && compare(items[child+1],items[child])<0)child++;
                if(compare(id,items[child])<=0)break;
                Place(index,items[child]);index=child;
            }
            Place(index,id);
        }
        public void Add(int id)
        { if(Contains(id))return;Place(Count,id);Count++;Up(Count-1); }
        public void Remove(int id)
        {
            int index=indices[id];if(index<0)return;
            indices[id]=-1;Count--;
            if(index==Count)return;
            Place(index,items[Count]);
            if(index>0 && compare(items[index],items[(index-1)/2])<0)Up(index);else Down(index);
        }
        public int Pop() {int id=First;Remove(id);return id;}
        public void Rebuild() {for(int i=Count/2-1;i>=0;i--)Down(i);}
        public void Clear() {for(int i=0;i<Count;i++)indices[items[i]]=-1;Count=0;}
    }

    internal sealed class GrassHandoffSet
    {
        private readonly int[] indices;
        public readonly List<int> Items;
        public int Count=>Items.Count;
        public GrassHandoffSet(int capacity)
        {
            indices=new int[capacity];Items=new List<int>(capacity);
            for(int i=0;i<indices.Length;i++)indices[i]=-1;
        }
        public bool Contains(int id)=>indices[id]>=0;
        public void Add(int id) {if(Contains(id))return;indices[id]=Items.Count;Items.Add(id);}
        public void Remove(int id)
        {
            int index=indices[id];if(index<0)return;
            int last=Items[Items.Count-1];Items[index]=last;indices[last]=index;
            Items.RemoveAt(Items.Count-1);indices[id]=-1;
        }
        public void Clear() {foreach(int id in Items)indices[id]=-1;Items.Clear();}
    }
}
