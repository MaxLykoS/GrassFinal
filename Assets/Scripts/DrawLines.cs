using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class DrawLines : MonoBehaviour
{
    public List<Transform> gos;
    public List<int> indexs;

    void Update()
    {
        for (int i = 0; i < indexs.Count; i+=2)
        {
            Debug.DrawLine(gos[indexs[i]].position, gos[indexs[i + 1]].position);
        }
    }
}
