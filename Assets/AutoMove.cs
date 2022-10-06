using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AutoMove : MonoBehaviour
{
    public GameObject mover;
    public bool startMoving;
    public float speed = 0.01f;

    // Update is called once per frame
    void FixedUpdate()
    {
        if(startMoving)
            mover.transform.Translate(Vector3.forward * speed);
    }
}
