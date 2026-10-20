using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;
namespace GrassFinal.PBDGrass.Editor
{
    public static class GrassMidrangeValidation
    {
        [StructLayout(LayoutKind.Sequential)] private struct Instance {public Vector3 Root;public float Seed;public Vector2 Facing;public float Width,Fade;}
        [Serializable] private sealed class Report
        {
            public bool passed,localPromotion,localReleased,collisionChangesPlastic,plasticPreserved,morphDoesNotWriteState,radialCoverage,stableSlots,bufferReuse;
            public string error,gpu;
            public int minActive=int.MaxValue,maxActive,residentBlades,outerVisible,outerInActive,duplicateRoots,legacyChurn,stableChurn,ticks;
            public long gpuBufferBytes;
            public float terminalCenterError,initialPresentationError,maxWarmLookahead,plasticDelta;
        }
        private static T Field<T>(object owner,string name)=>(T)owner.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(owner);
        private static void Require(bool ok,string message) {if(!ok)throw new InvalidOperationException(message);}
        [MenuItem("Tools/PBD Grass/Validate Middle Range Experiment")]
        public static void Run()
        {
            string output=Environment.GetEnvironmentVariable("GRASS_MIDDLE_OUTPUT")??Path.GetFullPath(GrassResponseBaker.Root+"/MiddleRangeValidationRun");Directory.CreateDirectory(output);
            var report=new Report {gpu=SystemInfo.graphicsDeviceName};GrassHandoffDemo demo=null;GrassOuterCoverage outer=null;
            try
            {
                EditorSceneManager.OpenScene(GrassMidrangeSceneBuilder.ScenePath);demo=Object.FindObjectOfType<GrassHandoffDemo>();demo.enabled=false;demo.Cam.enabled=false;
                demo.GetComponent<GrassWorldStreamer>().enabled=false;Object.FindObjectOfType<GrassMidrangeInteractor>().enabled=false;
                outer=demo.OuterCoverage;outer.enabled=false;demo.Initialize();outer.Initialize();var r=demo.Renderer;
                report.residentBlades=r.BladeCount;report.gpuBufferBytes=r.AllocatedGpuBytes+outer.AllocatedGpuBytes;
                object originalBones=Field<object>(r,"bones"),originalGeometry=Field<object>(r,"positions");
                Require(report.residentBlades==96*96*32,"Resident guard was not expanded.");
                Vector3 start=demo.Cam.transform.position;var actor=demo.AdditionalInteractions[0];var contact=actor.GetComponent<GrassInteractionCollider>();
                actor.position=new Vector3(start.x,.4f,start.z+24);contact.enabled=false;r.RefreshSimulationSelection();
                var actorCell=new Vector2Int(Mathf.FloorToInt(actor.position.x),Mathf.FloorToInt(actor.position.z));int actorGrid=GrassWorldIdentity.Slot(actorCell,96);
                report.localReleased=!Contains(r,actorGrid);Require(report.localReleased,"Middle contact cell is already near.");
                contact.enabled=true;r.RefreshSimulationSelection();report.localPromotion=Contains(r,actorGrid);Require(report.localPromotion && r.ActiveBladeCount==32768,"Local promotion changed the fixed active budget.");
                int actorBlade=actorGrid*32;var before=r.ReadPlasticBends();
                for(int i=0;i<180;i++) {actor.position=new Vector3(start.x+Mathf.Sin(i*.025f)*1.2f,.22f,start.z+24);Tick(demo,report);}
                var after=r.ReadPlasticBends();for(int i=actorBlade*7;i<(actorBlade+32)*7;i++)report.plasticDelta=Mathf.Max(report.plasticDelta,Vector4.Distance(before[i],after[i]));
                report.collisionChangesPlastic=report.plasticDelta>.0001f;Require(report.collisionChangesPlastic,"Remote middle collision did not affect physical plasticity.");
                actorCell=new Vector2Int(Mathf.FloorToInt(actor.position.x),Mathf.FloorToInt(actor.position.z));actorGrid=GrassWorldIdentity.Slot(actorCell,96);actorBlade=actorGrid*32+3;
                var bends=Field<ComputeBuffer>(r,"plasticBends");var marker=new[] {new Vector4(.15f,-.03f,0,0)};bends.SetData(marker,0,actorBlade*7,1);
                contact.enabled=false;r.RefreshSimulationSelection();Require(!Contains(r,actorGrid),"Middle interaction stayed physical after contact removal.");
                for(int i=0;i<45;i++)Tick(demo,report);
                var preserved=new Vector4[1];bends.GetData(preserved,0,actorBlade*7,1);report.plasticPreserved=preserved[0].Equals(marker[0]);
                VerifyPresentation(demo,actorBlade,report,output);
                demo.OverheadView=true;demo.ConfigureViewCamera();outer.PrepareVisible();VerifyCoverage(demo,report);
                Capture(demo,output,"OverheadDebug",true);Capture(demo,output,"OverheadLit",false);
                contact.enabled=true;actor.position=new Vector3(start.x,.4f,start.z+24);for(int i=0;i<30;i++)Tick(demo,report);
                Capture(demo,output,"OverheadLocalInteraction",true);
                demo.OverheadView=false;demo.ConfigureViewCamera();Capture(demo,output,"GroundLit",false);
                contact.enabled=false;
                // Boundary micro-motion stresses full-pool hysteresis without modifying the GPU state.
                var legacy=new GrassSimulationPool(r.Data.Grids,32768);var stable=new GrassSimulationPool(r.Data.Grids,32768);
                var oldLegacy=new HashSet<int>();var oldStable=new HashSet<int>();
                for(int i=0;i<120;i++)
                {
                    var focus=start+Vector3.right*(i%2==0?.025f:-.025f);
                    legacy.Select(focus,20,.35f,i,(a,b)=>{});stable.Select(focus,20,.35f,i,(a,b)=>{},true);
                    if(i>0) {foreach(var g in legacy.Selected)if(!oldLegacy.Contains(g.GridId))report.legacyChurn++;foreach(var g in stable.Selected)if(!oldStable.Contains(g.GridId))report.stableChurn++;}
                    oldLegacy.Clear();oldStable.Clear();foreach(var g in legacy.Selected)oldLegacy.Add(g.GridId);foreach(var g in stable.Selected)oldStable.Add(g.GridId);
                }
                report.stableSlots=report.stableChurn<report.legacyChurn;Require(report.stableSlots,"Full-pool hysteresis did not reduce boundary churn.");
                // 10m/s then 30m/s, diagonal moves, reversals and 8m page crossings.
                for(int i=0;i<480;i++)
                {
                    float speed=i<120?10:30,sign=i<300?1:-1;var p=demo.Cam.transform.position+new Vector3(sign*speed/60,0,Mathf.Sin(i*.04f)*speed/120);
                    demo.Cam.transform.position=p;r.StreamTo(p);Tick(demo,report);report.maxWarmLookahead=Mathf.Max(report.maxWarmLookahead,r.Handoff.WarmLookAheadMeters);
                }
                Require(report.maxWarmLookahead>=11.9f,"Fast movement did not expand history warming.");
                report.bufferReuse=ReferenceEquals(originalBones,Field<object>(r,"bones")) && ReferenceEquals(originalGeometry,Field<object>(r,"positions"));
                foreach(var shader in new[] {demo.GrassMaterial.shader,outer.LeafShader,Shader.Find("Hidden/PBDGrass/MiddlePresentationProbe")})foreach(var message in ShaderUtil.GetShaderMessages(shader))Require(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error,message.message);
                Require(report.minActive==32768 && report.maxActive==32768 && report.plasticPreserved && report.morphDoesNotWriteState && report.radialCoverage && report.bufferReuse,"Middle state or ownership regression.");
                report.passed=true;Debug.Log("GRASS_MIDDLE_PASS "+JsonUtility.ToJson(report));
            }
            catch(Exception e) {report.error=e.ToString();Debug.LogError("GRASS_MIDDLE_FAIL "+e);}
            finally {if(outer!=null)Object.DestroyImmediate(outer);demo?.Renderer?.Dispose();File.WriteAllText(Path.Combine(output,"Results.json"),JsonUtility.ToJson(report,true));}
            if(Application.isBatchMode)EditorApplication.Exit(report.passed?0:1);
        }
        private static bool Contains(PBDGrassPatchRenderer r,int grid) {foreach(var g in r.Pool.Selected)if(g.GridId==grid)return true;return false;}
        private static void Tick(GrassHandoffDemo demo,Report report)
        {
            var p=demo.Cam.transform.position;p.y=0;demo.Renderer.SimulationCenter=p;
            demo.Interaction.position=p+Vector3.forward*1.1f+Vector3.up*.6f;
            demo.Renderer.SetWind(GrassResponseDemo.WindAt(demo.Renderer.SimulationTime,true),.1f,new Vector4(.01f,.01f,0,1));demo.Renderer.Advance(1f/60);
            report.minActive=Mathf.Min(report.minActive,demo.Renderer.ActiveBladeCount);report.maxActive=Mathf.Max(report.maxActive,demo.Renderer.ActiveBladeCount);report.ticks++;
        }
        private static void VerifyPresentation(GrassHandoffDemo demo,int blade,Report report,string output)
        {
            var r=demo.Renderer;var positions=Field<ComputeBuffer>(r,"positions");var bends=Field<ComputeBuffer>(r,"plasticBends");
            var original=new Vector3[15];positions.GetData(original,0,blade*15,15);var plastic=new Vector4[7];bends.GetData(plastic,0,blade*7,7);
            for(int i=0;i<15;i++)Require(!float.IsNaN(original[i].sqrMagnitude),"Non-finite source geometry at vertex "+i);
            var probe=new Material(Shader.Find("Hidden/PBDGrass/MiddlePresentationProbe"));var rt=new RenderTexture(15,1,0,RenderTextureFormat.ARGBFloat);rt.Create();var image=new Texture2D(15,1,TextureFormat.RGBAFloat,false,true);
            try
            {
                var leaf=r.Data.Blades[blade];probe.SetBuffer("ProbePositions",positions);probe.SetInt("ProbeBlade",blade);probe.SetVector("ProbeRoot",leaf.Root);probe.SetFloat("ProbeSeed",leaf.Seed);
                probe.SetBuffer("OuterControls",Field<ComputeBuffer>(demo.OuterCoverage,"controls"));probe.SetTexture("OuterWindNoise",demo.WindNoise);probe.SetVector("OuterWind",Vector3.zero);probe.SetFloat("OuterMaxWind",demo.Handoff.BakedResponse.MaxWind);probe.SetFloat("OuterHalfWidth",demo.Handoff.BakedResponse.Shape.HalfWidth);
                probe.SetFloat("ProbeCoverage",1);var initial=ReadProbe(probe,rt,image);for(int n=0;n<15;n++)report.initialPresentationError=Mathf.Max(report.initialPresentationError,Vector3.Distance(Position(initial[n]),original[n]));
                probe.SetFloat("ProbeCoverage",leaf.Seed);var terminal=ReadProbe(probe,rt,image);
                for(int i=0;i<15;i++)Require(!float.IsNaN(terminal[i].r+terminal[i].g+terminal[i].b),"Non-finite presentation target at vertex "+i+" initial="+initial[i]+" terminal="+terminal[i]);
                var sample=demo.Handoff.BakedResponse.Samples[12];var cell=new Vector2Int(Mathf.FloorToInt(leaf.Root.x),Mathf.FloorToInt(leaf.Root.z));var yaw=GrassWorldIdentity.Yaw(cell);
                for(int n=0;n<8;n++)
                {
                    float t=n<=4?n/6f:n==5?5f/6:n==6?11f/12:1;float scaled=t*3;int segment=Mathf.Min(Mathf.FloorToInt(scaled),2);float f=scaled-segment;
                    Vector3 a=Curve(sample.C1,sample.C2,sample.Tip,segment/3f),b=Curve(sample.C1,sample.C2,sample.Tip,(segment+1)/3f);
                    Vector3 expected=leaf.Root+yaw*Vector3.Lerp(a,b,f),actual=n==7?Position(terminal[14]):(Position(terminal[n*2])+Position(terminal[n*2+1]))*.5f;
                    report.terminalCenterError=Mathf.Max(report.terminalCenterError,Vector3.Distance(expected,actual));
                }
                Vector3 start=demo.Cam.transform.position;demo.Cam.transform.position=start-Vector3.forward*8;
                r.StreamTo(demo.Cam.transform.position);Capture(demo,output,"MiddleBandLit",false);
                demo.Cam.transform.position=start;r.StreamTo(start);
                var endPositions=new Vector3[15];positions.GetData(endPositions,0,blade*15,15);var endPlastic=new Vector4[7];bends.GetData(endPlastic,0,blade*7,7);
                report.morphDoesNotWriteState=true;for(int i=0;i<15;i++)report.morphDoesNotWriteState&=original[i].Equals(endPositions[i]);for(int i=0;i<7;i++)report.morphDoesNotWriteState&=plastic[i].Equals(endPlastic[i]);
                Require(report.initialPresentationError<.00001f && report.terminalCenterError<.00002f,"Presentation morph does not meet the outer mesh at replacement.");
            }
            finally {rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(image);Object.DestroyImmediate(probe);}
        }
        private static Color[] ReadProbe(Material probe,RenderTexture rt,Texture2D image)
        {var old=RenderTexture.active;Graphics.Blit(null,rt,probe);RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,15,1),0,0);image.Apply();var pixels=image.GetPixels();RenderTexture.active=old;return pixels;}
        private static Vector3 Curve(Vector3 a,Vector3 b,Vector3 tip,float t) {float u=1-t;return 3*u*u*t*a+3*u*t*t*b+t*t*t*tip;}
        private static Vector3 Position(Color value)=>new Vector3(value.r,value.g,value.b);
        private static void VerifyCoverage(GrassHandoffDemo demo,Report report)
        {
            var outer=demo.OuterCoverage;var args=new uint[4];outer.DiagnosticArguments.GetData(args);var instances=new Instance[args[1]];outer.DiagnosticInstances.GetData(instances,0,0,instances.Length);report.outerVisible=instances.Length;
            var unique=new HashSet<Vector3>();var active=new HashSet<int>();foreach(var grid in demo.Renderer.Pool.Selected)active.Add(grid.GridId);Vector3 p=demo.Cam.transform.position;
            foreach(var leaf in instances)
            {
                if(!unique.Add(leaf.Root))report.duplicateRoots++;
                var cell=new Vector2Int(Mathf.FloorToInt(leaf.Root.x),Mathf.FloorToInt(leaf.Root.z));int g=GrassWorldIdentity.Slot(cell,96);
                if(demo.WorldLayout.Cells[g]==cell && active.Contains(g))report.outerInActive++;
                float d=Vector2.Distance(new Vector2(p.x,p.z),new Vector2(leaf.Root.x,leaf.Root.z));float t=Mathf.Clamp01((d-28)/8),coverage=1-t*t*(3-2*t);
                Require(leaf.Seed>coverage,"Outer grass violates the radial complementary selection.");
            }
            report.radialCoverage=report.outerVisible>100000 && report.duplicateRoots==0 && report.outerInActive==0;
        }
        private static void Capture(GrassHandoffDemo demo,string output,string name,bool debug)
        {
            demo.Handoff.DebugLodBaseColors=debug;var rt=new RenderTexture(1280,720,24);rt.Create();demo.Cam.targetTexture=rt;
            try
            {
                demo.ConfigureViewCamera();demo.OuterCoverage.PrepareVisible();demo.Renderer.Draw(demo.Cam);demo.OuterCoverage.RenderPrepared(demo.Cam);demo.Cam.Render();
                var old=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());Object.DestroyImmediate(image);RenderTexture.active=old;
            }
            finally {demo.Cam.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);}
        }
    }
}
