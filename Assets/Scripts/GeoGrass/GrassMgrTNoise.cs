using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class GrassMgrTNoise : MonoBehaviour
{
    public Transform[] obstacles;
    private Vector4[] obstaclePositions = new Vector4[100];

    /*private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Material grassMaterial;
    private void Start()
    {
        meshFilter = gameObject.GetComponent<MeshFilter>();
        meshRenderer = gameObject.GetComponent<MeshRenderer>();
        grassMaterial = meshRenderer.sharedMaterial;
    }
    */
    private void Update()
    {
        for (int i = 0; i < obstacles.Length; i++)
        {
            obstaclePositions[i] = obstacles[i].position;
        }

        Shader.SetGlobalFloat("_PositionArrayLen", obstacles.Length);
        Shader.SetGlobalVectorArray("_ObstaclePositions", obstaclePositions);
    }
}       
