using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum GrassType
{ 
    Geo,
    StarBillboard,
    Billboard,
    Unknown
}

public class GrassPatchRenderer
{
    public Mesh grassMesh { get; private set; }

    public GrassType DrawingType;
    public Vector2I Root { get; private set; }

    private static Material GeoMaterial;
    private static Material StarMaterial;
    private static Material BillboardMaterial;
    private static Mesh GroundMesh;
    private static Material GroundMaterial;

    private static List<Matrix4x4> starBillboards;
    private static List<Matrix4x4> billboards;
    private static HashSet<Matrix4x4> geoGrounds;

    private static MaterialPropertyBlock geoGroundsBlock;

    public bool isGeo { get { return DrawingType == GrassType.Geo; } }
    public bool isStar { get { return DrawingType == GrassType.StarBillboard; } }
    public bool isBill { get { return DrawingType == GrassType.Billboard; } }

    public GrassPatchRenderer(Vector2I root)
    {
        this.DrawingType = GrassType.Unknown;
        this.Root = root;
    }

    public static void SetMatAndMesh(Material geoMaterial, Material starMaterial, Material billboardMaterial, Material groundMaterial, Mesh groundMesh)
    {
        GeoMaterial = geoMaterial;
        StarMaterial = starMaterial;
        BillboardMaterial = billboardMaterial;
        GroundMaterial = groundMaterial;
        GroundMesh = groundMesh;

        starBillboards = new List<Matrix4x4>();
        billboards = new List<Matrix4x4>();
        geoGrounds = new HashSet<Matrix4x4>();

        geoGroundsBlock = new MaterialPropertyBlock();
        //geoGroundsBlock.SetColor("_Color", new Vector4(16.0f / 255.0f, 96.0f / 255.0f, 18.0f / 255.0f, 1.0f));
    }

    public void SwitchType(GrassType type, Mesh grassMesh)
    {
        this.grassMesh = grassMesh;
        Matrix4x4 m = Matrix4x4.Translate(Root.ToVector3());
        if (type == GrassType.Geo)
        {
            if (!geoGrounds.Contains(m))
            {
                geoGrounds.Add(m);
            }
        }
        else if (type == GrassType.StarBillboard)
        {
            if (DrawingType == GrassType.Geo)
                geoGrounds.Remove(m);
        }
        else
        {
            if (DrawingType == GrassType.Geo)
                geoGrounds.Remove(m);
        }

        DrawingType = type;
    }

    public void DrawGeoGrass()
    {
        if (DrawingType == GrassType.Geo && grassMesh) 
        {
            Graphics.DrawMesh(grassMesh, Matrix4x4.identity, GeoMaterial, 0);
        }
    }

    public static void DrawInstancing()
    {
        // geo grass ground
        Matrix4x4[] geoGroundArray = new Matrix4x4[geoGrounds.Count];
        geoGrounds.CopyTo(geoGroundArray);

        #region for differentiate all grass patches(delete this in the future)
        Vector4[] colors = new Vector4[geoGrounds.Count];
        for (int i = 0; i < colors.Length; ++i)
            colors[i].x = colors[i].y = colors[i].z = (float)i / (float)colors.Length;
        geoGroundsBlock.SetVectorArray("_Color", colors);
        #endregion

        Graphics.DrawMeshInstanced(GroundMesh, 0, GroundMaterial, geoGroundArray, geoGrounds.Count, geoGroundsBlock);

        // star billboard

        // billboard
    }
}
