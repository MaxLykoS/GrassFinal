using PBD;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class PBDGrassPatchRenderer
{
    public static RenderTexture DepthTex;
    private static Material PBDMaterial;
    private static PBDSolver Solver;
    private static ComputeBuffer ballBuffer;
    private static SphereCollisionStruct[] balls;
    public static void Setup(Material pbdMaterial, List<Transform> ballslist, Camera cam)
    {
        PBDMaterial = pbdMaterial;

        Solver = new PBDSolver(3.0f)
        {
            Gravity = Vector3.up * 1000,
            WindForce = Vector3.one
        };

        balls = new SphereCollisionStruct[ballslist.Count];
        for (int i = 0; i < ballslist.Count; i++)
        {
            balls[i].Position = ballslist[i].position;
            balls[i].Radius = 0.8f;
        }

        ballBuffer = new ComputeBuffer(balls.Length, sizeof(float) * 4);
        ballBuffer.SetData(balls);

        Cam = cam;
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

    private ComputeShader CS;

    #region PBD Solver Compute Shader
    private ComputeBuffer PositionBuffer;
    private ComputeBuffer PredictedBuffer;
    private ComputeBuffer VelocitiesBuffer;
    private ComputeBuffer OriginPosBuffer;
    private ComputeBuffer OffsetBuffer;
    private ComputeBuffer FconsBuffer;
    private ComputeBuffer DconsBuffer;
    private ComputeBuffer IndexOffsetBuffer;
    private ComputeBuffer resultPosBuffer;
    private int PBDSolverHandler;
    private int UpdateMeshHandler;
    #endregion

    #region draw procedual
    private ComputeBuffer resultTriangles;
    private ComputeBuffer NormalsBuffer;
    private ComputeBuffer UVsBuffer;
    #endregion

    #region draw procedual indirect
    private int[] drawIndirectArgs = { 0, 1, 0, 0};
    private ComputeBuffer drawIndirectArgsBuffer;
    #endregion

    #region dispatch indirect
    private static Camera Cam;
    private int GridCullingCSHandler;
    private ComputeBuffer dispatchArgsBuffer;
    private ComputeBuffer gridsAllBuffer;
    private ComputeBuffer gridsToComputeBuffer;
    private ComputeBuffer gridsVisibleBuffer;
    private uint[] gridCullingArgs = new uint[4] { 0, 0, 0, 0 };
    private int gridsLen;
    #endregion

    private Bounds bound;

    public PBDGrassPatchRenderer(PBDGrassPatch patch)
    {
        Timer = 0;

        Debug.Log("ÃæÊý:" + (patch.PatchMesh.triangles.Length / 3).ToString());
        gridsLen = Mathf.Max(patch.Width * patch.Length / 32, 1);

        bound = new Bounds(patch.Root, Vector3.one * 100000);

        InitCS(patch);
    }

    void InitCS(PBDGrassPatch patch)
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
        resultPosBuffer.SetData(patch.vertices);
        CS.SetBuffer(UpdateMeshHandler, "ResultPosBuffer", resultPosBuffer);

        #region draw procedual
        resultTriangles = new ComputeBuffer(patch.PatchMesh.triangles.Length, sizeof(int)); // to shader
        resultTriangles.SetData(patch.PatchMesh.triangles);

        NormalsBuffer = new ComputeBuffer(patch.PatchMesh.vertexCount, sizeof(float) * 3);
        NormalsBuffer.SetData(patch.PatchMesh.normals);

        UVsBuffer = new ComputeBuffer(patch.PatchMesh.vertexCount, sizeof(float) * 2);
        UVsBuffer.SetData(patch.PatchMesh.uv);

        PBDMaterial.SetBuffer("VertexBuffer", resultPosBuffer);
        PBDMaterial.SetBuffer("TriangleBuffer", resultTriangles);
        PBDMaterial.SetBuffer("NormalBuffer", NormalsBuffer);
        PBDMaterial.SetBuffer("UvBuffer", UVsBuffer);
        #endregion

        #region dispatch indirect

        gridsAllBuffer = new ComputeBuffer(patch.grids.Length, sizeof(float) * 3 + sizeof(int));
        gridsAllBuffer.SetData(patch.grids);
        gridsToComputeBuffer = new ComputeBuffer(patch.grids.Length, sizeof(float) * 3 + sizeof(int));
        gridsVisibleBuffer = new ComputeBuffer(patch.grids.Length, sizeof(float) * 3 + sizeof(int));

        PBDMaterial.SetBuffer("GridsVisibleBuffer", gridsVisibleBuffer);

        GridCullingCSHandler = CS.FindKernel("GridCulling");

        //https://docs.unity3d.com/540/Documentation/ScriptReference/ComputeShader.DispatchIndirect.html
        //https://github.com/cinight/MinimalCompute/blob/master/Assets/IndirectCompute/IndirectCompute.cs
        gridCullingArgs[0] = 1; // number of work groups in X
        gridCullingArgs[1] = 0; // number of work groups in Y
        gridCullingArgs[2] = 1; // number of work groups in Z
        gridCullingArgs[3] = 0; // idk
        dispatchArgsBuffer = new ComputeBuffer(1, sizeof(uint) * 4, ComputeBufferType.IndirectArguments);
        dispatchArgsBuffer.SetData(gridCullingArgs);

        CS.SetBuffer(GridCullingCSHandler, "GridsAllBuffer", gridsAllBuffer);
        CS.SetBuffer(GridCullingCSHandler, "GridsToComputeBuffer", gridsToComputeBuffer);
        CS.SetBuffer(GridCullingCSHandler, "GridsVisibleBuffer", gridsVisibleBuffer);
        CS.SetBuffer(GridCullingCSHandler, "bufferWithArgs", dispatchArgsBuffer);

        CS.SetBuffer(PBDSolverHandler, "GridsToComputeBuffer", gridsToComputeBuffer);
        CS.SetBuffer(UpdateMeshHandler, "GridsToComputeBuffer", gridsToComputeBuffer);
        #endregion

        #region draw procedual indirect
        drawIndirectArgs[0] = 32 * 5 * 3;
        drawIndirectArgs[1] = 0;
        drawIndirectArgs[2] = 0;
        drawIndirectArgs[3] = 0;
        drawIndirectArgsBuffer = new ComputeBuffer(1, sizeof(int) * 4, ComputeBufferType.IndirectArguments);
        drawIndirectArgsBuffer.SetData(drawIndirectArgs);
        CS.SetBuffer(GridCullingCSHandler, "bufferWithArgsDrawIndirect", drawIndirectArgsBuffer);
        #endregion
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

            #region dispatch indirect
            gridCullingArgs[1] = 0;
            dispatchArgsBuffer.SetData(gridCullingArgs);

            drawIndirectArgs[1] = 0;
            drawIndirectArgsBuffer.SetData(drawIndirectArgs);

            CS.SetTexture(GridCullingCSHandler, "_DepthTex", DepthTex);

            CS.SetVector("camPos", Cam.transform.position);
            CS.SetVector("camDir", Cam.transform.forward);
            CS.SetFloat("camHalfFov", Cam.fieldOfView / 2);

            Matrix4x4 VP = GL.GetGPUProjectionMatrix(Cam.projectionMatrix, false) * Cam.worldToCameraMatrix;
            CS.SetMatrix("_Matrix_VP", VP);

            CS.Dispatch(GridCullingCSHandler, gridsLen, 1, 1);
            #endregion

            CS.DispatchIndirect(PBDSolverHandler, dispatchArgsBuffer, 0);

            CS.DispatchIndirect(UpdateMeshHandler, dispatchArgsBuffer, 0);
        }
    }
    public void Update()
    {
        #region dispatch indirect
        gridCullingArgs[1] = 0;
        dispatchArgsBuffer.SetData(gridCullingArgs);

        drawIndirectArgs[1] = 0;
        drawIndirectArgsBuffer.SetData(drawIndirectArgs);

        CS.SetTexture(GridCullingCSHandler, "_DepthTex", DepthTex);

        CS.SetVector("camPos", Cam.transform.position);
        CS.SetVector("camDir", Cam.transform.forward);
        CS.SetFloat("camHalfFov", Cam.fieldOfView / 2);

        Matrix4x4 VP = GL.GetGPUProjectionMatrix(Cam.projectionMatrix, false) * Cam.worldToCameraMatrix;
        CS.SetMatrix("_Matrix_VP", VP);

        CS.Dispatch(GridCullingCSHandler, gridsLen, 1, 1);
        #endregion

        Graphics.DrawProceduralIndirect(PBDMaterial, bound, MeshTopology.Triangles, drawIndirectArgsBuffer);
    }

    public void SetWindForce(Vector3 wind)
    {
        CS.SetVector("WindForce", wind);
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
        resultTriangles.Release();
        NormalsBuffer.Release();
        UVsBuffer.Release();

        dispatchArgsBuffer.Release();
        gridsAllBuffer.Release();
        gridsToComputeBuffer.Release();
        gridsVisibleBuffer.Release();

        drawIndirectArgsBuffer.Release();

        GrassDemo.DestroyCS(CS);
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
        resultTriangles.Release();
        NormalsBuffer.Release();
        UVsBuffer.Release();

        dispatchArgsBuffer.Release();
        gridsAllBuffer.Release();
        gridsToComputeBuffer.Release();
        gridsVisibleBuffer.Release();

        drawIndirectArgsBuffer.Release();

        GrassDemo.DestroyCS(CS);
    }
}