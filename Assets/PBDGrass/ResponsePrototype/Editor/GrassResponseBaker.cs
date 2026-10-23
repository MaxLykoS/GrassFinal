using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace GrassFinal.PBDGrass.Editor
{
    public static class GrassResponseBaker
    {
        public const string Root="Assets/PBDGrass/ResponsePrototype";
        private static Camera camera;
        private static ComputeShader solver;
        private static Material material;
        private static Texture2D wind;
        [MenuItem("Tools/PBD Grass/Bake Eight Response Types")]
        public static void Bake()
        {
            var previous=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var scene=Application.isBatchMode?EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single)
                :UnityEngine.SceneManagement.SceneManager.CreateScene("Grass response bake");
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            bool passed=false;
            try
            {
                Directory.CreateDirectory(Root+"/Baked"); AssetDatabase.Refresh();
                camera=new GameObject("Bake camera").AddComponent<Camera>(); camera.enabled=false;
                solver=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/PBDGrass/Shaders/PBDSolverCS.compute");
                material=AssetDatabase.LoadAssetAtPath<Material>("Assets/PBDGrass/Materials/PBDGrassProcedualMaterial.mat");
                wind=GrassResponseType.CreateUniformWindTexture();
                for(int typeIndex=0;typeIndex<8;typeIndex++)BakeType(typeIndex);
                AssetDatabase.SaveAssets(); passed=true; Debug.Log("GRASS_RESPONSE_BAKE_PASS");
            }
            catch(Exception e) { Debug.LogError("GRASS_RESPONSE_BAKE_FAIL "+e); }
            finally
            {
                if(wind!=null)Object.DestroyImmediate(wind);
                if(!Application.isBatchMode)EditorSceneManager.CloseScene(scene,true);
                if(previous.IsValid() && previous.isLoaded)UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);
            }
            if(Application.isBatchMode)EditorApplication.Exit(passed?0:1);
        }
        public static string TypePath(int index) => Root+"/Baked/GrassResponse"+(index+1)+".asset";
        private static void BakeType(int index)
        {
            string path=TypePath(index); var type=AssetDatabase.LoadAssetAtPath<GrassResponseType>(path);
            if(type==null) { type=ScriptableObject.CreateInstance<GrassResponseType>(); AssetDatabase.CreateAsset(type,path); }
            var computed=ComputeType(index,camera,solver,material,wind);
            try { computed.name=type.name; EditorUtility.CopySerialized(computed,type); }
            finally { Object.DestroyImmediate(computed); }
            float speed=0,errorMax=0; foreach(var pose in type.Samples) { speed=Mathf.Max(speed,pose.Speed); errorMax=Mathf.Max(errorMax,pose.FitError); }
            EditorUtility.SetDirty(type); AssetDatabase.SaveAssets();
            Debug.Log("GRASS_RESPONSE_TYPE "+(index+1)+" "+type.DisplayName+" maxSpeed="+speed+" curveError="+errorMax
                +" omega="+type.AngularFrequency+" damping="+type.DampingRatio+" dynamicFit="+type.DynamicFitError);
        }
        // Same bake algorithm, but return an unsaved object so regression checks never replace
        // the accepted response bank with newly generated data.
        internal static GrassResponseType ComputeType(int index,Camera bakeCamera,ComputeShader bakeSolver,Material bakeMaterial,Texture2D bakeWind,
            int substeps=8,int iterations=3,float softComplianceScale=1)
        {
            var type=ScriptableObject.CreateInstance<GrassResponseType>();
            bool tall=(index&4)!=0,wide=(index&2)!=0,soft=(index&1)!=0;
            type.DisplayName=(tall?"Tall":"Short")+" / "+(wide?"Wide":"Narrow")+" / "+(soft?"Soft":"Firm");
            type.Shape=new GrassBladeShapeSettings { Height=tall?1.5f:.95f,HalfWidth=wide?.026f:.014f,
                HeightVariation=0,WidthVariation=0,TiltVariationDegrees=0,SideCurveRatio=0 };
            float compliance=soft?1.3f:.65f;
            type.Physics=new XPBDGrassSettings { EnablePlasticity=false,MaxActiveBlades=32,NearDistance=2,PoolHysteresis=0,
                SoftBendCompliance=3*compliance,HardBendCompliance=.2f*compliance,TwistCompliance=compliance,
                BaseBendCompliance=.08f*compliance,BladeMass=.01f*(type.Shape.Height/1.5f)*(type.Shape.HalfWidth/.02f) };
            type.Physics.NearSubsteps=substeps;type.Physics.ConstraintIterations=iterations;
            type.Physics.SoftBendCompliance*=softComplianceScale;
            type.GridSize=5; type.MaxWind=8; type.Samples=new GrassResponseType.Pose[25];
            var patch=GrassResponseType.CreatePatch(type,Vector3.zero);
            try
            {
                using(var renderer=new PBDGrassPatchRenderer(patch,bakeSolver,bakeMaterial,bakeCamera,bakeWind,type.Physics,null))
                {
                    renderer.SetWind(Vector3.zero,0,Vector4.zero);
                    type.Samples[type.ZeroIndex]=Settle(renderer);
                    var equilibrium=type.Samples[type.ZeroIndex];
                    for(int s=0;s<25;s++)
                    {
                        if(s==type.ZeroIndex)continue;
                        renderer.InitializeFromPose(equilibrium.Nodes,equilibrium.Frames);
                        renderer.SetWind(new Vector3((s%5-2)*4,0,(s/5-2)*4),0,Vector4.zero);
                        type.Samples[s]=Settle(renderer);
                    }
                    for(int axis=0;axis<2;axis++)
                    {
                        renderer.InitializeFromPose(equilibrium.Nodes,equilibrium.Frames);
                        renderer.SetWind(axis==0?new Vector3(4,0,0):new Vector3(0,0,4),0,Vector4.zero);
                        var target=type.Samples[type.ZeroIndex+(axis==0?1:5)];
                        float denominator=0; for(int n=1;n<8;n++)denominator+=(target.Nodes[n]-equilibrium.Nodes[n]).sqrMagnitude;
                        var response=new float[180];
                        for(int t=0;t<response.Length;t++)
                        {
                            renderer.Advance(1f/60); var nodes=renderer.ReadNodes(); float value=0;
                            for(int n=1;n<8;n++)value+=Vector3.Dot(nodes[n].Position-renderer.Data.GetBlade(0).Root-equilibrium.Nodes[n],target.Nodes[n]-equilibrium.Nodes[n]);
                            response[t]=value/Mathf.Max(denominator,1e-10f);
                        }
                        FitResponse(response,out float frequency,out float damping,out float error);
                        if(axis==0) { type.AngularFrequency.x=frequency; type.DampingRatio.x=damping; type.DynamicFitError.x=error; }
                        else { type.AngularFrequency.y=frequency; type.DampingRatio.y=damping; type.DynamicFitError.y=error; }
                    }
                }
            }
            finally { Object.DestroyImmediate(patch.PatchMesh); }
            type.BakeVersion=substeps==8 && iterations==3 && softComplianceScale==1
                ?"XPBD 60Hz / 8 small steps / 3 iterations; uniform wind acceleration; plasticity frozen; 2026-10-15"
                :"XPBD 60Hz / "+substeps+" small steps / "+iterations+" iterations / soft compliance x"+softComplianceScale.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    +"; uniform wind acceleration; plasticity frozen; 2026-10-23";
            return type;
        }
        private static GrassResponseType.Pose Settle(PBDGrassPatchRenderer renderer)
        {
            BoneInfo[] values=null; float speed=float.MaxValue; int quiet=0;
            for(int t=1;t<=720;t++)
            {
                renderer.Advance(1f/60);
                if(t<180 || t%30!=0)continue;
                values=renderer.ReadNodes(); speed=0; for(int n=1;n<8;n++)speed=Mathf.Max(speed,values[n].Velocities.magnitude);
                quiet=speed<.002f?quiet+1:0; if(quiet>=2)break;
            }
            if(values==null)values=renderer.ReadNodes();
            var pose=new GrassResponseType.Pose { Speed=speed }; Vector3 root=renderer.Data.GetBlade(0).Root;
            for(int n=0;n<8;n++)pose.Nodes[n]=values[n].Position-root;
            var frames=renderer.ReadFrames(); for(int s=0;s<7;s++)pose.Frames[s]=frames[s].Rotation;
            FitCurve(pose,renderer.Data); return pose;
        }
        private static void FitCurve(GrassResponseType.Pose pose,GrassBladeData data)
        {
            float aa=0,ab=0,bb=0,arc=0,total=data.GetBlade(0).Length;
            Vector3 rhs1=Vector3.zero,rhs2=Vector3.zero; pose.Tip=pose.Nodes[7];
            for(int n=1;n<7;n++)
            {
                arc+=data.GetRod(n-1).Length; float t=arc/total,u=1-t,a=3*u*u*t,b=3*u*t*t;
                Vector3 residual=pose.Nodes[n]-t*t*t*pose.Tip;
                aa+=a*a; ab+=a*b; bb+=b*b; rhs1+=a*residual; rhs2+=b*residual;
            }
            float det=aa*bb-ab*ab;
            pose.C1=(bb*rhs1-ab*rhs2)/det; pose.C2=(aa*rhs2-ab*rhs1)/det;
            arc=0; Vector3 previous=Vector3.zero;
            for(int n=1;n<8;n++)
            {
                arc+=data.GetRod(n-1).Length; float t=arc/total,u=1-t;
                Vector3 point=3*u*u*t*pose.C1+3*u*t*t*pose.C2+t*t*t*pose.Tip;
                point=previous+(point-previous).normalized*data.GetRod(n-1).Length;
                pose.FitError=Mathf.Max(pose.FitError,Vector3.Distance(point,pose.Nodes[n])); previous=point;
            }
        }
        private static float StepResponse(float t,float w,float z)
        {
            if(Mathf.Abs(z-1)<.001f)return 1-(1+w*t)*Mathf.Exp(-w*t);
            if(z<1)
            { float b=Mathf.Sqrt(1-z*z); return 1-Mathf.Exp(-z*w*t)*(Mathf.Cos(w*b*t)+z/b*Mathf.Sin(w*b*t)); }
            float d=Mathf.Sqrt(z*z-1),a=-w*(z-d),b2=-w*(z+d);
            return 1+(b2*Mathf.Exp(a*t)-a*Mathf.Exp(b2*t))/(a-b2);
        }
        private static void FitResponse(float[] values,out float frequency,out float damping,out float error)
        {
            frequency=4; damping=.3f; float best=float.MaxValue;
            for(float w=.75f;w<=20;w+=.25f)for(float z=.08f;z<=1.52f;z+=.04f)
            {
                float sum=0; for(int t=0;t<values.Length;t+=2)
                { float delta=StepResponse((t+1)/60f,w,z)-values[t]; sum+=delta*delta; }
                if(sum<best) { best=sum; frequency=w; damping=z; }
            }
            error=Mathf.Sqrt(best/((values.Length+1)/2));
        }
    }
}
