using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MyComputeShader : MonoBehaviour
{
    public int SphereAmount = 17;
    public ComputeShader shader;

    ComputeBuffer resultBuffer;
    int kernel;
    uint threadGroupSizeX;
    Vector3[] output;

    private void Start()
    {
        // program we're executing
        kernel = shader.FindKernel("CSMain");
        shader.GetKernelThreadGroupSizes(kernel, out threadGroupSizeX, out _, out _);

        // buffer on the gpu in the ram
        resultBuffer = new ComputeBuffer(SphereAmount, sizeof(float) * 3);
        output = new Vector3[SphereAmount];
    }

    private void Update()
    {
        shader.SetBuffer(kernel, "Result", resultBuffer);
        int threadGroups = (int)((SphereAmount + (threadGroupSizeX - 1)) / threadGroupSizeX);
        shader.Dispatch(kernel, threadGroups, 1, 1);
        resultBuffer.GetData(output);
    }

    private void OnDestroy()
    {
        resultBuffer.Dispose();
    }
}
