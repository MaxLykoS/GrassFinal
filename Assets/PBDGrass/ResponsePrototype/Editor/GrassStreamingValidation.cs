using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;
using Debug=UnityEngine.Debug;

namespace GrassFinal.PBDGrass.Editor
{
    public static class GrassStreamingValidation
    {
        [StructLayout(LayoutKind.Sequential)] private struct Instance
        { public Vector3 Root;public float Seed;public Vector2 Facing;public float Width,Fade; }
        [Serializable] private sealed class Report
        {
            public bool passed,retainedStateExact,retainedPlasticExact,unloadedPlasticCleared,stableReload,bufferReuse,rotationDoesNotStream;
            public string gpu,error;
            public int minActive=int.MaxValue,maxActive,walkTicks,windowShifts,lastShiftCells,outerEnter,outerExit,coreResidentCells,outerVisible;
            public long bufferBytesStart,bufferBytesEnd;
            public float maxRootError,maxCpuGpuRestError,maxCpuGpuRodError;
            public int insideActiveCore,invalidInstances;
            public double cpuShiftMean,cpuShiftMax;
        }
        private static T Field<T>(object owner,string name) => (T)owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner);
        [MenuItem("Tools/PBD Grass/Validate Streaming World")]
        public static void Run()
        {
            string output=Environment.GetEnvironmentVariable("GRASS_STREAM_OUTPUT");if(string.IsNullOrEmpty(output))output=Path.GetFullPath(GrassResponseBaker.Root+"/StreamingValidationRun");
            Directory.CreateDirectory(output);var report=new Report {gpu=SystemInfo.graphicsDeviceName};
            var previous=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var scene=EditorSceneManager.OpenScene(GrassStreamingSceneBuilder.ScenePath,Application.isBatchMode?OpenSceneMode.Single:OpenSceneMode.Additive);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);GrassHandoffDemo demo=null;GrassOuterCoverage outer=null;
            try
            {
                foreach(var root in scene.GetRootGameObjects()) {demo=root.GetComponentInChildren<GrassHandoffDemo>();if(demo!=null)break;}
                Require(demo!=null && demo.StreamWorld,"Streaming scene missing.");demo.enabled=false;demo.Cam.enabled=false;
                var controller=demo.GetComponent<GrassWorldStreamer>();controller.enabled=false;outer=demo.OuterCoverage;outer.enabled=false;
                demo.Cam.aspect=1920f/1080;demo.Initialize();outer.Initialize();var r=demo.Renderer;var world=demo.WorldLayout;
                report.coreResidentCells=world.Cells.Length;report.bufferBytesStart=r.AllocatedGpuBytes+outer.AllocatedGpuBytes;
                object positionBuffer=Field<object>(r,"positions"),boneBuffer=Field<object>(r,"bones"),outerBuffer=outer.DiagnosticInstances;
                for(int i=0;i<30;i++)Tick(demo,demo.Cam.transform.position,report);
                Vector2Int tracked=new Vector2Int(-12,-3);int grid=GrassWorldIdentity.Slot(tracked,world.Size),blade=grid*32+5;
                Require(world.Cells[grid]==tracked && r.Pool.SlotForGrid(grid)>=0,"Tracked cell is not active.");
                int poolSlot=r.Pool.SlotForGrid(grid);
                r.ImpulseBlade(blade,new Vector3(.7f,.1f,-.3f),new Vector3(.2f,-.1f,.15f));
                var plastic=Field<ComputeBuffer>(r,"plasticBends");var marker=new[] {new Vector4(.12f,-.04f,0,0)};plastic.SetData(marker,0,blade*7,1);
                var oldNodes=r.ReadNodes();var oldFrames=r.ReadFrames();var oldPlastic=r.ReadPlasticBends();
                var oldCells=(Vector2Int[])world.Cells.Clone();var oldBlades=(GrassBladeData.Blade[])r.Data.Blades.Clone();
                var start=demo.Cam.transform.position;Move(demo,start+Vector3.right*8);
                report.lastShiftCells=world.LastLoadedCells;Require(world.LastLoadedCells==512,"An 8m crossing should load 512 one-meter core cells.");
                controller.RefreshStatistics();controller.RefreshStatistics();
                // Seed controller statistics first, then cross another tile to inspect one-row turnover.
                Move(demo,start+Vector3.right*16);controller.RefreshStatistics();report.outerEnter=controller.OuterTilesLoaded;report.outerExit=controller.OuterTilesUnloaded;
                Require(report.outerEnter==32 && report.outerExit==32,"Outer window did not replace exactly one logical row.");
                var newNodes=r.ReadNodes();var newFrames=r.ReadFrames();var newPlastic=r.ReadPlasticBends();
                Require(r.Pool.SlotForGrid(grid)==poolSlot,"An overlapping active cell changed physical slots.");
                report.retainedStateExact=true;report.retainedPlasticExact=true;
                for(int n=0;n<8;n++)
                {var a=oldNodes[blade*8+n];var b=newNodes[blade*8+n];if(!a.Position.Equals(b.Position)||!a.Velocities.Equals(b.Velocities))report.retainedStateExact=false;}
                for(int s=0;s<7;s++)
                {var a=oldFrames[blade*7+s];var b=newFrames[blade*7+s];if(!a.Rotation.Equals(b.Rotation)||!a.AngularVelocity.Equals(b.AngularVelocity))report.retainedStateExact=false;if(!oldPlastic[blade*7+s].Equals(newPlastic[blade*7+s]))report.retainedPlasticExact=false;}
                for(int g=0;g<world.Cells.Length;g++)if(oldCells[g]==world.Cells[g])for(int leaf=0;leaf<32;leaf++)
                    Require(oldBlades[g*32+leaf].Root.Equals(r.Data.Blades[g*32+leaf].Root) && oldBlades[g*32+leaf].Seed==r.Data.Blades[g*32+leaf].Seed,"Overlapping grass moved or changed identity.");
                Vector3 trackedRoot=r.Data.Blades[blade].Root;float trackedSeed=r.Data.Blades[blade].Seed;
                Move(demo,new Vector3(1024,2.5f,-1024));Require(world.LastLoadedCells==4096,"Teleport did not replace the core residency.");
                var reboundPlastic=r.ReadPlasticBends();report.unloadedPlasticCleared=true;
                for(int s=0;s<7;s++)if(!reboundPlastic[blade*7+s].Equals(Vector4.zero))report.unloadedPlasticCleared=false;
                Move(demo,start);report.stableReload=r.Data.Blades[blade].Root.Equals(trackedRoot) && r.Data.Blades[blade].Seed==trackedSeed;
                var returnedPlastic=r.ReadPlasticBends();Require(returnedPlastic[blade*7].Equals(Vector4.zero),"Unloaded lodging leaked into a reloaded world cell.");
                var shiftTimes=new List<double>();
                for(int tick=0;tick<720;tick++)
                {
                    Vector3 focus=new Vector3(-12+tick*.5f,2.5f,-2.5f+Mathf.Sin(tick*.02f)*20);
                    var watch=Stopwatch.StartNew();bool shifted=Move(demo,focus);watch.Stop();if(shifted)shiftTimes.Add(watch.Elapsed.TotalMilliseconds);
                    Tick(demo,focus,report);if(tick%60==0)outer.PrepareVisible();report.walkTicks++;
                }
                report.windowShifts=world.Version;foreach(double ms in shiftTimes) {report.cpuShiftMean+=ms;report.cpuShiftMax=Math.Max(report.cpuShiftMax,ms);}report.cpuShiftMean/=Math.Max(1,shiftTimes.Count);
                report.rotationDoesNotStream=true;int version=world.Version;demo.Cam.transform.Rotate(0,90,0);r.StreamTo(demo.Cam.transform.position);if(version!=world.Version)report.rotationDoesNotStream=false;
                demo.Cam.transform.rotation=Quaternion.Euler(8,0,0);VerifyGpuMetadata(r,report);
                demo.OverheadView=true;demo.ConfigureViewCamera();outer.PrepareVisible();VerifyOuter(demo,outer,report);
                controller.RefreshStatistics();Capture(demo,outer,"OverheadDebug",output,true);
                Capture(demo,outer,"OverheadLit",output,false);
                demo.OverheadView=false;demo.ConfigureViewCamera();Capture(demo,outer,"GroundLit",output,false);
                report.bufferBytesEnd=r.AllocatedGpuBytes+outer.AllocatedGpuBytes;
                report.bufferReuse=ReferenceEquals(positionBuffer,Field<object>(r,"positions")) && ReferenceEquals(boneBuffer,Field<object>(r,"bones")) && ReferenceEquals(outerBuffer,outer.DiagnosticInstances);
                foreach(var shader in new[] {outer.LeafShader,demo.GrassMaterial.shader})foreach(var message in ShaderUtil.GetShaderMessages(shader))Require(message.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error,message.message);
                Require(report.retainedStateExact && report.retainedPlasticExact && report.stableReload && report.unloadedPlasticCleared,"World state ownership regression.");
                Require(report.minActive==32768 && report.maxActive==32768 && report.rotationDoesNotStream && report.bufferReuse && report.bufferBytesStart==report.bufferBytesEnd,"Streaming budget or buffer residency changed.");
                Require(report.insideActiveCore==0 && report.invalidInstances==0 && report.outerVisible>100000 && report.maxRootError<.001f && report.maxCpuGpuRestError<.00001f && report.maxCpuGpuRodError<.01f,"Streaming GPU identity or geometry is inconsistent.");
                report.passed=true;Debug.Log("GRASS_STREAMING_PASS "+JsonUtility.ToJson(report));
            }
            catch(Exception e) {report.error=e.ToString();Debug.LogError("GRASS_STREAMING_FAIL "+e);}
            finally
            {
                if(outer!=null)Object.DestroyImmediate(outer);demo?.Renderer?.Dispose();File.WriteAllText(Path.Combine(output,"Results.json"),JsonUtility.ToJson(report,true));
                if(!Application.isBatchMode)EditorSceneManager.CloseScene(scene,true);if(previous.IsValid()&&previous.isLoaded)UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);
            }
            if(Application.isBatchMode)EditorApplication.Exit(report.passed?0:1);
        }
        private static bool Move(GrassHandoffDemo demo,Vector3 position)
        {demo.Cam.transform.position=position;return demo.Renderer.StreamTo(position);}
        private static void Tick(GrassHandoffDemo demo,Vector3 position,Report report)
        {
            var r=demo.Renderer;Vector3 focus=position;focus.y=0;r.SimulationCenter=focus;demo.Interaction.position=focus+Vector3.forward*1.1f+Vector3.up*.6f;
            r.SetWind(GrassResponseDemo.WindAt(r.SimulationTime,true)*(demo.Wind.magnitude/6),.1f,new Vector4(.01f,.01f,0,1));r.Advance(1f/60);
            report.minActive=Mathf.Min(report.minActive,r.ActiveBladeCount);report.maxActive=Mathf.Max(report.maxActive,r.ActiveBladeCount);
        }
        private static void VerifyGpuMetadata(PBDGrassPatchRenderer r,Report report)
        {
            var shader=Field<ComputeShader>(r,"worldShader");Require(shader!=null,"No streaming shader.");
            // Static metadata buffers are held in the renderer buffer list; use shader-side blades via
            // the immutable first matching stride. Reflection remains restricted to this offline audit.
            var buffers=Field<List<ComputeBuffer>>(r,"buffers");ComputeBuffer bladeBuffer=null,rest=null,rods=null;
            foreach(var b in buffers)
            {if(b.count==r.BladeCount && b.stride==32)bladeBuffer=b;if(b.count==r.BladeCount*8 && b.stride==48)rest=b;if(b.count==r.BladeCount*7 && b.stride==64)rods=b;}
            Require(bladeBuffer!=null && rest!=null && rods!=null,"Metadata buffers not found.");
            var blade=new GrassBladeData.Blade[32];var nodes=new BoneInfo[8];var rod=new GrassBladeData.Rod[7];
            for(int g=0;g<r.Data.Grids.Length;g+=71)
            {
                bladeBuffer.GetData(blade,0,g*32,32);rest.GetData(nodes,0,g*32*8,8);rods.GetData(rod,0,g*32*7,7);
                for(int leaf=0;leaf<32;leaf++) {report.maxRootError=Mathf.Max(report.maxRootError,Vector3.Distance(blade[leaf].Root,r.Data.Blades[g*32+leaf].Root));Require(blade[leaf].Seed==r.Data.Blades[g*32+leaf].Seed,"GPU world seed differs.");}
                for(int n=0;n<8;n++)report.maxCpuGpuRestError=Mathf.Max(report.maxCpuGpuRestError,Vector3.Distance(nodes[n].Position,r.Data.RestNodes[g*32*8+n].Position));
                for(int s=0;s<7;s++)report.maxCpuGpuRodError=Mathf.Max(report.maxCpuGpuRodError,Quaternion.Angle(rod[s].Rotation,r.Data.Rods[g*32*7+s].Rotation));
            }
        }
        private static void VerifyOuter(GrassHandoffDemo demo,GrassOuterCoverage outer,Report report)
        {
            var args=new uint[4];outer.DiagnosticArguments.GetData(args);report.outerVisible=(int)args[1];var instances=new Instance[report.outerVisible];outer.DiagnosticInstances.GetData(instances,0,0,instances.Length);
            var world=demo.WorldLayout;var active=new HashSet<int>();foreach(var g in demo.Renderer.Pool.Selected)active.Add(g.GridId);
            foreach(var instance in instances)
            {
                var cell=new Vector2Int(Mathf.FloorToInt(instance.Root.x),Mathf.FloorToInt(instance.Root.z));int slot=GrassWorldIdentity.Slot(cell,world.Size);
                if(world.Cells[slot]==cell && active.Contains(slot))report.insideActiveCore++;
                bool found=false;
                for(int leaf=0;leaf<32;leaf++)if(GrassWorldIdentity.LeafSeed(cell,leaf)==instance.Seed)
                {report.maxRootError=Mathf.Max(report.maxRootError,Vector3.Distance(instance.Root,GrassWorldIdentity.Root(cell,leaf,world.Ground)));found=true;break;}
                if(!found || float.IsNaN(instance.Root.sqrMagnitude))report.invalidInstances++;
            }
        }
        private static void Capture(GrassHandoffDemo demo,GrassOuterCoverage outer,string name,string output,bool debug)
        {
            demo.Handoff.DebugLodBaseColors=debug;var rt=new RenderTexture(1920,1080,24);rt.Create();demo.Cam.targetTexture=rt;
            try
            {
                demo.ConfigureViewCamera();outer.PrepareVisible();demo.Renderer.Draw(demo.Cam);outer.RenderPrepared(demo.Cam);demo.Cam.Render();
                var old=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(1920,1080,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());Object.DestroyImmediate(image);RenderTexture.active=old;
            }
            finally {demo.Cam.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);}
        }
        private static void Require(bool condition,string message) {if(!condition)throw new InvalidOperationException(message);}
    }
}
