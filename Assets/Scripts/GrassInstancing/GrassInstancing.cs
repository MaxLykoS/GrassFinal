using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public struct GrassInfo
{
    public Matrix4x4 worldMat;
    public int grassType;

    public GrassInfo(Matrix4x4 TRS, int type)
    {
        this.worldMat = TRS;
        this.grassType = type;
    }
}
public class GrassInstancing : MonoBehaviour
{
    public static RenderTexture DepthTex;

    [Header("»æÖÆ²ÄÖÊ")]
    public Material GrassMaterialBackup;
    private Material GrassMaterialLOD0;
    private Material GrassMaterialLOD1;

    private ComputeBuffer grassPoolBufferLOD0;
    private ComputeBuffer grassPoolBufferLOD1;
    private ComputeBuffer grassPosBuffer;
    private ComputeBuffer argsBufferLOD0;
    private ComputeBuffer argsBufferLOD1;
    private uint[] argsLOD0 = new uint[5] { 0, 0, 0, 0, 0 };
    private uint[] argsLOD1 = new uint[5] { 0, 0, 0, 0, 0 };
    private Bounds drawBounds;

    [Header("ÌÞ³ý×ÅÉ«Æ÷")]
    public ComputeShader CS;
    private ComputeBuffer posVisibleBufferLOD0;
    private ComputeBuffer posVisibleBufferLOD1;
    private int CSCullingID;

    [Header("MainCamera")]
    public Camera cam;

    [Header("½ÅÓ¡")]
    public Camera StampCam;
    public float StampSize = 100;
    public RenderTexture StampRT;
    [Range(0, 1)] 
    public float stampMin = 0.1f;
    [Range(0.001f, 0.1f)]
    public float _GrassflakeCount;
    [Range(0f, 1f)]
    public float _GrassflakeOpacity;
    public Material StampRecoverMat;

    void Start()
    {
        Init();
    }

    private void Init()
    {
        GrassMaterialLOD0 = new Material(GrassMaterialBackup);
        GrassMaterialLOD0.CopyPropertiesFromMaterial(GrassMaterialBackup);
        GrassMaterialLOD1 = new Material(GrassMaterialBackup);
        GrassMaterialLOD1.CopyPropertiesFromMaterial(GrassMaterialBackup);

        InitStamp();

        drawBounds = new Bounds(Vector3.zero, new Vector3(10000, 10000, 10000));

        FillArgsBuffer();
        FillPosBuffer();

        InitCulling();
        InitMergeInstancing();
    }

    private void InitCulling()
    {
        CSCullingID = CS.FindKernel("CSCulling");

        posVisibleBufferLOD0 = new ComputeBuffer(GrassPool.LENGTH * GrassPool.LENGTH, sizeof(float) * 16 + sizeof(int));
        posVisibleBufferLOD1 = new ComputeBuffer(GrassPool.LENGTH * GrassPool.LENGTH, sizeof(float) * 16 + sizeof(int));
        CS.SetBuffer(CSCullingID, "bufferWithArgsLOD0", argsBufferLOD0);
        CS.SetBuffer(CSCullingID, "bufferWithArgsLOD1", argsBufferLOD1);
        CS.SetBuffer(CSCullingID, "posAllBuffer", grassPosBuffer);
        CS.SetBuffer(CSCullingID, "posVisibleBufferLOD0", posVisibleBufferLOD0);
        CS.SetBuffer(CSCullingID, "posVisibleBufferLOD1", posVisibleBufferLOD1);

        GrassMaterialLOD0.SetBuffer("posVisibleBuffer", posVisibleBufferLOD0);
        GrassMaterialLOD1.SetBuffer("posVisibleBuffer", posVisibleBufferLOD1);
    }

    private void Cull()
    {
        argsLOD0[1] = 0;
        argsBufferLOD0.SetData(argsLOD0);

        argsLOD1[1] = 0;
        argsBufferLOD1.SetData(argsLOD1);

        CS.SetTexture(CSCullingID, "_DepthTex", DepthTex);

        CS.SetVector("camPos", cam.transform.position);
        CS.SetVector("camDir", cam.transform.forward);
        CS.SetFloat("camHalfFov", cam.fieldOfView / 2);

        Matrix4x4 VP = GL.GetGPUProjectionMatrix(cam.projectionMatrix, false) * cam.worldToCameraMatrix;
        CS.SetMatrix("_Matrix_VP", VP);

        CS.Dispatch(CSCullingID, Mathf.Max(GrassPool.LENGTH * GrassPool.LENGTH / 1024, 1), 1, 1);
    }

