using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public struct Vector2I
{
    int X, Z;

    public Vector2I(int x, int z)
    {
        this.X = x;
        this.Z = z;
    }

    public Vector3 ToVector3()
    {
        return new Vector3(X, 0, Z);
    }
}