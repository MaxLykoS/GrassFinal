using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;
namespace GrassFinal.PBDGrass.Editor
{
    public static class GrassLowBudgetBaker
    {
        public const string Root="Assets/PBDGrass/ResponsePrototype/LowBudget";
        public static string TypePath(int index)=>Root+"/Baked/GrassResponse"+(index+1)+".asset";
        [Serializable] private sealed class TypeResult {public string name;public float maxSpeed,maxFit;public Vector2 dynamicFit;}
        [Serializable] private sealed class Report {public bool passed;public string error;public List<TypeResult> types=new List<TypeResult>();}
        [MenuItem("Tools/PBD Grass/Bake Low Budget 2x1 Response Bank")]
        public static void Bake()
        {
            string output=Environment.GetEnvironmentVariable("GRASS_LOW_BUDGET_OUTPUT")??Path.GetFullPath(Root+"/BakeRun");
            Directory.CreateDirectory(output);var report=new Report();Texture2D wind=null;
            var previous=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var scene=Application.isBatchMode?EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single)
                :UnityEngine.SceneManagement.SceneManager.CreateScene("Low budget response bake");
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            try
            {
                Directory.CreateDirectory(Root+"/Baked");AssetDatabase.Refresh();
                var camera=new GameObject("Low budget bake camera").AddComponent<Camera>();camera.enabled=false;
                var solver=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/PBDGrass/Shaders/PBDSolverCS.compute");
                var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/PBDGrass/Materials/PBDGrassProcedualMaterial.mat");
                wind=GrassResponseType.CreateUniformWindTexture();
                for(int t=0;t<8;t++)
                {
                    var computed=GrassResponseBaker.ComputeType(t,camera,solver,material,wind,2,1,.5f);
                    var result=new TypeResult{name=computed.DisplayName,dynamicFit=computed.DynamicFitError};
                    foreach(var pose in computed.Samples){result.maxSpeed=Mathf.Max(result.maxSpeed,pose.Speed);result.maxFit=Mathf.Max(result.maxFit,pose.FitError);}
                    if(result.maxSpeed>=.003f)throw new Exception("Low budget bake did not settle: "+result.name);
                    var existing=AssetDatabase.LoadAssetAtPath<GrassResponseType>(TypePath(t));
                    if(existing==null)AssetDatabase.CreateAsset(computed,TypePath(t));
                    else {EditorUtility.CopySerialized(computed,existing);Object.DestroyImmediate(computed);EditorUtility.SetDirty(existing);}
                    report.types.Add(result);AssetDatabase.SaveAssets();Debug.Log("LOW_BUDGET_BAKE "+JsonUtility.ToJson(result));
                }
                report.passed=true;
            }
            catch(Exception e){report.error=e.ToString();Debug.LogException(e);}
            finally
            {
                if(wind!=null)Object.DestroyImmediate(wind);
                if(!Application.isBatchMode)EditorSceneManager.CloseScene(scene,true);
                if(previous.IsValid() && previous.isLoaded)UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);
                File.WriteAllText(Path.Combine(output,"Results.json"),JsonUtility.ToJson(report,true));
            }
            if(Application.isBatchMode)EditorApplication.Exit(report.passed?0:1);
        }
    }
}
