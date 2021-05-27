using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PBD;

public class GrassDemo : MonoBehaviour
{
    const int GrassPatchWidth = 1;
    const int GrassPatchLength = 1;
    const int GrassBodyCounts = 1;
    const float CollisionRadius = 1.0f;

    //const int MapSize = 30;

    const float GeoDistance2 = 5*5*2;
    const float StarDistance2 = 21 * 21 * 2;

    Vector3 WindForce = Vector3.up * 1000;

    public Material GrassMaterial;
    public Material StarMaterial;
    public Material BillboardMaterial;
    public Material GroundMaterial;
    public List<Transform> colliders;
    public ComputeShader groundsCullingCS;

    private PBDSolver GEOsolver;
    private Dictionary<Vector3Int, GrassPatchRenderer> renderers;
    private Dictionary<Vector3Int, PBDGrassPatch> patches;

    private Transform camTr;

    void Start()
    {
        Application.targetFrameRate = 60;
        renderers = new Dictionary<Vector3Int, GrassPatchRenderer>();
        patches = new Dictionary<Vector3Int, PBDGrassPatch>();
        //camTr = Camera.main.transform;
        camTr = colliders[0].transform;

        GEOsolver = new PBDSolver(3.0f)
        {
            Gravity = Vector3.down * 9.8f,
            WindForce = WindForce
        };

        InitGrassPatchRenderer();

        foreach (Transform tr in colliders)
        {
            GEOsolver.AddCollider(new SphereCollision(tr, CollisionRadius));
        }
    }

    void FixedUpdate()
    {
        UpdateDrawPatches();

        GEOsolver.Update((float)(1.0 / 60.0 / 3.0));
    }


    private void Update()
    {
        // pbd

        //
        GrassPatchRenderer.DrawInstancing();
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

    private void InitGrassPatchRenderer()
    {
        GrassPatchRenderer.SetMatAndMesh(GrassMaterial, StarMaterial, BillboardMaterial, GroundMaterial, GenGroundMesh(), groundsCullingCS);
        for (int i = 0; i < 256; i++)
        {
            for (int j = 0; j < 256; j++)
            {
                renderers.Add(new Vector3Int(i, 0, j), new GrassPatchRenderer(new Vector3Int(i, 0, j)));
            }
        }

        GrassPatchRenderer.SubmitGroundsData();
    }

    private void UpdateDrawPatches()
    {
        
    }

    private void OnDestroy()
    {
        GrassPatchRenderer.ReleaseData();
    }
}
