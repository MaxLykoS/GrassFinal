using System;
using UnityEngine;

namespace GrassFinal.PBDGrass
{
    [CreateAssetMenu(menuName="PBD Grass/Baked Response Type")]
    public sealed class GrassResponseType : ScriptableObject
    {
        [Serializable] public sealed class Pose
        {
            public Vector3[] Nodes=new Vector3[8];
            public Quaternion[] Frames=new Quaternion[7];
            public Vector3 C1,C2,Tip;
            public float FitError,Speed;
        }
        public string DisplayName;
        public GrassBladeShapeSettings Shape=new GrassBladeShapeSettings();
        public XPBDGrassSettings Physics=new XPBDGrassSettings();
        public int GridSize=5;
        public float MaxWind=8;
        public Pose[] Samples;
        public Vector2 AngularFrequency=new Vector2(4,4);
        public Vector2 DampingRatio=new Vector2(.3f,.3f);
        public Vector2 DynamicFitError;
        public string BakeVersion;
        public int ZeroIndex => (GridSize/2)*GridSize+GridSize/2;
        public bool IsBaked => Samples!=null && Samples.Length==GridSize*GridSize && Samples[ZeroIndex]!=null;

        public static PBDGrassPatch CreatePatch(GrassResponseType type,Vector3 center)
        {
            var random=UnityEngine.Random.state; PBDGrassPatch patch;
            try { UnityEngine.Random.InitState(12345); patch=new PBDGrassPatch(center,1,32,type.Shape); }
            finally { UnityEngine.Random.state=random; }
            var uv=new Vector2[32*15]; var indices=new int[32*39];
            // Generate every leaf at the same local root before translating, so both sides and the
            // baker have identical intrinsic curves; no hidden position-hash shape differences.
            for(int b=0;b<32;b++)
            {
                var body=new PBDGrassBody(ref patch.vertices,ref uv,ref indices,b*15,b*39,
                    Vector3.zero,7,type.Shape.Height,type.Shape.HalfWidth,0,1,type.Shape);
                Vector3 root=center+new Vector3((b%4-1.5f)*.09f,0,(b/4-3.5f)*.065f);
                body.Transform(Matrix4x4.Translate(root)); patch.Bodies[b]=body;
            }
            patch.UpdateMesh(); patch.PatchMesh.uv=uv; patch.PatchMesh.triangles=indices; patch.PatchMesh.RecalculateNormals();
            return patch;
        }
        public static Texture2D CreateUniformWindTexture()
        {
            var texture=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true) { name="Uniform response comparison wind",wrapMode=TextureWrapMode.Repeat };
            texture.SetPixel(0,0,new Color(.5f,.5f,0,1)); texture.Apply(); return texture;
        }
        public static Quaternion HandoffYaw(int grid)
        {
            float value=Mathf.Sin(grid*7.13f+17.8f)*17423.127f;
            return Quaternion.AngleAxis((value-Mathf.Floor(value))*360,Vector3.up);
        }
        // Estimate the static moment-to-angle slope from the existing baked samples. This adds no
        // new runtime physics sampling and does not alter the accepted intact wind response table.
        public Vector4[] MomentCompliance(GrassBladeData data)
        {
            var numerator=new Vector2[7]; var denominator=new Vector2[7];
            Quaternion anchor=Quaternion.Inverse(data.GridYaw(0))*data.Rods[0].Rotation;
            for(int sample=0;sample<Samples.Length;sample++)
            {
                Vector3 forceWind=new Vector3((sample%5-2)*MaxWind/2,0,(sample/5-2)*MaxWind/2);
                var pose=Samples[sample]; Quaternion parent=anchor;
                for(int j=0;j<7;j++)
                {
                    Vector3 moment=Vector3.zero;
                    for(int n=j+1;n<8;n++)
                    {
                        float mass=Physics.BladeMass/Mathf.Max(data.InverseMass[n],1e-8f),height=n/7f;
                        moment+=Vector3.Cross(pose.Nodes[n]-pose.Nodes[j],mass*(Physics.Gravity+forceWind*height*height));
                    }
                    moment=Quaternion.Inverse(parent)*moment;
                    Quaternion error=Quaternion.Inverse(parent)*pose.Frames[j]*Quaternion.Inverse(data.Rods[j].Relative);
                    if(error.w<0)error=new Quaternion(-error.x,-error.y,-error.z,-error.w);
                    float length=new Vector3(error.x,error.y,error.z).magnitude;
                    Vector3 angle=new Vector3(error.x,error.y,error.z)*(length>1e-6f?2*Mathf.Atan2(length,error.w)/length:2);
                    numerator[j]+=new Vector2(Mathf.Sin(angle.x)*moment.x,Mathf.Sin(angle.y)*moment.y);
                    denominator[j]+=new Vector2(moment.x*moment.x,moment.y*moment.y); parent=pose.Frames[j];
                }
            }
            var result=new Vector4[7];
            for(int j=0;j<7;j++)
            {
                float x=j==0?Physics.BaseBendCompliance:Physics.SoftBendCompliance*data.Rods[j].SoftScale;
                float y=j==0?Physics.BaseBendCompliance:Physics.HardBendCompliance*data.Rods[j].HardScale;
                result[j]=new Vector4(Mathf.Clamp(numerator[j].x/Mathf.Max(denominator[j].x,1e-12f),x*.25f,x*4),
                    Mathf.Clamp(numerator[j].y/Mathf.Max(denominator[j].y,1e-12f),y*.25f,y*4),0,0);
            }
            return result;
        }
        public static PBDGrassPatch CreateHandoffPatch(GrassResponseType type,Vector3 center,int size)
        {
            var random=UnityEngine.Random.state; PBDGrassPatch patch;
            try { UnityEngine.Random.InitState(12345); patch=new PBDGrassPatch(center,size,32,type.Shape); }
            finally { UnityEngine.Random.state=random; }
            var uv=new Vector2[patch.Bodies.Length*15]; var indices=new int[patch.Bodies.Length*39];
            for(int b=0;b<patch.Bodies.Length;b++)
            {
                Vector3 root=patch.Bodies[b].OriginPos[0];
                var body=new PBDGrassBody(ref patch.vertices,ref uv,ref indices,b*15,b*39,
                    Vector3.zero,7,type.Shape.Height,type.Shape.HalfWidth,0,1,type.Shape);
                body.Transform(Matrix4x4.TRS(root,HandoffYaw(b/32),Vector3.one)); patch.Bodies[b]=body;
            }
            patch.UpdateMesh(); patch.PatchMesh.uv=uv; patch.PatchMesh.triangles=indices; patch.PatchMesh.RecalculateNormals();
            return patch;
        }
    }
}
