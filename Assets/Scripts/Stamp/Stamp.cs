using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class Stamp : MonoBehaviour
{
    //public RenderTexture StampRT;
    public Transform CameraTr;
    private Camera cam;
    private float height;
    public float Size = 100;

    public Vector3 Center
    {
        get { return transform.position; }
    }

    public RenderTexture GetTex()
    {
        return cam != null ? cam.targetTexture : null;
    }

    void Start()
    {
        cam = GetComponent<Camera>();
        //cam.targetTexture = StampRT;
        height = cam.farClipPlane * 0.9f;
        if (CameraTr == null)
            CameraTr = Camera.main.transform;
    }

    void LateUpdate()
    {
        transform.position = CameraTr.position + Vector3.up * 500;
    }
}