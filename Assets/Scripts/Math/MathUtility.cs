using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class MathUtility
{
    public static float Rand(Vector3 co)
    {
        float r = Mathf.Sin(Vector3.Dot(co, new Vector3(12.9898f, 78.233f, 53.539f))) * 43758.5453f;
        return r - (int)r;
    }
    public static Matrix3x3 AngleAxis3x3(float angle, Vector3 axis)
    {
        float c, s;
        s = Mathf.Sin(angle);
        c = Mathf.Cos(angle);

        float t = 1 - c;
        float x = axis.x;
        float y = axis.y;
        float z = axis.z;

        return new Matrix3x3(
            t * x * x + c, t * x * y - s * z, t * x * z + s * y,
            t * x * y + s * z, t * y * y + c, t * y * z - s * x,
            t * x * z - s * y, t * y * z + s * x, t * z * z + c
        );
    }

    public static int Clamp(int value, int min, int max)
    {
        if (value >= max)
            value = max;
        else if (value <= min)
            value = min;
        return value;
    }

    public static string V2S(ref Vector3 v)
    {
        return string.Format("{0} {1} {2}", v.x, v.y, v.z);
    }
}
