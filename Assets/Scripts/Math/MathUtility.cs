using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class MathUtility
{
    public static float Rand(Vector3 co)
    {
        float r = Mathf.Sin(Vector3.Dot(co, new Vector3(12.9898f, 78.233f, 53.539f))) * 43758.5453f;
        return r - (int)r;
    }
    public static Matrix3x3 AngleAxis3x3(float angle, Vector3 axis)
    {
        float c, s;
        s = Mathf.Sin(angle);
        c = Mathf.Cos(angle);

        float t = 1 - c;
        float x = axis.x;
        float y = axis.y;
        float z = axis.z;

        return new Matrix3x3(
            t * x * x + c, t * x * y - s * z, t * x * z + s * y,
            t * x * y + s * z, t * y * y + c, t * y * z - s * x,
            t * x * z - s * y, t * y * z + s * x, t * z * z + c
        );
    }

    public static int Clamp(int value, int min, int max)
    {
        if (value >= max)
            value = max;
        else if (value <= min)
            value = min;
        return value;
    }

    public static string V2S(ref Vector3 v)
    {
        return string.Format("{0} {1} {2}", v.x, v.y, v.z);
    }
    public static string V2S(Vector3 v)
    {
        return string.Format("{0} {1} {2}", v.x, v.y, v.z);
    }

    private static Mesh Gen1X1GroundMesh()
    {
        Mesh mesh = new Mesh();
        Vector3[] vertices = new Vector3[4];
        vertices[0] = new Vector3(-0.5f, 0, 0.5f);
        vertices[1] = new Vector3(0.5f, 0, 0.5f);
        vertices[2] = new Vector3(0.5f, 0, -0.5f);
        vertices[3] = new Vector3(-0.5f, 0, -0.5f);
        mesh.vertices = vertices;

        Vector2[] uvs = new Vector2[4];
        uvs[0] = new Vector2(0, 1);
        uvs[1] = new Vector2(1, 1);
        uvs[2] = new Vector2(1, 0);
        uvs[3] = new Vector2(0, 0);
        mesh.uv = uvs;

        Vector3[] normals = new Vector3[4];
        normals[0] = normals[1] = normals[2] = normals[3] = Vector3.up;
        mesh.normals = normals;

        int[] indexs = new int[6];
        indexs[0] = 0; indexs[1] = 1; indexs[2] = 2;
        indexs[4] = 0; indexs[4] = 2; indexs[5] = 3;
        mesh.triangles = indexs;

        mesh.RecalculateBounds();

        return mesh;
    }
}
