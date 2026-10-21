using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;
namespace GrassFinal.PBDGrass.Editor
{
    [InitializeOnLoad]
    public static class GrassCompactPlayValidation
    {
        private const string Key="GrassCompactPlayValidation";
        [Serializable] private sealed class Report
        {
            public bool passed,overheadWorked,returnedToGround,compact;
            public string error;public int minActive=int.MaxValue,maxActive,shifts,ticks,observedFrames;
            public uint outerDrawn;
            public float startX,endX,droppedTime;public long gpuBufferBytes;
        }
        private static GrassHandoffDemo demo;private static Report report;private static double start;private static bool top,ground;
        private static RenderTexture target;
        private static int lastFrame=-1;
        static GrassCompactPlayValidation() {if(SessionState.GetInt(Key,0)>0)Attach();}
        private static void Attach()
        {
            EditorApplication.playModeStateChanged-=State;EditorApplication.playModeStateChanged+=State;
            EditorApplication.update-=Update;EditorApplication.update+=Update;
            Application.logMessageReceived-=Log;Application.logMessageReceived+=Log;
        }
        public static void Run()
        {
            EditorSceneManager.OpenScene(GrassCompactSceneBuilder.ScenePath);SessionState.SetInt(Key,1);Attach();EditorApplication.EnterPlaymode();
        }
        private static void Log(string message,string stack,LogType type)
        {if(report!=null && (type==LogType.Error || type==LogType.Exception || type==LogType.Assert))report.error=message;}
        private static void State(PlayModeStateChange state)
        {
            if(state==PlayModeStateChange.EnteredPlayMode)
            {
                demo=Object.FindObjectOfType<GrassHandoffDemo>();report=new Report {startX=demo.Cam.transform.position.x};
                demo.AutoTour=true;start=EditorApplication.timeSinceStartup;top=ground=false;lastFrame=-1;
                target=new RenderTexture(1280,720,24);target.Create();demo.Cam.targetTexture=target;
            }
            if(state==PlayModeStateChange.EnteredEditMode && SessionState.GetInt(Key,0)==2)
            {
                bool passed=SessionState.GetBool(Key+"Passed",false);SessionState.SetInt(Key,0);
                EditorApplication.update-=Update;EditorApplication.playModeStateChanged-=State;Application.logMessageReceived-=Log;
                if(Application.isBatchMode)EditorApplication.Exit(passed?0:1);
            }
        }
        private static void Update()
        {
            if(!EditorApplication.isPlaying || report==null || demo==null || demo.Renderer==null)return;
            if(Time.frameCount==lastFrame)return;lastFrame=Time.frameCount;
            try
            {
                // Shader import/first draw can stall wall time before simulation advances.
                // Exercise eight simulated seconds and both view transitions, with a real timeout.
                if(EditorApplication.timeSinceStartup-start>180)throw new TimeoutException("Play validation did not advance within 180 seconds.");
                double t=demo.Renderer.SimulationTime;report.observedFrames++;
                // Batch mode has no visible Game view. Render the actual camera so both
                // Camera.onPreCull paths, lighting, shadows and outer coverage execute.
                demo.Cam.Render();
                report.minActive=Math.Min(report.minActive,demo.Renderer.ActiveBladeCount);report.maxActive=Math.Max(report.maxActive,demo.Renderer.ActiveBladeCount);
                if(t>3 && !top) {demo.OverheadView=true;top=true;}
                if(t>3.5 && demo.Cam.orthographic && demo.Cam.transform.position.y>170)report.overheadWorked=true;
                if(t>4.5 && !ground) {demo.OverheadView=false;ground=true;}
                if(t>5.5 && !demo.Cam.orthographic && Mathf.Abs(demo.Cam.transform.position.y-2.5f)<.01f)report.returnedToGround=true;
                if(t<8)return;
                report.compact=demo.Renderer.Compact!=null;report.endX=demo.Cam.transform.position.x;
                report.shifts=demo.WorldLayout.Version;report.ticks=demo.Renderer.SimulationTicks;report.droppedTime=demo.Renderer.DroppedTime;
                report.gpuBufferBytes=demo.Renderer.AllocatedGpuBytes+demo.OuterCoverage.AllocatedGpuBytes;
                var args=new uint[4];demo.OuterCoverage.DiagnosticArguments.GetData(args);report.outerDrawn=args[1];
                report.passed=string.IsNullOrEmpty(report.error) && report.compact && report.minActive==32768 && report.maxActive==32768 &&
                    report.shifts>=4 && report.endX-report.startX>35 && report.overheadWorked && report.returnedToGround &&
                    demo.OuterCoverage.AllocatedGpuBytes>0 && report.outerDrawn>0;
                if(!report.passed && string.IsNullOrEmpty(report.error))report.error="Play-mode population, paging or view toggle failed.";
                Finish();
            }
            catch(Exception e) {report.error=e.ToString();Finish();}
        }
        private static void Finish()
        {
            string output=Environment.GetEnvironmentVariable("GRASS_COMPACT_OUTPUT")??Path.GetFullPath(GrassCompactSceneBuilder.Root+"/ValidationRun");
            Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"PlayMode.json"),JsonUtility.ToJson(report,true));
            Debug.Log("COMPACT_PLAY "+JsonUtility.ToJson(report));SessionState.SetBool(Key+"Passed",report.passed);SessionState.SetInt(Key,2);
            demo.Cam.targetTexture=null;if(target!=null) {target.Release();Object.DestroyImmediate(target);target=null;}
            report=null;EditorApplication.ExitPlaymode();
        }
    }
}
