using System;
using UnityEngine;

namespace GrassFinal.PBDGrass
{
    [Serializable]
    public sealed class GrassHandoffSettings
    {
        [Min(.05f)] public float TransitionSeconds = .4f;
        [Tooltip("Authored middle-distance response in meters per unit wind acceleration; not a physical compliance.")]
        [Min(0)] public float WindDisplacement = .008f;
        [Tooltip("Optional baked material response. The patch and physical settings must match this type.")]
        public GrassResponseType BakedResponse;
        [Tooltip("Maximum display rotation rate of inactive lodged blades, in radians per second.")]
        [Min(.1f)] public float LodgedAngularSpeed=1;
        [Tooltip("Debug base color by current simulation ownership: XPBD green, Bezier blue. Lighting stays enabled.")]
        public bool DebugLodBaseColors;
        [Header("Experimental tiered state")]
        [Tooltip("Use a separate bounded history pool around the XPBD boundary; requires TieredGrassHandoff.compute.")]
        public bool UseTieredState;
        [Tooltip("Opt-in compact middle storage. Requires the compact solver/handoff/draw/streaming assets and a homogeneous streamed response type.")]
        public bool UseCompactMiddle;
        [Min(.1f)] public float HistoryBandWidth=2;
        [Tooltip("Warm incoming history in the movement direction over TransitionSeconds; discontinuous jumps use cold entry.")]
        public bool AdaptiveHistory;
        [Tooltip("History capacity, including interrupted transitions. 65536 supports disjoint exit/entry of the 32768-blade physics pool.")]
        [Min(32)] public int MaxHistoryBlades=65536;
    }
}
