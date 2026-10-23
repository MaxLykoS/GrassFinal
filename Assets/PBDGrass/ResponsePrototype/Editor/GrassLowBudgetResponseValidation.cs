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
    public static class GrassLowBudgetResponseValidation
    {
        [Serializable] private sealed class TypeResult
        {
            public string name;
            public float zeroTipGap,zeroNodeMaxGap,seededTipDrift,unloadedToEquilibrium;
            public float steadyWindMeanTipGap,randomMeanTipGap,randomMaxTipGap,maxNodeSpeed;
            public float gustMeanTipGap,gustMaxTipGap;
            public Vector2 frequency,damping,dynamicFit;
        }
        [Serializable] private sealed class BakeResult
        {
            public string type;
            public float maxTerminalSpeed,maxNodeDifference,maxControlDifference,maxFrameDifferenceDegrees,frequencyDifference,dampingDifference;
        }
        [Serializable] private sealed class Report
        {
            public bool passed; public string gpu,unity,error;
            public int blades,xpbdBlades,proxyStates;
            public List<TypeResult> types=new List<TypeResult>();
            public List<BakeResult> rebakes=new List<BakeResult>();
            public List<string> failedChecks=new List<string>();
        }
        private static Report report;
        private static GrassResponseDemo demo;
        private static string output;
        private static readonly List<GrassResponseType> clones=new List<GrassResponseType>();
        private static void ReleaseDemo()
        { if(demo!=null)typeof(GrassResponseDemo).GetMethod("Release",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(demo,null); }
        private static void Check(bool passed,string message) { if(!passed)report.failedChecks.Add(message); }
        private static readonly List<string> errors=new List<string>();
        [MenuItem("Tools/PBD Grass/Validate Low Budget Eight Type Response")]
        public static void Run()
        {
            output=Environment.GetEnvironmentVariable("GRASS_LOW_BUDGET_OUTPUT");
            if(string.IsNullOrEmpty(output))output=Path.GetFullPath(GrassLowBudgetBaker.Root+"/ValidationRun");
            Directory.CreateDirectory(output); report=new Report { gpu=SystemInfo.graphicsDeviceName,unity=Application.unityVersion };
            errors.Clear(); clones.Clear(); demo=null; Application.logMessageReceived+=CaptureError;
            var previous=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var scene=EditorSceneManager.OpenScene(GrassResponseBaker.Root+"/GrassResponseComparison.unity",
                Application.isBatchMode?OpenSceneMode.Single:OpenSceneMode.Additive);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            try
            {
                foreach(var root in scene.GetRootGameObjects()) { demo=root.GetComponentInChildren<GrassResponseDemo>(); if(demo!=null)break; }
                Require(demo!=null,"Comparison scene missing."); demo.enabled=false; ReleaseDemo();
                demo.Types=new GrassResponseType[8];
                for(int t=0;t<8;t++)
                {
                    var saved=AssetDatabase.LoadAssetAtPath<GrassResponseType>(GrassLowBudgetBaker.TypePath(t));
                    Require(saved!=null && saved.IsBaked,"Missing low budget response type "+(t+1));
                    var type=Object.Instantiate(saved);
                    type.Physics=JsonUtility.FromJson<XPBDGrassSettings>(JsonUtility.ToJson(saved.Physics));
                    clones.Add(type); demo.Types[t]=type;
                    Require(type.Physics.NearSubsteps==2 && type.Physics.ConstraintIterations==1,"Bank has wrong solver quality: "+(t+1));
                }
                demo.Initialize();
                Require(demo.Proxy!=null && demo.References.Length==8,"Eight baked types not loaded.");
                demo.Cam.enabled=false; demo.Cam.aspect=1.6f;
                report.blades=512; report.xpbdBlades=256; report.proxyStates=demo.Proxy.StateCount;
                var initial=new Vector3[8][];
                for(int t=0;t<8;t++)
                {
                    var type=demo.Types[t]; float maxBakeSpeed=0;
                    foreach(var pose in type.Samples)
                    { Require(pose!=null && pose.Nodes.Length==8 && pose.Frames.Length==7,"Incomplete bake."); maxBakeSpeed=Mathf.Max(maxBakeSpeed,pose.Speed); }
                    Check(maxBakeSpeed<.003f,"Bake did not settle: "+type.DisplayName);
                    var nodes=demo.References[t].ReadNodes(); initial[t]=new Vector3[8];
                    for(int n=0;n<8;n++)initial[t][n]=nodes[n].Position;
                    var result=new TypeResult { name=type.DisplayName,frequency=type.AngularFrequency,damping=type.DampingRatio,dynamicFit=type.DynamicFitError };
                    result.unloadedToEquilibrium=Vector3.Distance(nodes[7].Position,demo.References[t].Data.GetBlade(0).Root+demo.References[t].Data.GetRestNode(7).Position);
                    report.types.Add(result);
                }
                for(int tick=0;tick<180;tick++)demo.Step(Vector3.zero);
                var proxy=demo.Proxy.ReadNodes();
                for(int t=0;t<8;t++)
                {
                    var nodes=demo.References[t].ReadNodes(); Vector3 offset=demo.ProxyRoot(t)-demo.ReferenceRoot(t);
                    var result=report.types[t]; result.seededTipDrift=Vector3.Distance(nodes[7].Position,initial[t][7]);
                    result.zeroTipGap=Vector3.Distance(nodes[7].Position+offset,proxy[t*32*8+7]);
                    for(int n=0;n<8;n++)result.zeroNodeMaxGap=Mathf.Max(result.zeroNodeMaxGap,Vector3.Distance(nodes[n].Position+offset,proxy[t*32*8+n]));
                    Check(result.seededTipDrift<.01f && result.zeroNodeMaxGap<.06f,"Gravity initialization/proxy mismatch: "+result.name);
                }
                demo.SetView(-1); Capture("01_overview_zero"); demo.SetView(5); Capture("02_focused_zero");
                int steadySamples=0,randomSamples=0,gustSamples=0;
                for(int tick=0;tick<3120;tick++)
                {
                    float time=tick/60f;
                    Vector3 force=time<40?GrassResponseDemo.WindAt(time):GrassResponseDemo.WindAt(Mathf.Floor((time-40)/.35f)*1.8f,true);
                    demo.Step(force);
                    if(tick%6!=0)continue;
                    proxy=demo.Proxy.ReadNodes(); bool steady=(time>=8 && time<9) || (time>=19 && time<20),random=time>=22 && time<40,gust=time>=40;
                    if(steady)steadySamples++; if(random)randomSamples++; if(gust)gustSamples++;
                    for(int t=0;t<8;t++)
                    {
                        var nodes=demo.References[t].ReadNodes(); Vector3 offset=demo.ProxyRoot(t)-demo.ReferenceRoot(t);
                        for(int n=0;n<8;n++)
                        {
                            Require(Finite(nodes[n].Position) && Finite(proxy[t*256+n]),"Invalid response pose.");
                            report.types[t].maxNodeSpeed=Mathf.Max(report.types[t].maxNodeSpeed,nodes[n].Velocities.magnitude);
                        }
                        float gap=Vector3.Distance(nodes[7].Position+offset,proxy[t*256+7]);
                        if(steady)report.types[t].steadyWindMeanTipGap+=gap;
                        if(random) { report.types[t].randomMeanTipGap+=gap; report.types[t].randomMaxTipGap=Mathf.Max(report.types[t].randomMaxTipGap,gap); }
                        if(gust) { report.types[t].gustMeanTipGap+=gap; report.types[t].gustMaxTipGap=Mathf.Max(report.types[t].gustMaxTipGap,gap); }
                    }
                    if(tick==300)Capture("03_focused_wind");
                    if(tick==720)Capture("04_focused_release");
                    if(tick==1500) { Capture("05_focused_random"); demo.SetView(-1); Capture("06_overview_random"); demo.SetView(5); }
                }
                foreach(var result in report.types)
                {
                    result.steadyWindMeanTipGap/=steadySamples; result.randomMeanTipGap/=randomSamples; result.gustMeanTipGap/=gustSamples;
                    Check(result.steadyWindMeanTipGap<.15f,"Steady wind proxy too far from XPBD: "+result.name);
                    Check(result.randomMeanTipGap<.25f && result.randomMaxTipGap<.7f,"Random response too far from XPBD: "+result.name);
                    Check(result.gustMaxTipGap<1,"Rapid gust caused a large divergence: "+result.name);
                }
                foreach(var value in demo.Proxy.ReadResponse())Require(Finite((Vector3)value) && !float.IsNaN(value.w),"Invalid reduced state.");
                CheckRebakes();
                Require(report.failedChecks.Count==0,string.Join("\n",report.failedChecks));
                Require(errors.Count==0,string.Join("\n",errors)); report.passed=true;
                Debug.Log("GRASS_LOW_BUDGET_RESPONSE_PASS "+JsonUtility.ToJson(report));
            }
            catch(Exception e) { report.error=e.ToString(); Debug.LogError("GRASS_LOW_BUDGET_RESPONSE_FAIL "+e); }
            finally
            {
                ReleaseDemo();
                foreach(var clone in clones)if(clone!=null)Object.DestroyImmediate(clone); clones.Clear();
                Application.logMessageReceived-=CaptureError;
                File.WriteAllText(Path.Combine(output,"Results.json"),JsonUtility.ToJson(report,true));
                if(!Application.isBatchMode)EditorSceneManager.CloseScene(scene,true);
                if(previous.IsValid() && previous.isLoaded)UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);
            }
            if(Application.isBatchMode)EditorApplication.Exit(report.passed?0:1);
        }
        private static void CheckRebakes()
        {
            var wind=GrassResponseType.CreateUniformWindTexture();
            try
            {
                for(int t=0;t<8;t++)
                {
                    var accepted=AssetDatabase.LoadAssetAtPath<GrassResponseType>(GrassLowBudgetBaker.TypePath(t));
                    var fresh=GrassResponseBaker.ComputeType(t,demo.Cam,demo.Solver,demo.GrassMaterial,wind,2,1,.5f);
                    try
                    {
                        var result=new BakeResult { type=accepted.DisplayName,
                            frequencyDifference=(fresh.AngularFrequency-accepted.AngularFrequency).magnitude,
                            dampingDifference=(fresh.DampingRatio-accepted.DampingRatio).magnitude };
                        for(int sample=0;sample<25;sample++)
                        {
                            var a=accepted.Samples[sample]; var b=fresh.Samples[sample];
                            result.maxTerminalSpeed=Mathf.Max(result.maxTerminalSpeed,b.Speed);
                            for(int n=0;n<8;n++)result.maxNodeDifference=Mathf.Max(result.maxNodeDifference,(a.Nodes[n]-b.Nodes[n]).magnitude);
                            for(int j=0;j<7;j++)result.maxFrameDifferenceDegrees=Mathf.Max(result.maxFrameDifferenceDegrees,Quaternion.Angle(a.Frames[j],b.Frames[j]));
                            result.maxControlDifference=Mathf.Max(result.maxControlDifference,Mathf.Max((a.C1-b.C1).magnitude,
                                Mathf.Max((a.C2-b.C2).magnitude,(a.Tip-b.Tip).magnitude)));
                        }
                        report.rebakes.Add(result);
                        Check(result.maxTerminalSpeed<.003f && result.maxNodeDifference<.005f && result.maxControlDifference<.01f
                            && result.maxFrameDifferenceDegrees<1 && result.frequencyDifference<.36f && result.dampingDifference<.06f,
                            "Rebake no longer matches accepted low budget material "+(t+1));
                        Debug.Log("GRASS_LOW_BUDGET_REBAKE "+(t+1)+" "+JsonUtility.ToJson(result));
                    }
                    finally { Object.DestroyImmediate(fresh); }
                }
            }
            finally { Object.DestroyImmediate(wind); }
        }
        private static bool Finite(Vector3 value) => !float.IsNaN(value.sqrMagnitude) && !float.IsInfinity(value.sqrMagnitude);
        private static void CaptureError(string message,string stack,LogType type)
        { if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert)errors.Add(message); }
        private static void Require(bool yes,string message) { if(!yes)throw new InvalidOperationException(message); }
        private static void Capture(string name)
        {
            var camera=demo.Cam; var rt=new RenderTexture(1600,1000,24); rt.Create(); camera.targetTexture=rt;
            demo.DrawForValidation(camera); camera.Render(); var previous=RenderTexture.active; RenderTexture.active=rt;
            var tex=new Texture2D(1600,1000,TextureFormat.RGB24,false); tex.ReadPixels(new Rect(0,0,1600,1000),0,0); tex.Apply();
            File.WriteAllBytes(Path.Combine(output,name+".png"),tex.EncodeToPNG());
            RenderTexture.active=previous; camera.targetTexture=null; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        }
    }
}
