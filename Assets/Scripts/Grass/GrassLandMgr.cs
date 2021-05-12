using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GrassLandMgr : MonoBehaviour
{
    [Range(1,255)]
    public int Len;
    [Range(1, 100)]
    public int GridAmount;
    [Range(1, 100)]
    public float TerrainHeightScale;
    public Texture2D HeightMap;
    public Material GrassMat;
    public Material TerrainMat;

    private Mesh gm;
    private Mesh tm;
    private GrassLand[] grids;
    private List<GameObject> terrains;
    void Start()
    {
        InitTerrain();
        InitMeshAndMat();
        DrawTerrains();
        DrawGrassland();
    }

    private void InitTerrain()
    {
        tm = new Mesh();
        tm.Clear();

        Vector3[] v3s = new Vector3[Len * Len];
        List<int> tris = new List<int>();

        float offset = Len / 2;
        for (int i = 0; i < Len; i++)
        {
            for (int j = 0; j < Len; j++)
            {
                int index = i * Len + j;
                v3s[index].x = i - offset;
                //v3s[index].y = HeightMap.GetPixel(i, j).r * TerrainHeightScale;
                v3s[index].y = 0;
                v3s[index].z = j - offset;
                if (i == 0 || j == 0)
                    continue;
                tris.Add(Len * i + j);
                tris.Add(Len * i + j - 1);
                tris.Add(Len * (i - 1) + j - 1);
                tris.Add(Len * (i - 1) + j - 1);
                tris.Add(Len * (i - 1) + j);
                tris.Add(Len * i + j);
            }
        }

        Vector2[] uvs = new Vector2[Len * Len];

        for (int i = 0; i < uvs.Length; i++)
        {
            uvs[i] = new Vector2(v3s[i].x + offset, v3s[i].z + offset);
        }

        tm.vertices = v3s;
        tm.uv = uvs;
        tm.triangles = tris.ToArray();
        tm.RecalculateNormals();
        tm.RecalculateBounds();
    }

    private void InitMeshAndMat()
    {
        gm = new Mesh();
        gm.Clear();

        Vector3[] v3s = new Vector3[Len * Len];
        int[] indices = new int[Len * Len];
        float offset = Len / 2.0f;
        for (int i = 0; i < Len; i++)
            for (int j = 0; j < Len; j++)
            {
                int index = i * Len + j;
                v3s[index].x = i - offset;
                v3s[index].y = 0;
                v3s[index].z = j - offset;
                indices[index] = index;
            }

        gm.vertices = v3s;
        gm.SetIndices(indices, MeshTopology.Points, 0);
        gm.RecalculateBounds();
    }

    private void DrawGrassland()
    {
        grids = new GrassLand[GridAmount * GridAmount];
        int index = 0;
        int center = Len / 2;
        float offset = GridAmount / 2.0f * Len;
        for (int i = 1; i <= GridAmount; i++)
        {
            for (int j = 1; j <= GridAmount; j++)
            {
                GameObject go = new GameObject("Grassland" + index.ToString());
                go.AddComponent<MeshFilter>().sharedMesh = gm;
                go.AddComponent<MeshRenderer>().material = GrassMat;
                grids[index] = new GrassLand(new Vector3((j-1)*Len + center - offset, 0, (i-1)*Len + center - offset), go);
                index++;
            }
        }
    }

    private void DrawTerrains()
    {
        terrains = new List<GameObject>();
        int index = 0;
        int center = Len / 2;
        float offset = GridAmount / 2.0f * Len;
        for (int i = 1; i <= GridAmount; i++)
        {
            for (int j = 1; j <= GridAmount; j++)
            {
                GameObject go = new GameObject("Terrain" + index.ToString());
                go.AddComponent<MeshFilter>().sharedMesh = tm;
                go.AddComponent<MeshRenderer>().material = TerrainMat;
                go.transform.position = new Vector3((j - 1) * Len + center - offset, 0, (i - 1) * Len + center - offset);
                index++;
            }
        }
    }
}
