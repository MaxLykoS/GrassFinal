using UnityEngine;

namespace GrassFinal.PBDGrass
{
    // The full identity is (integer world cell, leaf 0..31). Hashes are random
    // attributes, never unique IDs or GPU addresses. Keep in sync with the include.
    public static class GrassWorldIdentity
    {
        public static uint Hash(uint x)
        { unchecked { x^=x>>16; x*=0x7feb352d; x^=x>>15; x*=0x846ca68b; return x^(x>>16); } }
        public static uint Seed(Vector2Int cell,int leaf)
        { unchecked { return Hash((uint)cell.x*73856093u ^ (uint)cell.y*19349663u ^ (uint)leaf*83492791u); } }
        public static float Unit(uint x) => (Hash(x)&0xffffff)/16777216f;
        public static float LeafSeed(Vector2Int cell,int leaf) => Unit(Seed(cell,leaf)+2);
        public static Vector3 Root(Vector2Int cell,int leaf,float ground)
        {
            uint h=Seed(cell,leaf),variation=Seed(cell,67);
            int x=((leaf&1)<<2)+((leaf>>2&1)<<1)+(leaf>>4&1);
            int z=((leaf>>1&1)<<1)+(leaf>>3&1);
            float u=(x+.1f+.8f*Unit(h))/8,v=(z+.1f+.8f*Unit(h+1))/4;
            if((variation&1)!=0)u=1-u; if((variation&2)!=0)v=1-v;
            if((variation&4)!=0) { float swap=u;u=v;v=swap; }
            return new Vector3(cell.x+u,ground,cell.y+v);
        }
        public static Quaternion Yaw(Vector2Int cell) => Quaternion.AngleAxis(Unit(Seed(cell,73))*360,Vector3.up);
        public static int Mod(int x,int size) { int result=x%size; return result<0?result+size:result; }
        public static int Slot(Vector2Int cell,int size) => Mod(cell.x,size)+Mod(cell.y,size)*size;
    }
}
