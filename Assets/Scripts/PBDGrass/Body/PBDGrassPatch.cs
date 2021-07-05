using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PBD
{
    public struct FixedConstraintStruct
    {
        public int i0;
        public Vector3 fixedPos;

        public static int Size()
        {
            return sizeof(int) + sizeof(float) * 3;
        }
    }
    public struct DistanceConstraintStruct
    {
        public float RestLength;

        public float ElasticModulus;

        public int i0, i1;

        public static int Size()
        {
            return sizeof(float) * 2 + sizeof(int) * 2;
        }
    }
    public struct PBDGrassBodyStruct
    {
        public Vector3[] Positions;
        public Vector3[] Predicted;
        public Vector3[] Velocities;
        public Vector3[] OriginPos;
        public Vector3[] Offset;

        public int IndexOffset;

        public FixedConstraintStruct[] Fcons;
        public DistanceConstraintStruct[] Dcons;
    }

    public struct SphereCollisionStruct
    {
        public Vector3 Position;
        public float Radius;
    };

    public struct GrassGridStruct
    {
        public Vector3 Pos;
        public int IdOffset;
    }

    public class PBDGrassPatch
    {
        public int SEGMENTS {get; private set;}
        const float GrassHeight = 0.5f;
        const float GrassWidth = 0.03f;
        const float GrassForward = 0.38f;
        const float GrassMass = 1.0f;

        public PBDGrassBody[] Bodies;

        public Vector3 Root { get; private set; }
        public Mesh PatchMesh { get; private set; }
        //public SpaceHash Hash;

        public Vector3[] vertices;

        public int Width;
        public int Length;

        public PBDGrassPatch(Vector3 root, int width, int length, int points, int segments)
        {
            this.SEGMENTS = segments;
            this.Root = root;
            this.Width = width;
            this.Length = length;
            this.Bodies = new PBDGrassBody[points];
            this.vertices = new Vector3[points * (SEGMENTS * 2 + 1)];

            float xOffset = 0.5f * width;
            float maxX = root.x + xOffset;
            float minX = root.x - xOffset;
            float zOffset = 0.5f * length;
            float maxZ = root.z + zOffset;
            float minZ = root.z - zOffset;

            int indexOffset = 0;
            int offsetIncreasment = SEGMENTS * 2 + 1;
            int triIndexOffset = 0;
            int triIndexOffsetIncreasment = (SEGMENTS * 2 - 1) * 3;

            Vector2[] uvs = new Vector2[points * (2 * SEGMENTS + 1)];
            int[] triangles = new int[points * ((SEGMENTS * 2 - 1) * 3)];

            for (int i = 0; i < points; ++i)
            {
                float newX = Random.Range(minX, maxX);
                float newZ = Random.Range(minZ, maxZ);

                Bodies[i] = new PBDGrassBody(ref vertices, ref uvs, ref triangles, indexOffset, triIndexOffset,
                    new Vector3(newX, 0, newZ), 
                    SEGMENTS, GrassHeight, GrassWidth, GrassForward, GrassMass);

                indexOffset += offsetIncreasment;
                triIndexOffset += triIndexOffsetIncreasment;
            }

            //Hash = new SpaceHash(root, width, length, ref Bodies);

            PatchMesh = new Mesh();
            if(vertices.Length >= 65536 )
                PatchMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            PatchMesh.vertices = vertices;
            PatchMesh.uv = uvs;
            PatchMesh.triangles = triangles;
            PatchMesh.RecalculateNormals();
        }

        public PBDGrassPatch(Vector3 root, int len, int pointsPerGrid)
        {
            int points = len * len * pointsPerGrid;
            this.SEGMENTS = 3;
            this.Root = root;
            this.Width = len;
            this.Length = len;
            this.Bodies = new PBDGrassBody[points];
            this.vertices = new Vector3[points * (SEGMENTS * 2 + 1)];

            int indexOffset = 0;
            int offsetIncreasment = SEGMENTS * 2 + 1;
            int triIndexOffset = 0;
            int triIndexOffsetIncreasment = (SEGMENTS * 2 - 1) * 3;

            Vector2[] uvs = new Vector2[points * (2 * SEGMENTS + 1)];
            int[] triangles = new int[points * ((SEGMENTS * 2 - 1) * 3)];

            // from left to right, from top to bottom
            float startCenterX = Root.x - len / 2 + 0.5f;
            float startCenterY = Root.y + len / 2 - 0.5f;

            int bodyID = 0;
            for (int i = 0; i < len; i++)
            {
                for (int j = 0; j < len; j++)
                {
                    Vector2 newCenter = new Vector2(startCenterX + j, startCenterY - i);
                    float maxX = newCenter.x + 0.5f;
                    float minX = newCenter.x - 0.5f;
                    float maxZ = newCenter.y + 0.5f;
                    float minZ = newCenter.y - 0.5f;

                    for (int k = 0; k < pointsPerGrid; k++)
                    {
                        float newX = Random.Range(minX, maxX);
                        float newZ = Random.Range(minZ, maxZ);

                        Bodies[bodyID] = new PBDGrassBody(ref vertices, ref uvs, ref triangles, indexOffset, triIndexOffset,
                            new Vector3(newX, 0, newZ),
                            SEGMENTS, GrassHeight, GrassWidth, GrassForward, GrassMass);
                        bodyID++;
                        indexOffset += offsetIncreasment;
                        triIndexOffset += triIndexOffsetIncreasment;
                    }
                }
            }

            //Hash = new SpaceHash(root, width, length, ref Bodies);

            PatchMesh = new Mesh();
            if (vertices.Length >= 65536)
                PatchMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            PatchMesh.vertices = vertices;
            PatchMesh.uv = uvs;
            PatchMesh.triangles = triangles;
            PatchMesh.RecalculateNormals();
        }

        public PBDGrassPatch(Vector3 root, int width, int length, int points, Vector2[] pointsList, int segments = 3)
        {
            this.SEGMENTS = segments;
            this.Root = root;
            this.Width = width;
            this.Length = length;
            this.Bodies = new PBDGrassBody[points];
            this.vertices = new Vector3[points * (SEGMENTS * 2 + 1)];

            int indexOffset = 0;
            int offsetIncreasment = SEGMENTS * 2 + 1;
            int triIndexOffset = 0;
            int triIndexOffsetIncreasment = (SEGMENTS * 2 - 1) * 3;

            Vector2[] uvs = new Vector2[points * (2 * SEGMENTS + 1)];
            int[] triangles = new int[points * ((SEGMENTS * 2 - 1) * 3)];

            for (int i = 0; i < points; ++i)
            {
                float newX = pointsList[i].x;
                float newZ = pointsList[i].y;

                Bodies[i] = new PBDGrassBody(ref vertices, ref uvs, ref triangles, indexOffset, triIndexOffset,
                    new Vector3(newX, 0, newZ),
                    SEGMENTS, GrassHeight, GrassWidth, GrassForward, GrassMass);

                indexOffset += offsetIncreasment;
                triIndexOffset += triIndexOffsetIncreasment;
            }

            //Hash = new SpaceHash(root, width, length, ref Bodies);

            PatchMesh = new Mesh();
            PatchMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            PatchMesh.vertices = vertices;
            PatchMesh.uv = uvs;
            PatchMesh.triangles = triangles;
            PatchMesh.RecalculateNormals();
        }

        public PBDGrassPatch(PBDGrassPatch origin)
        {
            this.SEGMENTS = origin.SEGMENTS;
            this.Root = origin.Root;
            this.Width = origin.Width;
            this.Length = origin.Length;

            this.vertices = new Vector3[origin.vertices.Length];
            origin.vertices.CopyTo(this.vertices, 0);

            this.Bodies = new PBDGrassBody[origin.Bodies.Length];
            for (int i = 0; i < Bodies.Length; i++)
            {
                Bodies[i] = new PBDGrassBody(origin.Bodies[i]);
            }

            PatchMesh = new Mesh();
            PatchMesh.vertices = origin.PatchMesh.vertices;
            PatchMesh.normals = origin.PatchMesh.normals;
            PatchMesh.uv = origin.PatchMesh.uv;
            PatchMesh.triangles = origin.PatchMesh.triangles;
            PatchMesh.tangents = origin.PatchMesh.tangents;
        }

        public void Transform(Matrix4x4 TRS)
        {
            for (int i = 0; i < Bodies.Length; i++)
            {
                Bodies[i].Transform(TRS);
            }

            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = TRS.MultiplyPoint3x4(vertices[i]);
            PatchMesh.vertices = vertices;
            PatchMesh.RecalculateNormals();
            PatchMesh.bounds = new Bounds(TRS.MultiplyPoint3x4(PatchMesh.bounds.center), Vector3.one);
        }

        public void UpdateMesh()
        {
            foreach (PBDGrassBody body in Bodies)
            {
                body.UpdateMesh(ref vertices);
            }

            PatchMesh.vertices = vertices;
        }

        /*public List<PBDGrassBody> QueryNearBodies(Vector3 pos)
        {
            return Hash.QueryPossibleBones(pos);
        }*/

        public Vector3[] GenPositionArray()
        {
            Vector3[] pos = new Vector3[Bodies.Length * Bodies[0].Positions.Length];
            for (int i = 0; i < Bodies.Length; i++)
                for (int j = 0; j < Bodies[i].Positions.Length; j++)
                    pos[i * Bodies[i].Positions.Length + j] = Bodies[i].Positions[j];
            return pos;
        }

        public Vector3[] GenPredictedArray()
        {
            Vector3[] pre = new Vector3[Bodies.Length * Bodies[0].Predicted.Length];
            for (int i = 0; i < Bodies.Length; i++)
                for (int j = 0; j < Bodies[i].Predicted.Length; j++)
                    pre[i * Bodies[i].Predicted.Length + j] = Bodies[i].Predicted[j];
            return pre;
        }

        public Vector3[] GenVelocitiesArray()
        {
            Vector3[] vel = new Vector3[Bodies.Length * Bodies[0].Velocities.Length];
            for (int i = 0; i < Bodies.Length; i++)
                for (int j = 0; j < Bodies[i].Velocities.Length; j++)
                    vel[i * Bodies[i].Velocities.Length + j] = Bodies[i].Velocities[j];
            return vel;
        }

        public Vector3[] GenOriginPosArray()
        {
            Vector3[] ori = new Vector3[Bodies.Length * Bodies[0].OriginPos.Length];
            for (int i = 0; i < Bodies.Length; i++)
                for (int j = 0; j < Bodies[i].OriginPos.Length; j++)
                    ori[i * Bodies[i].OriginPos.Length + j] = Bodies[i].OriginPos[j];
            return ori;
        }

        public Vector3[] GenOffsetArray()
        {
            Vector3[] off = new Vector3[Bodies.Length * Bodies[0].Offset.Length];
            for (int i = 0; i < Bodies.Length; i++)
                for (int j = 0; j < Bodies[i].Offset.Length; j++)
                    off[i * Bodies[i].Offset.Length + j] = Bodies[i].Offset[j];
            return off;
        }

        public FixedConstraintStruct[] GenFconsArray()
        {
            FixedConstraintStruct[] fcons = new FixedConstraintStruct[Bodies.Length * Bodies[0].Fcons.Count];
            for (int i = 0; i < Bodies.Length; i++)
                for (int j = 0; j < Bodies[i].Fcons.Count; j++)
                {
                    fcons[i * Bodies[i].Fcons.Count + j].fixedPos = Bodies[i].Fcons[j].fixedPos;
                    fcons[i * Bodies[i].Fcons.Count + j].i0 = Bodies[i].Fcons[j].i0;
                }          
            return fcons;
        }

        public DistanceConstraintStruct[] GenDconsArray()
        {
            DistanceConstraintStruct[] dcons = new DistanceConstraintStruct[Bodies.Length * Bodies[0].Dcons.Count];
            for (int i = 0; i < Bodies.Length; i++)
                for (int j = 0; j < Bodies[i].Dcons.Count; j++)
                {
                    dcons[i * Bodies[i].Dcons.Count + j].i0 = Bodies[i].Dcons[j].i0;
                    dcons[i * Bodies[i].Dcons.Count + j].i1 = Bodies[i].Dcons[j].i1;
                    dcons[i * Bodies[i].Dcons.Count + j].RestLength = Bodies[i].Dcons[j].RestLength;
                    dcons[i * Bodies[i].Dcons.Count + j].ElasticModulus = Bodies[i].Dcons[j].ElasticModulus;
                }
            return dcons;
        }

        public int[] GenIndexOffsetArray()
        {
            int[] indexOffset = new int[Bodies.Length];
            for (int i = 0; i < Bodies.Length; i++)
                indexOffset[i] = Bodies[i].IndexOffset;
            return indexOffset;
        }
    }
}