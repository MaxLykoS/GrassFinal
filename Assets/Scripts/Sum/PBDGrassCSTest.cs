using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PBD;
using UnityEngine.Rendering;

public class PBDGrassCSTest : MonoBehaviour
{
    const int GrassPatchWidth = 10;
    const int GrassPatchLength = 10;
    const int GrassBodyCounts = 100;
    const float CollisionRadius = 1.0f;

    public Material GrassMaterial;
    public Material GrassInstancingMaterial;
    public Transform ball;

    private PBDSolver GEOsolver;
    private Mesh grassMesh;
    private PBDoGrassPatch patch;

    private ComputeBuffer pathBuffer;
    private ComputeBuffer resultPosBuffer;
    public ComputeShader CS;

    public int DrawCounts = 1;
    private int PBDSolverHandler;

    void Start()
    {
        Application.targetFrameRate = 60;

        GEOsolver = new PBDSolver(3.0f)
        {
            Gravity = Vector3.down * 9.8f,
            WindForce = Vector3.up * 1000
        };

        patch = new PBDoGrassPatch(Vector3.zero, GrassPatchWidth, GrassPatchLength, GrassBodyCounts, null);
        GEOsolver.AddGrassPatch(patch);
        grassMesh = patch.PatchMesh;

        GEOsolver.AddCollider(new SphereCollision(ball, CollisionRadius));

        InitCS();
    }

    private void InitCS()
    {
        PBDSolverHandler = CS.FindKernel("PBDSolver");
        CS.SetVector("ballPos", ball.position);
    }

    void FixedUpdate()
    {
        for (int i = DrawCounts; i > 0; i--)
            GEOsolver.Update((float)(1.0 / 60.0 / 3.0));
    }


    private void Update()
    {
        for (int i = DrawCounts; i > 0; i--)
        {
            CS.SetVector("ballPos", ball.position);
            CS.Dispatch(PBDSolverHandler, 4, 1, 1);

            
            Graphics.DrawMesh(grassMesh, Matrix4x4.identity, GrassMaterial, 0);
        }
    }

    private void CSCallBack(AsyncGPUReadbackRequest request)
    {
        if (request.hasError)
        {
            Debug.Log("GPU readback error detected.");
            return;
        }

        //patch. = request.GetData<Vector3>().ToArray();
    }
}
