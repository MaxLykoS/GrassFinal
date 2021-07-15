using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PBD;

public class GrassDemo : MonoBehaviour
{
    public Material GrassProcedrualMaterial;
    public List<Transform> colliders;
    public ComputeShader PBDSolverCS;
    public Vector3 WindForce;
    public Camera Cam;
    public Texture2D WindNoiseTex;

    public float Frequency;
    public Vector4 TileAndOffset;

    private PBDGrassPatchRenderer r1;

    private static ComputeShader PBDSolverCS_Static;

    void Start()
    {
        PBDSolverCS_Static = PBDSolverCS;

        PBDGrassPatchRenderer.Setup(GrassProcedrualMaterial, colliders, Cam, WindNoiseTex);

        var patch = new PBDGrassPatch(Vector3.zero, 32, 32);//256(2 millions)

        r1 = new PBDGrassPatchRenderer(patch); 

        DestroyMesh(patch.PatchMesh);
    }


    private void Update()
    {
        // pbd
        PBDGrassPatchRenderer.UpdateCollision(colliders);

        r1.SetWindForce(WindForce);
        r1.SetWindNoise(Frequency, TileAndOffset);

        r1.FixedUpdate();
        r1.Update();
    }

    private void OnDestroy()
    {
        PBDGrassPatchRenderer.ReleaseStaticData();

        r1.Release();
    }

    public static ComputeShader CreateShader()
    {
        return Instantiate(PBDSolverCS_Static);
    }

    public static void DestroyMesh(Mesh mesh)
    {
        Destroy(mesh);
    }

    public static void DestroyCS(ComputeShader cs)
    {
        Destroy(cs);
    }
}
