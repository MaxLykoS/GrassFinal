using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PBD;

public class GrassDemo : MonoBehaviour
{
    const int GrassPatchWidth = 10;
    const int GrassPatchLength = 10;
    const int GrassBodyCounts = 100;
    const float CollisionRadius = 1.0f;
    Vector3 WindForce = Vector3.up * 1000;

    public Mesh GroundMesh;
    public Material GrassMaterial;
    public Material GroundMaterial;
    public List<Transform> colliders;

    private PBDSolver solver;
    private List<GrassPatchRenderer> renderers;
    private List<GrassPatch> patches;

    void Start()
    {
        Application.targetFrameRate = 60;
        renderers = new List<GrassPatchRenderer>();
        patches = new List<GrassPatch>();

        solver = new PBDSolver(3.0f)
        {
            Gravity = Vector3.down * 9.8f,
            WindForce = WindForce
        };

        patches.Add(new GrassPatch(Vector3.zero, GrassPatchWidth, GrassPatchLength, GrassBodyCounts));
        patches.Add(new GrassPatch(Vector3.left * 10, GrassPatchWidth, GrassPatchLength, GrassBodyCounts));
        foreach (GrassPatch patch in patches)
        {
            solver.AddGrassPatch(patch);
            renderers.Add(new GrassPatchRenderer(patch.Root, transform, GrassMaterial, GroundMaterial, patch.PatchMesh, GroundMesh));
        }

        foreach (Transform tr in colliders)
        {
            solver.AddCollider(new SphereCollision(tr, CollisionRadius));
        }
    }

    void FixedUpdate()
    {
        solver.Update((float)(1.0 / 60.0 / 3.0));
    }
}
