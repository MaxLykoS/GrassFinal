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
    private static Texture2D WindNoiseTex;
    public static void Setup(Material pbdMaterial, List<Transform> ballslist, Camera cam, Texture2D windNoiseTex)
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
            balls[i].Radius = 0.6f;
        }

        ballBuffer = new ComputeBuffer(balls.Length, sizeof(float) * 4);
        ballBuffer.SetData(balls);

        Cam = cam;

        WindNoiseTex = windNoiseTex;
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
    private ComputeBuffer BoneInfoBuffer;
    private ComputeBuffer OffsetBuffer;

    private ComputeBuffer FconsBuffer;
    private ComputeBuffer DconsBuffer;
    private ComputeBuffer IndexOffsetBuffer;
    private ComputeBuffer resultPosBuffer;
    private int PBDSolverHandlerLOD0;
    private int PBDSolverHandlerLOD1;
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
    private ComputeBuffer dispatchArgsBufferLOD0;
    private ComputeBuffer dispatchArgsBufferLOD1;
    private ComputeBuffer gridsAllBuffer;
    private int GridLODCSHandler;
    private ComputeBuffer gridsToComputeBufferLOD0;
    private ComputeBuffer gridsToComputeBufferLOD1;
    private ComputeBuffer gridsVisibleBuffer;
    private uint[] gridCullingArgs = new uint[4] { 0, 0, 0, 0 };
    private int gridsLen;
    #endregion

    private Bounds bound;

    public PBDGrassPatchRenderer(PBDGrassPatch patch)
    {
        Timer = 0;

        Debug.Log("����:" + (patch.PatchMesh.triangles.Length / 3).ToString());
        gridsLen = Mathf.Max(patch.Width * patch.Length / 32, 1);

        bound = new Bounds(patch.Root, Vector3.one * 100000);

        InitCS(patch);
    }

    void InitCS(PBDGrassPatch patch)
    {
        CS = GrassDemo.CreateShader();

        PBDSolverHandlerLOD0 = CS.FindKernel("PBDSolverLOD0");
        PBDSolverHandlerLOD1 = CS.FindKernel("PBDSolverLOD1");

        #region PBDSolver
        // for PBD solver
        CS.SetFloat("dt", 1.0f / 60.0f / 3.0f);
        CS.SetVector("Gravity", Solver.Gravity);
        CS.SetVector("WindForce", Solver.WindForce);
        CS.SetFloat("Friction", Solver.Friction);
        CS.SetFloat("StopThreshold", Solver.StopThreshold);
        // procedual Wind Force
        CS.SetFloat("_Time", 0);
        CS.SetFloat("_WindFrequency", 1);
        CS.SetVector("_WindForceMap_ST", new Vector4(0.01f, 0.01f, 1, 1));
        // for grass constants
        CS.SetFloat("Mass", patch.Bodies[0].Mass);

        CS.SetBuffer(PBDSolverHandlerLOD0, "BallBuffer", ballBuffer);
        CS.SetBuffer(PBDSolverHandlerLOD1, "BallBuffer", ballBuffer);

        BoneInfo[] t = patch.GenBoneInfoArray();
        BoneInfoBuffer = new ComputeBuffer(t.Length, sizeof(float) * 3 * 4);
        BoneInfoBuffer.SetData(t);
        CS.SetBuffer(PBDSolverHandlerLOD0, "BonesBuffer", BoneInfoBuffer);
        CS.SetBuffer(PBDSolverHandlerLOD1, "BonesBuffer", BoneInfoBuffer);
        t = null;

        Vector3[] vt = patch.GenOffsetArray();
        OffsetBuffer = new ComputeBuffer(vt.Length, sizeof(float) * 3);
        OffsetBuffer.SetData(vt);
        CS.SetBuffer(PBDSolverHandlerLOD0, "OffsetBuffer", OffsetBuffer);
        CS.SetBuffer(PBDSolverHandlerLOD1, "OffsetBuffer", OffsetBuffer);
        vt = null;

        FixedConstraintStruct[] tt = patch.GenFconsArray();
        FconsBuffer = new ComputeBuffer(tt.Length, FixedConstraintStruct.Size());
        FconsBuffer.SetData(tt);
        CS.SetBuffer(PBDSolverHandlerLOD0, "FconsBuffer", FconsBuffer);
        CS.SetBuffer(PBDSolverHandlerLOD1, "FconsBuffer", FconsBuffer);
        tt = null;

        DistanceConstraintStruct[] ttt = patch.GenDconsArray();
        DconsBuffer = new ComputeBuffer(ttt.Length, DistanceConstraintStruct.Size());
        DconsBuffer.SetData(ttt);
        CS.SetBuffer(PBDSolverHandlerLOD0, "DconsBuffer", DconsBuffer);
        CS.SetBuffer(PBDSolverHandlerLOD1, "DconsBuffer", DconsBuffer);
        ttt = null;

        int[] tttt = patch.GenIndexOffsetArray();
        IndexOffsetBuffer = new ComputeBuffer(tttt.Length, sizeof(int));
        IndexOffsetBuffer.SetData(tttt);
        CS.SetBuffer(PBDSolverHandlerLOD0, "IndexOffsetBuffer", IndexOffsetBuffer);
        CS.SetBuffer(PBDSolverHandlerLOD1, "IndexOffsetBuffer", IndexOffsetBuffer);
        tttt = null;

        CS.SetTexture(PBDSolverHandlerLOD0, "WindForceMap", WindNoiseTex);
        CS.SetTexture(PBDSolverHandlerLOD1, "WindForceMap", WindNoiseTex);

        #endregion

        resultPosBuffer = new ComputeBuffer(patch.vertices.Length, sizeof(float) * 3);
        resultPosBuffer.SetData(patch.vertices);
        CS.SetBuffer(PBDSolverHandlerLOD0, "ResultPosBuffer", resultPosBuffer);
        CS.SetBuffer(PBDSolverHandlerLOD1, "ResultPosBuffer", resultPosBuffer);

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

        CS.SetBuffer(PBDSolverHandlerLOD0, "NormalBuffer", NormalsBuffer);
        CS.SetBuffer(PBDSolverHandlerLOD1, "NormalBuffer", NormalsBuffer);
        #endregion

        #region dispatch indirect
        gridsAllBuffer = new ComputeBuffer(patch.grids.Length, sizeof(float) * 3 + sizeof(int));
        gridsAllBuffer.SetData(patch.grids);
        gridsToComputeBufferLOD0 = new ComputeBuffer(patch.grids.Length, sizeof(float) * 3 + sizeof(int));
        gridsToComputeBufferLOD1 = new ComputeBuffer(patch.grids.Length, sizeof(float) * 3 + sizeof(int));
        gridsVisibleBuffer = new ComputeBuffer(patch.grids.Length, sizeof(float) * 3 + sizeof(int));

        PBDMaterial.SetBuffer("GridsVisibleBuffer", gridsVisibleBuffer);

        GridCullingCSHandler = CS.FindKernel("GridCulling");

        //https://docs.unity3d.com/540/Documentation/ScriptReference/ComputeShader.DispatchIndirect.html
        //https://github.com/cinight/MinimalCompute/blob/master/Assets/IndirectCompute/IndirectCompute.cs
        gridCullingArgs[0] = 1; // number of work groups in X
        gridCullingArgs[1] = 0; // number of work groups in Y
        gridCullingArgs[2] = 1; // number of work groups in Z
        gridCullingArgs[3] = 0; // idk
        dispatchArgsBufferLOD0 = new ComputeBuffer(1, sizeof(uint) * 4, ComputeBufferType.IndirectArguments);
        dispatchArgsBufferLOD0.SetData(gridCullingArgs);
        dispatchArgsBufferLOD1 = new ComputeBuffer(1, sizeof(uint) * 4, ComputeBufferType.IndirectArguments);
        dispatchArgsBufferLOD1.SetData(gridCullingArgs);

        CS.SetBuffer(GridCullingCSHandler, "GridsAllBuffer", gridsAllBuffer);
        GridLODCSHandler = CS.FindKernel("GridLOD");
        CS.SetBuffer(GridLODCSHandler, "GridsAllBuffer", gridsAllBuffer);
        CS.SetBuffer(GridLODCSHandler, "GridsToComputeBufferLOD0", gridsToComputeBufferLOD0);
        CS.SetBuffer(GridLODCSHandler, "GridsToComputeBufferLOD1", gridsToComputeBufferLOD1);
        CS.SetBuffer(GridCullingCSHandler, "GridsVisibleBuffer", gridsVisibleBuffer);
        CS.SetBuffer(GridLODCSHandler, "bufferWithArgsLOD0", dispatchArgsBufferLOD0);
        CS.SetBuffer(GridLODCSHandler, "bufferWithArgsLOD1", dispatchArgsBufferLOD1);

        CS.SetBuffer(PBDSolverHandlerLOD0, "GridsToComputeBufferLOD0", gridsToComputeBufferLOD0);
        CS.SetBuffer(PBDSolverHandlerLOD1, "GridsToComputeBufferLOD1", gridsToComputeBufferLOD1);
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
            CS.SetBuffer(PBDSolverHandlerLOD0, "BallBuffer", ballBuffer);
            CS.SetBuffer(PBDSolverHandlerLOD1, "BallBuffer", ballBuffer);
            CS.SetFloat("_Time", Time.realtimeSinceStartup);

            #region dispatch indirect
            gridCullingArgs[1] = 0;
            dispatchArgsBufferLOD0.SetData(gridCullingArgs);
            dispatchArgsBufferLOD1.SetData(gridCullingArgs);

            CS.SetVector("camPos", Cam.transform.position);

            CS.Dispatch(GridLODCSHandler, gridsLen, 1, 1);
            #endregion

            CS.DispatchIndirect(PBDSolverHandlerLOD0, dispatchArgsBufferLOD0, 0);
            CS.DispatchIndirect(PBDSolverHandlerLOD1, dispatchArgsBufferLOD1, 0);
        }
    }
    public void Update()
    {
        #region dispatch indirect
        gridCullingArgs[1] = 0;
        dispatchArgsBufferLOD0.SetData(gridCullingArgs);

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
        BoneInfoBuffer.Release();
        OffsetBuffer.Release();
        FconsBuffer.Release();
        DconsBuffer.Release();
        IndexOffsetBuffer.Release();

        resultPosBuffer.Release();
        resultTriangles.Release();
        NormalsBuffer.Release();
        UVsBuffer.Release();

        dispatchArgsBufferLOD0.Release();
        dispatchArgsBufferLOD1.Release();
        gridsAllBuffer.Release();
        gridsToComputeBufferLOD0.Release();
        gridsToComputeBufferLOD1.Release();
        gridsVisibleBuffer.Release();

        drawIndirectArgsBuffer.Release();

        GrassDemo.DestroyCS(CS);
    }
    ~PBDGrassPatchRenderer()
    {
        BoneInfoBuffer.Release();
        OffsetBuffer.Release();
        FconsBuffer.Release();
        DconsBuffer.Release();
        IndexOffsetBuffer.Release();

        resultPosBuffer.Release();
        resultTriangles.Release();
        NormalsBuffer.Release();
        UVsBuffer.Release();

        dispatchArgsBufferLOD0.Release();
        dispatchArgsBufferLOD1.Release();
        gridsAllBuffer.Release();
        gridsToComputeBufferLOD0.Release();
        gridsToComputeBufferLOD1.Release();
        gridsVisibleBuffer.Release();

        drawIndirectArgsBuffer.Release();

        GrassDemo.DestroyCS(CS);
    }

    public void SetWindNoise(float frequency, Vector4 tileAndOffset)
    {
        CS.SetFloat("_WindFrequency", frequency);
        CS.SetVector("_WindForceMap_ST", tileAndOffset);
    }
}