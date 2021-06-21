using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GrassInstancing : MonoBehaviour
{
    public static RenderTexture DepthTex;

    [Header("最大绘制数量")]
    public int maxCount = 1000000;

    [Header("绘制模型")]
    public Mesh GrassMesh;

    [Header("绘制材质")]
    public Material GrassMaterial;

    [Header("绘制范围")]
    public Vector2 Size;

    private ComputeBuffer grassPosBuffer;
    private ComputeBuffer argsBuffer;
    private uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
    private Bounds drawBounds;

    [Header("剔除着色器")]
    public ComputeShader CS;
    private ComputeBuffer posVisibleBuffer;
    private int CSCullingID;

    [Header("Camera")]
    public Camera cam;

    [Header("脚印")]
    public Stamp stamp;
    
    [Header("压低程度")]
    [Range(0, 1)] public float stampMin = 0.1f;

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
        drawBounds = new Bounds(Vector3.zero, new Vector3(Size.x, 1.0f, Size.y));

        FillArgsBuffer();
        FillPosBuffer();

        InitCulling();
    }

    private void InitCulling()
    {
        CSCullingID = CS.FindKernel("CSCulling");

        posVisibleBuffer = new ComputeBuffer(maxCount, sizeof(float) * (16 + 3));
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

        CS.Dispatch(CSCullingID, maxCount / 64, 1, 1);
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
        grassPosBuffer = new ComputeBuffer(maxCount, sizeof(float) * (16 + 3));
        GrassInfo[] infos = new GrassInfo[maxCount];

        float maxX = Size.x / 2;
        float minX = -maxX;
        float maxZ = Size.y / 2;
        float minZ = -maxZ;

        for (int i = 0; i < maxCount; i++)
        {
            float x = Random.Range(minX, maxX);
            float z = Random.Range(minZ, maxZ);

            infos[i].worldMat = Matrix4x4.TRS(new Vector3(x, 0, z), Quaternion.Euler(0, Random.Range(0, 360), 0), Vector3.one * 100);
            infos[i].worldPos = new Vector3(x, 0, z);
        }
        grassPosBuffer.SetData(infos);
    }

    void Update()
    {
        Cull();

        if (stamp != null)
        {
            GrassMaterial.SetVector("_StampVector", 
                new Vector4(stamp.Center.x, stampMin, stamp.Center.z, stamp.Size));
        }

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
