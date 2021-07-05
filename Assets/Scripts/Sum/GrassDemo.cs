using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PBD;

/*
 * public class PBDGrassInfo
{
    const float TIME = 5.0f;
    public PBDGrassPatchRenderer renderer;
    public float TimeToDie;

    public PBDGrassInfo(PBDGrassPatchRenderer renderer)
    {
        this.renderer = renderer;
        this.TimeToDie = TIME;
    }

    public bool CheckTime()
    {
        return TimeToDie <= 0;
    }

    public void UpdateTimer()
    {
        TimeToDie -= Time.fixedDeltaTime;
    }

    public void ResetTimer()
    {
        TimeToDie = TIME;
    }

    public void Render()
    {
        renderer.FixedUpdate();
        renderer.Update();
    }

    public void Release()
    {
        renderer.Release();
    }
}
*/

public class GrassDemo : MonoBehaviour
{
    public int TEST;
    public Material GrassProcedrualMaterial;
    public List<Transform> colliders;
    public ComputeShader PBDSolverCS;

    //private Dictionary<Vector3Int, PBDGrassInfo> renderers;
    private PBDGrassPatchRenderer r1;

    private static ComputeShader PBDSolverCS_Static;

    void Start()
    {
        //Application.targetFrameRate = 60;
        //rToDestroy = new List<Vector3Int>();
        PBDSolverCS_Static = PBDSolverCS;

        PBDGrassPatchRenderer.Setup(GrassProcedrualMaterial, colliders);

        //r1 = new PBDGrassPatchRenderer(new PBDGrassPatch(Vector3.zero, 1, 1, 4, 3));
        r1 = new PBDGrassPatchRenderer(new PBDGrassPatch(Vector3.zero, 32, 32));
        //renderers = new Dictionary<Vector3Int, PBDGrassInfo>();
    }


    private void Update()
    {
        // pbd
        PBDGrassPatchRenderer.UpdateCollision(colliders);

        r1.FixedUpdate();
        r1.Update();

        //UpdateRenderers();
    }

    /*
    private List<Vector3Int> rToDestroy;
    void UpdateRenderers()
    {
        #region 更新周围四个格子
        for (int i = 0; i < colliders.Count; i++)
        {
            int maxX = Mathf.CeilToInt(colliders[i].position.x);
            int minX = Mathf.FloorToInt(colliders[i].position.x);
            int maxZ = Mathf.CeilToInt(colliders[i].position.z);
            int minZ = Mathf.FloorToInt(colliders[i].position.z);

            Vector3Int[] nearest = new Vector3Int[4];
            nearest[0] = new Vector3Int(maxX, 0, maxZ);
            nearest[1] = new Vector3Int(maxX, 0, minZ);
            nearest[2] = new Vector3Int(minX, 0, maxZ);
            nearest[3] = new Vector3Int(maxX, 0, minZ);

            for (int j = 0; j < nearest.Length; j++)
            {
                // nearest grid collision occurs
                if (Vector3.Distance(nearest[j], colliders[i].position) <= Mathf.Sqrt(0.5f * 0.5f * 2) + 0.00001f)
                {
                    if (!renderers.ContainsKey(nearest[j]))
                    {
                        PBDGrassPatchRenderer r = new PBDGrassPatchRenderer(
                            GrassPool.Instance.GetPBDPatchLOD(0, 1, Matrix4x4.TRS(nearest[j], Quaternion.identity, Vector3.one)));
                        renderers.Add(nearest[j], new PBDGrassInfo(r));
                    }
                    else
                    {
                        renderers[nearest[j]].ResetTimer();
                    }
                }
            }
        }
        #endregion

        #region 检查TOD
        foreach (Vector3Int key in renderers.Keys)
        {
            if (renderers[key].CheckTime())
            {
                rToDestroy.Add(key);
            }
        }
        #endregion

        #region 删除休眠草
        for (int i = 0; i < rToDestroy.Count; i++)
        {
            renderers[rToDestroy[i]].Release();
            renderers.Remove(rToDestroy[i]);
        }
        rToDestroy.Clear();
        #endregion

        #region 模拟和渲染
        foreach (PBDGrassInfo info in renderers.Values)
        {
            info.UpdateTimer();
            info.Render();
        }
        #endregion
    }
    */

    private void OnDestroy()
    {
        PBDGrassPatchRenderer.ReleaseStaticData();
        /*foreach (PBDGrassInfo r in renderers.Values)
            r.Release();
        renderers.Clear();*/

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
