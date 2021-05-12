using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PBD
{
    public class GrassBody
    {
        const float Stiffness = 0.2f;
        public Mesh GrassMesh { get; private set; }

        public Vector3[] Positions { get; set; }
        public Vector3[] NewPositions { get; set; }
        public Vector3[] Velocities { get; set; }
        public Vector3[] OriginPos { get; set; }
        public Vector3[] Offset { get; set; }

        public List<FixedConstraint> Fcons { get; private set; }
        public List<DistanceConstraint> Dcons { get; private set; }

        public float Mass { get; set; }
        public float Height { get; private set; }
        public int Segments { get; private set; }
        public int Counts { get; private set; }

        public GrassBody(Vector3 root, int segments, float h, float w, float f, float mass)
        {
            this.Mass = mass;
            this.Segments = segments;
            this.Counts = segments + 1;

            GrassMesh = CreateGrassMesh(root, segments, h, w, f);

            InitPositionsAndConstraints(segments);
        }
        ~GrassBody()
        {
            Fcons.Clear();
            Dcons.Clear();
        }

        void InitPositionsAndConstraints(int segments)
        {
            Positions = new Vector3[segments + 1];
            NewPositions = new Vector3[segments + 1];
            Velocities = new Vector3[segments + 1];
            OriginPos = new Vector3[segments + 1];
            Offset = new Vector3[segments];

            Fcons = new List<FixedConstraint>();
            Dcons = new List<DistanceConstraint>();

            int index = 0;
            for (int i = 0; i < GrassMesh.vertexCount - 2; i+=2)
            {
                Vector3 midPoint = (GrassMesh.vertices[i] + GrassMesh.vertices[i + 1])/ 2;
                NewPositions[index] = Positions[index] = OriginPos[index] = midPoint;
                Offset[index] = (GrassMesh.vertices[i] - GrassMesh.vertices[i + 1]) / 2;
                ++index;
            }
            NewPositions[index] = Positions[index] = OriginPos[index] = GrassMesh.vertices[segments * 2];

            // pinned
            Fcons.Add(new FixedConstraint(0, this));

            // distance
            for (int i = 1; i <= segments; ++i)
            {
                Dcons.Add(new DistanceConstraint(i, i - 1, Stiffness, this));
            }
        }

        public void UpdateMesh()
        {
            Vector3[] t = GrassMesh.vertices;
            int index = 0;
            for (int i = 0; i < GrassMesh.vertexCount - 2; i+=2)
            {
                t[i] = Positions[index] + Offset[index];
                t[i + 1] = Positions[index] - Offset[index];
                ++index;
            }
            t[GrassMesh.vertexCount - 1] = Positions[index];

            GrassMesh.vertices = t;
        }

        public static Mesh CreateGrassMesh(Vector3 root, int segments, float h, float w, float f)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector2> uvs = new List<Vector2>();

            Matrix3x3 facingRotationMatrix = MathUtl.AngleAxis3x3(MathUtl.Rand(root) * Mathf.PI * 2, Vector3.up);
            Matrix3x3 bendRotationMatrix = MathUtl.AngleAxis3x3(MathUtl.Rand(root) * Mathf.PI * 0.5f, Vector3.left);

            Matrix3x3 bendFacingRotationMatrix = facingRotationMatrix * bendRotationMatrix;

            float height = h;
            float width = w;
            float forward = f;

            void GenV(Vector3 vertexPosition, float _w, float _h, float _f, Vector2 uv, Matrix3x3 transformMatrix)
            {
                Vector3 tangentPoint = new Vector3(_w, _h, _f);
                Vector3 localPosition = vertexPosition + transformMatrix * tangentPoint;

                vertices.Add(localPosition);
                uvs.Add(uv);
            }

            for (int i = 0; i < segments; i++)
            {
                float t = i / (float)segments;

                float segmentHeight = height * t;
                float segmentWidth = width * (1 - t);
                float segmentForward = Mathf.Pow(t, 2) * forward;

                if (i == 0) //2 roots
                {
                    GenV(root, -segmentWidth, segmentHeight, segmentForward, new Vector2(0, 0), facingRotationMatrix);
                    GenV(root, segmentWidth, segmentHeight, segmentForward, new Vector2(1, 0), facingRotationMatrix);
                }
                else //vertices on top
                {
                    GenV(root, -segmentWidth, segmentHeight, segmentForward, new Vector2(0, t), bendFacingRotationMatrix);
                    GenV(root, segmentWidth, segmentHeight, segmentForward, new Vector2(1, t), bendFacingRotationMatrix);
                }
            }
            GenV(root, 0, height, forward, new Vector2(0.5f, 1), bendFacingRotationMatrix);

            if (segments != 1)
            {
                for (int i = 1; i <= segments - 1; i++)
                {
                    triangles.Add(i * 2);
                    triangles.Add(i * 2 + 1);
                    triangles.Add(i * 2 - 2);
                    triangles.Add(i * 2 + 1);
                    triangles.Add(i * 2 - 1);
                    triangles.Add(i * 2 - 2);
                }
                triangles.Add(segments * 2);
                triangles.Add(segments * 2 - 1);
                triangles.Add(segments * 2 - 2);
            }
            else
            {
                triangles.Add(0);
                triangles.Add(2);
                triangles.Add(1);
            }

            Mesh mesh = new Mesh
            {
                vertices = vertices.ToArray(),
                triangles = triangles.ToArray(),
                uv = uvs.ToArray(),
                bounds = new Bounds(Vector3.zero, Vector3.one * 50.0f)
            };

            vertices.Clear();
            triangles.Clear();
            uvs.Clear();

            return mesh;
        }
    }
}