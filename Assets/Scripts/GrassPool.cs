using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
    private PBD.PBDGrassPatch[] patchPool3;  //64 grass 3 blade
    private PBD.PBDGrassPatch[] patchPool1;  //16 grass 1 blade

    public GrassPool()
    {
        patchPool3 = new PBD.PBDGrassPatch[COUNT];
        patchPool1 = new PBD.PBDGrassPatch[COUNT];

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
            patchPool3[i] = new PBD.PBDGrassPatch(Vector3.zero, 1, 1, grassPoints[0][0].Length, grassPoints[0][i]);
            patchPool1[i] = new PBD.PBDGrassPatch(Vector3.zero, 1, 1, grassPoints[1][0].Length, grassPoints[1][i]);
        }
    }

    public Mesh GetMeshLOD(int LOD, int type)
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
}
