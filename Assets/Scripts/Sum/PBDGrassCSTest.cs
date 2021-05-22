using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PBD;
using UnityEngine.Rendering;

public struct FixedConstraintStruct
{
    public int i0;
    public Vector3 fixedPos;

    public static int Size()
    {
        return sizeof(int) + sizeof(float) * 3;
    }
}
public struct DistanceConstraintStruct
{
    public float RestLength;

    public float ElasticModulus;

    public int i0, i1;

    public static int Size()
    {
        return sizeof(float) * 2 + sizeof(int) * 2;
    }
}
public struct PBDGrassBodyStruct
{
    public Vector3[] Positions;
    public Vector3[] Predicted;
    public Vector3[] Velocities;
    public Vector3[] OriginPos;
    public Vector3[] Offset;

    public int IndexOffset;

    public FixedConstraintStruct[] Fcons;
    public DistanceConstraintStruct[] Dcons;
}

struct SphereCollisionStruct
{
    public Vector3 Position;
    public float Radius;
};

public class PBDGrassCSTest : MonoBehaviour
{
    const int GrassPatchWidth = 10;
    const int GrassPatchLength = 10;
    const int GrassBodyCounts = 1024;
    const float CollisionRadius = 1.0f;

    int meshTrianglesCounts;
    public Material GrassMaterial;
    public Material TestMaterial;
    public List<Transform> balls;

    private PBDSolver GEOsolver;

    private ComputeBuffer PositionBuffer;
    private ComputeBuffer PredictedBuffer;
    private ComputeBuffer VelocitiesBuffer;
    private ComputeBuffer OriginPosBuffer;
    private ComputeBuffer OffsetBuffer;
    private ComputeBuffer FconsBuffer;
    private ComputeBuffer DconsBuffer;
    private ComputeBuffer IndexOffsetBuffer;

    private ComputeBuffer ballBuffer;
    private ComputeBuffer resultPosBuffer;
    private ComputeBuffer resultTriangles;

    public ComputeShader CS;
    private int PBDSolverHandler;
    private int UpdateMeshHandler;

    private Vector3[] vertArray;
    private Vector3[] boneArray;
    private PBDGrassPatch patch;

    void Start()
    {
        Application.targetFrameRate = 60;

        GEOsolver = new PBDSolver(3.0f)
        {
            Gravity = Vector3.down * 9.8f,
            WindForce = Vector3.up * 1000
        };

        PBDGrassPatch patch = new PBDGrassPatch(Vector3.zero, GrassPatchWidth, GrassPatchLength, GrassBodyCounts, null);
        meshTrianglesCounts = patch.PatchMesh.triangles.Length;

        InitCS(patch);
    }

