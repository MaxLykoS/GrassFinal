using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GrassID
{
    public float yRotation;
    public int Type;

    public GrassID(float yRot, int type)
    {
        this.yRotation = yRot;
        this.Type = type;
    }
}

public class GrassPool
{
    public static GrassPool Instance
    {
        get 
        { 
            if (instance == null)
            {
                instance = new GrassPool();
            }
            return instance;
        }
    }
    private static GrassPool instance;


    // 8 kinds in total
    const int COUNT = 8;
    const int LOD0BLADES = 64;
    const int LOD1BLADES = 32;
    public const int LENGTH = 256;
    private PBD.PBDGrassPatch[] patchPool3;  //64 grass 3 blade
    private PBD.PBDGrassPatch[] patchPool1;  //16 grass 1 blade

    private Dictionary<Vector2Int, GrassID> grassGrids;

    public GrassPool()
    {
        grassGrids = new Dictionary<Vector2Int, GrassID>();

        patchPool3 = new PBD.PBDGrassPatch[COUNT];
        patchPool1 = new PBD.PBDGrassPatch[COUNT];

        FillGrassPos();
        FillGrassPool(GenPointsLists(Vector3.zero, 1, 1));
    }

    // 0-3 1-1
    Vector2[][][] GenPointsLists(Vector3 root, int width, int length)
    {
        Vector2[][][] grassPoints = new Vector2[2][][];

        for (int i = 0; i < 2; i++)
            grassPoints[i] = new Vector2[COUNT][];

        for (int i = 0; i < COUNT; i++)
        {
            grassPoints[0][i] = new Vector2[LOD0BLADES];
            grassPoints[1][i] = new Vector2[LOD1BLADES];
        }

        float xOffset = 0.5f * width;
        float maxX = root.x + xOffset;
        float minX = root.x - xOffset;
        float zOffset = 0.5f * length;
        float maxZ = root.z + zOffset;
        float minZ = root.z - zOffset;

        for (int i = 0; i < COUNT; i++)
        {
            for (int j = 0; j < LOD0BLADES; j++)
            {
                float newX = Random.Range(minX, maxX);
                float newZ = Random.Range(minZ, maxZ);

                grassPoints[0][i][j].x = newX;
                grassPoints[0][i][j].y = newZ;
            }

            for (int j = 0; j < LOD1BLADES; j++)
            {
                float newX = Random.Range(minX, maxX);
                float newZ = Random.Range(minZ, maxZ);

                grassPoints[1][i][j].x = newX;
                grassPoints[1][i][j].y = newZ;
            }
        }

        return grassPoints;
    }

    void FillGrassPool(Vector2[][][] grassPoints)
    {
        for (int i = 0; i < COUNT; i++)
        {
            patchPool3[i] = new PBD.PBDGrassPatch(Vector3.zero, 1, 1, grassPoints[0][0].Length, grassPoints[0][i], 3);
            patchPool1[i] = new PBD.PBDGrassPatch(Vector3.zero, 1, 1, grassPoints[1][0].Length, grassPoints[1][i], 1);
        }
    }

    public Mesh GetMeshLOD(int LOD, int type = 0)
    {
        switch (LOD)
        {
            case 0: return patchPool3[type].PatchMesh;
            case 1: return patchPool1[type].PatchMesh;
            default:throw new System.Exception("Unknown grass type");
        }
    }

    public PBD.PBDGrassPatch GetPBDPatchLOD(int LOD, int type, Matrix4x4 TRS)
    {
        PBD.PBDGrassPatch newPatch;
        switch (LOD)
        {
            case 0: newPatch = new PBD.PBDGrassPatch(patchPool3[type]);break;
            case 1: newPatch = new PBD.PBDGrassPatch(patchPool1[type]);break;
            default:throw new System.Exception("Unkown grass type");
        }
        newPatch.Transform(TRS);
        return newPatch;
    }

    void FillGrassPos()
    {
        for (int i = 0; i < LENGTH; i++)
            for (int j = 0; j < LENGTH; j++)
            {
                grassGrids[new Vector2Int(i, j)] = new GrassID(Random.Range(0, 360), Random.Range(0, 8));
            }
    }

    public GrassInfo[] GetGrassPosBuffer()
    {
        GrassInfo[] posBuffer = new GrassInfo[LENGTH * LENGTH];
        int id = 0;
        foreach (Vector2Int v in grassGrids.Keys)
        {
            var value = grassGrids[v];
            posBuffer[id] = new GrassInfo(
                Matrix4x4.TRS(new Vector3(v.x, 0, v.y), Quaternion.Euler(0, value.yRotation, 0), Vector3.one)
                ,value.Type);
            id++;
        }
        return posBuffer;
    }

    public Vector3[] GetGrassPoolBuffer(int LOD)
    {
        Vector3[] poolBuffer;
        PBD.PBDGrassPatch[] targetPatchPool = LOD == 0 ? patchPool3 : patchPool1;

        poolBuffer = new Vector3[COUNT * targetPatchPool[0].PatchMesh.vertexCount];
        int id = 0;
        for (int i = 0; i < targetPatchPool.Length; i++)
        {
            for (int j = 0; j < targetPatchPool[i].PatchMesh.vertexCount; j++)
            {
                poolBuffer[id] = targetPatchPool[i].PatchMesh.vertices[j];
                ++id;
            }
        }
        return poolBuffer;
    }

    public int GetPoolStride(int LOD)
    {
        switch (LOD)
        {
            case 0 : return patchPool3[0].PatchMesh.vertexCount;
            case 1 : return patchPool1[0].PatchMesh.vertexCount;
            default: throw new System.Exception("Unknown LOD");
        }
    }

    public uint GetIndexCountLOD(int LOD)
    {
        switch (LOD)
        {
            case 0: return patchPool3[0].PatchMesh.GetIndexCount(0);
            case 1: return patchPool1[0].PatchMesh.GetIndexCount(0);
            default: throw new System.Exception("Unknown LOD");
        }
    }

    public uint GetIndexStartLOD(int LOD)
    {
        switch (LOD)
        {
            case 0: return patchPool3[0].PatchMesh.GetIndexStart(0);
            case 1: return patchPool1[0].PatchMesh.GetIndexStart(0);
            default: throw new System.Exception("Unknown LOD");
        }
    }

    public uint GetBaseVertexLOD(int LOD)
    {
        switch (LOD)
        {
            case 0: return patchPool3[0].PatchMesh.GetBaseVertex(0);
            case 1: return patchPool1[0].PatchMesh.GetBaseVertex(0);
            default: throw new System.Exception("Unknown LOD");
        }
    }
}
