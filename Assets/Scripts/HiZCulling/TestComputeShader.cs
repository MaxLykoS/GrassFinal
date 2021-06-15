using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public class TestComputeShader : MonoBehaviour
{
    public static RenderTexture DepthTex;

    public Mesh Mesh;  //  ÷Õœ
    public ComputeShader CS;  //  ÷Õœ
    public Material Mat;  //  ÷Õœ
    ComputeBuffer bufferWithArgs;
    ComputeBuffer posVisibleBuffer;
    private uint[] args;
    private int CSCullingID;
    private Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 10000);
    ComputeBuffer posBuffer;

    public bool useHzb = false;

    static int staticRandomID = 0;
    float StaticRandom()
    {
        float v = 0;
        v = Mathf.Abs(Mathf.Sin(staticRandomID)) * 1000 + Mathf.Abs(Mathf.Cos(staticRandomID * 0.1f)) * 100;
        v -= (int)v;

        staticRandomID++;
        return v;
    }

    private void Start()
    {
        #region Init Positions
        const int counts = 400 * 400;
        Vector3[] posList = new Vector3[counts];
        for (int i = 0; i < counts; ++i)
        {
            int x = i % 400;
            int z = i / 400;
            posList[i] = new Vector3(x * 0.5f + StaticRandom(), 0, z * 0.5f + StaticRandom());
            posList[i].y = 0;
        }
        #endregion

        #region Init CS
        args = new uint[] { Mesh.GetIndexCount(0), 0, 0, 0, 0 };
        bufferWithArgs = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);
        bufferWithArgs.SetData(args);
        CSCullingID = CS.FindKernel("CSCulling");

        posBuffer = new ComputeBuffer(counts, sizeof(float) * 3);
        posBuffer.SetData(posList);
        posVisibleBuffer = new ComputeBuffer(counts, sizeof(float) * 3);
        CS.SetBuffer(CSCullingID, "bufferWithArgs", bufferWithArgs);
        CS.SetBuffer(CSCullingID, "posAllBuffer", posBuffer);
        CS.SetBuffer(CSCullingID, "posVisibleBuffer", posVisibleBuffer);

        Mat.SetBuffer("posVisibleBuffer", posVisibleBuffer);
        #endregion
    }

    void Culling()
    {
        CS.SetFloat("useHzb", useHzb ? 1 : 0);
        args[1] = 0;
        bufferWithArgs.SetData(args);

        CS.SetTexture(CSCullingID, "_DepthTex", DepthTex);

        CS.SetVector("camPos", Camera.main.transform.position);
        CS.SetVector("camDir", Camera.main.transform.forward);
        CS.SetFloat("camHalfFov", Camera.main.fieldOfView / 2);

        Matrix4x4 VP = GL.GetGPUProjectionMatrix(Camera.main.projectionMatrix, false) * Camera.main.worldToCameraMatrix;
        CS.SetMatrix("_Matrix_VP", VP);
        
        CS.Dispatch(CSCullingID, 400 / 16, 400 / 16, 1);
    }

    private void Update()
    {
        Culling();

        Graphics.DrawMeshInstancedIndirect(Mesh, 0, Mat, 
            bounds, 
            bufferWithArgs, 0, null, ShadowCastingMode.Off, false);
    }

    private void OnDisable()
    {
        bufferWithArgs.Release();
        posBuffer.Release();
        posVisibleBuffer.Release();
    }
}