using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PBD;

public class GrassDemo : MonoBehaviour
{
    public Material GrassMaterial;
    public List<Transform> colliders;

    private Dictionary<Vector3Int, PBDGrassPatchRenderer> renderers;
    private PBDGrassPatchRenderer pbdRenderer1;
    private PBDGrassPatchRenderer pbdRenderer2;

    private Transform camTr;

    void Start()
    {
        Application.targetFrameRate = 60;
        renderers = new Dictionary<Vector3Int, PBDGrassPatchRenderer>();
        camTr = Camera.main.transform;
        //camTr = colliders[0].transform;

        PBDGrassPatchRenderer.Setup(GrassMaterial, colliders);

        pbdRenderer1 = new PBDGrassPatchRenderer(new Vector3Int(1, 0, 1), new PBDGrassPatch(new Vector3(1, 0, 1), 1, 1, 128));
        pbdRenderer2 = new PBDGrassPatchRenderer(new Vector3Int(-1, 0, -1), new PBDGrassPatch(new Vector3(-1, 0, -1), 1, 1, 128));
    }

    void FixedUpdate()
    {

    }


    private void Update()
    {
        // create grass around
        float maxX = camTr.position.x + 5;
        float minX = camTr.position.x - 5;
        float maxY = camTr.position.y + 5;
        float minY = camTr.position.y - 5;

        // pbd
        PBDGrassPatchRenderer.UpdateCollision(colliders);

        for (int i = 0; i < 100; i++)
        {
            pbdRenderer1.FixedUpdate();
            pbdRenderer1.Update();
        }

        //
        //PBDGrassPatchRenderer.DrawInstancing();
    }

    private static Mesh GenGroundMesh()
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

    private void OnDestroy()
    {
        PBDGrassPatchRenderer.ReleaseStaticData();
        pbdRenderer1.Release();
        pbdRenderer2.Release();
    }

    public static ComputeShader CreateShader(int index)
    {
        ComputeShader newCS = (ComputeShader)Instantiate(Resources.Load("PBDSolverCS" + index.ToString()));
        return newCS;
    }
}
