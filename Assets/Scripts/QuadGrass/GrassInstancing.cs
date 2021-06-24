using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GrassInstancing : MonoBehaviour
{
    public static RenderTexture DepthTex;

    [Header("绘制模型")]
    private Mesh GrassMesh;

    [Header("绘制材质")]
    public Material GrassMaterial;

    [Header("绘制宽度（矩形）")]
    public int Length;

    private ComputeBuffer grassPosBuffer;
    private ComputeBuffer argsBuffer;
    private uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
    private Bounds drawBounds;

    [Header("剔除着色器")]
    public ComputeShader CS;
    private ComputeBuffer posVisibleBuffer;
    private int CSCullingID;

    [Header("MainCamera")]
    public Camera cam;

    [Header("脚印")]
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

    public struct GrassInfo
    {
        public Matrix4x4 worldMat;
        public Vector3 worldPos;
    }

    void Start()
    {
        Init();
    }

    private void Init()
    {
        InitStamp();

        PBD.PBDGrassPatch p1 = new PBD.PBDGrassPatch(Vector3Int.zero, 1, 1, 64);
        GrassMesh = p1.PatchMesh;

        drawBounds = new Bounds(Vector3.zero, new Vector3(10000, 10000, 10000));

        FillArgsBuffer();
        FillPosBuffer();

        InitCulling();
    }

    private void InitCulling()
    {
        CSCullingID = CS.FindKernel("CSCulling");

        posVisibleBuffer = new ComputeBuffer(Length * Length, sizeof(float) * (16 + 3));
        CS.SetBuffer(CSCullingID, "bufferWithArgs", argsBuffer);
        CS.SetBuffer(CSCullingID, "posAllBuffer", grassPosBuffer);
        CS.SetBuffer(CSCullingID, "posVisibleBuffer", posVisibleBuffer);

        GrassMaterial.SetBuffer("posVisibleBuffer", posVisibleBuffer);
    }

    private void Cull()
    {
        args[1] = 0;
        argsBuffer.SetData(args);

        CS.SetTexture(CSCullingID, "_DepthTex", DepthTex);

        CS.SetVector("camPos", cam.transform.position);
        CS.SetVector("camDir", cam.transform.forward);
        CS.SetFloat("camHalfFov", cam.fieldOfView / 2);

        Matrix4x4 VP = GL.GetGPUProjectionMatrix(cam.projectionMatrix, false) * cam.worldToCameraMatrix;
        CS.SetMatrix("_Matrix_VP", VP);

        CS.Dispatch(CSCullingID, Mathf.Max(Length * Length / 1024, 1), 1, 1);
    }

    private void FillArgsBuffer()
    {
        argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        args[0] = GrassMesh.GetIndexCount(0);
        args[1] = (uint)0;
        args[2] = GrassMesh.GetIndexStart(0);
        args[3] = GrassMesh.GetBaseVertex(0);
        argsBuffer.SetData(args);
    }

    private void FillPosBuffer()
    {
        grassPosBuffer = new ComputeBuffer(Length * Length, sizeof(float) * (16 + 3));
        GrassInfo[] infos = new GrassInfo[Length * Length];

        int id = 0;
        for (int i = 0; i < Length; i++)
            for (int j = 0; j < Length; j++)
            {
                infos[id].worldMat = Matrix4x4.TRS(new Vector3(i, 0, j), Quaternion.Euler(0, Random.Range(0, 360), 0), Vector3.one);
                infos[id].worldPos = new Vector3(i, 0, j);
                id++;
            }
        grassPosBuffer.SetData(infos);
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

        GrassMaterial.SetVector("_StampVector", 
            new Vector4(StampCam.transform.position.x, stampMin, StampCam.transform.position.z, 
            StampSize));
        Graphics.DrawMeshInstancedIndirect(GrassMesh, 0, GrassMaterial, drawBounds, argsBuffer, 0,
            null, UnityEngine.Rendering.ShadowCastingMode.Off, false, 0);
    }

    private void OnDestroy()
    {
        grassPosBuffer.Release();
        argsBuffer.Release();

        posVisibleBuffer.Release();
    }
}
