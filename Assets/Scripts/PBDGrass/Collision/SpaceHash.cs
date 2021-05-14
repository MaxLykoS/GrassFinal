using System.Collections.Generic;
using UnityEngine;

namespace PBD
{
    public struct Vector2I
    {
        int X, Z;

        public Vector2I(int x, int z)
        {
            this.X = x;
            this.Z = z;
        }
    }
    public class SpaceHash
    {
        public Dictionary<Vector2I, List<GrassBody>> Hash;

        public int Width;
        public int Length;
        public Vector3 root;

        public SpaceHash(Vector3 root, int w, int l, ref GrassBody[] bodies)
        {
            this.Width = w;
            this.Length = l;
            this.root = root;

            foreach (GrassBody body in bodies)
            { 
                
            }
        }

        public IList<GrassBody> QueryPossibleBones(ref Vector3 place)
        {
            return null;
        }

        public static Vector2I GenCoord(Vector3 root)
        {
            int x = Mathf.FloorToInt(root.x);
            int z = Mathf.FloorToInt(root.z);

            return new Vector2I(x, z);
        }
    }
}
