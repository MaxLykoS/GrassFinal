using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LineGrassTest : MonoBehaviour
{
    const int SEGMENTS = 3;
    const float GrassHeight = 0.5f;
    const float GrassWidth = 0.05f;
    const float GrassForward = 0.38f;

    public Material lineGrassMat;
    private Mesh lineGrass;
    void Start()
    {
        lineGrass = CreateGrassLineMesh(Vector3.zero, SEGMENTS, GrassHeight, GrassWidth, GrassForward);
    }


    void Update()
    {
        Graphics.DrawMesh(lineGrass, Matrix4x4.identity, lineGrassMat, 0);
    }

    public static Mesh CreateGrassMesh(Vector3 root, int segments, float h, float w, float f)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Vector2> uvs = new List<Vector2>();

        Matrix3x3 facingRotationMatrix = MathUtility.AngleAxis3x3(MathUtility.Rand(root) * Mathf.PI * 2, Vector3.up);
        Matrix3x3 bendRotationMatrix = MathUtility.AngleAxis3x3(MathUtility.Rand(root) * Mathf.PI * 0.5f, Vector3.left);

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

    public static Mesh CreateGrassLineMesh(Vector3 root, int segments, float h, float w, float f)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<int> lines = new List<int>();
        List<Vector2> uvs = new List<Vector2>();

        Matrix3x3 facingRotationMatrix = MathUtility.AngleAxis3x3(MathUtility.Rand(root) * Mathf.PI * 2, Vector3.up);
        Matrix3x3 bendRotationMatrix = MathUtility.AngleAxis3x3(MathUtility.Rand(root) * Mathf.PI * 0.5f, Vector3.left);

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
                GenV(root, 0, segmentHeight, segmentForward, new Vector2(0, 0), facingRotationMatrix);
            }
            else //vertices on top
            {
                GenV(root, 0, segmentHeight, segmentForward, new Vector2(0, t), bendFacingRotationMatrix);
            }
        }
        GenV(root, 0, height, forward, new Vector2(0.5f, 1), bendFacingRotationMatrix);

        for (int i = 0; i < segments; i++)
        {
            lines.Add(i);
            lines.Add(i + 1);
        }

        Mesh mesh = new Mesh
        {
            vertices = vertices.ToArray(),
            uv = uvs.ToArray(),
            bounds = new Bounds(Vector3.zero, Vector3.one * 50.0f)
        };
        mesh.SetIndices(lines.ToArray(), MeshTopology.Lines, 0);

        vertices.Clear();
        lines.Clear();
        uvs.Clear();

        return mesh;
    }
}
