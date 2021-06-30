using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PBD;

public struct PBDGrassInfo
{
    public PBDGrassPatchRenderer renderer;
    public float TimeToDie;

    public PBDGrassInfo(PBDGrassPatchRenderer renderer, float timeToDie)
    {
        this.renderer = renderer;
        this.TimeToDie = timeToDie;
    }

    public void Release()
    {
        renderer.Release();
    }
}

public class GrassDemo : MonoBehaviour
{
    public Material GrassMaterial;
    public List<Transform> colliders;
    public ComputeShader PBDSolverCSLOD0;
    public ComputeShader PBDSolverCSLOD1;

    private Dictionary<Vector3Int, PBDGrassPatchRenderer> renderers;
    //private PBDGrassPatchRenderer p1;

    private Transform camTr;
    private static ComputeShader PBDSolverCS_StaticLOD0;
    private static ComputeShader PBDSolverCS_StaticLOD1;

    void Start()
    {
        Application.targetFrameRate = 60;
        renderers = new Dictionary<Vector3Int, PBDGrassPatchRenderer>();
        camTr = Camera.main.transform;
        rToDestroy = new List<Vector3Int>();
        PBDSolverCS_StaticLOD0 = PBDSolverCSLOD0;
        PBDSolverCS_StaticLOD1 = PBDSolverCSLOD1;
        //camTr = colliders[0].transform;

        PBDGrassPatchRenderer.Setup(GrassMaterial, colliders);

        /*p1 = new PBDGrassPatchRenderer(GrassPool.Instance.GetPBDPatchLOD(0, 0, 
            Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one)), 0);*/
    }

    private List<Vector3Int> rToDestroy;
    private void Update()
    {
        // pbd
        PBDGrassPatchRenderer.UpdateCollision(colliders);

        for (int i = 0; i < colliders.Count; i++)
        {
            int maxX = Mathf.CeilToInt(colliders[i].position.x);
            int minX = Mathf.FloorToInt(colliders[i].position.x);
            int maxZ = Mathf.CeilToInt(colliders[i].position.z);
            int minZ = Mathf.FloorToInt(colliders[i].position.z);

            Vector3[] nearest = new Vector3[4];
            nearest[0] = new Vector3(maxX, 0, maxZ);
            nearest[1] = new Vector3(maxX, 0, minZ);
            nearest[2] = new Vector3(minX, 0, maxZ);
            nearest[3] = new Vector3(maxX, 0, minZ);

            for (int j = 0; j < nearest.Length; j++)
            {
                if (Vector3.Distance(nearest[j], colliders[i].position) <= Mathf.Sqrt(0.5f * 0.5f * 2) + 0.00001f)
                { 
                    // nearest grid collision occurs

                }
            }
        }

        /*p1.FixedUpdate();
        p1.Update();*/
    }

    private void OnDestroy()
    {
        PBDGrassPatchRenderer.ReleaseStaticData();
        /*foreach (PBDGrassPatchRenderer r in renderers.Values)
            r.Release();*/
    }

    public static ComputeShader CreateShader(int LOD)
    {
        switch (LOD)
        {
            case 0: return Instantiate(PBDSolverCS_StaticLOD0);
            case 1: return Instantiate(PBDSolverCS_StaticLOD1);
            default: throw new System.Exception("Unknown LOD");
        }
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
