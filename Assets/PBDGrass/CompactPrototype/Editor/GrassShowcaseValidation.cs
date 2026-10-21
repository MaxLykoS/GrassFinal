using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace GrassFinal.PBDGrass.Editor
{
    [InitializeOnLoad]
    public static class GrassShowcaseValidation
    {
        private const string Key="GrassShowcaseValidation";
        [Serializable] private sealed class Report
        {
            public bool passed,elevatedView,allActorsMoved,stampsReleased,trailEnteredMiddle,trailReturned,lowView,topView,tierOverview,debugColors,finiteNearState;
            public bool eventDriven,returnedWithoutSimulation;
            public string error;
            public int colliders,minActive=int.MaxValue,maxActive,ticks,pageShifts,patchSize;
            public float plasticBeforeTour,plasticAfterTour,maxNearReach,middleFullDistance,middleOuterDistance;
            public long gpuBufferBytes;
        }
        private static GrassShowcaseController lab;
        private static Report report;
        private static RenderTexture target;
        private static string output;
        private static Vector3[] initial;
        private static float[] moved,minY,maxY;
        private static int lastFrame=-1,stage,marker;
        private static double started;
        static GrassShowcaseValidation() {if(SessionState.GetInt(Key,0)>0)Attach();}
        private static void Attach()
        {
            EditorApplication.playModeStateChanged-=State;EditorApplication.playModeStateChanged+=State;
            EditorApplication.update-=Update;EditorApplication.update+=Update;
            Application.logMessageReceived-=Log;Application.logMessageReceived+=Log;
        }
        [MenuItem("Tools/PBD Grass/Validate Contact Lab")]
        public static void Run()
        {
            EditorSceneManager.OpenScene(GrassShowcaseSceneBuilder.ScenePath);
            SessionState.SetInt(Key,1);Attach();EditorApplication.EnterPlaymode();
        }
        private static void Log(string text,string stack,LogType type)
        {if(report!=null && (type==LogType.Error || type==LogType.Exception || type==LogType.Assert))report.error=text;}
        private static void State(PlayModeStateChange state)
        {
            if(state==PlayModeStateChange.EnteredPlayMode)
            {
                lab=Object.FindObjectOfType<GrassShowcaseController>();report=new Report {colliders=lab.Actors.Length};
                output=Environment.GetEnvironmentVariable("GRASS_SHOWCASE_OUTPUT")??Path.GetFullPath(GrassShowcaseSceneBuilder.Root+"/Validation");
                Directory.CreateDirectory(output);started=EditorApplication.timeSinceStartup;lastFrame=-1;stage=0;
                Time.captureFramerate=60;
                target=new RenderTexture(1600,900,24);target.Create();lab.Demo.Cam.targetTexture=target;
                initial=new Vector3[lab.Actors.Length];moved=new float[initial.Length];minY=new float[initial.Length];maxY=new float[initial.Length];
                for(int i=0;i<initial.Length;i++) {initial[i]=lab.Actors[i].Collider.transform.position;minY[i]=maxY[i]=initial[i].y;}
                marker=GrassWorldIdentity.Slot(new Vector2Int(0,-5),lab.Demo.PatchSize)*32;
                report.patchSize=lab.Demo.PatchSize;report.debugColors=lab.Demo.Handoff.DebugLodBaseColors;
                report.eventDriven=lab.Demo.Physics.ContactDrivenSimulation;report.finiteNearState=true;
                report.middleFullDistance=lab.Demo.OuterCoverage.MiddleFullDistance;
                report.middleOuterDistance=lab.Demo.OuterCoverage.MiddleOuterDistance;
                report.elevatedView=lab.Demo.Cam.transform.position.y>=15 && !lab.Demo.Cam.orthographic;
            }
            if(state==PlayModeStateChange.EnteredEditMode && SessionState.GetInt(Key,0)==2)
            {
                bool ok=SessionState.GetBool(Key+"Passed",false);SessionState.SetInt(Key,0);
                EditorApplication.update-=Update;EditorApplication.playModeStateChanged-=State;Application.logMessageReceived-=Log;
                if(Application.isBatchMode)EditorApplication.Exit(ok?0:1);
            }
        }
        private static void Update()
        {
            if(!EditorApplication.isPlaying || report==null || lab==null || lab.Demo.Renderer==null || Time.frameCount==lastFrame)return;
            lastFrame=Time.frameCount;
            try
            {
                if(EditorApplication.timeSinceStartup-started>300)throw new TimeoutException("Contact lab exceeded the validation time limit.");
                var demo=lab.Demo;var r=demo.Renderer;float t=r.SimulationTime;
                demo.Cam.Render();
                report.minActive=Math.Min(report.minActive,r.ActiveBladeCount);report.maxActive=Math.Max(report.maxActive,r.ActiveBladeCount);
                for(int i=0;i<initial.Length;i++)
                {
                    var p=lab.Actors[i].Collider.transform.position;
                    moved[i]=Mathf.Max(moved[i],Vector3.Distance(initial[i],p));minY[i]=Mathf.Min(minY[i],p.y);maxY[i]=Mathf.Max(maxY[i],p.y);
                }
                if(stage==0 && t>=6) {Capture("ElevatedContacts");stage++;}
                if(stage==1 && t>=14)
                {
                    CheckFiniteNear();demo.ContactEnabled=false;Capture("AfterContact");stage++;
                    report.allActorsMoved=true;report.stampsReleased=true;
                    for(int i=0;i<initial.Length;i++)
                    {
                        if(lab.Actors[i].Movement!=GrassShowcaseController.Motion.Fixed)report.allActorsMoved&=moved[i]>.5f;
                        if(lab.Actors[i].Movement==GrassShowcaseController.Motion.Stamp)report.stampsReleased&=maxY[i]-minY[i]>2.9f;
                    }
                }
                if(stage==2 && t>=17) {report.plasticBeforeTour=MarkerPlastic();demo.AutoTour=true;stage++;}
                if(stage==3 && t>=27) {report.trailEnteredMiddle=r.Pool.SlotForGrid(marker/32)<0;Capture("MiddleTrail");stage++;}
                if(stage==4 && t>=41.2f) {demo.AutoTour=false;stage++;}
                if(stage==5 && t>=42)
                {
                    report.trailReturned=r.Pool.SlotForGrid(marker/32)>=0;report.plasticAfterTour=MarkerPlastic();Capture("ReturnedTrail");
                    report.returnedWithoutSimulation=report.eventDriven && r.ActiveBladeCount==0 &&
                        Vector3.ProjectOnPlane(r.Data.GetBlade(marker).Root-demo.Cam.transform.position,Vector3.up).magnitude<report.middleFullDistance;
                    lab.SetView(2);stage++;
                }
                if(stage==6 && t>=43)
                {
                    report.lowView=demo.Cam.transform.position.y<=3.1f;Capture("CloseView");lab.SetView(3);stage++;
                }
                if(stage==7 && t>=44)
                {
                    report.topView=demo.Cam.orthographic && demo.Cam.transform.position.y>=30;Capture("TopView");
                    lab.SetView(4);stage++;
                }
                if(stage==8 && t>=45)
                {
                    report.tierOverview=demo.Cam.orthographic && demo.Cam.orthographicSize>report.middleOuterDistance;Capture("TierOverview");
                    CheckFiniteNear();
                    report.ticks=r.SimulationTicks;report.pageShifts=demo.WorldLayout.Version;
                    report.gpuBufferBytes=r.AllocatedGpuBytes+demo.OuterCoverage.AllocatedGpuBytes;
                    bool budget=report.eventDriven?report.minActive==0 && report.maxActive>0 && report.maxActive<=32768:report.minActive==32768 && report.maxActive==32768;
                    report.passed=string.IsNullOrEmpty(report.error) && report.colliders==12 && budget &&
                        report.elevatedView && report.allActorsMoved && report.stampsReleased && report.trailEnteredMiddle && (report.trailReturned || report.returnedWithoutSimulation) &&
                        report.lowView && report.topView && report.tierOverview && report.debugColors && report.finiteNearState && report.maxNearReach<3 &&
                        report.plasticBeforeTour>.01f && report.plasticAfterTour>.01f && report.pageShifts>=6;
                    if(!report.passed && string.IsNullOrEmpty(report.error))report.error="Contact, camera, plastic persistence or LOD round-trip check failed.";
                    Finish();
                }
            }
            catch(Exception e) {report.error=e.ToString();Finish();}
        }
        private static void CheckFiniteNear()
        {
            var r=lab.Demo.Renderer;
            var buffer=(ComputeBuffer)typeof(PBDGrassPatchRenderer).GetField("bones",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(r);
            var nodes=new BoneInfo[buffer.count];buffer.GetData(nodes);
            foreach(var grid in r.Pool.Selected)for(int i=grid.PoolOffset*8;i<(grid.PoolOffset+32)*8;i++)
            {float length=nodes[i].Position.magnitude;report.finiteNearState&=!float.IsNaN(length) && !float.IsInfinity(length);report.maxNearReach=Mathf.Max(report.maxNearReach,length);}
        }
        private static float MarkerPlastic()
        {
            var values=lab.Demo.Renderer.ReadPlasticBends();float peak=0;
            for(int i=marker*7;i<(marker+32)*7;i++)peak=Mathf.Max(peak,values[i].magnitude);
            return peak;
        }
        private static void Capture(string name)
        {
            var previous=RenderTexture.active;RenderTexture.active=target;
            var pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            try {pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),pixels.EncodeToPNG());}
            finally {RenderTexture.active=previous;Object.DestroyImmediate(pixels);}
        }
        private static void Finish()
        {
            Time.captureFramerate=0;
            File.WriteAllText(Path.Combine(output,"Results.json"),JsonUtility.ToJson(report,true));Debug.Log("CONTACT_LAB "+JsonUtility.ToJson(report));
            SessionState.SetBool(Key+"Passed",report.passed);SessionState.SetInt(Key,2);
            lab.Demo.Cam.targetTexture=null;target.Release();Object.DestroyImmediate(target);
            report=null;EditorApplication.ExitPlaymode();
        }
    }
}
