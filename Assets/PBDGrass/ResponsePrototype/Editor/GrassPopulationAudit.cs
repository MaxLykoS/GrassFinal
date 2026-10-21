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
    // Offline inspection only: read the actual GPU geometry, LOD classification and
    // indirect draw arguments. No audit readback is added to the runtime demo.
    public static class GrassPopulationAudit
    {
        [StructLayout(LayoutKind.Sequential)] private struct GridState
        { public int pool,changed; public float started,transitioning; }
        [Serializable] private sealed class View
        {
            public int width,height,drawnGrids,drawnBlades,submittedTriangles,verticesOutsideView;
            public Vector3 cameraPosition; public float orthographicSize;
        }
        [Serializable] private sealed class Report
        {
            public bool passed; public string gpu,error;
            public int generatedGrids,generatedBlades,activeXpbdBlades,bezierBlades,gpuXpbdBlades,gpuBezierBlades;
            public int gpuVertexCount,nondegenerateBlades,invalidVertices,minActiveBlades,maxActiveBlades;
            public Vector3 geometryMin,geometryMax;
            public List<View> views=new List<View>();
        }
        private static T Field<T>(object owner,string name)
        {
            var field=owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic);
            if(field==null)throw new InvalidOperationException("Diagnostic field changed: "+name);
            return (T)field.GetValue(owner);
        }
        [MenuItem("Tools/PBD Grass/Audit Tiered Scene Population")]
        public static void Run()
        {
            string output=Environment.GetEnvironmentVariable("GRASS_POPULATION_OUTPUT");
            if(string.IsNullOrEmpty(output))output=Path.GetFullPath(GrassResponseBaker.Root+"/PopulationAuditRun");
            Directory.CreateDirectory(output); var report=new Report { gpu=SystemInfo.graphicsDeviceName,minActiveBlades=int.MaxValue };
            var previous=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var scene=EditorSceneManager.OpenScene(GrassTieredSceneBuilder.ScenePath,Application.isBatchMode?OpenSceneMode.Single:OpenSceneMode.Additive);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene); GrassHandoffDemo demo=null;
            try
            {
                foreach(var root in scene.GetRootGameObjects()) { demo=root.GetComponentInChildren<GrassHandoffDemo>(); if(demo!=null)break; }
                Require(demo!=null && demo.Handoff.UseTieredState && demo.OverheadView && demo.SimulationFocus!=null,"Overhead scene configuration is missing.");
                demo.enabled=false; demo.Cam.enabled=false; demo.ConfigureViewCamera(); demo.Initialize();
                var r=demo.Renderer;
                report.generatedGrids=r.Data.Grids.Length; report.generatedBlades=r.BladeCount;
                for(int tick=0;tick<720;tick++)
                {
                    var focus=demo.TourPosition((tick+1)/60f); focus.y=demo.transform.position.y; demo.SimulationFocus.position=focus;
                    r.SimulationCenter=focus;
                    var direction=Vector3.ProjectOnPlane(demo.transform.position-focus,Vector3.up).normalized;
                    demo.Interaction.position=focus+direction*1.1f+Vector3.up*.6f;
                    r.SetWind(GrassResponseDemo.WindAt(r.SimulationTime,true)*(demo.Wind.magnitude/6),.1f,new Vector4(.01f,.01f,0,1));
                    r.Advance(1f/60); report.minActiveBlades=Mathf.Min(report.minActiveBlades,r.ActiveBladeCount); report.maxActiveBlades=Mathf.Max(report.maxActiveBlades,r.ActiveBladeCount);
                }
                report.activeXpbdBlades=r.ActiveBladeCount; report.bezierBlades=r.BladeCount-r.ActiveBladeCount;
                object tiered=Field<object>(r.Handoff,"tiered"); var states=Field<ComputeBuffer>(tiered,"states");
                var classification=new GridState[states.count]; states.GetData(classification);
                foreach(var state in classification)if(state.pool>=0)report.gpuXpbdBlades+=32;
                report.gpuBezierBlades=report.generatedBlades-report.gpuXpbdBlades;
                var positions=Field<ComputeBuffer>(r,"positions"); var vertices=new Vector3[positions.count]; positions.GetData(vertices);
                report.gpuVertexCount=vertices.Length; report.geometryMin=Vector3.one*float.MaxValue; report.geometryMax=Vector3.one*float.MinValue;
                foreach(var p in vertices)
                {
                    if(float.IsNaN(p.sqrMagnitude) || float.IsInfinity(p.sqrMagnitude))report.invalidVertices++;
                    report.geometryMin=Vector3.Min(report.geometryMin,p); report.geometryMax=Vector3.Max(report.geometryMax,p);
                }
                for(int blade=0;blade<r.BladeCount;blade++)
                {
                    int start=blade*15; Vector3 point=(vertices[start]+vertices[start+1])*.5f; float arc=0;
                    for(int n=1;n<8;n++) { Vector3 next=n==7?vertices[start+14]:(vertices[start+n*2]+vertices[start+n*2+1])*.5f; arc+=Vector3.Distance(point,next); point=next; }
                    if((vertices[start]-vertices[start+1]).sqrMagnitude>1e-10f && arc>r.Data.GetBlade(blade).Length*.95f)report.nondegenerateBlades++;
                }
                foreach(var size in new[] { new Vector2Int(1920,1080),new Vector2Int(1200,900),new Vector2Int(900,1600) })
                {
                    var rt=new RenderTexture(size.x,size.y,24); rt.Create(); demo.Cam.targetTexture=rt; demo.Cam.aspect=(float)size.x/size.y; demo.ConfigureViewCamera();
                    try
                    {
                        r.Draw(demo.Cam); demo.Cam.Render();
                        var args=new uint[4]; Field<ComputeBuffer>(r,"drawArgs").GetData(args);
                        var view=new View { width=size.x,height=size.y,drawnGrids=(int)args[1],drawnBlades=(int)args[1]*32,
                            submittedTriangles=(int)(args[0]*args[1]/3),cameraPosition=demo.Cam.transform.position,orthographicSize=demo.Cam.orthographicSize };
                        Matrix4x4 matrix=demo.Cam.projectionMatrix*demo.Cam.worldToCameraMatrix;
                        foreach(var p in vertices)
                        {
                            Vector4 clip=matrix*new Vector4(p.x,p.y,p.z,1);
                            if(Mathf.Abs(clip.x)>clip.w+1e-4f || Mathf.Abs(clip.y)>clip.w+1e-4f || Mathf.Abs(clip.z)>clip.w+1e-4f)view.verticesOutsideView++;
                        }
                        report.views.Add(view);
                        Require(args[0]==1248 && view.drawnGrids==4096 && view.verticesOutsideView==0,"Not all GPU geometry is drawn and inside the camera view.");
                        var before=RenderTexture.active; RenderTexture.active=rt; var texture=new Texture2D(size.x,size.y,TextureFormat.RGB24,false);
                        texture.ReadPixels(new Rect(0,0,size.x,size.y),0,0); texture.Apply();
                        File.WriteAllBytes(Path.Combine(output,"Overhead_"+size.x+"x"+size.y+".png"),texture.EncodeToPNG());
                        RenderTexture.active=before; Object.DestroyImmediate(texture);
                    }
                    finally { demo.Cam.targetTexture=null; rt.Release(); Object.DestroyImmediate(rt); }
                }
                Require(report.generatedBlades==131072 && report.activeXpbdBlades==32768 && report.bezierBlades==98304
                    && report.gpuXpbdBlades==32768 && report.gpuBezierBlades==98304 && report.minActiveBlades==32768 && report.maxActiveBlades==32768
                    && report.nondegenerateBlades==131072 && report.invalidVertices==0,"GPU population audit failed.");
                report.passed=true; Debug.Log("GRASS_POPULATION_AUDIT_PASS "+JsonUtility.ToJson(report));
            }
            catch(Exception e) { report.error=e.ToString(); Debug.LogError("GRASS_POPULATION_AUDIT_FAIL "+e); }
            finally
            {
                if(demo!=null)demo.Renderer?.Dispose();
                File.WriteAllText(Path.Combine(output,"Results.json"),JsonUtility.ToJson(report,true));
                if(!Application.isBatchMode)EditorSceneManager.CloseScene(scene,true);
                if(previous.IsValid() && previous.isLoaded)UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);
            }
            if(Application.isBatchMode)EditorApplication.Exit(report.passed?0:1);
        }
        private static void Require(bool value,string message) { if(!value)throw new InvalidOperationException(message); }
    }
}
