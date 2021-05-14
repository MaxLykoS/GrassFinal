using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PBD
{
    public class GrassPatch
    {
        const int SEGMENTS = 3;
        const float GrassHeight = 0.5f;
        const float GrassWidth = 0.05f;
        const float GrassForward = 0.38f;
        const float GrassMass = 1.0f;

        public GrassBody[] Bodies;

        public Mesh PatchMesh { get; private set; }
        //public SpaceHash Hash;

        private Vector3[] vertices;

        public GrassPatch(Vector3 root, int width, int length, int points)
        {
            Bodies = new GrassBody[points];
            vertices = new Vector3[points * (SEGMENTS * 2 + 1)];

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

                Bodies[i] = new GrassBody(ref vertices, ref uvs, ref triangles, indexOffset, triIndexOffset,
                    new Vector3(newX, 0, newZ), 
                    SEGMENTS, GrassHeight, GrassWidth, GrassForward, GrassMass);

                indexOffset += offsetIncreasment;
                triIndexOffset += triIndexOffsetIncreasment;
            }

            //Hash = new SpaceHash(root, width, length, ref Bodies);

            PatchMesh = new Mesh()
            {
                vertices = vertices,
                uv = uvs,
                triangles = triangles
            };
        }

        public void UpdateMesh()
        {
            foreach (GrassBody body in Bodies)
            {
                body.UpdateMesh(ref vertices);
            }

            PatchMesh.vertices = vertices;
        }
    }
}