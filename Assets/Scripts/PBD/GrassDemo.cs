using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PBD;

public class GrassDemo : MonoBehaviour
{
    public int Segments = 3;
    public Vector3 windForce;
    public Material material;
    public float CollisionRadius;
    public List<Transform> colliders;

    private PBDSolver solver;
    private GrassBody grassBody;

    void Start()
    {
        Application.targetFrameRate = 60;
        GetComponent<MeshRenderer>().sharedMaterial = material;

        solver = new PBDSolver(3.0f)
        {
            Gravity = Vector3.down * 9.8f,
            WindForce = windForce
        };

        grassBody = new GrassBody(Vector3.zero, Segments, 0.5f, 0.05f, 0.38f, 1.0f);
        solver.AddGrass(grassBody);
        GetComponent<MeshFilter>().sharedMesh = grassBody.GrassMesh;

        foreach (Transform tr in colliders)
        {
            solver.AddCollider(new SphereCollision(tr, CollisionRadius));
        }
    }

    void FixedUpdate()
    {
        solver.WindForce = windForce;

        solver.Update((float)(1.0 / 60.0 / 3.0));
    }
}
