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
    public static class GrassCompactValidation
    {
        [Serializable] private sealed class Comparison
        {public string phase;public float positionRms,maxPosition,maxNearPosition,maxMiddlePosition,normalRmsDegrees,maxNormalDegrees;}
        [Serializable] private sealed class Report
        {
            public bool passed,independentNearPhysicsEquivalent,compactPlasticRetained,streamRetained,unloadedCleared,bufferReused;
            public string error,gpu;
            public int residentBlades,minActive=int.MaxValue,maxActive,ticks;
            public long baselineGpuBytes,compactGpuBytes,geometryGpuBytes,plasticGpuBytes,staticGpuBytes,historyBytes;
            public float retainedPlasticError,peakPlastic;
            public float codecMaxNodeError,codecMaxEdgeError,codecMaxNormalDegrees;
            public List<Comparison> comparisons=new List<Comparison>();
            public List<GrassCompactNearAudit.Comparison> nearPhysics;
        }
        private static readonly List<string> errors=new List<string>();
        private static readonly Dictionary<string,Vector4[]> baseline=new Dictionary<string,Vector4[]>();
        private static Report report;
        private static string output;
        private static T Field<T>(object owner,string name)=>(T)owner.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(owner);
        private static void Require(bool ok,string text) {if(!ok)throw new InvalidOperationException(text);}
        private static void Log(string message,string stack,LogType type)
        {if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert)errors.Add(message);}
        [MenuItem("Tools/PBD Grass/Validate Compact Middle")]
        public static void Run()
        {
            output=Environment.GetEnvironmentVariable("GRASS_COMPACT_OUTPUT")??Path.GetFullPath(GrassCompactSceneBuilder.Root+"/ValidationRun");
            Directory.CreateDirectory(output);report=new Report {gpu=SystemInfo.graphicsDeviceName};
            errors.Clear();baseline.Clear();GrassCompactNearAudit.Begin();report.nearPhysics=GrassCompactNearAudit.Comparisons;Application.logMessageReceived+=Log;
            try
            {
                RunScene(false);RunScene(true);
                GrassCompactNearAudit.AssertEquivalent();report.independentNearPhysicsEquivalent=true;
                Require(errors.Count==0,string.Join("\n",errors));
                Require(report.compactGpuBytes<report.baselineGpuBytes*.35,"The actual allocation did not shrink sufficiently.");
                Require(report.minActive==32768 && report.maxActive==32768,"Active XPBD budget changed.");
                Require(report.compactPlasticRetained && report.streamRetained && report.unloadedCleared && report.bufferReused,"State lifecycle failed.");
                report.passed=true;Debug.Log("GRASS_COMPACT_PASS "+JsonUtility.ToJson(report));
            }
            catch(Exception e) {report.error=e.ToString();Debug.LogError("GRASS_COMPACT_FAIL "+e);}
            finally
            {
                Application.logMessageReceived-=Log;
                File.WriteAllText(Path.Combine(output,"Results.json"),JsonUtility.ToJson(report,true));
            }
            if(Application.isBatchMode)EditorApplication.Exit(report.passed?0:1);
        }
        private static void RunScene(bool compact)
        {
            EditorSceneManager.OpenScene(compact?GrassCompactSceneBuilder.ScenePath:GrassMidrangeSceneBuilder.ScenePath);
            var demo=Object.FindObjectOfType<GrassHandoffDemo>();demo.enabled=false;demo.Cam.enabled=false;
            demo.GetComponent<GrassWorldStreamer>().enabled=false;Object.FindObjectOfType<GrassMidrangeInteractor>().enabled=false;
            var outer=demo.OuterCoverage;outer.enabled=false;
            var primary=demo.Interaction.GetComponent<GrassInteractionCollider>();primary.enabled=false;
            foreach(var actor in demo.AdditionalInteractions)actor.GetComponent<GrassInteractionCollider>().enabled=false;
            try
            {
                demo.Initialize();outer.Initialize();var r=demo.Renderer;
                report.residentBlades=r.BladeCount;Require(r.BladeCount==294912,"Resident size changed.");
                Require((r.Compact!=null)==compact,"Incorrect storage mode.");
                long bytes=r.AllocatedGpuBytes+outer.AllocatedGpuBytes;
                if(compact)
                {
                    report.compactGpuBytes=bytes;report.geometryGpuBytes=r.GeometryGpuBytes;
                    report.plasticGpuBytes=r.PlasticGpuBytes;report.staticGpuBytes=r.StaticGpuBytes;report.historyBytes=r.Handoff.HistoryBytes;
                }
                else report.baselineGpuBytes=bytes;
                var sourceGeometry=Field<ComputeBuffer>(r,"positions");
                Vector3 start=demo.Cam.transform.position;start.y=0;
                var ids=new List<uint>();
                for(int z=-32;z<=32;z+=4)for(int x=-32;x<=32;x+=4)
                {
                    var cell=new Vector2Int(Mathf.FloorToInt(start.x)+x,Mathf.FloorToInt(start.z)+z);
                    ids.Add((uint)(GrassWorldIdentity.Slot(cell,96)*32+9));
                }
                Vector3 trail=start+new Vector3(.4f,0,8.4f);
                var trailCell=new Vector2Int(Mathf.FloorToInt(trail.x),Mathf.FloorToInt(trail.z));
                int trailBlade=GrassWorldIdentity.Slot(trailCell,96)*32;
                for(int leaf=0;leaf<32;leaf++)ids.Add((uint)(trailBlade+leaf));
                Sample(r,ids,"Birth",compact);
                GrassCompactNearAudit.Check(r,"Birth",compact);
                for(int i=0;i<60;i++)
                {
                    Tick(demo,start);
                    if(i==0 || i==59)GrassCompactNearAudit.Check(r,"Wind"+(i+1),compact);
                }
                Sample(r,ids,"Wind",compact);
                primary.enabled=true;
                for(int i=0;i<180;i++)
                {
                    demo.Interaction.position=trail+new Vector3(Mathf.Sin(i*.04f)*.3f,.22f,0);Tick(demo,start);
                    if(i==0 || i==1 || i==4 || i==29 || i==179)GrassCompactNearAudit.Check(r,"Contact"+(i+1),compact);
                }
                primary.enabled=false;
                for(int i=0;i<45;i++)Tick(demo,start);
                Sample(r,ids,"IndependentLodgedNear",compact);
                GrassCompactNearAudit.Check(r,"Release45",compact);
                if(compact)AuditCodec(r);
                var before=r.ReadPlasticBends();
                for(int i=trailBlade*7;i<(trailBlade+32)*7;i++)report.peakPlastic=Mathf.Max(report.peakPlastic,((Vector2)before[i]).magnitude);
                Require(report.peakPlastic>.01f,"The test did not create plastic lodging.");
                Vector3 away=start+Vector3.right*24;
                r.StreamTo(away);r.SimulationCenter=away;r.RefreshSimulationSelection();
                Require(r.Pool.SlotForGrid(trailBlade/32)<0,"Trail did not enter middle distance.");
                for(int i=0;i<90;i++)Tick(demo,away);
                var after=r.ReadPlasticBends();
                float error=0;for(int i=trailBlade*7;i<(trailBlade+32)*7;i++)error=Mathf.Max(error,Vector4.Distance(before[i],after[i]));
                if(compact) {report.retainedPlasticError=error;report.compactPlasticRetained=error<.00005f;}
                Sample(r,ids,"LodgedMiddle",compact);
                Capture(demo,compact?"CompactMiddle":"BaselineMiddle",false);
                // Nearby paging retains the marker. An entire-window jump unloads it.
                if(compact)
                {
                    var raw=new uint[r.Compact.Plastic.count];r.Compact.Plastic.GetData(raw);
                    r.StreamTo(away+Vector3.forward*8);
                    var kept=new uint[8*32];r.Compact.Plastic.GetData(kept,0,trailBlade*8,kept.Length);
                    report.streamRetained=true;for(int i=0;i<kept.Length;i++)report.streamRetained&=kept[i]==raw[trailBlade*8+i];
                    r.StreamTo(away);
                }
                r.StreamTo(start);
                for(int i=0;i<90;i++)Tick(demo,start);
                Sample(r,ids,"Reentered",compact);
                Capture(demo,compact?"CompactGround":"BaselineGround",false);
                if(compact)
                {
                    demo.OverheadView=true;demo.ConfigureViewCamera();Capture(demo,"CompactOverhead",true);
                    r.StreamTo(start+Vector3.right*256);
                    var unloaded=new uint[8*32];r.Compact.Plastic.GetData(unloaded,0,trailBlade*8,unloaded.Length);
                    report.unloadedCleared=true;foreach(uint value in unloaded)report.unloadedCleared&=value==0;
                    report.bufferReused=ReferenceEquals(sourceGeometry,Field<ComputeBuffer>(r,"positions")) && bytes==r.AllocatedGpuBytes+outer.AllocatedGpuBytes;
                    Require(sourceGeometry.count==32768*15,"A world-sized vertex buffer survived.");
                    Require(r.Compact.Pose.count*4==294912*80,"Compact pose stride mismatch.");
                }
                foreach(var message in ShaderUtil.GetShaderMessages(demo.GrassMaterial.shader))
                    Require(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error,message.message);
            }
            finally {Object.DestroyImmediate(outer);demo.Renderer?.Dispose();}
        }
        private static void Tick(GrassHandoffDemo demo,Vector3 focus)
        {
            var p=demo.Cam.transform.position;p.x=focus.x;p.z=focus.z;demo.Cam.transform.position=p;
            demo.Renderer.SimulationCenter=focus;
            demo.Renderer.SetWind(GrassResponseDemo.WindAt(demo.Renderer.SimulationTime,true),.1f,new Vector4(.01f,.01f,0,1));
            demo.Renderer.Advance(1f/60);report.ticks++;
            report.minActive=Mathf.Min(report.minActive,demo.Renderer.ActiveBladeCount);report.maxActive=Mathf.Max(report.maxActive,demo.Renderer.ActiveBladeCount);
        }
        private static void Sample(PBDGrassPatchRenderer r,List<uint> ids,string phase,bool compact)
        {
            var shader=Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(GrassCompactSceneBuilder.Root+"/Shaders/CompactGrassAudit.compute"));
            int kernel=shader.FindKernel("Probe");var sample=new Vector4[ids.Count*30];
            using(var blades=new ComputeBuffer(ids.Count,4))
            using(var result=new ComputeBuffer(sample.Length,16))
            using(var dummy=new ComputeBuffer(20,4))
            using(var widths=new ComputeBuffer(7,4))
            {
                blades.SetData(ids);var half=new float[7];for(int i=0;i<7;i++)half[i]=r.World.TemplateRods[i].HalfWidth;widths.SetData(half);
                shader.SetBuffer(kernel,"ProbeBlades",blades);shader.SetBuffer(kernel,"ProbeOutput",result);
                shader.SetBuffer(kernel,"VertexBuffer",Field<ComputeBuffer>(r,"positions"));shader.SetBuffer(kernel,"NormalBuffer",Field<ComputeBuffer>(r,"normals"));
                shader.SetBuffer(kernel,"CompactPose",compact?r.Compact.Pose:dummy);shader.SetBuffer(kernel,"CompactHalfWidths",widths);
                shader.SetBuffer(kernel,"BladesBuffer",Field<ComputeBuffer>(r,"bladeMetadata"));
                shader.SetBuffer(kernel,"_GrassLodGridStates",r.Handoff.ClassificationStates);
                shader.SetInt("ProbeCount",ids.Count);shader.SetInt("UseCompact",compact?1:0);shader.Dispatch(kernel,(ids.Count*15+63)/64,1,1);result.GetData(sample);
            }
            Object.DestroyImmediate(shader);
            foreach(var v in sample)Require(!float.IsNaN(v.sqrMagnitude) && !float.IsInfinity(v.sqrMagnitude),"Non-finite geometry in "+phase);
            if(!compact) {baseline[phase]=sample;return;}
            var reference=baseline[phase];var c=new Comparison {phase=phase};double squares=0,angles=0;
            for(int leaf=0;leaf<ids.Count;leaf++)for(int v=0;v<15;v++)
            {
                int k=leaf*30+v;float d=Vector3.Distance(reference[k],sample[k]);squares+=d*d;c.maxPosition=Mathf.Max(c.maxPosition,d);
                if(r.Pool.SlotForGrid((int)ids[leaf]/32)>=0)c.maxNearPosition=Mathf.Max(c.maxNearPosition,d);
                else c.maxMiddlePosition=Mathf.Max(c.maxMiddlePosition,d);
                float a=Vector3.Angle(reference[k+15],sample[k+15]);angles+=a*a;c.maxNormalDegrees=Mathf.Max(c.maxNormalDegrees,a);
            }
            c.positionRms=(float)Math.Sqrt(squares/(ids.Count*15));c.normalRmsDegrees=(float)Math.Sqrt(angles/(ids.Count*15));report.comparisons.Add(c);
            // Independent simulation is an acceptance requirement, not just codec accuracy.
            if(phase=="Birth")Require(c.maxPosition<.002f && c.maxNormalDegrees<.5f,"Initial compact reconstruction exceeded 2mm / 0.5 degrees.");
            if(phase=="Wind")Require(c.maxPosition<.002f && c.maxNormalDegrees<.5f,"Wind reconstruction exceeded 2mm / 0.5 degrees.");
            if(phase=="IndependentLodgedNear")Require(c.maxNearPosition==0,"Independent near collision geometry changed.");
            if(phase=="LodgedMiddle")Require(c.positionRms<.005f && c.maxMiddlePosition<.02f,"Lodging handoff exceeded 5mm RMS / 20mm middle maximum.");
            Debug.Log("COMPACT_COMPARE "+JsonUtility.ToJson(c));
        }
        private static void AuditCodec(PBDGrassPatchRenderer r)
        {
            var shader=Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(GrassCompactSceneBuilder.Root+"/Shaders/CompactGrassCodecAudit.compute"));
            int count=r.Pool.Capacity*32;var values=new Vector4[count];
            using(var pose=new ComputeBuffer(count*20,4))
            using(var result=new ComputeBuffer(count,16))
            using(var widths=new ComputeBuffer(7,4))
            {
                var half=new float[7];for(int i=0;i<7;i++)half[i]=r.World.TemplateRods[i].HalfWidth;widths.SetData(half);
                foreach(string name in new[] {"Encode","Compare"})
                {
                    int k=shader.FindKernel(name);
                    shader.SetBuffer(k,"CodecNodes",Field<ComputeBuffer>(r,"bones"));shader.SetBuffer(k,"CodecFrames",Field<ComputeBuffer>(r,"frames"));
                    shader.SetBuffer(k,"CompactPose",pose);shader.SetBuffer(k,"CodecErrors",result);shader.SetBuffer(k,"CodecWidths",widths);
                    shader.SetInt("CodecCount",count);shader.Dispatch(k,(count+63)/64,1,1);
                }
                result.GetData(values);
            }
            Object.DestroyImmediate(shader);
            foreach(var v in values)
            {
                Require(!float.IsNaN(v.sqrMagnitude) && !float.IsInfinity(v.sqrMagnitude),"Invalid codec error.");
                report.codecMaxNodeError=Mathf.Max(report.codecMaxNodeError,v.x);report.codecMaxNormalDegrees=Mathf.Max(report.codecMaxNormalDegrees,v.y);
                report.codecMaxEdgeError=Mathf.Max(report.codecMaxEdgeError,v.z);
            }
            Require(report.codecMaxNodeError<.002f && report.codecMaxEdgeError<.002f && report.codecMaxNormalDegrees<.5f,"Real simulated grass exceeded codec error budget.");
        }
        private static void Capture(GrassHandoffDemo demo,string name,bool debug)
        {
            demo.Handoff.DebugLodBaseColors=debug;var rt=new RenderTexture(1280,720,24);rt.Create();demo.Cam.targetTexture=rt;
            try
            {
                demo.OuterCoverage.PrepareVisible();demo.Renderer.Draw(demo.Cam);demo.OuterCoverage.RenderPrepared(demo.Cam);demo.Cam.Render();
                var old=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(1280,720,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());
                Object.DestroyImmediate(image);RenderTexture.active=old;
            }
            finally {demo.Cam.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);}
        }
    }
}
