using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PBD;

public class GrassDemo : MonoBehaviour
{
    const int GrassPatchWidth = 10;
    const int GrassPatchLength = 10;
    const int GrassBodyCounts = 100;

    public Vector3 windForce;
    public Material material;
    public float CollisionRadius;
    public List<Transform> colliders;

    private PBDSolver solver;

    void Start()
    {
        Application.targetFrameRate = 60;
        GetComponent<MeshRenderer>().sharedMaterial = material;

        solver = new PBDSolver(3.0f)
        {
            Gravity = Vector3.down * 9.8f,
            WindForce = windForce
        };

        GrassPatch patch1 = new GrassPatch(Vector3.zero, GrassPatchWidth, GrassPatchLength, GrassBodyCounts);
        solver.AddGrassPatch(patch1);
        GetComponent<MeshFilter>().sharedMesh = patch1.PatchMesh;

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
