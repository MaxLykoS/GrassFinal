using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Test : MonoBehaviour
{
    public Transform t;
    void Start()
    {
        float angle = Vector3.Angle(t.position, Vector3.forward);
        t.position = MathUtl.AngleAxis3x3(Mathf.Deg2Rad * (angle - 80.0f), Vector3.right) * t.position;
    }
}
