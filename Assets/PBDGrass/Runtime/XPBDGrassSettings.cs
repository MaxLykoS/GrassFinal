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
        [Min(0)] public float SoftBendCompliance = 3;
        [Min(0)] public float HardBendCompliance = 0.2f;
        [Min(0)] public float TwistCompliance = 1;
        [Min(0)] public float BaseBendCompliance = 0.08f;
        [Min(0.0001f)] public float BladeMass = 0.01f;
        [Range(0.1f, 1)] public float TipThicknessRatio = 0.45f;
        [Min(0)] public float VelocityDamping = 2;
        [Min(0)] public float AngularDamping = 3;
        public Vector3 Gravity = new Vector3(0, -9.81f, 0);
        [Header("Bending plasticity (no stretch plasticity or fracture)")]
        public bool EnablePlasticity = true;
        [Tooltip("Elastic bend curvature before yielding, in radians per meter.")]
        [Min(0.01f)] public float PlasticYieldCurvature = 2.5f;
        [Tooltip("Rate of permanent bend accumulation above yield, in inverse seconds.")]
        [Min(0)] public float PlasticFlowRate = 6;
        [Min(0)] public float PlasticMaxCurvature = 4;
        [Range(1, 89)] public float BaseYieldDegrees = 40;
        [Range(0, 85)] public float BaseMaxPlasticDegrees = 50;
        [Header("Segment contacts")]
        [Min(0.001f)] public float ColliderRadius = 0.6f;
        [Min(0)] public float CollisionMargin = 0.002f;
        [Min(0)] public float Friction = 0.45f;
        public bool GroundCollision = true;
    }
}
