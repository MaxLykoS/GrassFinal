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
        private readonly List<int> candidates = new List<int>();
        private readonly List<ActiveGrid> selected = new List<ActiveGrid>();
        public readonly List<int> InitializedSlots = new List<int>();
        public int Capacity { get; }
        public int ActiveBlades => selected.Count * 32;
        public int InitializationCount { get; private set; }
        public int BudgetRejectedGrids { get; private set; }
        public IReadOnlyList<ActiveGrid> Selected => selected;
        public GrassSimulationPool(GrassGridStruct[] grids, int bladeCapacity)
        {
            this.grids = grids; Capacity = Mathf.Min(grids.Length, Mathf.Max(1, bladeCapacity / 32));
            gridSlot = new int[grids.Length]; slotGrid = new int[Capacity]; lastUsed = new int[Capacity];
            active = new bool[grids.Length]; reserved = new bool[Capacity];
            for (int i = 0; i < grids.Length; i++) gridSlot[i] = -1;
            for (int i = 0; i < Capacity; i++) slotGrid[i] = -1;
        }
        public void Select(Vector3 center, float radius, float hysteresis, int tick, Action<int,int> initialize)
        {
            candidates.Clear(); selected.Clear(); InitializedSlots.Clear(); Array.Clear(reserved, 0, reserved.Length);
            for (int g = 0; g < grids.Length; g++)
            {
                Vector3 d = grids[g].Pos - center; d.y = 0;
                float reach = radius + (active[g] ? hysteresis : 0);
                if (d.sqrMagnitude <= reach*reach) candidates.Add(g);
            }
            candidates.Sort((a,b) => { float da = (grids[a].Pos-center).sqrMagnitude, db = (grids[b].Pos-center).sqrMagnitude;
                int compare = da.CompareTo(db); return compare != 0 ? compare : a.CompareTo(b); });
            BudgetRejectedGrids = Mathf.Max(0, candidates.Count - Capacity);
            if (candidates.Count > Capacity) candidates.RemoveRange(Capacity, candidates.Count-Capacity);
            foreach (int g in candidates) if (gridSlot[g] >= 0) reserved[gridSlot[g]] = true;
            Array.Clear(active, 0, active.Length);
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
                selected.Add(new ActiveGrid { Position = grids[g].Pos, BladeOffset = grids[g].IdOffset,
                    PoolOffset = slot*32, GridId = g });
            }
        }
        public int SlotForGrid(int grid) => gridSlot[grid];
    }
}
