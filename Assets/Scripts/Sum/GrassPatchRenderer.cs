using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum GrassType
{ 
    PBD,
    GEO,
    Billboard,
    Unknown
}

public class GrassPatchRenderer
{
    public Mesh grassMesh { get; private set; }

    public GrassType DrawingType;
    public Vector3Int Root { get; private set; }

    private static Material PBDMaterial;
    private static Material GEOMaterial;
    private static Material BillboardMaterial;
    private static Mesh GroundMesh;
    private static Material GroundMaterial;

    private static List<Vector3Int> grounds;
    private static HashSet<Vector3Int> pbdGrounds;

    static uint[] args;
    private static ComputeShader groundsCullingCS;
    private static int groundsCullingCSHandler;
    private static ComputeBuffer groundsArgsBuffer;
    private static ComputeBuffer groundsPosBuffer;
    private static ComputeBuffer groundVisibleBuffer;

    public GrassPatchRenderer(Vector3Int root)
    {
        this.DrawingType = GrassType.Unknown;
        this.Root = root;

        grounds.Add(root);
    }

    public static void SetMatAndMesh(Material pbdMaterial, Material geoMaterial, Material billboardMaterial, 
        Material groundMaterial, Mesh groundMesh, ComputeShader groundCullingCS)
    {
        PBDMaterial = pbdMaterial;
        GEOMaterial = geoMaterial;
        BillboardMaterial = billboardMaterial;
        GroundMaterial = groundMaterial;
        GroundMesh = groundMesh;

        grounds = new List<Vector3Int>();
        pbdGrounds = new HashSet<Vector3Int>();

        groundsCullingCS = groundCullingCS;
    }

    public static void SubmitGroundsData()
    {
        groundsCullingCSHandler = groundsCullingCS.FindKernel("CSMain");

        args = new uint[5] { 0, 0, 0, 0, 0 };
        args[0] = (uint)GroundMesh.GetIndexCount(0);
        //args[1] = (uint)grounds.Count;
        args[1] = 0;
        args[2] = (uint)GroundMesh.GetIndexStart(0);
        args[3] = (uint)GroundMesh.GetBaseVertex(0);
        groundsArgsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        groundsArgsBuffer.SetData(args);
        groundsCullingCS.SetBuffer(groundsCullingCSHandler, "bufferWithArgs", groundsArgsBuffer);

        groundsPosBuffer = new ComputeBuffer(grounds.Count, sizeof(int) * 3);
        groundsPosBuffer.SetData(grounds.ToArray());
        groundsCullingCS.SetBuffer(groundsCullingCSHandler, "posAllBuffer", groundsPosBuffer);


        groundVisibleBuffer = new ComputeBuffer(grounds.Count, sizeof(float) * 3);
        groundsCullingCS.SetBuffer(groundsCullingCSHandler, "posVisibleBuffer", groundVisibleBuffer);
        GroundMaterial.SetBuffer("posVisibleBuffer", groundVisibleBuffer);
    }

    public static void ReleaseData()
    {
        groundsArgsBuffer.Release();
        groundsPosBuffer.Release();
}

    public void DrawPBDGrass()
    {
        if (DrawingType == GrassType.PBD && grassMesh) 
        {
            Graphics.DrawMesh(grassMesh, Matrix4x4.identity, PBDMaterial, 0);
        }
    }

    public static void DrawInstancing()
    {
        // draw all grass patches' grounds
        args[1] = 0;
        groundsArgsBuffer.SetData(args);

        groundsCullingCS.SetVector("camPos", Camera.main.transform.position);
        GroundMaterial.SetVector("camPos", Camera.main.transform.position);
        groundsCullingCS.Dispatch(groundsCullingCSHandler, grounds.Count / 256, 1, 1);

        const float BoundSize = 10000.0f;
        Graphics.DrawMeshInstancedIndirect(GroundMesh, 0, GroundMaterial,
            new Bounds(Vector3.zero, Vector3.one * BoundSize)
            , groundsArgsBuffer);
    }
}
