using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PBD;

public class GrassDemo : MonoBehaviour
{
    public Material GrassMaterial;
    public List<Transform> colliders;
    public ComputeShader PBDSolverCS;

    //private Dictionary<Vector3Int, PBDGrassPatchRenderer> renderers;
    private PBDGrassPatchRenderer p1;

    private Transform camTr;
    private static ComputeShader PBDSolverCS_Static;

    void Start()
    {
        Application.targetFrameRate = 60;
        //renderers = new Dictionary<Vector3Int, PBDGrassPatchRenderer>();
        //camTr = Camera.main.transform;
        rToDestroy = new List<Vector3Int>();
        PBDSolverCS_Static = PBDSolverCS;
        camTr = colliders[0].transform;

        PBDGrassPatchRenderer.Setup(GrassMaterial, colliders);

        p1 = new PBDGrassPatchRenderer(Vector3Int.zero, new PBDGrassPatch(Vector3Int.zero, 1, 1, 1));
    }

    private List<Vector3Int> rToDestroy;
    private void Update()
    {
        // pbd
        PBDGrassPatchRenderer.UpdateCollision(colliders);

        p1.FixedUpdate();
        p1.Update();

        /*
        // create PBDgrass around
        int maxX = (int)camTr.position.x + 5;
        int minX = (int)camTr.position.x - 5;
        int maxZ = (int)camTr.position.z + 5;
        int minZ = (int)camTr.position.z - 5;

        foreach (Vector3Int r in renderers.Keys)
        {
            if (Vector3.Distance(r, camTr.position) > 5 * Mathf.Sqrt(2))
            {
                rToDestroy.Add(r);
            }
        }
        for (int i = 0; i < rToDestroy.Count; i++)
        {
            PBDGrassPatchRenderer _r;
            renderers.TryGetValue(rToDestroy[i], out _r);
            _r.Release();
            renderers.Remove(rToDestroy[i]);
        }
        rToDestroy.Clear();

        for (int i = minX; i <= maxX; i++)
        {
            for (int j = minZ; j <= maxZ; j++)
            {
                Vector3Int pos = new Vector3Int(i, 0, j);
                PBDGrassPatchRenderer r;
                if (renderers.TryGetValue(pos, out r))
                {
                    r.FixedUpdate();
                    r.Update();
                }
                else if (Vector3.Distance(pos, camTr.position) <= 5 * Mathf.Sqrt(2))
                {
                    r = new PBDGrassPatchRenderer(pos, new PBDGrassPatch(pos, 1, 1, 1));
                    renderers.Add(pos, r);
                    r.FixedUpdate();
                    r.Update();
                }
            }
        }*/
    }

    private void OnDestroy()
    {
        PBDGrassPatchRenderer.ReleaseStaticData();
        /*foreach (PBDGrassPatchRenderer r in renderers.Values)
            r.Release();*/
        p1.Release();
    }

    public static ComputeShader CreateShader()
    {
        ComputeShader newCS = Instantiate(PBDSolverCS_Static);
        return newCS;
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
