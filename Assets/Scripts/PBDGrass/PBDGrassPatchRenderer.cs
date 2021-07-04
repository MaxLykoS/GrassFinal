using PBD;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class PBDGrassPatchRenderer
{
    private static Material PBDMaterial;

    private static PBDSolver Solver;

    private static ComputeBuffer ballBuffer;
    private static SphereCollisionStruct[] balls;

    int meshTrianglesCounts;
    private ComputeBuffer PositionBuffer;
    private ComputeBuffer PredictedBuffer;
    private ComputeBuffer VelocitiesBuffer;
    private ComputeBuffer OriginPosBuffer;
    private ComputeBuffer OffsetBuffer;
    private ComputeBuffer FconsBuffer;
    private ComputeBuffer DconsBuffer;
    private ComputeBuffer IndexOffsetBuffer;

    private ComputeBuffer resultPosBuffer;
    //private ComputeBuffer resultTriangles;

    private ComputeShader CS;
    private int PBDSolverHandler;
    private int UpdateMeshHandler;

    public Mesh grassMesh;

    private Vector3[] vertArray;

    private Bounds bound;

    private int bodyCount;

    public PBDGrassPatchRenderer(PBDGrassPatch patch)
    {
        Timer = 0;

        meshTrianglesCounts = patch.PatchMesh.triangles.Length;
        grassMesh = patch.PatchMesh;
        bodyCount = patch.Bodies.Length;
        Debug.Log(grassMesh.vertexCount);

        bound = new Bounds(patch.Root, Vector3.one);

        InitCS(patch);
    }

    public static void Setup(Material pbdMaterial, List<Transform> ballslist)
    {
        PBDMaterial = new Material(pbdMaterial);

        Solver = new PBDSolver(3.0f)
        {
            Gravity = Vector3.down * 9.8f,
            WindForce = Vector3.up * 1000
        };

        balls = new SphereCollisionStruct[ballslist.Count];
        for (int i = 0; i < ballslist.Count; i++)
        {
            balls[i].Position = ballslist[i].position;
            balls[i].Radius = 0.8f;
        }

        ballBuffer = new ComputeBuffer(balls.Length, sizeof(float) * 4);
        ballBuffer.SetData(balls);
    }

    public static void UpdateCollision(List<Transform> ballsList)
    {
        for (int i = 0; i < balls.Length; i++)
            balls[i].Position = ballsList[i].position;
    }

    public static void ReleaseStaticData()
    {
        ballBuffer.Release();
    }


    public void Release()
    {
        PositionBuffer.Release();
        PredictedBuffer.Release();
        VelocitiesBuffer.Release();
        OriginPosBuffer.Release();
        OffsetBuffer.Release();
        FconsBuffer.Release();
        DconsBuffer.Release();
        IndexOffsetBuffer.Release();

        resultPosBuffer.Release();
        //resultTriangles.Release();

        GrassDemo.DestroyCS(CS);

        GrassDemo.DestroyMesh(grassMesh);
    }
    ~PBDGrassPatchRenderer()
    {
        PositionBuffer.Release();
        PredictedBuffer.Release();
        VelocitiesBuffer.Release();
        OriginPosBuffer.Release();
        OffsetBuffer.Release();
        FconsBuffer.Release();
        DconsBuffer.Release();
        IndexOffsetBuffer.Release();

        resultPosBuffer.Release();
        //resultTriangles.Release();

        GrassDemo.DestroyMesh(grassMesh);
    }

    private float Timer;
    public void FixedUpdate()
    {
        Timer += Time.fixedDeltaTime;
        if (Timer >= 0.02f)
        {
            Timer = 0;

            ballBuffer.SetData(balls);
            CS.SetBuffer(PBDSolverHandler, "BallBuffer", ballBuffer);

            CS.Dispatch(PBDSolverHandler, bodyCount / 32, 1, 1);
            CS.Dispatch(UpdateMeshHandler, bodyCount / 32, 1, 1);

            //resultPosBuffer.GetData(vertArray);
            //grassMesh.vertices = vertArray;

            AsyncGPUReadback.Request(resultPosBuffer, CSBufferCallBack);
        }
    }

    public void Update()
    {
        //Graphics.DrawProcedural(PBDMaterial, new Bounds(Root, Vector3.one), MeshTopology.Triangles, meshTrianglesCounts)
        Graphics.DrawMesh(grassMesh, Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one), PBDMaterial, 0);
    }

    private void InitCS(PBDGrassPatch patch)
    {
        CS = GrassDemo.CreateShader();

        PBDSolverHandler = CS.FindKernel("PBDSolver");
        UpdateMeshHandler = CS.FindKernel("UpdateMesh");

        // for PBD solver
        CS.SetFloat("dt", 1.0f / 60.0f / 3.0f);
        CS.SetVector("Gravity", Solver.Gravity);
        CS.SetVector("WindForce", Solver.WindForce);
        CS.SetFloat("Friction", Solver.Friction);
        CS.SetFloat("StopThreshold", Solver.StopThreshold);
        // for grass constants
        CS.SetFloat("Mass", patch.Bodies[0].Mass);

        CS.SetBuffer(PBDSolverHandler, "BallBuffer", ballBuffer);

        Vector3[] t = patch.GenPositionArray();
        PositionBuffer = new ComputeBuffer(t.Length, sizeof(float) * 3);
        PositionBuffer.SetData(t);
        CS.SetBuffer(PBDSolverHandler, "PositionBuffer", PositionBuffer);
        CS.SetBuffer(UpdateMeshHandler, "PositionBuffer", PositionBuffer);

        t = patch.GenPredictedArray();
        PredictedBuffer = new ComputeBuffer(t.Length, sizeof(float) * 3);
        PredictedBuffer.SetData(t);
        CS.SetBuffer(PBDSolverHandler, "PredictedBuffer", PredictedBuffer);

        t = patch.GenVelocitiesArray();
        VelocitiesBuffer = new ComputeBuffer(t.Length, sizeof(float) * 3);
        VelocitiesBuffer.SetData(t);
        CS.SetBuffer(PBDSolverHandler, "VelocitiesBuffer", VelocitiesBuffer);

        t = patch.GenOriginPosArray();
        OriginPosBuffer = new ComputeBuffer(t.Length, sizeof(float) * 3);
        OriginPosBuffer.SetData(t);
        CS.SetBuffer(PBDSolverHandler, "OriginPosBuffer", OriginPosBuffer);

        t = patch.GenOffsetArray();
        OffsetBuffer = new ComputeBuffer(t.Length, sizeof(float) * 3);
        OffsetBuffer.SetData(t);
        CS.SetBuffer(UpdateMeshHandler, "OffsetBuffer", OffsetBuffer);

        FixedConstraintStruct[] tt = patch.GenFconsArray();
        FconsBuffer = new ComputeBuffer(tt.Length, FixedConstraintStruct.Size());
        FconsBuffer.SetData(tt);
        CS.SetBuffer(PBDSolverHandler, "FconsBuffer", FconsBuffer);

        DistanceConstraintStruct[] ttt = patch.GenDconsArray();
        DconsBuffer = new ComputeBuffer(ttt.Length, DistanceConstraintStruct.Size());
        DconsBuffer.SetData(ttt);
        CS.SetBuffer(PBDSolverHandler, "DconsBuffer", DconsBuffer);

        int[] tttt = patch.GenIndexOffsetArray();
        IndexOffsetBuffer = new ComputeBuffer(tttt.Length, sizeof(int));
        IndexOffsetBuffer.SetData(tttt);
        CS.SetBuffer(UpdateMeshHandler, "IndexOffsetBuffer", IndexOffsetBuffer);

        resultPosBuffer = new ComputeBuffer(patch.vertices.Length, sizeof(float) * 3);
        vertArray = patch.vertices;
        resultPosBuffer.SetData(vertArray);
        CS.SetBuffer(UpdateMeshHandler, "ResultPosBuffer", resultPosBuffer);

        //resultTriangles = new ComputeBuffer(patch.PatchMesh.triangles.Length, sizeof(int)); // to shader
        //resultTriangles.SetData(patch.PatchMesh.triangles);

        //PBDMaterial.SetBuffer("VertexBuffer", resultPosBuffer);
        //PBDMaterial.SetBuffer("TriangleBuffer", resultTriangles);
    }

    private void CSBufferCallBack(AsyncGPUReadbackRequest request)
    {
        if (request.hasError)
        {
            Debug.Log("GPU readback error detected.");
            return;
        }
        if (grassMesh == null)
            return;
        
        vertArray = request.GetData<Vector3>().ToArray();
        grassMesh.vertices = vertArray;

        //grassMesh.RecalculateNormals(MeshUpdateFlags.DontNotifyMeshUsers);
    }
}
