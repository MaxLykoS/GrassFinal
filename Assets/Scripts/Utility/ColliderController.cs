using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ColliderController : MonoBehaviour
{
    public List<Transform> Colliders;
    public float speed;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKey(KeyCode.O))
            foreach (Transform tr in Colliders)
                tr.Translate(Vector3.right * speed);
        else if (Input.GetKey(KeyCode.P))
            foreach (Transform tr in Colliders)
                tr.Translate(-Vector3.right * speed);
    }
}
