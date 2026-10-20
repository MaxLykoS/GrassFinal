using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace GrassFinal.PBDGrass.Editor
{
    public static class GrassTieredValidation
    {
        [Serializable] private sealed class Metric { public string name; public double value; }
        [Serializable] private sealed class Report
        { public bool passed; public string gpu,error; public List<Metric> metrics=new List<Metric>(); }
        private static Report report;
        private static string output;
        private static readonly List<string> errors=new List<string>();
        private static Camera camera;
        private static ComputeShader solver,baselineShader,tieredShader;
        private static Material material;
        private static Texture2D wind;
        [MenuItem("Tools/PBD Grass/Validate Tiered State Against Baseline")]
        public static void Run()
        {
            output=Environment.GetEnvironmentVariable("GRASS_TIERED_OUTPUT");
            if(string.IsNullOrEmpty(output))output=Path.GetFullPath(GrassResponseBaker.Root+"/TieredRun");
            Directory.CreateDirectory(output); report=new Report { gpu=SystemInfo.graphicsDeviceName };
            var previous=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var scene=Application.isBatchMode?EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single)
                :UnityEngine.SceneManagement.SceneManager.CreateScene("Tiered state validation");
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene); errors.Clear(); Application.logMessageReceived+=CaptureError;
            try
            {
                solver=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/PBDGrass/Shaders/PBDSolverCS.compute");
                baselineShader=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/PBDGrass/Shaders/GrassHandoff.compute");
                tieredShader=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/PBDGrass/Shaders/TieredGrassHandoff.compute");
                material=AssetDatabase.LoadAssetAtPath<Material>("Assets/PBDGrass/Materials/PBDGrassProcedualMaterial.mat");
                wind=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/PBDGrass/Textures/Wind.png");
                camera=new GameObject("Tiered validation camera").AddComponent<Camera>(); camera.enabled=false;
                camera.aspect=1.5f; camera.nearClipPlane=.04f; camera.farClipPlane=160;
                camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.035f,.045f,.06f);
                var sun=new GameObject("Sun").AddComponent<Light>(); sun.type=LightType.Directional; sun.transform.rotation=Quaternion.Euler(55,-25,0);
                var sh=new UnityEngine.Rendering.SphericalHarmonicsL2(); sh.AddAmbientLight(new Color(.23f,.27f,.20f)); RenderSettings.ambientProbe=sh;
                CheckEightMaterials(); CheckFullTour(); CheckHistoryScaling();
                Require(errors.Count==0,string.Join("\n",errors)); report.passed=true;
                Debug.Log("GRASS_TIERED_PASS "+JsonUtility.ToJson(report));
            }
            catch(Exception e) { report.error=e.ToString(); Debug.LogError("GRASS_TIERED_FAIL "+e); }
            finally
            {
                Application.logMessageReceived-=CaptureError;
                File.WriteAllText(Path.Combine(output,"Results.json"),JsonUtility.ToJson(report,true));
                if(!Application.isBatchMode)EditorSceneManager.CloseScene(scene,true);
                if(previous.IsValid() && previous.isLoaded)UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);
            }
            if(Application.isBatchMode)EditorApplication.Exit(report.passed?0:1);
        }
        private static void Record(string name,double value) => report.metrics.Add(new Metric { name=name,value=value });
        private static void Require(bool value,string message) { if(!value)throw new InvalidOperationException(message); }
        private static void CaptureError(string message,string stack,LogType severity)
        { if(severity==LogType.Error || severity==LogType.Exception || severity==LogType.Assert)errors.Add(message); }
        private static XPBDGrassSettings Physics(GrassResponseType type,int capacity,float radius)
        {
            var value=JsonUtility.FromJson<XPBDGrassSettings>(JsonUtility.ToJson(type.Physics));
            value.EnablePlasticity=true; value.MaxActiveBlades=capacity; value.NearDistance=radius;
            value.PoolHysteresis=.35f; value.ColliderRadius=.38f; value.MaxDistance=100; return value;
        }
        private static PBDGrassPatchRenderer Make(PBDGrassPatch patch,GrassResponseType type,XPBDGrassSettings physics,bool tiered,Transform contact=null,int history=65536)
        {
            return new PBDGrassPatchRenderer(patch,solver,material,camera,wind,physics,
                contact==null?null:new[] { contact },new GrassHandoffSettings { BakedResponse=type,UseTieredState=tiered,
                    MaxHistoryBlades=history,HistoryBandWidth=2 },tiered?tieredShader:baselineShader);
        }
        private static float Difference(BoneInfo[] a,BoneInfo[] b)
        {
            float difference=0;
            for(int n=0;n<a.Length;n++)
            {
                Require(!float.IsNaN(b[n].Position.sqrMagnitude) && !float.IsInfinity(b[n].Position.sqrMagnitude),"Nonfinite pose.");
                difference=Mathf.Max(difference,Vector3.Distance(a[n].Position,b[n].Position));
            }
            return difference;
        }
        private static void CheckEightMaterials()
        {
            for(int t=0;t<8;t++)
            {
                var type=AssetDatabase.LoadAssetAtPath<GrassResponseType>(GrassResponseBaker.TypePath(t));
                var patch=GrassResponseType.CreateHandoffPatch(type,Vector3.zero,4);
                try
                {
                    using(var a=Make(patch,type,Physics(type,32,1),false))using(var b=Make(patch,type,Physics(type,32,1),true))
                    {
                        a.SimulationCenter=b.SimulationCenter=Vector3.one*1000;
                        a.RefreshSimulationSelection(); b.RefreshSimulationSelection(); a.ResetDeformation(); b.ResetDeformation();
                        float max=0;
                        for(int tick=0;tick<600;tick++)
                        {
                            var force=GrassResponseDemo.WindAt(tick/60f,true);
                            a.SetWind(force,.1f,new Vector4(.01f,.01f,0,1)); b.SetWind(force,.1f,new Vector4(.01f,.01f,0,1));
                            a.Advance(1f/60); b.Advance(1f/60);
                            if(tick%60==0)max=Mathf.Max(max,Difference(a.Handoff.ReadDisplayNodes(),b.Handoff.ReadDisplayNodes()));
                        }
                        Record("type_"+(t+1)+"_middle_max_node_difference_m",max);
                        Require(max<.001f,"Intact baked response changed for material "+(t+1));
                        Require(b.Handoff.HistoryResidentBlades==0,"An empty near field retained global display history.");
                    }
                }
                finally { Object.DestroyImmediate(patch.PatchMesh); }
            }
        }
        private static void CheckFullTour()
        {
            var type=AssetDatabase.LoadAssetAtPath<GrassResponseType>(GrassResponseBaker.TypePath(5));
            var contact=new GameObject("Tiered tour contact");
            var shape=contact.AddComponent<GrassInteractionCollider>(); shape.Type=GrassInteractionCollider.Shape.Sphere; shape.Radius=.38f;
            var patch=GrassResponseType.CreateHandoffPatch(type,Vector3.zero,64);
            try
            {
                camera.transform.position=new Vector3(-12,2.5f,-2.5f); camera.transform.LookAt(Vector3.up*.6f);
                contact.transform.position=new Vector3(-12,.6f,-2.5f)+Vector3.ProjectOnPlane(camera.transform.forward,Vector3.up).normalized*1.1f;
                using(var a=Make(patch,type,Physics(type,32768,20),false,contact.transform))
                using(var b=Make(patch,type,Physics(type,32768,20),true,contact.transform))
                {
                    a.SimulationCenter=b.SimulationCenter=new Vector3(-12,0,-2.5f);
                    a.RefreshSimulationSelection(); b.RefreshSimulationSelection(); a.ResetDeformation(); b.ResetDeformation();
                    Record("baseline_gpu_buffer_bytes",a.AllocatedGpuBytes); Record("tiered_gpu_buffer_bytes",b.AllocatedGpuBytes);
                    Record("baseline_full_history_bytes",a.Handoff.HistoryBytes); Record("tiered_full_history_bytes",b.Handoff.HistoryBytes);
                    Require(b.AllocatedGpuBytes<a.AllocatedGpuBytes && b.Handoff.HistoryCapacityBlades==65536,"History/geometry storage was not reduced.");
                    float maxDifference=0,steadyFarDifference=0,activeDifference=0; int minHistory=int.MaxValue,maxHistory=0,peakTransitions=0;
                    int startingEntries=b.Handoff.EntryInitializations;
                    for(int tick=0;tick<1440;tick++)
                    {
                        float time=(tick+1)/60f;
                        camera.transform.position=new Vector3(-Mathf.Cos(time*Mathf.PI/12)*12,2.5f,-2.5f); camera.transform.LookAt(Vector3.up*.6f);
                        var focus=camera.transform.position; focus.y=0;
                        contact.transform.position=focus+Vector3.ProjectOnPlane(camera.transform.forward,Vector3.up).normalized*1.1f+Vector3.up*.6f;
                        var force=GrassResponseDemo.WindAt(time,true)*(new Vector3(4,0,2).magnitude/6);
                        a.SimulationCenter=b.SimulationCenter=focus;
                        a.SetWind(force,.1f,new Vector4(.01f,.01f,0,1)); b.SetWind(force,.1f,new Vector4(.01f,.01f,0,1));
                        a.Advance(1f/60); b.Advance(1f/60);
                        Require(a.ActiveBladeCount==32768 && b.ActiveBladeCount==32768,"Tour lost full physical workload.");
                        if(tick>60)minHistory=Mathf.Min(minHistory,b.Handoff.HistoryResidentBlades);
                        maxHistory=Mathf.Max(maxHistory,b.Handoff.HistoryResidentBlades); peakTransitions=Mathf.Max(peakTransitions,b.Handoff.TransitionBlades);
                        if(tick%120==0)
                        {
                            var old=a.Handoff.ReadDisplayNodes(); var next=b.Handoff.ReadDisplayNodes();
                            maxDifference=Mathf.Max(maxDifference,Difference(old,next));
                            var selected=new bool[4096]; foreach(var grid in b.Pool.Selected)selected[grid.GridId]=true;
                            for(int blade=0;blade<131072;blade++)
                            {
                                float distance=Vector3.Distance(old[blade*8+7].Position,next[blade*8+7].Position);
                                if(selected[blade/32])activeDifference=Mathf.Max(activeDifference,distance);
                                else steadyFarDifference=Mathf.Max(steadyFarDifference,distance);
                            }
                        }
                        if(tick==720) { Capture("Baseline",a); Capture("Tiered",b); }
                    }
                    Record("full_tour_max_node_difference_m",maxDifference); Record("full_tour_active_tip_difference_m",activeDifference);
                    Record("full_tour_far_tip_difference_m",steadyFarDifference); Record("full_tour_min_history_blades",minHistory);
                    Record("full_tour_max_history_blades",maxHistory); Record("full_tour_peak_transition_blades",peakTransitions);
                    Record("full_tour_new_entries",b.Handoff.EntryInitializations-startingEntries);
                    Require(minHistory<32768 && maxHistory<65536,"History did not shrink to a boundary region.");
                    Require(b.Handoff.EntryInitializations>startingEntries,"No LOD entries exercised.");
                    Require(maxDifference<.15f,"Tiered visible shape diverged excessively from the accepted baseline.");
                    Record("full_tour_ticks",b.SimulationTicks); Require(b.SimulationTicks==1440 && b.DroppedTime==0,"Fixed ticks changed.");
                    var pa=a.ReadPlasticBends(); var pb=b.ReadPlasticBends(); float plasticDifference=0;
                    for(int n=0;n<pa.Length;n++)plasticDifference=Mathf.Max(plasticDifference,(pa[n]-pb[n]).magnitude);
                    Record("full_tour_plastic_difference_rad",plasticDifference);
                    camera.transform.position=new Vector3(43,48,-43); camera.transform.LookAt(Vector3.zero); Capture("TieredOverview",b);
                }
            }
            finally { Object.DestroyImmediate(patch.PatchMesh); Object.DestroyImmediate(contact); }
        }
        private static void CheckHistoryScaling()
        {
            var type=AssetDatabase.LoadAssetAtPath<GrassResponseType>(GrassResponseBaker.TypePath(5));
            long bytes=-1;
            foreach(int size in new[] { 8,12 })
            {
                var patch=GrassResponseType.CreateHandoffPatch(type,Vector3.zero,size);
                try
                {
                    using(var r=Make(patch,type,Physics(type,32,1),true,null,512))
                    {
                        if(bytes>=0)Require(r.Handoff.HistoryBytes==bytes,"History capacity grew with field size.");
                        bytes=r.Handoff.HistoryBytes;
                        Record("field_"+size+"_history_bytes",bytes);
                        for(int tick=0;tick<120;tick++)r.Advance(1f/60);
                        r.SimulationCenter=Vector3.one*100; r.RefreshSimulationSelection();
                        for(int tick=0;tick<60;tick++)r.Advance(1f/60);
                        Require(r.Handoff.HistoryResidentBlades==0,"Completed transitions were never released.");
                    }
                }
                finally { Object.DestroyImmediate(patch.PatchMesh); }
            }
        }
        private static void Capture(string name,PBDGrassPatchRenderer r)
        {
            var rt=new RenderTexture(1200,800,24); rt.Create(); camera.targetTexture=rt;
            var previous=RenderTexture.active; var texture=new Texture2D(1200,800,TextureFormat.RGB24,false);
            try
            { r.Draw(camera); camera.Render(); RenderTexture.active=rt; texture.ReadPixels(new Rect(0,0,1200,800),0,0); texture.Apply();
                File.WriteAllBytes(Path.Combine(output,name+".png"),texture.EncodeToPNG()); }
            finally { RenderTexture.active=previous; camera.targetTexture=null; rt.Release(); Object.DestroyImmediate(texture); Object.DestroyImmediate(rt); }
        }
    }
}
