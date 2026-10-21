using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace GrassFinal.PBDGrass.Editor
{
    public static class BakedGrassHandoffValidation
    {
        [Serializable] private sealed class Metric { public string name; public float value; }
        [Serializable] private sealed class Report
        { public bool passed; public string gpu,error; public List<Metric> metrics=new List<Metric>(); }
        private static Report report;
        private static string output;
        private static Camera camera;
        private static ComputeShader solver,handoff;
        private static Material material;
        private static Texture2D wind;
        private static GrassResponseType type;
        private static readonly List<string> errors=new List<string>();
        private static string metricPrefix="";
        private static bool Tiered => Environment.GetEnvironmentVariable("PBD_TIERED_HANDOFF_TEST")=="1";
        [MenuItem("Tools/PBD Grass/Validate Baked Wind And Plastic Handoff")]
        public static void Run()
        {
            output=Environment.GetEnvironmentVariable("BAKED_HANDOFF_OUTPUT");
            if(string.IsNullOrEmpty(output))output=Path.GetFullPath(GrassResponseBaker.Root+"/BakedHandoffRun");
            Directory.CreateDirectory(output); report=new Report { gpu=SystemInfo.graphicsDeviceName };
            var previous=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var scene=Application.isBatchMode?EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single)
                :UnityEngine.SceneManagement.SceneManager.CreateScene("Baked plastic handoff validation");
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene); errors.Clear(); Application.logMessageReceived+=CaptureError;
            try
            {
                solver=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/PBDGrass/Shaders/PBDSolverCS.compute");
                handoff=AssetDatabase.LoadAssetAtPath<ComputeShader>(Tiered?"Assets/PBDGrass/Shaders/TieredGrassHandoff.compute":"Assets/PBDGrass/Shaders/GrassHandoff.compute");
                material=AssetDatabase.LoadAssetAtPath<Material>("Assets/PBDGrass/Materials/PBDGrassProcedualMaterial.mat");
                type=AssetDatabase.LoadAssetAtPath<GrassResponseType>(GrassResponseBaker.TypePath(5));
                Require(type!=null && type.IsBaked,"Bake grass type six first.");
                wind=GrassResponseType.CreateUniformWindTexture();
                camera=new GameObject("Baked handoff validation camera").AddComponent<Camera>(); camera.enabled=false;
                camera.nearClipPlane=.03f; camera.aspect=1.5f; camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.06f,.08f,.1f);
                camera.transform.position=new Vector3(3,2,-5); camera.transform.LookAt(Vector3.up*.6f);
                var sun=new GameObject("Sun").AddComponent<Light>(); sun.type=LightType.Directional; sun.transform.rotation=Quaternion.Euler(55,-30,0);
                var sh=new UnityEngine.Rendering.SphericalHarmonicsL2(); sh.AddAmbientLight(new Color(.3f,.35f,.25f)); RenderSettings.ambientProbe=sh;
                CheckIntactWind(); CheckElasticExit(); CheckPlasticEviction(false); CheckPlasticEviction(true); CheckSavedScene();
                Require(errors.Count==0,string.Join("\n",errors)); report.passed=true;
                Debug.Log("BAKED_HANDOFF_PASS "+JsonUtility.ToJson(report));
            }
            catch(Exception e) { report.error=e.ToString(); Debug.LogError("BAKED_HANDOFF_FAIL "+e); }
            finally
            {
                Application.logMessageReceived-=CaptureError;
                File.WriteAllText(Path.Combine(output,"results.json"),JsonUtility.ToJson(report,true));
                if(wind!=null)Object.DestroyImmediate(wind);
                if(!Application.isBatchMode)EditorSceneManager.CloseScene(scene,true);
                if(previous.IsValid() && previous.isLoaded)UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);
            }
            if(Application.isBatchMode)EditorApplication.Exit(report.passed?0:1);
        }
        public static XPBDGrassSettings Physics(GrassResponseType source,int capacity)
        {
            var settings=JsonUtility.FromJson<XPBDGrassSettings>(JsonUtility.ToJson(source.Physics));
            settings.EnablePlasticity=true; settings.MaxActiveBlades=capacity;
            settings.NearDistance=capacity==32?1:100; settings.PoolHysteresis=0; settings.ColliderRadius=.38f;
            return settings;
        }
        private static PBDGrassPatchRenderer Renderer(int capacity,Transform contact=null,bool isolate=false,float radius=.38f)
        {
            var patch=GrassResponseType.CreateHandoffPatch(type,Vector3.zero,4);
            if(isolate)for(int b=1;b<patch.Bodies.Length;b++)patch.Bodies[b].Transform(Matrix4x4.Translate(Vector3.right*100));
            try
            {
                var physics=Physics(type,capacity); physics.ColliderRadius=radius;
                var r=new PBDGrassPatchRenderer(patch,solver,material,camera,wind,physics,contact==null?null:new[] { contact },
                    new GrassHandoffSettings { BakedResponse=type,UseTieredState=Tiered },handoff);
                r.SimulationCenter=r.Data.Grids[0].Pos; r.RefreshSimulationSelection(); r.SetWind(Vector3.zero,0,Vector4.zero); r.ResetDeformation(); return r;
            }
            finally { Object.DestroyImmediate(patch.PatchMesh); }
        }
        private static void Ticks(PBDGrassPatchRenderer r,int count) { for(int t=0;t<count;t++)r.Advance(1f/60); }
        private static void Require(bool condition,string message) { if(!condition)throw new InvalidOperationException(message); }
        private static void Record(string name,float value) => report.metrics.Add(new Metric { name=metricPrefix+name,value=value });
        private static void CaptureError(string message,string stack,LogType severity)
        { if(severity==LogType.Error || severity==LogType.Exception || severity==LogType.Assert)errors.Add(message); }
        private static float Difference(BoneInfo[] a,BoneInfo[] b,int blade,int count=1)
        {
            float difference=0;
            for(int i=blade*8;i<(blade+count)*8;i++)
            {
                Require(!float.IsNaN(a[i].Position.sqrMagnitude) && !float.IsInfinity(a[i].Position.sqrMagnitude),"Invalid pose.");
                difference=Mathf.Max(difference,Vector3.Distance(a[i].Position,b[i].Position));
            }
            return difference;
        }
        private static float PlasticDifference(Vector4[] a,Vector4[] b)
        { float max=0; for(int j=0;j<7;j++)max=Mathf.Max(max,(a[j]-b[j]).magnitude); return max; }
        private static float Peak(Vector4[] values,int count=-1)
        { float max=0; if(count<0)count=values.Length; for(int i=0;i<count;i++)max=Mathf.Max(max,values[i].magnitude); return max; }
        private static void CheckIntactWind()
        {
            using(var near=Renderer(512))using(var middle=Renderer(32))
            {
                middle.SimulationCenter=Vector3.one*100; middle.RefreshSimulationSelection(); middle.ResetDeformation();
                var initial=near.Handoff.ReadDisplayNodes(); Ticks(near,180); Ticks(middle,180);
                Record("zero_wind_birth_max_node_drift_m",Difference(initial,near.Handoff.ReadDisplayNodes(),0,512));
                float mean=0,max=0; int count=0;
                for(int tick=0;tick<600;tick++)
                {
                    Vector3 force=GrassResponseDemo.WindAt(tick/60f,true);
                    near.SetWind(force,.1f,new Vector4(.01f,.01f,0,1)); middle.SetWind(force,.1f,new Vector4(.01f,.01f,0,1));
                    Ticks(near,1); Ticks(middle,1);
                    if(tick%6!=0)continue;
                    var a=near.Handoff.ReadDisplayNodes(); var b=middle.Handoff.ReadDisplayNodes();
                    for(int g=0;g<16;g++)
                    { float delta=Vector3.Distance(a[g*256+7].Position,b[g*256+7].Position); mean+=delta; max=Mathf.Max(max,delta); count++; }
                }
                Record("intact_rotated_random_wind_mean_tip_gap_m",mean/count); Record("intact_rotated_random_wind_max_tip_gap_m",max);
                Record("intact_plastic_peak_rad",Peak(near.ReadPlasticBends()));
                Require(mean/count<.08f && max<.25f,"Intact wind response mismatch after rotation.");
                Require(Peak(near.ReadPlasticBends())<.02f,"Ordinary wind/gravity created permanent lodging.");
                int entries=middle.Handoff.EntryInitializations; middle.SimulationCenter=middle.Data.Grids[0].Pos;
                var before=middle.Handoff.ReadDisplayNodes(); middle.RefreshSimulationSelection(); CheckSeed(middle,before,"wind");
                Require(middle.Handoff.EntryInitializations>entries,"Wind reentry not exercised.");
            }
        }
        private static void CheckSeed(PBDGrassPatchRenderer r,BoneInfo[] before,string prefix)
        {
            var nodes=r.ReadNodes(); var shown=r.Handoff.ReadDisplayFrames(); var frames=r.ReadFrames();
            float position=Difference(nodes,before,0),velocity=0,angle=0,angular=0;
            for(int n=0;n<8;n++)velocity=Mathf.Max(velocity,(nodes[n].Velocities-before[n].Velocities).magnitude);
            for(int s=0;s<7;s++)
            { angle=Mathf.Max(angle,Quaternion.Angle(shown[s].Rotation,frames[s].Rotation)); angular=Mathf.Max(angular,(shown[s].AngularVelocity-frames[s].AngularVelocity).magnitude); }
            Record(prefix+"_seed_position_error_m",position); Record(prefix+"_seed_velocity_error_mps",velocity);
            Record(prefix+"_seed_rotation_error_deg",angle); Record(prefix+"_seed_angular_error_radps",angular);
            Require(position<1e-6f && velocity<1e-6f && angle<.1f && angular<1e-6f,"State transfer mismatch.");
        }
        private static void CheckElasticExit()
        {
            var go=new GameObject("Light touch"); go.transform.position=Vector3.up*10;
            using(var r=Renderer(32,go.transform,true,.04f))
            {
                Ticks(r,180); var rest=r.Handoff.ReadDisplayNodes(); Vector3 point=rest[5].Position;
                Vector3 tangent=(rest[6].Position-point).normalized,axis=r.ReadFrames()[4].Rotation*Vector3.right;
                Vector3 normal=Vector3.Cross(axis,tangent).normalized;
                for(int t=0;t<60;t++)
                { go.transform.position=point+normal*Mathf.Lerp(.12f,.035f,Mathf.Clamp01(t/30f)); Ticks(r,1); }
                float plastic=Peak(r.ReadPlasticBends(),7); Record("light_touch_plastic_peak_rad",plastic);
                go.SetActive(false); r.SimulationCenter=r.Data.Grids[15].Pos; r.RefreshSimulationSelection(); Ticks(r,120);
                Record("elastic_exit_max_node_residual_m",Difference(rest,r.Handoff.ReadDisplayNodes(),0));
                Require(plastic<.005f && Difference(rest,r.Handoff.ReadDisplayNodes(),0)<.08f,"Elastic contact was retained as permanent shape.");
            }
            Object.DestroyImmediate(go);
        }
        private static void CheckPlasticEviction(bool withWind)
        {
            metricPrefix=withWind?"with_wind_":"";
            var go=new GameObject("Press sphere"); go.transform.position=Vector3.up*10;
            using(var r=Renderer(32,go.transform,true))
            {
                Vector3 root=r.Data.GetBlade(0).Root, direction=r.Data.GetRod(0).Rotation*Vector3.up;
                Vector3 view=r.Data.GetRod(0).Rotation*new Vector3(.7f,1,.1f).normalized;
                camera.transform.position=root+Vector3.up*.55f+view*3.2f; camera.transform.LookAt(root+Vector3.up*.55f); camera.fieldOfView=40;
                Ticks(r,180); var initial=r.Handoff.ReadDisplayNodes(); Capture("01_intact",r);
                for(int tick=0;tick<120;tick++)
                { go.transform.position=root+Vector3.up*.65f+direction*Mathf.Lerp(1.2f,-.15f,Mathf.Clamp01(tick/60f)); Ticks(r,1); }
                go.SetActive(false); Ticks(r,360);
                if(withWind) { r.SetWind(new Vector3(3,0,1),0,Vector4.zero); Ticks(r,240); }
                var near=r.Handoff.ReadDisplayNodes(); var plastic=r.ReadPlasticBends();
                Record("pressed_plastic_peak_rad",Peak(plastic,7)); Record("near_lodged_tip_residual_m",Vector3.Distance(initial[7].Position,near[7].Position));
                Capture("02_near_lodged",r); Require(Peak(plastic,7)>.1f,"Contact failed to lodge the blade.");
                r.SimulationCenter=r.Data.Grids[15].Pos; r.RefreshSimulationSelection();
                Require(r.Pool.SlotForGrid(0)<0,"Blade not evicted.");
                Record("exit_selection_jump_m",Difference(near,r.Handoff.ReadDisplayNodes(),0)); Ticks(r,120);
                var middle=r.Handoff.ReadDisplayNodes();
                Record("evicted_plastic_drift_rad",PlasticDifference(plastic,r.ReadPlasticBends()));
                Record("lodged_near_middle_max_node_gap_m",Difference(near,middle,0));
                Record("lodged_near_middle_tip_gap_m",Vector3.Distance(near[7].Position,middle[7].Position)); Capture("03_middle_lodged",r);
                Require(PlasticDifference(plastic,r.ReadPlasticBends())==0,"Evicted plastic state changed.");
                r.SimulationCenter=r.Data.Grids[0].Pos; r.RefreshSimulationSelection(); CheckSeed(r,middle,"plastic");
                float maxStep=0; var previous=middle;
                for(int tick=0;tick<180;tick++)
                {
                    if(withWind)r.SetWind(GrassResponseDemo.WindAt(tick/60f,true)*.7f,0,Vector4.zero);
                    Ticks(r,1); var next=r.Handoff.ReadDisplayNodes(); maxStep=Mathf.Max(maxStep,Difference(previous,next,0)); previous=next;
                }
                float drift=PlasticDifference(plastic,r.ReadPlasticBends());
                Record("plastic_reentry_max_display_step_m",maxStep); Record("plastic_reentry_added_plastic_rad",drift);
                Capture("04_returned",r);
                Require(Difference(near,middle,0)<.15f && maxStep<.08f && drift<.05f,"Plastic handoff produced a large shape difference or transient.");
                float reversal=0;
                for(int n=0;n<8;n++)
                {
                    var before=r.Handoff.ReadDisplayNodes(); r.SimulationCenter=r.Data.Grids[n%2==0?15:0].Pos; r.RefreshSimulationSelection();
                    reversal=Mathf.Max(reversal,Difference(before,r.Handoff.ReadDisplayNodes(),0)); Ticks(r,5);
                }
                Record("interrupted_selection_jump_m",reversal); Require(reversal==0,"Interrupted handoff popped.");
                r.ResetDeformation(); Require(Peak(r.ReadPlasticBends())==0,"Reset left plastic history.");
            }
            Object.DestroyImmediate(go);
            metricPrefix="";
        }
        private static void CheckSavedScene()
        {
            foreach(var sun in Object.FindObjectsOfType<Light>())sun.enabled=false;
            var scene=EditorSceneManager.OpenScene(BakedGrassHandoffSceneBuilder.ScenePath,OpenSceneMode.Additive);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            GrassHandoffDemo demo=null;
            foreach(var root in scene.GetRootGameObjects()) { demo=root.GetComponentInChildren<GrassHandoffDemo>(); if(demo!=null)break; }
            Require(demo!=null && demo.Handoff.BakedResponse==type,"Saved scene is not using the tested material.");
            if(Tiered) { demo.Handoff.UseTieredState=true; demo.HandoffShader=handoff; }
            demo.enabled=false; demo.Initialize(); Camera previousCamera=camera; camera=demo.Cam; camera.enabled=false; camera.aspect=1.5f;
            Record("scene_total_blades",demo.Renderer.BladeCount); Require(demo.Renderer.BladeCount==2048,"Scene is not a 2048-blade patch.");
            int minActive=int.MaxValue,maxActive=0; float maxMotion=0,maxSteadyFarMotion=0,maxActiveMotion=0;
            BoneInfo[] previous=null; var wasActive=new bool[64]; var lastChange=new float[64];
            for(int tick=0;tick<1440;tick++)
            {
                float time=(tick+1)/60f; camera.transform.position=new Vector3(-Mathf.Cos(time*Mathf.PI/12)*8*.72f,2.5f,-2.5f);
                camera.transform.LookAt(Vector3.up*.6f); Vector3 focus=camera.transform.position; focus.y=0;
                Vector3 forward=Vector3.ProjectOnPlane(camera.transform.forward,Vector3.up).normalized;
                demo.Interaction.position=focus+forward*1.1f+Vector3.up*.6f;
                demo.Renderer.SimulationCenter=focus;
                demo.Renderer.SetWind(GrassResponseDemo.WindAt(time,true)*(demo.Wind.magnitude/6),.1f,new Vector4(.01f,.01f,0,1));
                Ticks(demo.Renderer,1); minActive=Mathf.Min(minActive,demo.Renderer.ActiveBladeCount); maxActive=Mathf.Max(maxActive,demo.Renderer.ActiveBladeCount);
                Require(demo.Renderer.Pool.BudgetRejectedGrids==0,"Scene activation exceeds the pool capacity.");
                var isActive=new bool[64]; foreach(var grid in demo.Renderer.Pool.Selected)isActive[grid.GridId]=true;
                for(int g=0;g<64;g++)if(isActive[g]!=wasActive[g]) { lastChange[g]=time; wasActive[g]=isActive[g]; }
                if(tick%6==0)
                {
                    var pose=demo.Renderer.Handoff.ReadDisplayNodes(); Difference(pose,pose,0,2048);
                    if(previous!=null)
                    {
                        for(int g=0;g<64;g++)
                        {
                            float move=Difference(previous,pose,g*32,32); maxMotion=Mathf.Max(maxMotion,move);
                            if(isActive[g])maxActiveMotion=Mathf.Max(maxActiveMotion,move);
                            else if(time-lastChange[g]>demo.Handoff.TransitionSeconds+.2f)maxSteadyFarMotion=Mathf.Max(maxSteadyFarMotion,move);
                        }
                    }
                    previous=pose;
                }
                if(tick==0)Capture("scene_01_birth",demo.Renderer);
                if(tick==720)Capture("scene_02_half_tour",demo.Renderer);
            }
            Capture("scene_03_returned",demo.Renderer);
            Record("scene_min_active_blades",minActive); Record("scene_max_active_blades",maxActive);
            Record("scene_peak_plastic_rad",Peak(demo.Renderer.ReadPlasticBends())); Record("scene_max_10hz_node_motion_m",maxMotion);
            Record("scene_steady_middle_max_10hz_node_motion_m",maxSteadyFarMotion); Record("scene_active_max_10hz_node_motion_m",maxActiveMotion);
            Require(minActive>0 && maxActive<2048 && Peak(demo.Renderer.ReadPlasticBends())>.1f,"Scene did not exercise near/middle lodging.");
            Require(maxSteadyFarMotion<.2f,"Inactive lodged grass has a large pose change outside the handoff interval.");
            var before=demo.Renderer.Handoff.ReadDisplayNodes(); int ticks=demo.Renderer.SimulationTicks;
            var probe=new GameObject("Scene view probe").AddComponent<Camera>(); probe.enabled=false; probe.cameraType=CameraType.SceneView;
            probe.transform.position=new Vector3(10,8,-10); probe.transform.LookAt(Vector3.up*.6f);
            demo.Renderer.Draw(camera); camera.Render(); uint visible=demo.Renderer.VisibleGridCount();
            demo.Renderer.Draw(probe); probe.Render();
            Require(demo.Renderer.VisibleGridCount()==visible && demo.Renderer.SimulationTicks==ticks && Difference(before,demo.Renderer.Handoff.ReadDisplayNodes(),0,2048)==0,
                "Scene view changed culling or simulation.");
            Record("scene_main_scene_visible_grids",visible); Object.DestroyImmediate(probe.gameObject);
            camera=previousCamera; EditorSceneManager.CloseScene(scene,true);
        }
        private static void Capture(string name,PBDGrassPatchRenderer renderer)
        {
            var rt=new RenderTexture(960,640,24); rt.Create(); camera.targetTexture=rt;
            renderer.Draw(camera); camera.Render(); var previous=RenderTexture.active; RenderTexture.active=rt;
            var tex=new Texture2D(960,640,TextureFormat.RGB24,false); tex.ReadPixels(new Rect(0,0,960,640),0,0); tex.Apply();
            File.WriteAllBytes(Path.Combine(output,metricPrefix+name+".png"),tex.EncodeToPNG());
            RenderTexture.active=previous; camera.targetTexture=null; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        }
    }
}
