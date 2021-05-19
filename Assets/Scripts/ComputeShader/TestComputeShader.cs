using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public class TestComputeShader : MonoBehaviour
{
    const int Range = 1024;
    const int Population = Range * Range;

    public Mesh mesh;  // 手拖unity内置mesh
    public Material mat;  // 手拖

    private ComputeBuffer meshPropertiesBuffer;
    private ComputeBuffer argsBuffer;

    public Transform pusher;  // 手拖
    public ComputeShader CS;  // 手拖

    private struct MeshProperties
    {
        public Matrix4x4 mat;
        public Vector4 color;

        public static int Size() { return sizeof(float) * 4 * 4 + sizeof(float) * 4; }
    }

    private void Start()
    {
        Init();
    }

    private void Update()
    {
        UpdateWorldMatAndDraw();
    }

    private void OnDisable()
    {
        meshPropertiesBuffer.Release();
        meshPropertiesBuffer = null;

        argsBuffer.Release();
        argsBuffer = null;
    }


    private void Init()
    {
        uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
        args[0] = (uint)mesh.GetIndexCount(0);
        args[1] = (uint)Population;
        args[2] = (uint)mesh.GetIndexStart(0);
        args[3] = (uint)mesh.GetBaseVertex(0);
        foreach (uint i in args)
            Debug.Log(i);
        argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        argsBuffer.SetData(args);

        MeshProperties[] properties = new MeshProperties[Population];
        for (int i = 0; i < Population; ++i)
        {
            properties[i].mat = Matrix4x4.TRS(new Vector3(Random.Range(-Range, Range), Random.Range(-Range, Range), Random.Range(-Range, Range)),
                Quaternion.Euler(Random.Range(-180, 180), Random.Range(-180, 180), Random.Range(-180, 180)),
                Vector3.one);
            float color = (float)i / (float)Population;
            properties[i].color = new Vector4(color, color, color, 1);
        }
        meshPropertiesBuffer = new ComputeBuffer(Population, MeshProperties.Size());
        meshPropertiesBuffer.SetData(properties);
        mat.SetBuffer("_Properties", meshPropertiesBuffer);

        int kernelHandler = CS.FindKernel("CSMain");
        CS.SetBuffer(kernelHandler, "_Properties", meshPropertiesBuffer);
    }

    private void UpdateWorldMatAndDraw()
    {
        int kernelHandler = CS.FindKernel("CSMain");
        CS.SetVector("_ColliderPosition", pusher.position);
        CS.Dispatch(kernelHandler, Population / 64, 1, 1);

        const float BoundSize = 10000.0f;
        Graphics.DrawMeshInstancedIndirect(mesh, 0, mat,
            new Bounds(Vector3.zero, new Vector3(BoundSize, BoundSize, BoundSize)),
            argsBuffer);
    }
}