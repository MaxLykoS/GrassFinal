using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestDrawInstancing : MonoBehaviour
{
    public Mesh mesh;
    public Material mat;

    private Matrix4x4[] matrixs;
    private MaterialPropertyBlock block;

    private void Start()
    {
        matrixs = new Matrix4x4[1023];
        block = new MaterialPropertyBlock();
        Vector4[] colors = new Vector4[1023];

        for (var i = 0; i < 32; i++)
            for (var j = 0; j < 32; j++)
            {
                var ind = j * 32 + i;
                if (ind >= 1023) break;
                matrixs[ind] = Matrix4x4.TRS(new Vector3(i, j, 0), Quaternion.identity, Vector3.one * 0.5f);
                colors[ind] = new Vector4(1 - i / 32.0f, 1 - j / 32.0f, 1, 1);
            }

        block.SetVectorArray("_Color", colors);
    }

    // Update is called once per frame
    private void Update()
    {
        //Graphics.DrawMesh(mesh, Matrix4x4.Translate(Vector3.zero), mat, 0);
        Graphics.DrawMeshInstanced(mesh, 0, mat, matrixs, 1023, block, UnityEngine.Rendering.ShadowCastingMode.Off, false);
    }
}
