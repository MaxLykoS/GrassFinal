using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace GrassFinal.PBDGrass.Editor
{
    // Route the existing rigorous scenarios to the new material without weakening
    // their lifecycle, state-seeding or plastic persistence assertions.
    public static class GrassLowBudgetIntegrationValidation
    {
        public static void Lifecycle()=>Run(GrassContactLifecycleValidation.Run);
        public static void Combined()=>Run(GrassCombinedBaselineValidation.Run);
        private static void Run(Action validation)
        {
            EditorSceneManager.sceneOpened+=Configure;
            try{validation();}
            finally{EditorSceneManager.sceneOpened-=Configure;}
        }
        private static void Configure(Scene scene,OpenSceneMode mode)
        {
            foreach(var root in scene.GetRootGameObjects())
            {
                var demo=root.GetComponentInChildren<GrassHandoffDemo>();if(demo==null)continue;
                typeof(GrassHandoffDemo).GetMethod("Release",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(demo,null);
                demo.Physics.NearSubsteps=2;demo.Physics.ConstraintIterations=1;
                var bank=AssetDatabase.LoadAssetAtPath<GrassResponseType>(GrassLowBudgetBaker.TypePath(5));
                demo.Physics.SoftBendCompliance=bank.Physics.SoftBendCompliance;demo.Handoff.BakedResponse=bank;
            }
        }
        public static void Switching()
        {
            string output=Environment.GetEnvironmentVariable("GRASS_LOW_BUDGET_OUTPUT");
            try
            {
                EditorSceneManager.OpenScene("Assets/PBDGrass/Scenes/PBDGrassIterationBudget.unity");
                var control=Object.FindObjectOfType<GrassIterationBudgetDemo>();var demo=control.Demo;
                var lab=Object.FindObjectOfType<GrassShowcaseController>();lab.enabled=false;lab.ApplyActors(0,true);
                demo.enabled=false;demo.Cam.enabled=false;demo.OuterCoverage.enabled=false;
                control.Apply();demo.Initialize();demo.OuterCoverage.Initialize();
                for(int i=0;i<90;i++)demo.Renderer.Advance(1f/60);
                var renderer=demo.Renderer;long bytes=renderer.AllocatedGpuBytes+demo.OuterCoverage.AllocatedGpuBytes;
                var before=Read<BoneInfo>(renderer,"bones");var plastic=Read<Vector4>(renderer,"plasticBends");
                var directions=Read<GrassBladeData.Frame>(renderer,"frames");
                float peakPlastic=0;foreach(var p in plastic)peakPlastic=Mathf.Max(peakPlastic,p.magnitude);
                Require(renderer.ActiveBladeCount>0 && peakPlastic>.01f,"Switch test did not establish live lodged grass.");
                int ticks=renderer.SimulationTicks,entries=renderer.Handoff.EntryInitializations;
                foreach(var budget in new[]{GrassIterationBudgetDemo.Budget.Reference8x3,GrassIterationBudgetDemo.Budget.Minimum2x1})
                {
                    control.Quality=budget;control.Apply();
                    var expected=budget==GrassIterationBudgetDemo.Budget.Minimum2x1?control.MinimumResponse:control.ReferenceResponse;
                    Require(demo.Handoff.BakedResponse==expected,"Wrong response bank after switch.");
                    Require(ReferenceEquals(renderer,demo.Renderer)&&ticks==renderer.SimulationTicks&&entries==renderer.Handoff.EntryInitializations,"Switch reset live solver.");
                    Require(bytes==renderer.AllocatedGpuBytes+demo.OuterCoverage.AllocatedGpuBytes,"Switch allocated new GPU resources.");
                    var after=Read<BoneInfo>(renderer,"bones");var afterPlastic=Read<Vector4>(renderer,"plasticBends");
                    var afterDirections=Read<GrassBladeData.Frame>(renderer,"frames");
                    for(int n=0;n<before.Length;n++)Require(before[n].Position.Equals(after[n].Position)&&before[n].Velocities.Equals(after[n].Velocities),"Switch changed live nodes.");
                    for(int n=0;n<plastic.Length;n++)Require(plastic[n].Equals(afterPlastic[n]),"Switch changed plastic.");
                    for(int n=0;n<directions.Length;n++)Require(directions[n].Rotation.Equals(afterDirections[n].Rotation)&&directions[n].AngularVelocity.Equals(afterDirections[n].AngularVelocity),"Switch changed material frames.");
                    var outer=typeof(GrassOuterCoverage).GetField("bakedResponse",BindingFlags.NonPublic|BindingFlags.Instance);
                    Require(outer!=null && ReferenceEquals(outer.GetValue(demo.OuterCoverage),expected),"Outer bank stale.");
                    var controls=Read<Vector4>(demo.OuterCoverage,"controls");
                    for(int s=0;s<25;s++)Require(controls[s*3].Equals((Vector4)expected.Samples[s].C1)&&controls[s*3+1].Equals((Vector4)expected.Samples[s].C2)&&controls[s*3+2].Equals((Vector4)expected.Samples[s].Tip),"Outer GPU controls stale.");
                }
                File.WriteAllText(Path.Combine(output,"Results.json"),"{\"passed\":true,\"statePreserved\":true,\"gpuBytes\":"+bytes+"}");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);File.WriteAllText(Path.Combine(output,"Results.json"),JsonUtility.ToJson(new Failure{error=e.ToString()},true));EditorApplication.Exit(1);}
        }
        [Serializable] private sealed class SeedReport
        {public bool passed,cold,warm;public string error;public float position,velocity,rotation,angular;}
        public static void Seeds()
        {
            var report=new SeedReport();string output=Environment.GetEnvironmentVariable("GRASS_LOW_BUDGET_OUTPUT");
            try
            {
                EditorSceneManager.OpenScene(GrassShowcaseSceneBuilder.ScenePath);
                var demo=Object.FindObjectOfType<GrassHandoffDemo>();var lab=Object.FindObjectOfType<GrassShowcaseController>();
                demo.enabled=false;lab.enabled=false;demo.OuterCoverage.enabled=false;demo.Cam.enabled=false;
                typeof(GrassHandoffDemo).GetMethod("Release",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(demo,null);
                foreach(var a in lab.Actors)a.Collider.enabled=false;
                demo.PatchSize=64;demo.OuterCoverage.MiddleFullDistance=8;demo.OuterCoverage.MiddleOuterDistance=16;
                demo.Physics.NearSubsteps=2;demo.Physics.ConstraintIterations=1;
                var bank=AssetDatabase.LoadAssetAtPath<GrassResponseType>(GrassLowBudgetBaker.TypePath(5));
                demo.Physics.SoftBendCompliance=bank.Physics.SoftBendCompliance;demo.Handoff.BakedResponse=bank;
                demo.Cam.transform.position=new Vector3(0,100,0);demo.Cam.transform.rotation=Quaternion.Euler(90,0,0);demo.Initialize();
                var r=demo.Renderer;for(int t=0;t<60;t++)r.Advance(1f/60);
                int cold=GrassWorldIdentity.Slot(new Vector2Int(8,0),64);
                var before=r.Handoff.ReadDisplayNodes();var frames=r.Handoff.ReadDisplayFrames();
                var actor=lab.Actors[0].Collider;actor.Type=GrassInteractionCollider.Shape.Sphere;actor.Radius=1.2f;actor.transform.position=new Vector3(8.5f,.35f,.5f);actor.enabled=true;
                r.RefreshSimulationSelection();CheckSeed(r,cold,before,frames,report);report.cold=true;
                for(int t=0;t<90;t++)r.Advance(1f/60);
                var tiered=typeof(GrassLodHandoff).GetField("tiered",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(r.Handoff);
                var map=(Vector2Int[])tiered.GetType().GetField("historyMap",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(tiered);
                int warm=-1;float score=float.MaxValue;
                for(int g=0;g<map.Length;g++)if(map[g].x>=0 && !r.Pool.IsActive(g))
                {var p=r.Data.Grids[g].Pos;float d=(p-new Vector3(12,0,0)).sqrMagnitude;if(d<score){score=d;warm=g;}}
                Require(warm>=0,"No warmed inactive boundary grid found.");
                before=r.Handoff.ReadDisplayNodes();frames=r.Handoff.ReadDisplayFrames();
                var target=r.Data.Grids[warm].Pos;actor.transform.position=new Vector3(target.x,.35f,target.z);r.RefreshSimulationSelection();
                CheckSeed(r,warm,before,frames,report);report.warm=true;
                Require(report.position<1e-6f && report.velocity<1e-6f && report.rotation<.1f && report.angular<1e-6f,"Cold/warm seed mismatch.");
                report.passed=true;
            }
            catch(Exception e){report.error=e.ToString();Debug.LogException(e);}
            File.WriteAllText(Path.Combine(output,"Results.json"),JsonUtility.ToJson(report,true));EditorApplication.Exit(report.passed?0:1);
        }
        private static void CheckSeed(PBDGrassPatchRenderer r,int grid,BoneInfo[] before,GrassBladeData.Frame[] frames,SeedReport report)
        {
            Require(r.Pool.IsActive(grid),"Probe grid was not promoted.");var nodes=r.ReadNodes();var actual=r.ReadFrames();
            for(int b=grid*32;b<(grid+1)*32;b++)
            {
                for(int n=b*8;n<b*8+8;n++){report.position=Mathf.Max(report.position,(nodes[n].Position-before[n].Position).magnitude);report.velocity=Mathf.Max(report.velocity,(nodes[n].Velocities-before[n].Velocities).magnitude);}
                for(int s=b*7;s<b*7+7;s++){report.rotation=Mathf.Max(report.rotation,Quaternion.Angle(actual[s].Rotation,frames[s].Rotation));report.angular=Mathf.Max(report.angular,(actual[s].AngularVelocity-frames[s].AngularVelocity).magnitude);}
            }
        }
        [Serializable] private sealed class Failure{public bool passed;public string error;}
        private static T[] Read<T>(object source,string name)where T:struct
        {var b=(ComputeBuffer)source.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(source);var a=new T[b.count];b.GetData(a);return a;}
        private static void Require(bool value,string message){if(!value)throw new Exception(message);}
    }
}
