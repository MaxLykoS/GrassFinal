using System.Collections.Generic;
using UnityEngine;

namespace PBD
{
    public class SpaceHash
    {
        public Dictionary<MyVector2Int, List<GrassBody>> Hash;

        public int Width;
        public int Length;
        public Vector3 root;

        public SpaceHash(Vector3 root, int w, int l, ref GrassBody[] bodies)
        {
            this.Width = w;
            this.Length = l;
            this.root = root;
            this.Hash = new Dictionary<MyVector2Int, List<GrassBody>>();

            foreach (GrassBody body in bodies)
            {
                MyVector2Int key = GenCoord(ref body.OriginPos[0]);

                if (Hash.ContainsKey(key))
                {
                    Hash[key].Add(body);
                }
                else
                {           
                    List<GrassBody> newList = new List<GrassBody>();
                    newList.Add(body);
                    Hash.Add(key, newList);
                }
            }
        }

        public List<GrassBody> QueryPossibleBones(Vector3 place)
        {
            MyVector2Int key = GenCoord(ref place);
            List<GrassBody> possibleBones;
            if (Hash.TryGetValue(key, out possibleBones))
                return possibleBones;
            else
                return null;
        }

        public static MyVector2Int GenCoord(ref Vector3 root)
        {
            int x = Mathf.FloorToInt(root.x);
            int z = Mathf.FloorToInt(root.z);

            return new MyVector2Int(x, z);
        }
    }
}
