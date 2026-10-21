using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace GrassFinal.PBDGrass.Editor
{
    public static class GrassHandoffValidation
    {
        [Serializable] private sealed class Metric { public string name; public float value; }
        [Serializable] private sealed class Report
        { public bool passed; public string gpu,error; public List<Metric> metrics=new List<Metric>(); }
        private static Report report;
        private static Camera camera;
        private static ComputeShader solver,handoff;
        private static Material material;
        private static Texture2D wind;
        private static string output;
        private static readonly List<string> errors=new List<string>();
        [MenuItem("Tools/PBD Grass/Validate Near Middle Handoff")]
        public static void Run()
        {
            output=Environment.GetEnvironmentVariable("GRASS_HANDOFF_OUTPUT");
            if(string.IsNullOrEmpty(output))output=Path.GetFullPath("Assets/PBDGrass/Diagnostics/HandoffRun");
            Directory.CreateDirectory(output); report=new Report { gpu=SystemInfo.graphicsDeviceName };
            var previous=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var scene=Application.isBatchMode?EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single)
                :UnityEngine.SceneManagement.SceneManager.CreateScene("Handoff validation");
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            errors.Clear(); Application.logMessageReceived+=CaptureError;
            try
            {
                solver=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/PBDGrass/Shaders/PBDSolverCS.compute");
                handoff=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/PBDGrass/Shaders/GrassHandoff.compute");
                material=AssetDatabase.LoadAssetAtPath<Material>("Assets/PBDGrass/Materials/PBDGrassProcedualMaterial.mat");
                wind=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/PBDGrass/Textures/Wind.png");
                foreach(string kernel in new[] { "ResetDisplay","CaptureTransition","SeedSimulation","UpdateDisplay" })
                    Require(handoff!=null && handoff.IsSupported(handoff.FindKernel(kernel)),"Handoff kernel unsupported: "+kernel);
                camera=new GameObject("Handoff validation camera").AddComponent<Camera>(); camera.enabled=false;
                camera.transform.position=new Vector3(5,3,-5); camera.transform.LookAt(Vector3.up*.7f);
                camera.nearClipPlane=.03f; camera.aspect=1.5f;
                camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.08f,.11f,.14f);
                var sun=new GameObject("Sun").AddComponent<Light>(); sun.type=LightType.Directional;
                sun.transform.rotation=Quaternion.Euler(55,-30,0);
                RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientLight=new Color(.3f,.35f,.4f);
                var sh=new SphericalHarmonicsL2(); sh.AddAmbientLight(RenderSettings.ambientLight); RenderSettings.ambientProbe=sh;
                CheckEvictionAndPlasticity(); CheckCachedReentry(); CheckSavedScene();
                Require(errors.Count==0,string.Join("\n",errors)); report.passed=true;
                Debug.Log("GRASS_HANDOFF_PASS "+JsonUtility.ToJson(report));
            }
            catch(Exception e) { report.error=e.ToString(); Debug.LogError("GRASS_HANDOFF_FAIL "+e); }
            finally
            {
                Application.logMessageReceived-=CaptureError;
                File.WriteAllText(Path.Combine(output,"results.json"),JsonUtility.ToJson(report,true));
                if(!Application.isBatchMode)EditorSceneManager.CloseScene(scene,true);
                if(previous.IsValid() && previous.isLoaded)UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);
            }
            if(Application.isBatchMode)EditorApplication.Exit(report.passed?0:1);
        }
        private static void CaptureError(string message,string stack,LogType type)
        { if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert)errors.Add(message); }
        private static void Require(bool yes,string message) { if(!yes)throw new InvalidOperationException(message); }
        private static void Record(string name,float value) => report.metrics.Add(new Metric { name=name,value=value });
        private static PBDGrassPatchRenderer Renderer(int capacity,Transform contact=null,bool isolate=false)
        {
            var random=UnityEngine.Random.state; UnityEngine.Random.InitState(12345);
            var patch=new PBDGrassPatch(Vector3.zero,4,32,new GrassBladeShapeSettings()); UnityEngine.Random.state=random;
            if(isolate)for(int b=1;b<patch.Bodies.Length;b++)patch.Bodies[b].Transform(Matrix4x4.Translate(Vector3.right*100));
            var physics=new XPBDGrassSettings { MaxActiveBlades=capacity,NearDistance=1,PoolHysteresis=0,
                Gravity=Vector3.zero,GroundCollision=false,ColliderRadius=.38f };
            try
            {
                var r=new PBDGrassPatchRenderer(patch,solver,material,camera,wind,physics,
                    contact==null?null:new[] { contact },new GrassHandoffSettings(),handoff);
                r.SetWind(Vector3.zero,0,Vector4.zero); r.SimulationCenter=r.Data.Grids[0].Pos;
                r.RefreshSimulationSelection(); r.ResetDeformation(); return r;
            }
            finally { Object.DestroyImmediate(patch.PatchMesh); }
        }
        private static void Ticks(PBDGrassPatchRenderer r,int count) { for(int t=0;t<count;t++)r.Advance(1f/60); }
        private static float Difference(BoneInfo[] a,BoneInfo[] b,int blade,int count=1)
        {
            float max=0; for(int n=blade*8;n<(blade+count)*8;n++)
            { Require(!float.IsNaN(a[n].Position.sqrMagnitude) && !float.IsInfinity(a[n].Position.sqrMagnitude),"Invalid pose."); max=Mathf.Max(max,Vector3.Distance(a[n].Position,b[n].Position)); }
            return max;
        }
        private static float Peak(Vector4[] values,int begin=0,int count=-1)
        { float max=0; if(count<0)count=values.Length; for(int i=begin;i<begin+count;i++)max=Mathf.Max(max,values[i].magnitude); return max; }
        private static void CheckEvictionAndPlasticity()
        {
            var go=new GameObject("Contact"); go.transform.position=Vector3.up*10;
            Vector3 cameraPosition=camera.transform.position; Quaternion cameraRotation=camera.transform.rotation;
            using(var r=Renderer(32,go.transform,true))
            {
                Vector3 view=r.Data.GetRod(0).Rotation*new Vector3(.55f,1,0).normalized;
                camera.transform.position=r.Data.GetBlade(0).Root+Vector3.up*.7f+view*3;
                camera.transform.LookAt(r.Data.GetBlade(0).Root+Vector3.up*.7f); camera.fieldOfView=40;
                foreach(var light in Object.FindObjectsOfType<Light>())light.transform.rotation=Quaternion.LookRotation(-view+Vector3.down*.7f);
                Ticks(r,90);
                var rest=r.Handoff.ReadDisplayNodes(); Capture("01_rest",r);
                Vector3 root=r.Data.GetBlade(0).Root, direction=r.Data.GetRod(0).Rotation*Vector3.up;
                for(int t=0;t<120;t++)
                { go.transform.position=root+Vector3.up*.65f+direction*Mathf.Lerp(1.2f,-.15f,Mathf.Clamp01(t/60f)); Ticks(r,1); }
                go.SetActive(false); Ticks(r,180);
                var near=r.Handoff.ReadDisplayNodes(); var plastic=r.ReadPlasticBends();
                float nearResidual=Vector3.Distance(near[7].Position,rest[7].Position);
                Record("near_plastic_tip_residual_m",nearResidual); Record("plastic_peak_rad",Peak(plastic,0,7));
                Require(nearResidual>.3f && Peak(plastic,0,7)>.1f,"Contact did not create meaningful lodging.");
                Capture("02_near_lodged",r);
                r.SimulationCenter=r.Data.Grids[r.Data.Grids.Length-1].Pos; r.RefreshSimulationSelection();
                Require(r.Pool.SlotForGrid(0)<0 && r.ActiveBladeCount==32,"Target did not leave the physical pool.");
                Record("exit_selection_pose_jump_m",Difference(near,r.Handoff.ReadDisplayNodes(),0));
                Require(Difference(near,r.Handoff.ReadDisplayNodes(),0)==0,"Exit selection popped.");
                Ticks(r,120); var middle=r.Handoff.ReadDisplayNodes(); var farPlastic=r.ReadPlasticBends();
                float drift=0; for(int i=0;i<7;i++)drift=Mathf.Max(drift,(plastic[i]-farPlastic[i]).magnitude);
                float middleResidual=Vector3.Distance(middle[7].Position,rest[7].Position);
                Record("evicted_plastic_drift_rad",drift); Record("middle_plastic_tip_residual_m",middleResidual);
                Record("near_middle_lodged_tip_difference_m",Vector3.Distance(near[7].Position,middle[7].Position));
                Require(drift==0 && middleResidual>.3f,"Middle representation lost plastic history.");
                Capture("03_middle_lodged",r);
                r.SimulationCenter=r.Data.Grids[0].Pos; r.RefreshSimulationSelection();
                CheckEntry(r,middle,"evicted");
                float maxStep=0,maxStretch=0; var last=middle;
                for(int t=0;t<180;t++)
                {
                    Ticks(r,1); var pose=r.Handoff.ReadDisplayNodes(); maxStep=Mathf.Max(maxStep,Difference(last,pose,0)); last=pose;
                    var physical=r.ReadNodes();
                    for(int s=0;s<7;s++)maxStretch=Mathf.Max(maxStretch,Mathf.Abs(Vector3.Distance(physical[s+1].Position,physical[s].Position)/r.Data.GetRod(s).Length-1));
                }
                Record("reentry_max_display_step_m",maxStep); Record("reentry_max_stretch_ratio",maxStretch);
                float returnDrift=0; var returnedPlastic=r.ReadPlasticBends();
                for(int s=0;s<7;s++)returnDrift=Mathf.Max(returnDrift,(plastic[s]-returnedPlastic[s]).magnitude);
                Record("reentry_plastic_change_rad",returnDrift);
                Require(maxStep<.08f && maxStretch<.01f && returnDrift<.05f,"Reentry caused a pose spike, stretch or material change.");
                Capture("04_near_reentered",r);
                // Reverse the transition before it finishes; selection must remain position-continuous.
                float reversalJump=0;
                for(int n=0;n<8;n++)
                {
                    var before=r.Handoff.ReadDisplayNodes();
                    r.SimulationCenter=r.Data.Grids[n%2==0?15:0].Pos; r.RefreshSimulationSelection();
                    reversalJump=Mathf.Max(reversalJump,Difference(before,r.Handoff.ReadDisplayNodes(),0)); Ticks(r,5);
                }
                Record("rapid_reversal_selection_jump_m",reversalJump); Require(reversalJump==0,"Interrupted transition popped.");
                r.ResetDeformation(); Require(Peak(r.ReadPlasticBends())==0,"Reset left material history.");
            }
            camera.transform.SetPositionAndRotation(cameraPosition,cameraRotation); camera.fieldOfView=60;
            Object.DestroyImmediate(go);
        }
        private static void CheckEntry(PBDGrassPatchRenderer r,BoneInfo[] displayed,string prefix)
        {
            var nodes=r.ReadNodes(); var frames=r.ReadFrames(); var shownFrames=r.Handoff.ReadDisplayFrames();
            float pos=Difference(nodes,displayed,0),velocity=0,rotation=0,angular=0;
            for(int n=0;n<8;n++)velocity=Mathf.Max(velocity,(nodes[n].Velocities-displayed[n].Velocities).magnitude);
            for(int s=0;s<7;s++)
            {
                rotation=Mathf.Max(rotation,Quaternion.Angle(frames[s].Rotation,shownFrames[s].Rotation));
                angular=Mathf.Max(angular,(frames[s].AngularVelocity-shownFrames[s].AngularVelocity).magnitude);
            }
            Record(prefix+"_seed_position_error_m",pos); Record(prefix+"_seed_velocity_error_mps",velocity);
            Record(prefix+"_seed_rotation_error_degrees",rotation); Record(prefix+"_seed_angular_error_radps",angular);
            Require(pos<1e-6f && velocity<1e-6f && rotation<.1f && angular<1e-6f,"Entry did not transfer the displayed state.");
        }
        private static void CheckCachedReentry()
        {
            using(var r=Renderer(512))
            {
                r.SetWind(new Vector3(10,0,3),.1f,new Vector4(.01f,.01f,0,1)); Ticks(r,120);
                int slot=r.Pool.SlotForGrid(0); r.SimulationCenter=new Vector3(100,0,100); r.RefreshSimulationSelection(); Ticks(r,90);
                Require(r.ActiveBladeCount==0 && r.Pool.SlotForGrid(0)==slot,"Dormant-cache test evicted or still simulated the target.");
                var before=r.Handoff.ReadDisplayNodes(); float speed=0;
                for(int n=0;n<8;n++)speed=Mathf.Max(speed,before[n].Velocities.magnitude);
                Record("middle_wind_max_speed_mps",speed); Require(speed>.001f,"Procedural wind was stationary.");
                int entries=r.Handoff.EntryInitializations;
                r.SimulationCenter=r.Data.Grids[0].Pos; r.RefreshSimulationSelection();
                Require(r.Handoff.EntryInitializations>entries,"Dormant slot resumed stale physics."); CheckEntry(r,before,"cached");
                Ticks(r,120); Require(Peak(r.ReadPlasticBends())<.005f,"Weak wind/reentry created plastic damage.");
                r.Draw(camera); camera.Render(); uint visible=r.VisibleGridCount();
                Record("draw_visible_grids",visible); Record("draw_active_grids",r.ActiveBladeCount/32);
                Require(visible>r.ActiveBladeCount/32,"Middle grass was not rendered outside the active pool.");
                int ticks=r.SimulationTicks; var sceneCamera=new GameObject("Scene probe").AddComponent<Camera>();
                sceneCamera.enabled=false; sceneCamera.cameraType=CameraType.SceneView; sceneCamera.transform.SetPositionAndRotation(camera.transform.position,camera.transform.rotation);
                r.Draw(sceneCamera); sceneCamera.Render(); Require(r.VisibleGridCount()==visible && r.SimulationTicks==ticks,"Scene view changed culling or advanced time.");
                Object.DestroyImmediate(sceneCamera.gameObject);
            }
        }
        private static void CheckSavedScene()
        {
            foreach(var light in Object.FindObjectsOfType<Light>())light.enabled=false;
            var scene=EditorSceneManager.OpenScene("Assets/PBDGrass/Scenes/PBDGrassHandoff.unity",OpenSceneMode.Additive);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            GrassHandoffDemo demo=null;
            foreach(var root in scene.GetRootGameObjects()) { demo=root.GetComponentInChildren<GrassHandoffDemo>(); if(demo!=null)break; }
            Require(demo!=null,"Prototype scene missing."); demo.enabled=false; demo.Initialize();
            Record("saved_scene_blades",demo.Renderer.BladeCount); Record("saved_scene_active_blades",demo.Renderer.ActiveBladeCount);
            Require(demo.Renderer.BladeCount==2048 && demo.Renderer.ActiveBladeCount<2048 && demo.Renderer.Handoff!=null,"Scene contract is wrong.");
            Camera old=camera; camera=demo.Cam; camera.enabled=false; camera.aspect=1.5f;
            Capture("05_saved_scene",demo.Renderer); camera=old;
            int minActive=int.MaxValue,maxActive=0;
            for(int tick=0;tick<1440;tick++)
            {
                float time=(tick+1)/60f;
                demo.Cam.transform.position=new Vector3(-Mathf.Cos(time*Mathf.PI/12)*8*.72f,2.5f,-2.5f);
                demo.Cam.transform.LookAt(Vector3.up*.6f);
                Vector3 focus=demo.Cam.transform.position; focus.y=0;
                Vector3 forward=Vector3.ProjectOnPlane(demo.Cam.transform.forward,Vector3.up).normalized;
                demo.Interaction.position=focus+forward*1.1f+Vector3.up*.6f;
                demo.Renderer.SimulationCenter=focus; Ticks(demo.Renderer,1);
                minActive=Mathf.Min(minActive,demo.Renderer.ActiveBladeCount); maxActive=Mathf.Max(maxActive,demo.Renderer.ActiveBladeCount);
                Require(demo.Renderer.Pool.BudgetRejectedGrids==0,"Demo pool is too small for its activation radius.");
                if(tick%60==0)
                {
                    var pose=demo.Renderer.Handoff.ReadDisplayNodes(); Difference(pose,pose,0,demo.Renderer.BladeCount);
                    if(tick==720) { camera=demo.Cam; Capture("06_tour_midpoint",demo.Renderer); camera=old; }
                }
            }
            float peak=Peak(demo.Renderer.ReadPlasticBends());
            Record("tour_min_active_blades",minActive); Record("tour_max_active_blades",maxActive);
            Record("tour_peak_plastic_rad",peak); Record("tour_entry_initializations",demo.Renderer.Handoff.EntryInitializations);
            Require(minActive>0 && maxActive<2048 && peak>.1f,"Tour failed to exercise limited near physics and lodging.");
            camera=demo.Cam; Capture("07_tour_returned",demo.Renderer); camera=old;
            EditorSceneManager.CloseScene(scene,true);
        }
        private static void Capture(string name,PBDGrassPatchRenderer r)
        {
            var rt=new RenderTexture(960,640,24); rt.Create(); camera.targetTexture=rt;
            r.Draw(camera); camera.Render(); var previous=RenderTexture.active; RenderTexture.active=rt;
            var tex=new Texture2D(960,640,TextureFormat.RGB24,false); tex.ReadPixels(new Rect(0,0,960,640),0,0); tex.Apply();
            File.WriteAllBytes(Path.Combine(output,name+".png"),tex.EncodeToPNG());
            RenderTexture.active=previous; camera.targetTexture=null; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        }
    }
}