    private void FillArgsBuffer()
    {
        argsBufferLOD0 = new ComputeBuffer(1, argsLOD0.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        argsLOD0[0] = GrassPool.Instance.GetIndexCountLOD(0);
        argsLOD0[1] = (uint)0;
        argsLOD0[2] = GrassPool.Instance.GetIndexStartLOD(0);
        argsLOD0[3] = GrassPool.Instance.GetBaseVertexLOD(0);
        argsBufferLOD0.SetData(argsLOD0);

        argsBufferLOD1 = new ComputeBuffer(1, argsLOD0.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        argsLOD1[0] = GrassPool.Instance.GetIndexCountLOD(1);
        argsLOD1[1] = (uint)0;
        argsLOD1[2] = GrassPool.Instance.GetIndexStartLOD(1);
        argsLOD1[3] = GrassPool.Instance.GetBaseVertexLOD(1);
        argsBufferLOD1.SetData(argsLOD1);
    }

    private void FillPosBuffer()
    {
        grassPosBuffer = new ComputeBuffer(GrassPool.LENGTH * GrassPool.LENGTH, sizeof(float) * (16) + sizeof(int));

        GrassInfo[] infos = GrassPool.Instance.GetGrassPosBuffer();
        grassPosBuffer.SetData(infos);
    }

    private void InitMergeInstancing()
    {
        Vector3[] poolLOD0 = GrassPool.Instance.GetGrassPoolBuffer(0);
        Vector3[] poolLOD1 = GrassPool.Instance.GetGrassPoolBuffer(1);

        grassPoolBufferLOD0 = new ComputeBuffer(poolLOD0.Length, sizeof(float) * 3);
        grassPoolBufferLOD1 = new ComputeBuffer(poolLOD1.Length, sizeof(float) * 3);
        grassPoolBufferLOD0.SetData(poolLOD0);
        grassPoolBufferLOD1.SetData(poolLOD1);
        GrassMaterialLOD0.SetBuffer("grassPoolBuffer", grassPoolBufferLOD0);
        GrassMaterialLOD0.SetInteger("_GrassPoolStride", GrassPool.Instance.GetPoolStride(0));
        GrassMaterialLOD1.SetBuffer("grassPoolBuffer", grassPoolBufferLOD1);
        GrassMaterialLOD1.SetInteger("_GrassPoolStride", GrassPool.Instance.GetPoolStride(1));
    }

    private void InitStamp()
    {
        StampCam.targetTexture = StampRT;
    }

    void Update()
    {
        Cull();

        StampRecoverMat.SetFloat("_GrassflakeCount", _GrassflakeCount);
        StampRecoverMat.SetFloat("_GrassflakeOpacity", _GrassflakeOpacity);
        RenderTexture temp = RenderTexture.GetTemporary(StampRT.width, StampRT.height, 0, StampRT.format);
        Graphics.Blit(StampRT, temp, StampRecoverMat);
        Graphics.Blit(temp, StampRT);
        RenderTexture.ReleaseTemporary(temp);

        Vector4 _StampVector = new Vector4(StampCam.transform.position.x, stampMin, StampCam.transform.position.z,
            StampSize);
        GrassMaterialLOD0.SetVector("_StampVector", _StampVector);
        Graphics.DrawMeshInstancedIndirect(GrassPool.Instance.GetMeshLOD(0), 0, GrassMaterialLOD0, drawBounds, argsBufferLOD0, 0,
            null, UnityEngine.Rendering.ShadowCastingMode.Off, false, 0);
        GrassMaterialLOD1.SetVector("_StampVector", _StampVector);
        Graphics.DrawMeshInstancedIndirect(GrassPool.Instance.GetMeshLOD(1), 0, GrassMaterialLOD1, drawBounds, argsBufferLOD1, 0,
            null, UnityEngine.Rendering.ShadowCastingMode.Off, false, 0);
    }

    private void LateUpdate()
    {
        StampCam.transform.position = cam.transform.position + Vector3.up * 500;
    }

    private void OnDestroy()
    {
        grassPosBuffer.Release();

        argsBufferLOD0.Release();
        argsBufferLOD1.Release();

        posVisibleBufferLOD0.Release();
        posVisibleBufferLOD1.Release();

        grassPoolBufferLOD0.Release();
        grassPoolBufferLOD1.Release();
    }
}
