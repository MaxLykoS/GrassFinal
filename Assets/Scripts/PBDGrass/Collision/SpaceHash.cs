﻿using System.Collections.Generic;
using UnityEngine;

namespace PBD
{
    public class SpaceHash
    {
        public Dictionary<MyVector2Int, List<PBDGrassBody>> Hash;

        public int Width;
        public int Length;
        public Vector3 root;

        public SpaceHash(Vector3 root, int w, int l, ref PBDGrassBody[] bodies)
        {
            this.Width = w;
            this.Length = l;
            this.root = root;
            this.Hash = new Dictionary<MyVector2Int, List<PBDGrassBody>>();

            foreach (PBDGrassBody body in bodies)
            {
                MyVector2Int key = GenCoord(ref body.OriginPos[0]);

                if (Hash.ContainsKey(key))
                {
                    Hash[key].Add(body);
                }
                else
                {           
                    List<PBDGrassBody> newList = new List<PBDGrassBody>();
                    newList.Add(body);
                    Hash.Add(key, newList);
                }
            }
        }

        public List<PBDGrassBody> QueryPossibleBones(Vector3 place)
        {
            MyVector2Int key = GenCoord(ref place);
            List<PBDGrassBody> possibleBones;
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