    private void InitCS(PBDGrassPatch patch)
    {
        this.patch = patch;

        PBDSolverHandler = CS.FindKernel("PBDSolver");
        UpdateMeshHandler = CS.FindKernel("UpdateMesh");

        // for PBD solver
        CS.SetFloat("dt", 1.0f / 60.0f / 3.0f);
        CS.SetVector("Gravity", GEOsolver.Gravity);
        CS.SetVector("WindForce", GEOsolver.WindForce);
        CS.SetFloat("Friction", GEOsolver.Friction);
        CS.SetFloat("StopThreshold", GEOsolver.StopThreshold);
        // for grass constants
        CS.SetFloat("Mass", patch.Bodies[0].Mass);

        ballBuffer = new ComputeBuffer(balls.Count, sizeof(float) * 4);
        ballBuffer.SetData(GenBallArray());
        CS.SetBuffer(PBDSolverHandler, "BallBuffer", ballBuffer);

        PositionBuffer = new ComputeBuffer(1024 * 4, sizeof(float) * 3);
        boneArray = patch.GenPositionArray();
        PositionBuffer.SetData(boneArray);
        CS.SetBuffer(PBDSolverHandler, "PositionBuffer", PositionBuffer);
        CS.SetBuffer(UpdateMeshHandler, "PositionBuffer", PositionBuffer);

        PredictedBuffer = new ComputeBuffer(1024 * 4, sizeof(float) * 3);
        PredictedBuffer.SetData(patch.GenPredictedArray());
        CS.SetBuffer(PBDSolverHandler, "PredictedBuffer", PredictedBuffer);

        VelocitiesBuffer = new ComputeBuffer(1024 * 4, sizeof(float) * 3);
        VelocitiesBuffer.SetData(patch.GenVelocitiesArray());
        CS.SetBuffer(PBDSolverHandler, "VelocitiesBuffer", VelocitiesBuffer);

        OriginPosBuffer = new ComputeBuffer(1024 * 4, sizeof(float) * 3);
        OriginPosBuffer.SetData(patch.GenOriginPosArray());
        CS.SetBuffer(PBDSolverHandler, "OriginPosBuffer", OriginPosBuffer);

        OffsetBuffer = new ComputeBuffer(1024 * 3, sizeof(float) * 3);
        OffsetBuffer.SetData(patch.GenOffsetArray());
        CS.SetBuffer(UpdateMeshHandler, "OffsetBuffer", OffsetBuffer);

        FconsBuffer = new ComputeBuffer(1024, FixedConstraintStruct.Size());
        FconsBuffer.SetData(patch.GenFconsArray());
        CS.SetBuffer(PBDSolverHandler, "FconsBuffer", FconsBuffer);

        DconsBuffer = new ComputeBuffer(1024 * 3, DistanceConstraintStruct.Size());
        DconsBuffer.SetData(patch.GenDconsArray());
        CS.SetBuffer(PBDSolverHandler, "DconsBuffer", DconsBuffer);

        IndexOffsetBuffer = new ComputeBuffer(1024, sizeof(int));
        IndexOffsetBuffer.SetData(patch.GenIndexOffsetArray());
        CS.SetBuffer(UpdateMeshHandler, "IndexOffsetBuffer", IndexOffsetBuffer);

        resultPosBuffer = new ComputeBuffer(patch.vertices.Length, sizeof(float) * 3);      
        resultPosBuffer.SetData(patch.vertices);
        vertArray = patch.vertices;
        CS.SetBuffer(UpdateMeshHandler, "ResultPosBuffer", resultPosBuffer);

        resultTriangles = new ComputeBuffer(patch.PatchMesh.triangles.Length, sizeof(int)); // to shader
        resultTriangles.SetData(patch.PatchMesh.triangles);

        GrassMaterial.SetBuffer("VertexBuffer", resultPosBuffer);
        GrassMaterial.SetBuffer("TriangleBuffer", resultTriangles);
    }

    void FixedUpdate()
    {
        CS.Dispatch(PBDSolverHandler, 4, 1, 1);
        CS.Dispatch(UpdateMeshHandler, 4, 1, 1);

        //PositionBuffer.GetData(boneArray);

        resultPosBuffer.GetData(vertArray);
        patch.PatchMesh.vertices = vertArray;
        //Debug.Log("¸üÐÂ");
        //Debug.Log(boneArray[100]);
    }

    private void Update()
    {
        ballBuffer.SetData(GenBallArray());
        CS.SetBuffer(PBDSolverHandler, "BallBuffer", ballBuffer);

        Graphics.DrawProcedural(GrassMaterial, new Bounds(Vector3.zero, Vector3.one * 100), MeshTopology.Triangles, meshTrianglesCounts);

        //Graphics.DrawMesh(patch.PatchMesh, Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one),TestMaterial, 0);
    }

    private SphereCollisionStruct[] GenBallArray()
    {
        SphereCollisionStruct[] ballStruct = new SphereCollisionStruct[balls.Count];
        for (int i = 0; i < ballStruct.Length; i++)
        {
            ballStruct[i].Position = balls[i].position;
            ballStruct[i].Radius = CollisionRadius;
        }
        return ballStruct;
    }

    private void OnDestroy()
    {
        PositionBuffer.Release();
        PredictedBuffer.Release();
        VelocitiesBuffer.Release();
        OriginPosBuffer.Release();
        OffsetBuffer.Release();
        FconsBuffer.Release();
        DconsBuffer.Release();
        IndexOffsetBuffer.Release();

        ballBuffer.Release();
        resultPosBuffer.Release();
        resultTriangles.Release();
    }
}
