using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GrassPatchRenderer
{
    private MeshRenderer grassRenderer;
    private MeshFilter grassFilter;

    private MeshRenderer groundRenderer;
    private MeshFilter groundFilter;

    public GrassPatchRenderer(Vector3 root, Transform mgr, Material grassMat, Material groundMat, Mesh grassMesh, Mesh groundMesh)
    {
        GameObject grass = new GameObject("Grass");
        GameObject ground = new GameObject("Ground");
        //grass.transform.SetPositionAndRotation(root, Quaternion.identity);
        ground.transform.SetPositionAndRotation(root, Quaternion.Euler(Vector3.left * 90));
        ground.transform.SetParent(grass.transform);
        grass.transform.SetParent(mgr);

        grassRenderer = grass.AddComponent<MeshRenderer>();
        grassRenderer.sharedMaterial = grassMat;
        grassFilter = grass.AddComponent<MeshFilter>();
        grassFilter.sharedMesh = grassMesh;

        groundRenderer = ground.AddComponent<MeshRenderer>();
        groundRenderer.sharedMaterial = groundMat;
        groundFilter = ground.AddComponent<MeshFilter>();
        groundFilter.sharedMesh = groundMesh;
    }
}
