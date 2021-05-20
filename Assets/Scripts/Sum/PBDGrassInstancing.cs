using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PBD;

public class PBDGrassInstancing : MonoBehaviour
{
    const int GrassPatchWidth = 10;
    const int GrassPatchLength = 10;
    const int GrassBodyCounts = 1;
    const float CollisionRadius = 1.0f;

    Vector3 WindForce = Vector3.up * 1000;

    public Material GrassMaterial;
    public Transform ball;

    private PBDSolver GEOsolver;
    private Mesh grassMesh;
    private GeoGrassPatch patch;

    void Start()
    {
        Application.targetFrameRate = 60;

        GEOsolver = new PBDSolver(3.0f)
        {
            Gravity = Vector3.down * 9.8f,
            WindForce = WindForce
        };

        patch = new GeoGrassPatch(Vector3.zero, GrassPatchWidth, GrassPatchLength, GrassBodyCounts, null);
        GEOsolver.AddGrassPatch(patch);
        grassMesh = patch.PatchMesh;

        GEOsolver.AddCollider(new SphereCollision(ball, CollisionRadius));
    }

    void FixedUpdate()
    {
        GEOsolver.Update((float)(1.0 / 60.0 / 3.0));
    }


    private void Update()
    {
        Graphics.DrawMesh(grassMesh, Matrix4x4.identity, GrassMaterial, 0);
    }
}
