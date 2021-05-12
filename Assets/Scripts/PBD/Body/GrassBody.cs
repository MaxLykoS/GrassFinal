using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PBD
{
    public class GrassBody
    {
        const float Stiffness = 1;
        public Mesh GrassMesh { get; private set; }

        public Vector3[] Positions { get; set; }
        public Vector3[] NewPositions { get; set; }
        public Vector3[] Velocities { get; set; }
        public Vector3[] OriginPos { get; set; }

        public List<FixedConstraint> Fcons { get; private set; }
        public List<DistanceConstraint> Dcons { get; private set; }

        public float Mass { get; set; }
        public float Height { get; private set; }

        public GrassBody(Vector3 root, int segments, float h, float w, float f, float mass)
        {
            this.Mass = mass;

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
            Positions = new Vector3[segments * 2 + 1];
            NewPositions = new Vector3[segments * 2 + 1];
            Velocities = new Vector3[segments * 2 + 1];
            OriginPos = new Vector3[segments * 2 + 1];

            Fcons = new List<FixedConstraint>();
            Dcons = new List<DistanceConstraint>();

            for (int i = 0; i < GrassMesh.vertexCount; i++)
            {
                NewPositions[i].x = Positions[i].x = OriginPos[i].x = GrassMesh.vertices[i].x;
                NewPositions[i].y = Positions[i].y = OriginPos[i].y = GrassMesh.vertices[i].y;
                NewPositions[i].z = Positions[i].z = OriginPos[i].z = GrassMesh.vertices[i].z;
            }

            // pinned
            Fcons.Add(new FixedConstraint(0, this));
            Fcons.Add(new FixedConstraint(1, this));

            // distance
            for (int i = 2; i < GrassMesh.vertexCount - 2; i += 2)
            {
                Dcons.Add(new DistanceConstraint(i - 2, i, Stiffness, this));
                Dcons.Add(new DistanceConstraint(i + 1, i - 1, Stiffness, this));
            }
            if (segments != 1)
            {
                for (int i = 2; i < GrassMesh.vertexCount - 2; i += 2)
                {
                    Dcons.Add(new DistanceConstraint(i, i + 1, Stiffness, this)); // middle
                }
            }
            Dcons.Add(new DistanceConstraint(GrassMesh.vertexCount - 1, GrassMesh.vertexCount - 3, Stiffness, this));
            Dcons.Add(new DistanceConstraint(GrassMesh.vertexCount - 1, GrassMesh.vertexCount - 2, Stiffness, this));
        }

        public void UpdateMesh()
        {
            GrassMesh.vertices = Positions;
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