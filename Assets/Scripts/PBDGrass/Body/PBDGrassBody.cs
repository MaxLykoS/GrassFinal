using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PBD
{
    public class PBDGrassBody
    {
        private const float DistanceConstraintStiffness = 0.2f; 

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

        public PBDGrassBody(ref Vector3[] vertices, ref Vector2[] uvs, ref int[] triangles, int indexOffset, int triIndexOffset,
            Vector3 root, int segments, float h, float w, float f, float mass)
        {
            this.Mass = mass;
            this.Segments = segments;
            this.BoneCounts = segments + 1;
            this.vertexCounts = 2 * segments + 1;
            this.IndexOffset = indexOffset;

            FillGrassMesh(triIndexOffset, ref vertices, ref uvs, ref triangles, ref root, h, w, f);

            InitPositionsAndConstraints(ref vertices);
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

        void GenV(int vi, int ui, ref Vector3[] vertices, ref Vector2[] uvs, 
            Vector3 vertexPosition, float _w, float _h, float _f, Vector2 uv, Matrix3x3 transformMatrix)
        {
            Vector3 tangentPoint = new Vector3(_w, _h, _f);
            Vector3 localPosition = vertexPosition + transformMatrix * tangentPoint;

            vertices[vi + IndexOffset] = localPosition;
            uvs[ui + IndexOffset] = uv;
        }
        private void FillGrassMesh(int triIndexOffset, ref Vector3[] vertices, ref Vector2[] uvs, ref int[] triangles, 
            ref Vector3 root, float h, float w, float f)
        {
            int vi = 0;
            int ti = 0;
            int ui = 0;

            Matrix3x3 facingRotationMatrix = MathUtility.AngleAxis3x3(MathUtility.Rand(root) * Mathf.PI * 2, Vector3.up);
            Matrix3x3 bendRotationMatrix = MathUtility.AngleAxis3x3(MathUtility.Rand(root) * Mathf.PI * 0.5f, Vector3.left);

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
    }
}