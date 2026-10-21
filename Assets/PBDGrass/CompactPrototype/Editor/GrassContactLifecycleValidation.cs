using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace GrassFinal.PBDGrass.Editor
{
    public static class GrassContactLifecycleValidation
    {
        [Serializable] private sealed class Report
        {
            public bool passed,idleZero,heldTwoSeconds,liveDuringFade,recontactPreservesSolver,releasedAfterThreeSeconds,
                plasticRetained,streamRetained,unloadClears,finiteState,capRespected,outerCoverageContinuous;
            public string error,gpu;
            public int ticks,maxActive,maxHistoryResident,historyCapacity,outerCoverageChecks;
            public float firstFadeAt,releasedAt,physicalMotionDuringFade,recontactNodeError,maxTipStep,releaseTipStep,peakPlastic;
            public float maxTipStepAt,maxTipStepPhase,maxPhysicalTipStep;
            public float maxFadeTipStep;
            public long gpuBufferBytes;
        }
        private struct State {public int Pool,Changed;public float Started,Phase;}
        private struct OuterInstance {public Vector3 Root;public float Seed;public Vector2 Facing;public float Width,Fade;}
        private static Report report;
        private static GrassHandoffDemo demo;
        private static GrassShowcaseController lab;
        private static string output;
        private static int marker;
        private static Vector3 contactCenter;
        private static readonly List<string> errors=new List<string>();
        private static PBDGrassPatchRenderer R=>demo.Renderer;
        private static T Field<T>(object obj,string name)=>(T)obj.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(obj);
        private static void Require(bool value,string message) {if(!value)throw new InvalidOperationException(message);}
        private static void Log(string message,string stack,LogType type) {if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert)errors.Add(message);}
        [MenuItem("Tools/PBD Grass/Validate Contact Lifecycle")]
        public static void Run()
        {
            output=Environment.GetEnvironmentVariable("GRASS_LIFECYCLE_OUTPUT")??Path.GetFullPath("Assets/PBDGrass/CompactPrototype/EventDriven/ValidationRun");
            Directory.CreateDirectory(output);report=new Report {gpu=SystemInfo.graphicsDeviceName};errors.Clear();Application.logMessageReceived+=Log;
            try
            {
                EditorSceneManager.OpenScene(GrassShowcaseSceneBuilder.ScenePath);
                demo=Object.FindObjectOfType<GrassHandoffDemo>();lab=Object.FindObjectOfType<GrassShowcaseController>();
                demo.enabled=false;lab.enabled=false;demo.OuterCoverage.enabled=false;demo.Cam.enabled=false;
                ReleaseDemo();foreach(var actor in lab.Actors) {actor.Collider.enabled=false;foreach(var v in actor.Visuals)v.enabled=false;}
                demo.Cam.transform.SetPositionAndRotation(new Vector3(0,100,0),Quaternion.Euler(90,0,0));
                demo.Cam.orthographic=true;demo.Cam.orthographicSize=72;demo.Cam.aspect=1600f/900;
                demo.AutoTour=false;demo.RandomWind=false;demo.Wind=new Vector3(4,0,2);
                Require(demo.Physics.ContactDrivenSimulation,"The contact lab did not enable event-driven simulation.");
                demo.Initialize();demo.OuterCoverage.Initialize();Focus(Vector3.zero);
                marker=GrassWorldIdentity.Slot(new Vector2Int(40,0),demo.PatchSize);
                contactCenter=new Vector3(40.5f,.35f,.5f);
                report.gpuBufferBytes=R.AllocatedGpuBytes+demo.OuterCoverage.AllocatedGpuBytes;report.historyCapacity=R.Handoff.HistoryCapacityBlades;
                Steps(60);report.idleZero=R.ActiveBladeCount==0;Require(report.idleZero,"Idle grass filled the pool.");
                Contact(true);Steps(180);Require(R.Pool.IsActive(marker),"A middle collision did not wake XPBD.");
                var plast=LivePlastic();foreach(var v in plast)report.peakPlastic=Mathf.Max(report.peakPlastic,((Vector2)v).magnitude);
                Require(report.peakPlastic>.01f,"The collision did not lodge grass.");Capture("Contact");
                Contact(false);R.RefreshSimulationSelection();float release=R.SimulationTime;
                Steps(115);report.heldTwoSeconds=R.Pool.IsActive(marker) && GridState().Phase!=2;
                Require(report.heldTwoSeconds,"Physics ended or began fading before its two-second settling period.");
                while(GridState().Phase!=2) {Step();Require(R.SimulationTime-release<2.1f,"The exit fade never started.");}
                report.firstFadeAt=R.SimulationTime-release;
                var movingBefore=Nodes();Steps(25);var movingAfter=Nodes();
                report.physicalMotionDuringFade=NodeDifference(movingBefore,movingAfter);
                report.liveDuringFade=R.Pool.IsActive(marker) && GridState().Phase==2 && report.physicalMotionDuringFade>1e-6f;
                Require(report.liveDuringFade,"The solver stopped while the picture was fading.");Capture("LiveFade");
                int oldSlot=R.Pool.SlotForGrid(marker),seeds=R.Handoff.EntryInitializations;var beforeResume=Nodes();
                Contact(true);R.RefreshSimulationSelection();report.recontactNodeError=NodeDifference(beforeResume,Nodes());
                report.recontactPreservesSolver=R.Pool.SlotForGrid(marker)==oldSlot && R.Handoff.EntryInitializations==seeds && report.recontactNodeError==0 && GridState().Phase<0;
                Require(report.recontactPreservesSolver,"Recontact reset the solver or changed its slot.");Steps(45);
                Contact(false);R.RefreshSimulationSelection();release=R.SimulationTime;
                Vector3[] previous=Tips();bool wasActive=true;var previousNodes=Nodes();
                while(R.Pool.IsActive(marker))
                {
                    Step();var now=Tips();float delta=TipDifference(previous,now);
                    if(delta>report.maxTipStep) {report.maxTipStep=delta;report.maxTipStepAt=R.SimulationTime-release;report.maxTipStepPhase=GridState().Phase;}
                    if(GridState().Phase==2)report.maxFadeTipStep=Mathf.Max(report.maxFadeTipStep,delta);
                    if(R.Pool.IsActive(marker)) {var nodes=Nodes();report.maxPhysicalTipStep=Mathf.Max(report.maxPhysicalTipStep,NodeDifference(previousNodes,nodes));previousNodes=nodes;}
                    if(wasActive && !R.Pool.IsActive(marker))report.releaseTipStep=delta;
                    previous=now;wasActive=R.Pool.IsActive(marker);
                    Require(R.SimulationTime-release<3.1f,"The slot was not reclaimed after the full exit.");
                }
                report.releasedAt=R.SimulationTime-release;
                report.releasedAfterThreeSeconds=report.releasedAt>=2.99f && report.releasedAt<3.06f;
                Require(report.releasedAfterThreeSeconds,"The slot lifetime was not two seconds plus one second.");
                Require(report.releaseTipStep<.02f,"Visible tips jumped at slot reclamation.");
                Require(report.maxFadeTipStep<.1f,"The one-second fade introduced a visible tip discontinuity.");
                var saved=Packed();Steps(60);report.plasticRetained=Equal(saved,Packed());
                Require(report.plasticRetained && R.ActiveBladeCount==0,"Released grass lost plastic or stayed simulated.");Capture("Released");
                Focus(Vector3.right*8);Steps(30);report.streamRetained=Equal(saved,Packed());Require(report.streamRetained,"Overlapping streaming lost plastic.");
                // Resume the same world identity, then move the residency window while its tail runs.
                Contact(true);Steps(45);Contact(false);R.RefreshSimulationSelection();Focus(Vector3.right*16);Steps(60);
                Require(R.Pool.IsActive(marker),"An overlapping page move cut the physical tail short.");Steps(130);Require(!R.Pool.IsActive(marker),"A page move prevented slot reclamation.");
                // A lifted but enabled collider is not a collision event in this flat-field demo.
                Contact(true);lab.Actors[0].Collider.transform.position+=Vector3.up*10;Steps(190);
                Require(R.ActiveBladeCount==0,"An airborne actor kept the simulation awake.");Contact(false);
                Focus(Vector3.right*256);Focus(Vector3.zero);Steps(30);report.unloadClears=true;
                foreach(uint v in Packed())report.unloadClears&=v==0;
                Require(report.unloadClears,"An unloaded world identity retained stale plastic.");
                Contact(true);lab.Actors[0].Collider.transform.position=new Vector3(.5f,30,.5f);lab.Actors[0].Collider.Radius=32;
                R.RefreshSimulationSelection();report.capRespected=R.ActiveBladeCount==32768;
                Require(report.capRespected,"An oversized event did not respect the 32768 ceiling.");
                Contact(false);Steps(190);Require(R.ActiveBladeCount==0,"The full pool did not drain after its tail.");
                contactCenter=new Vector3(60.5f,.35f,.5f);marker=GrassWorldIdentity.Slot(new Vector2Int(60,0),demo.PatchSize);
                Contact(true);Steps(90);CheckOuterCoverage();Contact(false);R.RefreshSimulationSelection();Steps(135);CheckOuterCoverage();Capture("OuterFade");
                Steps(30);CheckOuterCoverage();Contact(true);R.RefreshSimulationSelection();CheckOuterCoverage();Steps(12);CheckOuterCoverage();
                Contact(false);Steps(190);CheckOuterCoverage();report.outerCoverageContinuous=true;
                Require(report.gpuBufferBytes==R.AllocatedGpuBytes+demo.OuterCoverage.AllocatedGpuBytes,"The lifecycle expanded GPU buffers.");
                Require(errors.Count==0,string.Join("\n",errors));report.finiteState=true;report.ticks=R.SimulationTicks;report.passed=true;
            }
            catch(Exception e) {report.error=e.ToString();Debug.LogError("CONTACT_LIFECYCLE_FAIL "+e);}
            finally
            {
                if(demo!=null) {typeof(GrassOuterCoverage).GetMethod("Release",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(demo.OuterCoverage,null);ReleaseDemo();}
                Application.logMessageReceived-=Log;File.WriteAllText(Path.Combine(output,"Results.json"),JsonUtility.ToJson(report,true));Debug.Log("CONTACT_LIFECYCLE_RESULT "+JsonUtility.ToJson(report));
            }
            if(Application.isBatchMode)EditorApplication.Exit(report.passed?0:1);
        }
        private static void ReleaseDemo()=>typeof(GrassHandoffDemo).GetMethod("Release",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(demo,null);
        private static void Focus(Vector3 p) {demo.Cam.transform.position=new Vector3(p.x,100,p.z);R.SetOuterCoverage(demo.OuterCoverage.CoverageTransition,true);R.StreamTo(p);R.RefreshSimulationSelection();}
        private static void Contact(bool enabled)
        {var actor=lab.Actors[0];var c=actor.Collider;c.Type=GrassInteractionCollider.Shape.Sphere;c.Radius=1.2f;c.HalfSegment=0;c.transform.position=contactCenter;c.enabled=enabled;
            foreach(var visual in actor.Visuals) {visual.enabled=enabled;visual.transform.localScale=Vector3.one*2.4f;}}
        private static State GridState() {var a=new State[1];R.Handoff.ClassificationStates.GetData(a,0,marker,1);return a[0];}
        private static void Step()
        {
            R.Advance(1f/60);report.maxActive=Math.Max(report.maxActive,R.ActiveBladeCount);report.maxHistoryResident=Math.Max(report.maxHistoryResident,R.Handoff.HistoryResidentBlades);
            Require(R.ActiveBladeCount<=32768,"The simulation exceeded its hard cap.");
        }
        private static void Steps(int n) {for(int i=0;i<n;i++)Step();}
        private static BoneInfo[] Nodes()
        {var a=new BoneInfo[32*8];Field<ComputeBuffer>(R,"bones").GetData(a,0,R.Pool.SlotForGrid(marker)*32*8,a.Length);return a;}
        private static float NodeDifference(BoneInfo[] a,BoneInfo[] b)
        {float d=0;for(int i=0;i<a.Length;i++) {float v=Vector3.Distance(a[i].Position,b[i].Position);Require(!float.IsNaN(v) && !float.IsInfinity(v),"Non-finite physical state.");d=Mathf.Max(d,v);}return d;}
        private static Vector4[] LivePlastic() {var a=new Vector4[32*7];Field<ComputeBuffer>(R,"plasticBends").GetData(a,0,R.Pool.SlotForGrid(marker)*32*7,a.Length);return a;}
        private static uint[] Packed() {var a=new uint[32*8];R.Compact.Plastic.GetData(a,0,marker*32*8,a.Length);return a;}
        private static bool Equal(uint[] a,uint[] b) {for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
        private static Vector3[] Tips()
        {
            var tips=new Vector3[32];
            if(R.Pool.IsActive(marker))
            {var vertices=new Vector3[32*15];Field<ComputeBuffer>(R,"positions").GetData(vertices,0,R.Pool.SlotForGrid(marker)*32*15,vertices.Length);for(int i=0;i<32;i++)tips[i]=vertices[i*15+14];}
            else
            {var words=new uint[32*20];R.Compact.Pose.GetData(words,0,marker*32*20,words.Length);for(int i=0;i<32;i++)tips[i]=R.Data.GetBlade(marker*32+i).Root+
                new Vector3(Mathf.HalfToFloat((ushort)words[i*20+9]),Mathf.HalfToFloat((ushort)(words[i*20+9]>>16)),Mathf.HalfToFloat((ushort)words[i*20+10]));}
            return tips;
        }
        private static float TipDifference(Vector3[] a,Vector3[] b) {float v=0;for(int i=0;i<a.Length;i++)v=Mathf.Max(v,Vector3.Distance(a[i],b[i]));return v;}
        private static void CheckOuterCoverage()
        {
            demo.OuterCoverage.PrepareVisible();var args=new uint[4];demo.OuterCoverage.DiagnosticArguments.GetData(args);
            var instances=new OuterInstance[args[1]];demo.OuterCoverage.DiagnosticInstances.GetData(instances,0,0,instances.Length);
            var state=GridState();float weight=state.Pool<0?0:1;
            if(state.Pool>=0 && state.Phase==2)weight=1-Mathf.SmoothStep(0,1,(R.SimulationTime-state.Started)/demo.Physics.ContactFadeSeconds);
            if(state.Pool>=0 && state.Phase<0)weight=Mathf.Lerp(-state.Phase-1,1,Mathf.SmoothStep(0,1,(R.SimulationTime-state.Started)/demo.Handoff.TransitionSeconds));
            for(int leaf=0;leaf<32;leaf++)
            {
                var blade=R.Data.GetBlade(marker*32+leaf);float distance=new Vector2(blade.Root.x,blade.Root.z).magnitude;
                float coverage=1-Mathf.SmoothStep(0,1,(distance-52)/12),effective=Mathf.Lerp(coverage,1,weight);
                int count=0;foreach(var instance in instances)if((instance.Root-blade.Root).sqrMagnitude<1e-8f)count++;
                Require(count==(blade.Seed>effective?1:0),"Core/outer coverage disagreed during a live exit or recontact.");
            }
            report.outerCoverageChecks++;
        }
        private static void Capture(string name)
        {
            var target=new RenderTexture(1600,900,24);target.Create();var previous=RenderTexture.active;var pixels=new Texture2D(1600,900,TextureFormat.RGB24,false);
            var position=demo.Cam.transform.position;var rotation=demo.Cam.transform.rotation;float size=demo.Cam.orthographicSize;
            try {demo.Cam.targetTexture=target;demo.OuterCoverage.PrepareVisible();R.Draw(demo.Cam);demo.OuterCoverage.RenderPrepared(demo.Cam);demo.Cam.Render();RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,1600,900),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),pixels.EncodeToPNG());
                demo.Cam.transform.position=contactCenter+new Vector3(0,3.65f,-5);demo.Cam.transform.LookAt(contactCenter+Vector3.up*.15f);demo.Cam.orthographicSize=2.5f;
                demo.OuterCoverage.PrepareVisible();R.Draw(demo.Cam);demo.OuterCoverage.RenderPrepared(demo.Cam);demo.Cam.Render();RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,1600,900),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(output,name+"Close.png"),pixels.EncodeToPNG());}
            finally {demo.Cam.transform.SetPositionAndRotation(position,rotation);demo.Cam.orthographicSize=size;R.SetOuterCoverage(demo.OuterCoverage.CoverageTransition,true);
                demo.Cam.targetTexture=null;RenderTexture.active=previous;Object.DestroyImmediate(pixels);target.Release();Object.DestroyImmediate(target);}
        }
    }
}
