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
    // Offline integration checks. Readbacks are only used here, never in runtime selection.
    public static class GrassInteractionCoverageValidation
    {
        [Serializable] private sealed class Report
        {
            public bool passed,legacyRangePreserved,middleActivated,nearEvicted,plasticRetained,plasticRestored,
                simultaneousContacts,boundaryOverlap,outerExcluded,coverageFollowsCamera,streamRetained,finiteState;
            public string error,gpu;
            public int ticks,minActive=int.MaxValue,maxActive,maxInteractionBlades,maxHistoryResident,historyCapacity;
            public float peakPlastic,seedPlasticError,maxNearReach;
            public long gpuBufferBytes;
        }
        private static Report report;
        private static GrassHandoffDemo demo;
        private static GrassShowcaseController lab;
        private static string output;
        private static readonly List<string> errors=new List<string>();
        private static PBDGrassPatchRenderer R=>demo.Renderer;
        private static T Field<T>(object owner,string name)=>(T)owner.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(owner);
        private static void Require(bool ok,string message) {if(!ok)throw new InvalidOperationException(message);}
        private static void Log(string message,string stack,LogType type)
        {if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert)errors.Add(message);}

        [MenuItem("Tools/PBD Grass/Validate Full Middle Interactions")]
        public static void Run()
        {
            output=Environment.GetEnvironmentVariable("GRASS_INTERACTION_OUTPUT")??Path.GetFullPath("Assets/PBDGrass/CompactPrototype/InteractionCoverage/ValidationRun");
            Directory.CreateDirectory(output);report=new Report {gpu=SystemInfo.graphicsDeviceName};errors.Clear();Application.logMessageReceived+=Log;
            try
            {
                EditorSceneManager.OpenScene(GrassShowcaseSceneBuilder.ScenePath);
                demo=Object.FindObjectOfType<GrassHandoffDemo>();lab=Object.FindObjectOfType<GrassShowcaseController>();
                demo.enabled=false;lab.enabled=false;demo.Cam.enabled=false;demo.OuterCoverage.enabled=false;
                typeof(GrassHandoffDemo).GetMethod("Release",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(demo,null);
                demo.Cam.transform.SetPositionAndRotation(new Vector3(0,100,0),Quaternion.Euler(90,0,0));
                demo.Cam.orthographic=true;demo.Cam.orthographicSize=72;demo.Cam.aspect=1600f/900;
                demo.AutoTour=false;demo.RandomWind=false;demo.Wind=new Vector3(4,0,2);
                // This historical coverage regression deliberately keeps its original full-pool policy.
                demo.Physics.ContactDrivenSimulation=false;
                foreach(var actor in lab.Actors)actor.Collider.enabled=false;
                Require(demo.Physics.InteractionsAcrossMiddle,"The contact lab must opt in to full middle interactions.");
                demo.Initialize();demo.OuterCoverage.Initialize();Focus(Vector3.zero);
                report.gpuBufferBytes=R.AllocatedGpuBytes+demo.OuterCoverage.AllocatedGpuBytes;
                report.historyCapacity=R.Handoff.HistoryCapacityBlades;
                Steps(30);
                var near=new HashSet<int>();foreach(var grid in R.Pool.Selected)near.Add(grid.GridId);
                int marker=Grid(40,0);Require(R.Pool.SlotForGrid(marker)<0,"The middle marker unexpectedly started in XPBD.");
                Contact(0,new Vector3(40.5f,.35f,.5f),1.2f);
                demo.Physics.InteractionsAcrossMiddle=false;R.RefreshSimulationSelection();
                report.legacyRangePreserved=R.Pool.SlotForGrid(marker)<0;
                Require(report.legacyRangePreserved,"The opt-out legacy 28m policy changed.");
                demo.Physics.InteractionsAcrossMiddle=true;R.RefreshSimulationSelection();
                report.middleActivated=R.Pool.SlotForGrid(marker)>=0;
                foreach(int g in near)if(R.Pool.SlotForGrid(g)<0)report.nearEvicted=true;
                Require(report.middleActivated && report.nearEvicted,"A middle contact did not replace ordinary near slots.");
                Steps(150);
                var raw=Packed(marker);var live=LivePlastic(marker);
                foreach(var value in live)report.peakPlastic=Mathf.Max(report.peakPlastic,((Vector2)value).magnitude);
                Require(report.peakPlastic>.01f,"The distant collision did not create plastic lodging.");
                Capture("MiddleContact");
                lab.Actors[0].Collider.enabled=false;R.RefreshSimulationSelection();Steps(60);
                Require(R.Pool.SlotForGrid(marker)<0,"The released middle contact retained an XPBD slot.");
                report.plasticRetained=Equal(raw,Packed(marker));Require(report.plasticRetained,"Plastic cache changed after release to Bezier.");
                Capture("MiddleReleased");
                Focus(Vector3.right*8);Steps(30);
                report.streamRetained=Equal(raw,Packed(marker));Require(report.streamRetained,"An overlapping stream page lost plastic.");
                Focus(Vector3.zero);Steps(30);
                Contact(0,new Vector3(40.5f,.35f,.5f),1.2f);R.RefreshSimulationSelection();
                live=LivePlastic(marker);
                for(int blade=0;blade<32;blade++)for(int s=0;s<7;s++)
                {
                    uint word=raw[blade*8+s];var expected=new Vector2(unchecked((short)(word&65535))*(2f/32767),unchecked((short)(word>>16))*(2f/32767));
                    report.seedPlasticError=Mathf.Max(report.seedPlasticError,Vector2.Distance(expected,live[blade*7+s]));
                }
                report.plasticRestored=report.seedPlasticError<1e-6f;Require(report.plasticRestored,"Reentry did not seed the cached plastic into the new slot.");
                Contact(1,new Vector3(-40.5f,.4f,.5f),1.1f);
                Contact(2,new Vector3(.5f,.4f,60.5f),1.1f);R.RefreshSimulationSelection();Steps(90);
                report.simultaneousContacts=R.Pool.SlotForGrid(Grid(-41,0))>=0 && R.Pool.SlotForGrid(Grid(0,60))>=0 && R.Pool.SlotForGrid(marker)>=0;
                Require(report.simultaneousContacts,"Multiple disconnected middle contacts did not share the pool.");Capture("SharedPoolOverview");
                foreach(var actor in lab.Actors)actor.Collider.enabled=false;Steps(30);
                Contact(0,new Vector3(65,.4f,.5f),2);R.RefreshSimulationSelection();Steps(30);
                report.boundaryOverlap=R.Pool.SlotForGrid(Grid(63,0))>=0;
                Require(report.boundaryOverlap,"A contact crossing the outer boundary was discarded as a whole.");
                foreach(var grid in R.Pool.Selected)
                    Require(new Vector2(grid.Position.x,grid.Position.z).magnitude<=64.71f,"Collision priority escaped the compatible boundary.");
                Contact(0,new Vector3(80,.4f,.5f),1);R.RefreshSimulationSelection();Steps(30);
                report.outerExcluded=R.Pool.InteractionBlades==0;Require(report.outerExcluded,"A wholly outer contact consumed XPBD slots.");
                Focus(Vector3.right*8);Contact(0,new Vector3(73,.4f,.5f),2);R.RefreshSimulationSelection();Steps(30);
                report.coverageFollowsCamera=R.Pool.SlotForGrid(Grid(71,0))>=0;
                Require(report.coverageFollowsCamera,"The interaction boundary did not follow the moving camera.");
                var nodes=new BoneInfo[32768*8];Field<ComputeBuffer>(R,"bones").GetData(nodes);report.finiteState=true;
                foreach(var n in nodes) {float length=n.Position.magnitude;report.finiteState&=!float.IsNaN(length) && !float.IsInfinity(length);report.maxNearReach=Mathf.Max(report.maxNearReach,length);}
                Require(report.finiteState && report.maxNearReach<3,"The promoted near state diverged.");
                Require(report.gpuBufferBytes==R.AllocatedGpuBytes+demo.OuterCoverage.AllocatedGpuBytes,"Interactions expanded GPU allocations.");
                Require(report.minActive==32768 && report.maxActive==32768,"The active XPBD budget changed.");
                Require(errors.Count==0,string.Join("\n",errors));report.ticks=R.SimulationTicks;report.passed=true;
            }
            catch(Exception e) {report.error=e.ToString();Debug.LogError("GRASS_INTERACTION_FAIL "+e);}
            finally
            {
                if(demo!=null)
                {
                    typeof(GrassOuterCoverage).GetMethod("Release",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(demo.OuterCoverage,null);
                    typeof(GrassHandoffDemo).GetMethod("Release",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(demo,null);
                }
                Application.logMessageReceived-=Log;
                File.WriteAllText(Path.Combine(output,"Results.json"),JsonUtility.ToJson(report,true));
                Debug.Log("GRASS_INTERACTION_RESULT "+JsonUtility.ToJson(report));
            }
            if(Application.isBatchMode)EditorApplication.Exit(report.passed?0:1);
        }
        private static int Grid(int x,int z)=>GrassWorldIdentity.Slot(new Vector2Int(x,z),demo.PatchSize);
        private static void Focus(Vector3 focus)
        {
            demo.Cam.transform.position=new Vector3(focus.x,100,focus.z);
            R.SetOuterCoverage(demo.OuterCoverage.CoverageTransition,true);R.StreamTo(focus);R.RefreshSimulationSelection();
        }
        private static void Contact(int index,Vector3 position,float radius)
        {
            var actor=lab.Actors[index];actor.Collider.Type=GrassInteractionCollider.Shape.Sphere;
            actor.Collider.Radius=radius;actor.Collider.HalfSegment=0;actor.Collider.transform.position=position;actor.Collider.enabled=true;
        }
        private static void Steps(int count)
        {
            for(int i=0;i<count;i++)
            {
                R.Advance(1f/60);report.minActive=Math.Min(report.minActive,R.ActiveBladeCount);report.maxActive=Math.Max(report.maxActive,R.ActiveBladeCount);
                report.maxInteractionBlades=Math.Max(report.maxInteractionBlades,R.Pool.InteractionBlades);
                report.maxHistoryResident=Math.Max(report.maxHistoryResident,R.Handoff.HistoryResidentBlades);
            }
        }
        private static uint[] Packed(int grid)
        {var values=new uint[32*8];R.Compact.Plastic.GetData(values,0,grid*32*8,values.Length);return values;}
        private static Vector4[] LivePlastic(int grid)
        {
            int slot=R.Pool.SlotForGrid(grid);Require(slot>=0,"The marker has no live physics slot.");
            var values=new Vector4[32*7];Field<ComputeBuffer>(R,"plasticBends").GetData(values,0,slot*32*7,values.Length);return values;
        }
        private static bool Equal(uint[] a,uint[] b) {for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
        private static void Capture(string name)
        {
            var camera=demo.Cam;var target=new RenderTexture(1600,900,24);target.Create();var previous=RenderTexture.active;
            var pixels=new Texture2D(1600,900,TextureFormat.RGB24,false);
            try {camera.targetTexture=target;demo.OuterCoverage.PrepareVisible();R.Draw(camera);demo.OuterCoverage.RenderPrepared(camera);camera.Render();
                RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1600,900),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),pixels.EncodeToPNG());}
            finally {camera.targetTexture=null;RenderTexture.active=previous;Object.DestroyImmediate(pixels);target.Release();Object.DestroyImmediate(target);}
        }
    }
}
