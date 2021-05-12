using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GrassLand
{
    public Vector3 Pos { get { return pos; } }
    private Vector3 pos;
    public GameObject Go { get { return go; } }
    private GameObject go;
    public GrassLand(Vector3 _pos, GameObject _go)
    {
        pos = _pos;
        go = _go;

        go.transform.position = pos;
    }
}
