using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GrassFinal.PBDGrass
{
    public class PBDGrassBody
    {
        private const float DistanceConstraintStiffness = 1.0f; 

        public Vector3[] Positions { get; set; }
        public Vector3[] Predicted { get; set; }
        public Vector3[] Velocities { get; set; }
        public Vector3[] OriginPos { get; set; }
        public Vector3[] Offset { get; set; }

        public int IndexOffset { get; private set; }

        public List<FixedConstraint> Fcons { get; private set; }
        public List<DistanceConstraint> Dcons { get; private set; }

        public float Mass { get; set; }
        public int Segments { get; private set; }
        public int BoneCounts { get; private set; }
        public int vertexCounts { get; private set; }
        public bool UsesBezierShape { get; private set; }

        public PBDGrassBody(ref Vector3[] vertices, ref Vector2[] uvs, ref int[] triangles, int indexOffset, int triIndexOffset,
            Vector3 root, int segments, float h, float w, float f, float mass, GrassBladeShapeSettings shape = null)
        {
            this.Mass = mass;
            this.Segments = segments;
            this.BoneCounts = segments + 1;
            this.vertexCounts = 2 * segments + 1;
            this.IndexOffset = indexOffset;
            UsesBezierShape = shape != null;

            FillGrassMesh(triIndexOffset, ref vertices, ref uvs, ref triangles, ref root, h, w, f, shape);

            InitPositionsAndConstraints(ref vertices);
        }

        public PBDGrassBody(PBDGrassBody origin)
        {
            this.Mass = origin.Mass;
            this.Segments = origin.Segments;
            this.BoneCounts = origin.BoneCounts;
            this.vertexCounts = origin.vertexCounts;
            this.IndexOffset = origin.IndexOffset;
            UsesBezierShape = origin.UsesBezierShape;

            this.Positions = new Vector3[origin.Positions.Length];
            origin.Positions.CopyTo(this.Positions, 0);

            this.Predicted = new Vector3[origin.Predicted.Length];
            origin.Predicted.CopyTo(this.Predicted, 0);

            this.Velocities = new Vector3[origin.Velocities.Length];
            origin.Velocities.CopyTo(this.Velocities, 0);

            this.OriginPos = new Vector3[origin.OriginPos.Length];
            origin.OriginPos.CopyTo(this.OriginPos, 0);

            this.Offset = new Vector3[origin.Offset.Length];
            origin.Offset.CopyTo(this.Offset, 0);

            Fcons = new List<FixedConstraint>();
            for (int i = 0; i < origin.Fcons.Count; i++)
            {
                Fcons.Add(new FixedConstraint(origin.Fcons[i], this));
            }

            Dcons = new List<DistanceConstraint>();
            for (int i = 0; i < origin.Dcons.Count; i++)
            {
                Dcons.Add(new DistanceConstraint(origin.Dcons[i], this));
            }
        }
        ~PBDGrassBody()
        {
            Fcons.Clear();
            Dcons.Clear();
        }

        void InitPositionsAndConstraints(ref Vector3[] vertices)
        {
            Positions = new Vector3[Segments + 1];
            Predicted = new Vector3[Segments + 1];
            Velocities = new Vector3[Segments + 1];
            OriginPos = new Vector3[Segments + 1];
            Offset = new Vector3[Segments];

            Fcons = new List<FixedConstraint>();
            Dcons = new List<DistanceConstraint>();

            int index = 0;
            for (int i = 0; i < vertexCounts - 2; i+=2)
            {
                Vector3 midPoint = (vertices[i + IndexOffset] + vertices[i + 1 + IndexOffset])/ 2;
                Predicted[index] = Positions[index] = OriginPos[index] = midPoint;
                Offset[index] = (vertices[i + IndexOffset] - vertices[i + 1 + IndexOffset]) / 2;
                ++index;
            }
            Predicted[index] = Positions[index] = OriginPos[index] = vertices[Segments * 2 + IndexOffset];

            // pinned
            Fcons.Add(new FixedConstraint(0, this));

            // distance
            for (int i = 1; i <= Segments; ++i)
            {
                Dcons.Add(new DistanceConstraint(i, i - 1, DistanceConstraintStiffness, this));
            }
        }

        public void UpdateMesh(ref Vector3[] vertices)
        {
            int index = 0;
            for (int i = 0; i < vertexCounts - 2; i += 2)
            {
                vertices[i + IndexOffset] = Positions[index] + Offset[index];
                vertices[i + 1 + IndexOffset] = Positions[index] - Offset[index];
                ++index;
            }
            vertices[vertexCounts - 1 + IndexOffset] = Positions[index];
        }

        public void Transform(Matrix4x4 TRS)
        {
            for (int i = 0; i < Positions.Length; i++)
            {
                Positions[i] = TRS.MultiplyPoint3x4(Positions[i]);
                Predicted[i] = Positions[i];
                OriginPos[i] = Positions[i];
            }
            for (int i = 0; i < Offset.Length; i++)
            {
                Offset[i] = TRS.MultiplyVector(Offset[i]);
            }

            for (int i = 0; i < Fcons.Count; i++)
            {
                Fcons[i].fixedPos = TRS.MultiplyPoint3x4(Fcons[i].fixedPos);
            }
    }

        void GenV(int vi, int ui, ref Vector3[] vertices, ref Vector2[] uvs, 
            Vector3 rootPosition, float _w, float _h, float _f, Vector2 uv, Matrix3x3 transformMatrix)
        {
            Vector3 localPosition = new Vector3(_w, _h, _f);
            Vector3 worldPosition = rootPosition + transformMatrix * localPosition;

            vertices[vi + IndexOffset] = worldPosition;
            uvs[ui + IndexOffset] = uv;
        }
        private void FillGrassMesh(int triIndexOffset, ref Vector3[] vertices, ref Vector2[] uvs, ref int[] triangles, 
            ref Vector3 root, float h, float w, float f, GrassBladeShapeSettings shape)
        {
            if (shape != null)
            {
                FillBezierGrassMesh(triIndexOffset, vertices, uvs, triangles, root, shape);
                return;
            }
            int vi = 0;
            int ti = 0;
            int ui = 0;

            Matrix3x3 facingRotationMatrix = MathUtility.AngleAxis3x3(MathUtility.Rand(root) * Mathf.PI * 2, Vector3.up);
            Matrix3x3 bendRotationMatrix = MathUtility.AngleAxis3x3((0.10f + MathUtility.Rand(root) * 0.20f), Vector3.left);

            Matrix3x3 bendFacingRotationMatrix = facingRotationMatrix * bendRotationMatrix;

            float height = h;
            float width = w;
            float forward = f;

            for (int i = 0; i < Segments; i++)
            {
                float t = i / (float)Segments;

                float segmentHeight = height * t;
                float segmentWidth = width * (1 - t);
                float segmentForward = Mathf.Pow(t, 2) * forward;

                if (i == 0) //2 roots
                {
                    GenV(vi++, ui++, ref vertices, ref uvs,
                        root, -segmentWidth, segmentHeight, segmentForward, new Vector2(0, 0), facingRotationMatrix);
                    GenV(vi++, ui++, ref vertices, ref uvs
                        , root, segmentWidth, segmentHeight, segmentForward, new Vector2(1, 0), facingRotationMatrix);
                }
                else //vertices on top
                {
                    GenV(vi++, ui++, ref vertices, ref uvs,
                        root, -segmentWidth, segmentHeight, segmentForward, new Vector2(0, t), bendFacingRotationMatrix);
                    GenV(vi++, ui++, ref vertices, ref uvs,
                        root, segmentWidth, segmentHeight, segmentForward, new Vector2(1, t), bendFacingRotationMatrix);
                }
            }
            GenV(vi++, ui++, ref vertices, ref uvs,
                root, 0, height, forward, new Vector2(0.5f, 1), bendFacingRotationMatrix);

            if (Segments != 1)
            {
                for (int i = 1; i <= Segments - 1; i++)
                {
                    triangles[triIndexOffset + ti++] = i * 2 + IndexOffset;
                    triangles[triIndexOffset + ti++] = i * 2 + 1 + IndexOffset;
                    triangles[triIndexOffset + ti++] = i * 2 - 2 + IndexOffset;
                    triangles[triIndexOffset + ti++] = i * 2 + 1 + IndexOffset;
                    triangles[triIndexOffset + ti++] = i * 2 - 1 + IndexOffset;
                    triangles[triIndexOffset + ti++] = i * 2 - 2 + IndexOffset;
                }
                triangles[triIndexOffset + ti++] = Segments * 2 + IndexOffset;
                triangles[triIndexOffset + ti++] = Segments * 2 - 1 + IndexOffset;
                triangles[triIndexOffset + ti++] = Segments * 2 - 2 + IndexOffset;
            }
            else
            {
                triangles[triIndexOffset + ti++] = 0 + IndexOffset;
                triangles[triIndexOffset + ti++] = 2 + IndexOffset;
                triangles[triIndexOffset + ti++] = 1 + IndexOffset;
            }
        }

        private static float ShapeHash(Vector3 root, float salt)
        {
            float value = Mathf.Sin(Vector3.Dot(root, new Vector3(12.9898f, 78.233f, 53.539f)) + salt) * 43758.5453f;
            return value - Mathf.Floor(value);
        }

        private void FillBezierGrassMesh(int triangleOffset, Vector3[] vertices, Vector2[] uvs, int[] triangles,
            Vector3 root, GrassBladeShapeSettings shape)
        {
            // The curve defines the natural leaf only. XPBD remains responsible for its moving shape.
            float height = Mathf.Max(.1f, shape.Height) * (1 + (ShapeHash(root, 11) * 2 - 1) * Mathf.Clamp(shape.HeightVariation, 0, .4f));
            float width = Mathf.Max(.001f, shape.HalfWidth) * (1 + (ShapeHash(root, 23) * 2 - 1) * Mathf.Clamp(shape.WidthVariation, 0, .5f));
            float bend = height * Mathf.Clamp(shape.BendRatio, 0, .8f) * Mathf.Lerp(.8f, 1.2f, ShapeHash(root, 37));
            float side = height * Mathf.Clamp(shape.SideCurveRatio, 0, .15f) * (ShapeHash(root, 41) * 2 - 1);
            float tilt = shape.TiltDegrees + (ShapeHash(root, 53) * 2 - 1) * shape.TiltVariationDegrees;
            Quaternion rotation = Quaternion.AngleAxis(ShapeHash(root, 67) * 360, Vector3.up)
                * Quaternion.AngleAxis(Mathf.Clamp(tilt, 0, 40), Vector3.right);
            Vector3 p1 = new Vector3(0, height * .4f, 0);
            Vector3 p2 = new Vector3(side * .35f, height * .95f, bend * .15f);
            Vector3 p3 = new Vector3(side, height * .9f, bend);
            for (int s = 0; s <= Segments; s++)
            {
                float t = s / (float)Segments, a = 1 - t;
                Vector3 center = 3 * a * a * t * p1 + 3 * a * t * t * p2 + t * t * t * p3;
                Vector3 tangent = (3 * a * a * p1 + 6 * a * t * (p2 - p1) + 3 * t * t * (p3 - p2)).normalized;
                Vector3 across = Vector3.ProjectOnPlane(Vector3.right, tangent).normalized;
                float profile = (.72f + .28f * Mathf.Sin(Mathf.PI * t)) * Mathf.Pow(a, .75f);
                int vertex = IndexOffset + s * 2;
                if (s == Segments)
                {
                    vertices[vertex] = root + rotation * center;
                    uvs[vertex] = new Vector2(.5f, 1);
                }
                else
                {
                    Vector3 offset = rotation * across * (width * profile);
                    vertices[vertex] = root + rotation * center - offset;
                    vertices[vertex + 1] = root + rotation * center + offset;
                    uvs[vertex] = new Vector2(0, t); uvs[vertex + 1] = new Vector2(1, t);
                }
            }
            int ti = triangleOffset;
            for (int s = 1; s < Segments; s++)
            {
                int v = IndexOffset + s * 2;
                triangles[ti++] = v; triangles[ti++] = v + 1; triangles[ti++] = v - 2;
                triangles[ti++] = v + 1; triangles[ti++] = v - 1; triangles[ti++] = v - 2;
            }
            triangles[ti++] = IndexOffset + Segments * 2;
            triangles[ti++] = IndexOffset + Segments * 2 - 1;
            triangles[ti] = IndexOffset + Segments * 2 - 2;
        }
    }
}
