using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace GrassFinal.PBDGrass.Editor
{
    public static class GrassCombinedBaselineValidation
    {
        [Serializable] private sealed class BakeResult
        {
            public string type;
            public float maxNodeDifference,maxControlDifference,maxFrameDifferenceDegrees;
            public float frequencyDifference,dampingDifference,maxTerminalSpeed;
        }
        [Serializable] private sealed class Report
        {
            public bool passed;
            public string gpu,unity,error;
            public int totalBlades,minActiveBlades=int.MaxValue,maxActiveBlades,bezierBlades;
            public int exitedGrids,reenteredGrids,evictions,simulationTicks,visibleGrids;
            public int seedChecks;
            public float maxSeedPositionError,maxSeedVelocityError,maxSeedRotationError,maxSeedAngularError;
            public float retainedPlasticError,peakPlastic;
            public bool lodgedGridEvicted,lodgedGridReturned,sceneViewPreserved;
            public bool acceptedBankUnchanged;
            public List<BakeResult> rebakes=new List<BakeResult>();
        }
        private static readonly List<string> errors=new List<string>();
        [Serializable] private sealed class BankFile { public string path,sha256; }
        [Serializable] private sealed class BankManifest { public BankFile[] files; }
        private static Report report;
        private static string output;
        [MenuItem("Tools/PBD Grass/Validate Combined 32768 XPBD Baseline")]
        public static void Run()
        {
            output=Environment.GetEnvironmentVariable("GRASS_COMBINED_OUTPUT");
            if(string.IsNullOrEmpty(output))output=Path.GetFullPath(GrassResponseBaker.Root+"/CombinedBaselineRun");
            Directory.CreateDirectory(output);
            report=new Report { gpu=SystemInfo.graphicsDeviceName,unity=Application.unityVersion };
            errors.Clear(); Application.logMessageReceived+=CaptureError;
            var previous=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var scene=EditorSceneManager.OpenScene(GrassCombinedBaselineSceneBuilder.ScenePath,
                Application.isBatchMode?OpenSceneMode.Single:OpenSceneMode.Additive);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            try
            {
                GrassHandoffDemo demo=null;
                foreach(var root in scene.GetRootGameObjects())
                { demo=root.GetComponentInChildren<GrassHandoffDemo>(); if(demo!=null)break; }
                Require(demo!=null,"Combined baseline scene is missing its demo.");
                demo.enabled=false; demo.Cam.enabled=false; demo.Cam.aspect=1.5f;
                CheckAcceptedBank(); demo.Initialize(); CheckTour(demo);
                demo.Renderer.Dispose();
                CheckRebakes(demo);
                Require(errors.Count==0,string.Join("\n",errors)); report.passed=true;
                Debug.Log("GRASS_COMBINED_BASELINE_PASS "+JsonUtility.ToJson(report));
            }
            catch(Exception e) { report.error=e.ToString(); Debug.LogError("GRASS_COMBINED_BASELINE_FAIL "+e); }
            finally
            {
                Application.logMessageReceived-=CaptureError;
                File.WriteAllText(Path.Combine(output,"Results.json"),JsonUtility.ToJson(report,true));
                if(!Application.isBatchMode)EditorSceneManager.CloseScene(scene,true);
                if(previous.IsValid() && previous.isLoaded)UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);
            }
            if(Application.isBatchMode)EditorApplication.Exit(report.passed?0:1);
        }
        private static void Require(bool condition,string message)
        { if(!condition)throw new InvalidOperationException(message); }
        private static void CaptureError(string message,string stack,LogType severity)
        { if(severity==LogType.Error || severity==LogType.Exception || severity==LogType.Assert)errors.Add(message); }
        private static bool[] Active(PBDGrassPatchRenderer r)
        {
            var result=new bool[r.Data.Grids.Length];
            foreach(var grid in r.Pool.Selected)result[grid.GridId]=true;
            return result;
        }
        private static void CheckAcceptedBank()
        {
            var manifest=JsonUtility.FromJson<BankManifest>(File.ReadAllText(
                GrassResponseBaker.Root+"/CombinedBaselineValidation/AcceptedBakeBank.json"));
            Require(manifest.files!=null && manifest.files.Length==8,"Accepted bake bank manifest is missing.");
            using(var sha=SHA256.Create())foreach(var file in manifest.files)
            {
                string actual=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file.path))).Replace("-","").ToLowerInvariant();
                Require(actual==file.sha256,"Accepted bake asset changed: "+file.path);
            }
            report.acceptedBankUnchanged=true;
        }
        private static void CheckTour(GrassHandoffDemo demo)
        {
            var r=demo.Renderer;
            Require(r.Handoff!=null && demo.Handoff.BakedResponse.IsBaked,"Baked handoff is not enabled.");
            report.totalBlades=r.BladeCount; report.bezierBlades=r.BladeCount-32768;
            Require(r.BladeCount==131072 && r.Pool.Capacity==1024,"Baseline must contain 32768 XPBD + 98304 Bezier blades.");
            var previous=Active(r); var seen=(bool[])previous.Clone();
            int tracked=-1,trackedBlade=0; var trackedPlastic=new Vector4[7];
            int startingEntries=r.Handoff.EntryInitializations;
            for(int tick=0;tick<1440;tick++)
            {
                float time=(tick+1)/60f;
                demo.Cam.transform.position=demo.TourPosition(time);
                demo.Cam.transform.LookAt(demo.transform.position+Vector3.up*.6f);
                Vector3 focus=demo.Cam.transform.position; focus.y=demo.transform.position.y;
                Vector3 forward=Vector3.ProjectOnPlane(demo.Cam.transform.forward,Vector3.up).normalized;
                demo.Interaction.position=focus+forward*1.1f+Vector3.up*.6f;
                r.SetWind(GrassResponseDemo.WindAt(time,true)*(demo.Wind.magnitude/6),.1f,new Vector4(.01f,.01f,0,1));
                r.SimulationCenter=focus;
                bool sampleSeed=tick%180==90;
                BoneInfo[] before=sampleSeed?r.Handoff.ReadDisplayNodes():null;
                r.RefreshSimulationSelection(); var active=Active(r);
                int entering=-1;
                for(int g=0;g<active.Length;g++)
                {
                    if(previous[g] && !active[g])report.exitedGrids++;
                    if(!previous[g] && active[g])
                    { if(seen[g])report.reenteredGrids++; seen[g]=true; if(entering<0)entering=g; }
                }
                if(sampleSeed && entering>=0)CheckSeed(r,before,entering);
                if(tracked>=0 && active[tracked])report.lodgedGridReturned=true;
                r.Advance(1f/60);
                report.minActiveBlades=Mathf.Min(report.minActiveBlades,r.ActiveBladeCount);
                report.maxActiveBlades=Mathf.Max(report.maxActiveBlades,r.ActiveBladeCount);
                Require(r.ActiveBladeCount==32768,"Benchmark lost its full XPBD workload at tick "+tick);
                if(tick%120==0)
                {
                    var plastic=r.ReadPlasticBends();
                    foreach(var bend in plastic)report.peakPlastic=Mathf.Max(report.peakPlastic,bend.magnitude);
                    if(tracked<0 && tick>=600)
                    {
                        for(int g=0;g<active.Length && tracked<0;g++)
                        {
                            if(active[g] || r.Pool.SlotForGrid(g)>=0)continue;
                            int offset=g*32*7;
                            // Track the first blade that was actually lodged and then evicted.
                            for(int b=0;b<32 && tracked<0;b++)for(int j=0;j<7;j++)
                                if(plastic[offset+b*7+j].magnitude>.1f)
                                {
                                    tracked=g;
                                    Array.Copy(plastic,offset+b*7,trackedPlastic,0,7);
                                    trackedBlade=b;
                                    report.lodgedGridEvicted=true;
                                    break;
                                }
                        }
                    }
                    if(tracked>=0 && !active[tracked])
                        for(int j=0;j<7;j++)report.retainedPlasticError=Mathf.Max(report.retainedPlasticError,
                            (plastic[(tracked*32+trackedBlade)*7+j]-trackedPlastic[j]).magnitude);
                    var nodes=r.Handoff.ReadDisplayNodes();
                    foreach(var node in nodes)Require(Finite(node.Position) && Finite(node.Velocities),"Nonfinite display state.");
                }
                if(tick==0 || tick==720 || tick==1439)Capture("Tour"+tick,r,demo.Cam);
                previous=active;
            }
            report.simulationTicks=r.SimulationTicks;
            report.evictions=r.Handoff.EntryInitializations-startingEntries;
            Require(report.simulationTicks==1440 && r.DroppedTime==0,"Fixed-tick workload changed.");
            Require(report.exitedGrids>0 && report.reenteredGrids>0 && report.evictions>0,"Tour did not exercise LOD replacement.");
            Require(report.seedChecks>0,"No entering grid was checked for exact state seeding.");
            Require(report.lodgedGridEvicted && report.lodgedGridReturned && report.retainedPlasticError==0,
                "Lodged material did not survive actual eviction and return.");
            Require(report.maxSeedPositionError<1e-6f && report.maxSeedVelocityError<1e-6f
                && report.maxSeedRotationError<.1f && report.maxSeedAngularError<1e-6f,"LOD state seeding changed.");
            int ticks=r.SimulationTicks;
            Capture("CloseView",r,demo.Cam); report.visibleGrids=(int)r.VisibleGridCount();
            var probe=new GameObject("Baseline scene view probe").AddComponent<Camera>();
            try
            {
                probe.enabled=false; probe.cameraType=CameraType.SceneView; probe.aspect=1.5f;
                probe.transform.position=new Vector3(45,42,-45); probe.transform.LookAt(Vector3.zero);
                Capture("Overview",r,probe);
                report.sceneViewPreserved=r.SimulationTicks==ticks && r.VisibleGridCount()==report.visibleGrids;
                Require(report.sceneViewPreserved,"Scene camera changed simulation or main-camera culling.");
            }
            finally { Object.DestroyImmediate(probe.gameObject); }
            Vector3 savedPosition=demo.Cam.transform.position; Quaternion savedRotation=demo.Cam.transform.rotation;
            try
            {
                demo.Cam.transform.position=new Vector3(43,48,-43); demo.Cam.transform.LookAt(Vector3.zero);
                Capture("FieldLayout",r,demo.Cam);
            }
            finally { demo.Cam.transform.SetPositionAndRotation(savedPosition,savedRotation); }
        }
        private static bool Finite(Vector3 p) => !float.IsNaN(p.sqrMagnitude) && !float.IsInfinity(p.sqrMagnitude);
        private static void CheckSeed(PBDGrassPatchRenderer r,BoneInfo[] before,int grid)
        {
            report.seedChecks++;
            var nodes=r.ReadNodes(); var frames=r.ReadFrames(); var shown=r.Handoff.ReadDisplayFrames();
            for(int b=grid*32;b<(grid+1)*32;b++)
            {
                for(int n=b*8;n<b*8+8;n++)
                {
                    report.maxSeedPositionError=Mathf.Max(report.maxSeedPositionError,(nodes[n].Position-before[n].Position).magnitude);
                    report.maxSeedVelocityError=Mathf.Max(report.maxSeedVelocityError,(nodes[n].Velocities-before[n].Velocities).magnitude);
                }
                for(int s=b*7;s<b*7+7;s++)
                {
                    report.maxSeedRotationError=Mathf.Max(report.maxSeedRotationError,Quaternion.Angle(frames[s].Rotation,shown[s].Rotation));
                    report.maxSeedAngularError=Mathf.Max(report.maxSeedAngularError,(frames[s].AngularVelocity-shown[s].AngularVelocity).magnitude);
                }
            }
        }
        private static void CheckRebakes(GrassHandoffDemo demo)
        {
            var wind=GrassResponseType.CreateUniformWindTexture();
            try
            {
                for(int t=0;t<8;t++)
                {
                    var accepted=AssetDatabase.LoadAssetAtPath<GrassResponseType>(GrassResponseBaker.TypePath(t));
                    var fresh=GrassResponseBaker.ComputeType(t,demo.Cam,demo.Solver,demo.GrassMaterial,wind);
                    try
                    {
                        var result=new BakeResult { type=accepted.DisplayName,
                            frequencyDifference=(fresh.AngularFrequency-accepted.AngularFrequency).magnitude,
                            dampingDifference=(fresh.DampingRatio-accepted.DampingRatio).magnitude };
                        for(int s=0;s<25;s++)
                        {
                            var a=accepted.Samples[s]; var b=fresh.Samples[s];
                            result.maxTerminalSpeed=Mathf.Max(result.maxTerminalSpeed,b.Speed);
                            for(int n=0;n<8;n++)result.maxNodeDifference=Mathf.Max(result.maxNodeDifference,(a.Nodes[n]-b.Nodes[n]).magnitude);
                            for(int j=0;j<7;j++)result.maxFrameDifferenceDegrees=Mathf.Max(result.maxFrameDifferenceDegrees,Quaternion.Angle(a.Frames[j],b.Frames[j]));
                            result.maxControlDifference=Mathf.Max(result.maxControlDifference,Mathf.Max((a.C1-b.C1).magnitude,
                                Mathf.Max((a.C2-b.C2).magnitude,(a.Tip-b.Tip).magnitude)));
                        }
                        report.rebakes.Add(result);
                        Require(result.maxTerminalSpeed<.003f && result.maxNodeDifference<.005f && result.maxControlDifference<.01f
                            && result.maxFrameDifferenceDegrees<1 && result.frequencyDifference<.36f && result.dampingDifference<.06f,
                            "Rebake no longer matches accepted material "+(t+1));
                        Debug.Log("GRASS_COMBINED_REBAKE "+(t+1)+" "+JsonUtility.ToJson(result));
                    }
                    finally { Object.DestroyImmediate(fresh); }
                }
            }
            finally { Object.DestroyImmediate(wind); }
        }
        private static void Capture(string name,PBDGrassPatchRenderer r,Camera camera)
        {
            var rt=new RenderTexture(1200,800,24); rt.Create(); var previousTarget=camera.targetTexture;
            var previous=RenderTexture.active; var tex=new Texture2D(1200,800,TextureFormat.RGB24,false);
            try
            {
                camera.targetTexture=rt; r.Draw(camera); camera.Render(); RenderTexture.active=rt;
                tex.ReadPixels(new Rect(0,0,1200,800),0,0); tex.Apply();
                File.WriteAllBytes(Path.Combine(output,name+".png"),tex.EncodeToPNG());
            }
            finally
            { RenderTexture.active=previous; camera.targetTexture=previousTarget; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); }
        }
    }
}
