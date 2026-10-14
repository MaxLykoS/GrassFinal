using System;
using UnityEngine;
namespace GrassFinal.PBDGrass
{
    [Serializable]
    public sealed class XPBDGrassSettings
    {
        [Header("Near simulation pool")]
        [Min(32)] public int MaxActiveBlades = 32768;
        [Min(1)] public float NearDistance = 16;
        [Min(0)] public float PoolHysteresis = 2;
        [Min(1)] public float MaxDistance = 40;
        public bool UseOcclusionCulling;
        [Header("Small Steps")]
        [Range(30, 120)] public int SimulationRate = 60;
        [Range(1, 16)] public int NearSubsteps = 8;
        [Range(1, 6)] public int ConstraintIterations = 3;
        [Range(1, 8)] public int MaxCatchUpSteps = 4;
        [Header("Elastic ribbon (compliance: larger is softer)")]
        [Min(0)] public float StretchCompliance = 0.00000001f;
        [Min(0)] public float AlignmentCompliance = 0.000000001f;
        [Min(0)] public float SoftBendCompliance = 12;
        [Min(0)] public float HardBendCompliance = 0.8f;
        [Min(0)] public float TwistCompliance = 3;
        [Min(0)] public float BaseBendCompliance = 0.5f;
        [Min(0.0001f)] public float BladeMass = 0.01f;
        [Range(0.1f, 1)] public float TipThicknessRatio = 0.45f;
        [Min(0)] public float VelocityDamping = 2;
        [Min(0)] public float AngularDamping = 3;
        public Vector3 Gravity = new Vector3(0, -9.81f, 0);
        [Header("Segment contacts")]
        [Min(0.001f)] public float ColliderRadius = 0.6f;
        [Min(0)] public float CollisionMargin = 0.002f;
        [Min(0)] public float Friction = 0.45f;
        public bool GroundCollision = true;
    }
}
