using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public struct MyVector2Int
{
    int X, Z;

    public MyVector2Int(int x, int z)
    {
        this.X = x;
        this.Z = z;
    }

    public Vector3 ToVector3()
    {
        return new Vector3(X, 0, Z);
    }
}