using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PBD;

public class GrassDemo : MonoBehaviour
{
    const int GrassPatchWidth = 10;
    const int GrassPatchLength = 10;
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

    private PBDSolver GEOsolver;
    private Dictionary<MyVector2Int, GrassPatchRenderer> renderers;
    private Dictionary<MyVector2Int, GeoGrassPatch> patches;
    private Mesh GroundMesh;

    private Transform camTr;

    void Start()
    {
        Application.targetFrameRate = 60;
        GroundMesh = GenGroundMesh();
        renderers = new Dictionary<MyVector2Int, GrassPatchRenderer>();
        patches = new Dictionary<MyVector2Int, GeoGrassPatch>();
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
        // geo
        foreach (var kv in patches)
        {
            kv.Value.Renderer.DrawGeoGrass();
        }
        // star billboard and billboard
        GrassPatchRenderer.DrawInstancing();
    }

    private static Mesh GenGroundMesh()
    {
        Mesh mesh = new Mesh();
        Vector3[] vertices = new Vector3[4];
        vertices[0] = new Vector3(-5, 0, 5);
        vertices[1] = new Vector3(5, 0, 5);
        vertices[2] = new Vector3(5, 0, -5);
        vertices[3] = new Vector3(-5, 0, -5);
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

        return mesh;
    }

    private void InitGrassPatchRenderer()
    {
        GrassPatchRenderer.SetMatAndMesh(GrassMaterial, StarMaterial, BillboardMaterial, GroundMaterial, GroundMesh);
        for (int i = -100; i <= 100; i += 10)
        {
            for (int j = -100; j <= 100; j += 10)
            {
                renderers.Add(new MyVector2Int(i, j), new GrassPatchRenderer(new MyVector2Int(i, j)));
            }
        }
    }

    private void UpdateDrawPatches()
    {
        foreach (KeyValuePair<MyVector2Int, GrassPatchRenderer> kv in renderers)
        {
            Vector3 rendererRoot = kv.Key.ToVector3();
            Vector3 dist = camTr.position - rendererRoot;
            if (dist.sqrMagnitude <= GeoDistance2) // should be geo
            {
                if (!kv.Value.isGeo)
                {
                    GeoGrassPatch patch = new GeoGrassPatch(rendererRoot, GrassPatchWidth, GrassPatchLength, GrassBodyCounts, kv.Value);
                    patches.Add(kv.Key, patch);
                    kv.Value.SwitchType(GrassType.Geo, patch.PatchMesh);
                    GEOsolver.AddGrassPatch(patch);
                }
            }
            else if (dist.sqrMagnitude <= StarDistance2)  // should be star
            {
                if (kv.Value.isGeo)  // geo to star
                {
                    GEOsolver.RemoveGrassPatch(patches[kv.Key]);
                    patches.Remove(kv.Key);                  
                }
                else if (kv.Value.isStar)
                {
                    
                }
                else 
                {
                    // bill to star
                }

                kv.Value.SwitchType(GrassType.StarBillboard, null);
            }
            else // should be billboard
            {
                if (kv.Value.isGeo)// geo to star
                {
                    GEOsolver.RemoveGrassPatch(patches[kv.Key]);
                    patches.Remove(kv.Key);
                }
                else if (kv.Value.isStar) // star to billboard
                {

                }
                kv.Value.SwitchType(GrassType.Billboard, null);
            }
        }
    }
}
