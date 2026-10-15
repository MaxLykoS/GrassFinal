using System;
using UnityEngine;
namespace GrassFinal.PBDGrass
{
    // Immutable identity/rest shape shared by generation, physics and rendering.
    // A later representation can sample these nodes without creating another population.
    public sealed class GrassBladeData
    {
        public const int Nodes = 8, Segments = 7, Vertices = 15, Indices = 39;
        public struct Blade { public Vector3 Root; public float Length; public uint Id; public float Width, Height, Seed; }
        public struct Rod
        {
            public Quaternion Rotation, Relative;
            public float Length, HalfWidth, InverseInertia, SoftScale;
            public float HardScale, TwistScale, Padding0, Padding1;
        }
        public struct Frame { public Quaternion Rotation; public Vector3 AngularVelocity; public float Padding; }
        public readonly Blade[] Blades;
        public readonly BoneInfo[] RestNodes;
        public readonly Rod[] Rods;
        public readonly float[] InverseMass;
        public readonly GrassGridStruct[] Grids;
        public readonly Vector3[] VerticesData, Normals;
        public readonly int[] Triangles;
        public readonly Vector2[] UV;
        public readonly Bounds Bounds;
        public float MaximumBladeLength { get; private set; }
        public GrassBladeData(PBDGrassPatch patch, float tipThickness)
        {
            if (patch.SEGMENTS != Segments || patch.grids == null || patch.Bodies.Length != patch.grids.Length * 32)
                throw new ArgumentException("Near ribbon requires 8 nodes and 32 blades per grid.");
            Blades = new Blade[patch.Bodies.Length]; RestNodes = patch.GenBoneInfoArray();
            Rods = new Rod[Blades.Length * Segments]; InverseMass = new float[RestNodes.Length];
            Grids = patch.grids; VerticesData = patch.vertices; Normals = patch.PatchMesh.normals;
            Triangles = patch.PatchMesh.triangles; UV = patch.PatchMesh.uv;
            for (int b = 0; b < Blades.Length; b++)
            {
                var body = patch.Bodies[b]; Vector3 root = body.OriginPos[0];
                float total = 0; float[] weights = new float[Nodes];
                Quaternion previous = Quaternion.identity;
                float bladeLength = 0;
                float maximumWidth = 1e-6f;
                foreach (Vector3 offset in body.Offset) maximumWidth = Mathf.Max(maximumWidth, offset.magnitude);
                for (int s = 0; s < Segments; s++)
                {
                    Vector3 tangent = (body.OriginPos[s+1] - body.OriginPos[s]).normalized;
                    Vector3 width = Vector3.ProjectOnPlane(body.Offset[s], tangent).normalized;
                    Quaternion q = Quaternion.LookRotation(tangent, Vector3.Cross(tangent, width).normalized);
                    float len = Vector3.Distance(body.OriginPos[s], body.OriginPos[s+1]);
                    float u = (s + .5f) / Segments;
                    float w = Mathf.Max(.08f, 1 - u), t = Mathf.Lerp(1, tipThickness, u);
                    if (body.UsesBezierShape)
                    {
                        float nextWidth = s + 1 < Segments ? body.Offset[s + 1].magnitude : 0;
                        w = Mathf.Max(.08f, .5f * (body.Offset[s].magnitude + nextWidth) / maximumWidth);
                    }
                    float mass = len * w * t;
                    weights[s] += mass*.5f; weights[s+1] += mass*.5f; total += mass;
                    float thin = len / .1f / (w*t*t*t);
                    Rods[b*Segments+s] = new Rod { Rotation = q,
                        Relative = s == 0 ? Quaternion.identity : Quaternion.Inverse(previous)*q,
                        Length = len, HalfWidth = body.Offset[s].magnitude,
                        InverseInertia = 1 / Mathf.Max(1e-8f, mass * len*len / 12),
                        SoftScale = thin, HardScale = len / .1f / (t*w*w*w), TwistScale = thin };
                    previous = q; bladeLength += len;
                }
                for (int n = 0; n < Nodes; n++)
                {
                    InverseMass[b*Nodes+n] = n == 0 ? 0 : total / weights[n];
                    RestNodes[b*Nodes+n].Position -= root; RestNodes[b*Nodes+n].Predicted -= root;
                }
                for (int s = 0; s < Segments; s++)
                {
                    Rod r = Rods[b*Segments+s]; r.InverseInertia *= total; Rods[b*Segments+s] = r;
                }
                Blades[b] = new Blade { Root = root, Length = bladeLength, Id = (uint)b,
                    Width = body.Offset[0].magnitude*2, Height = body.OriginPos[Nodes-1].y-root.y,
                    Seed = Mathf.Repeat(b * .61803398875f, 1) };
                MaximumBladeLength = Mathf.Max(MaximumBladeLength, bladeLength);
            }
            float reach = MaximumBladeLength * 1.1f + .1f;
            Bounds = new Bounds(patch.Root, new Vector3(patch.Width + 2 * reach, 2 * reach, patch.Length + 2 * reach));
        }
    }
}
