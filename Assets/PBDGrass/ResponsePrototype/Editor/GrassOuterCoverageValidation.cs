using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace GrassFinal.PBDGrass.Editor
{
    public static class GrassOuterCoverageValidation
    {
        [StructLayout(LayoutKind.Sequential)] private struct Instance
        { public Vector3 Root; public float Seed; public Vector2 Facing; public float WidthScale,Fade; }
        [Serializable] private sealed class View
        { public string name; public int drawnOuterBlades,outerTriangles; }
        [Serializable] private sealed class Report
        {
            public bool passed,statelessRevisit,physicalBuffersUnchanged;
            public string gpu,error;
            public int candidates,tiles,coreBlades,activeMin=int.MaxValue,activeMax;
            public long reservedOuterBytes;
            public int invalidInstances,insideExcludedCore,insideActiveCore,farSamples,nearSamples;
            public int nearDensityRegion,farDensityRegion;
            public Vector3 rootMin=Vector3.one*float.MaxValue,rootMax=Vector3.one*float.MinValue;
            public List<View> views=new List<View>();
        }
        private static string output;
        [MenuItem("Tools/PBD Grass/Validate Outer Coverage")]
        public static void Run()
        {
            output=Environment.GetEnvironmentVariable("GRASS_OUTER_OUTPUT");
            if(string.IsNullOrEmpty(output))output=Path.GetFullPath(GrassResponseBaker.Root+"/OuterCoverageValidationRun");
            Directory.CreateDirectory(output); var report=new Report { gpu=SystemInfo.graphicsDeviceName };
            var previous=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var scene=EditorSceneManager.OpenScene(GrassOuterCoverageSceneBuilder.ScenePath,Application.isBatchMode?OpenSceneMode.Single:OpenSceneMode.Additive);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene); GrassHandoffDemo demo=null; GrassOuterCoverage outer=null;
            try
            {
                foreach(var root in scene.GetRootGameObjects()) { demo=root.GetComponentInChildren<GrassHandoffDemo>(); if(demo!=null)break; }
                Require(demo!=null && demo.OuterCoverage!=null,"Outer coverage scene is missing."); outer=demo.OuterCoverage;
                demo.enabled=false; outer.enabled=false; demo.Cam.enabled=false; demo.Cam.aspect=1920f/1080; demo.ConfigureViewCamera(); demo.Initialize(); outer.Initialize();
                for(int tick=0;tick<720;tick++)
                {
                    var focus=demo.TourPosition((tick+1)/60f); focus.y=0; demo.SimulationFocus.position=focus; demo.Renderer.SimulationCenter=focus;
                    var forward=Vector3.ProjectOnPlane(demo.transform.position-focus,Vector3.up).normalized;
                    demo.Interaction.position=focus+forward*1.1f+Vector3.up*.6f;
                    demo.Renderer.SetWind(GrassResponseDemo.WindAt(demo.Renderer.SimulationTime,true)*(demo.Wind.magnitude/6),.1f,new Vector4(.01f,.01f,0,1));
                    demo.Renderer.Advance(1f/60); report.activeMin=Mathf.Min(report.activeMin,demo.Renderer.ActiveBladeCount); report.activeMax=Mathf.Max(report.activeMax,demo.Renderer.ActiveBladeCount);
                    if(tick%60==0)outer.PrepareVisible();
                }
                report.candidates=outer.CandidateCount; report.tiles=outer.TileCount; report.reservedOuterBytes=outer.AllocatedGpuBytes; report.coreBlades=demo.Renderer.BladeCount;
                var nodes=demo.Renderer.ReadNodes(); outer.PrepareVisible(); var after=demo.Renderer.ReadNodes();
                report.physicalBuffersUnchanged=true;
                for(int n=0;n<nodes.Length;n++)if(!nodes[n].Position.Equals(after[n].Position) || !nodes[n].Velocities.Equals(after[n].Velocities))report.physicalBuffersUnchanged=false;
                var original=Read(outer); Vector3 originalFocus=demo.SimulationFocus.position,originalContact=demo.Interaction.position;
                demo.SimulationFocus.position+=new Vector3(90,0,50); demo.Interaction.position=new Vector3(100,0,100); outer.PrepareVisible();
                demo.SimulationFocus.position=originalFocus; demo.Interaction.position=originalContact; outer.PrepareVisible(); var revisited=Read(outer);
                report.statelessRevisit=original.Length==revisited.Length;
                for(int i=0;i<Mathf.Min(original.Length,revisited.Length);i++)
                    if(!original[i].Root.Equals(revisited[i].Root) || original[i].Seed!=revisited[i].Seed || original[i].WidthScale!=revisited[i].WidthScale
                        || !original[i].Facing.Equals(revisited[i].Facing) || original[i].Fade!=revisited[i].Fade)report.statelessRevisit=false;
                var activeGrids=new HashSet<int>(); foreach(var grid in demo.Renderer.Pool.Selected)activeGrids.Add(grid.GridId);
                foreach(var blade in original)
                {
                    int x=Mathf.FloorToInt(blade.Root.x+demo.PatchSize*.5f),z=Mathf.FloorToInt(blade.Root.z+demo.PatchSize*.5f);
                    if(x>=0 && z>=0 && x<demo.PatchSize && z<demo.PatchSize && activeGrids.Contains((demo.PatchSize-1-z)*demo.PatchSize+x))report.insideActiveCore++;
                    report.rootMin=Vector3.Min(report.rootMin,blade.Root); report.rootMax=Vector3.Max(report.rootMax,blade.Root);
                    if(blade.Root.x>=48 && blade.Root.x<56 && blade.Root.z>=0 && blade.Root.z<8)report.nearDensityRegion++;
                    if(blade.Root.x>=104 && blade.Root.x<120 && blade.Root.z>=104 && blade.Root.z<120)report.farDensityRegion++;
                    if(float.IsNaN(blade.Root.sqrMagnitude) || blade.WidthScale<1 || blade.WidthScale>3 || Mathf.Abs(blade.Facing.sqrMagnitude-1)>1e-4)report.invalidInstances++;
                    float edge=Mathf.Max(Mathf.Abs(blade.Root.x),Mathf.Abs(blade.Root.z));
                    if(edge<demo.PatchSize*.5f-outer.BlendWidth)report.insideExcludedCore++;
                    float distance=Vector2.Distance(new Vector2(blade.Root.x,blade.Root.z),new Vector2(originalFocus.x,originalFocus.z));
                    if(distance>100)report.farSamples++; else if(edge>40 && distance<65)report.nearSamples++;
                }
                demo.Handoff.DebugLodBaseColors=true; Capture(demo,outer,"OverheadDebug",report);
                demo.Handoff.DebugLodBaseColors=false; Capture(demo,outer,"OverheadLit",report);
                demo.OverheadView=false; demo.ConfigureViewCamera(); Capture(demo,outer,"GroundLit",report);
                demo.Cam.transform.SetPositionAndRotation(new Vector3(40,6,-42),Quaternion.Euler(12,18,0)); Capture(demo,outer,"BoundaryLit",report);
                demo.Cam.transform.SetPositionAndRotation(new Vector3(70,2.5f,-40),Quaternion.Euler(4,35,0)); Capture(demo,outer,"OuterLit",report);
                demo.Cam.transform.position=new Vector3(70,50,-40); demo.Cam.transform.rotation=Quaternion.Euler(-90,0,0); outer.PrepareVisible();
                Require(Read(outer).Length==0,"Main-camera frustum culling did not reject the sky view.");
                foreach(var shader in new[] {outer.LeafShader,demo.GrassMaterial.shader})
                    foreach(var message in ShaderUtil.GetShaderMessages(shader))
                        Require(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error,message.message);
                Require(report.coreBlades==131072 && report.activeMin==32768 && report.activeMax==32768,"Core simulation workload changed.");
                Require(report.statelessRevisit && report.physicalBuffersUnchanged && report.invalidInstances==0 && report.insideExcludedCore==0 && report.insideActiveCore==0,"Outer state isolation failed.");
                Require(report.farSamples>10000 && report.nearSamples>10000 && original.Length<report.candidates,"Density reduction did not produce useful coverage.");
                Require(report.nearDensityRegion>=400 && report.nearDensityRegion<=640 && report.farDensityRegion>=400 && report.farDensityRegion<=640,"Measured density differs from the 8/2 blades per square meter targets.");
                report.passed=true; Debug.Log("GRASS_OUTER_COVERAGE_PASS "+JsonUtility.ToJson(report));
            }
            catch(Exception e) { report.error=e.ToString(); Debug.LogError("GRASS_OUTER_COVERAGE_FAIL "+e); }
            finally
            {
                if(outer!=null)Object.DestroyImmediate(outer); demo?.Renderer?.Dispose();
                File.WriteAllText(Path.Combine(output,"Results.json"),JsonUtility.ToJson(report,true));
                if(!Application.isBatchMode)EditorSceneManager.CloseScene(scene,true);
                if(previous.IsValid() && previous.isLoaded)UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);
            }
            if(Application.isBatchMode)EditorApplication.Exit(report.passed?0:1);
        }
        private static Instance[] Read(GrassOuterCoverage outer)
        {
            var args=new uint[4]; outer.DiagnosticArguments.GetData(args); Require(args[0]==15,"Unexpected ribbon topology.");
            var result=new Instance[args[1]]; if(result.Length>0)outer.DiagnosticInstances.GetData(result,0,0,result.Length);
            Array.Sort(result,(a,b)=>a.Root.x!=b.Root.x?a.Root.x.CompareTo(b.Root.x):a.Root.z.CompareTo(b.Root.z)); return result;
        }
        private static void Capture(GrassHandoffDemo demo,GrassOuterCoverage outer,string name,Report report)
        {
            var rt=new RenderTexture(1920,1080,24); rt.Create(); demo.Cam.targetTexture=rt; demo.Cam.aspect=1920f/1080;
            demo.ConfigureViewCamera();
            try
            {
                outer.PrepareVisible(); demo.Renderer.Draw(demo.Cam); outer.RenderPrepared(demo.Cam); demo.Cam.Render();
                int count=Read(outer).Length; report.views.Add(new View { name=name,drawnOuterBlades=count,outerTriangles=count*5 });
                var before=RenderTexture.active; RenderTexture.active=rt; var texture=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
                texture.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); texture.Apply(); File.WriteAllBytes(Path.Combine(output,name+".png"),texture.EncodeToPNG());
                RenderTexture.active=before; Object.DestroyImmediate(texture);
            }
            finally { demo.Cam.targetTexture=null; rt.Release(); Object.DestroyImmediate(rt); }
        }
        private static void Require(bool value,string message) { if(!value)throw new InvalidOperationException(message); }
    }
}
