using System;
using UnityEngine;

namespace GrassFinal.PBDGrass
{
    [Serializable]
    public sealed class GrassBladeShapeSettings
    {
        [Min(0.1f)] public float Height = 1.5f;
        [Range(0, 0.4f)] public float HeightVariation = 0.15f;
        [Min(0.001f)] public float HalfWidth = 0.02f;
        [Range(0, 0.5f)] public float WidthVariation = 0.15f;
        [Range(0, 30)] public float TiltDegrees = 6;
        [Range(0, 15)] public float TiltVariationDegrees = 4;
        [Range(0, 0.8f)] public float BendRatio = 0.32f;
        [Range(0, 0.15f)] public float SideCurveRatio = 0.035f;
    }
}
