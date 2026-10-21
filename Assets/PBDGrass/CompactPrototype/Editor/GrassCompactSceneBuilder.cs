using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GrassFinal.PBDGrass.Editor
{
    public static class GrassCompactSceneBuilder
    {
        public const string Root="Assets/PBDGrass/CompactPrototype";
        public const string ScenePath="Assets/PBDGrass/Scenes/PBDGrassCompactMiddle.unity";
        [MenuItem("Tools/PBD Grass/Create Compact Middle Experiment")]
        public static void Create()
        {
            Build();
            if(Application.isBatchMode)EditorApplication.Exit(0);
        }
        public static void Build()
        {
            EditorSceneManager.OpenScene(GrassMidrangeSceneBuilder.ScenePath);
            var demo=Object.FindObjectOfType<GrassHandoffDemo>();
            demo.name="Compact middle - 80 byte centerline and shared material";
            demo.Handoff.UseCompactMiddle=true;
            demo.Solver=AssetDatabase.LoadAssetAtPath<ComputeShader>(Root+"/Shaders/CompactGrassSolver.compute");
            demo.HandoffShader=AssetDatabase.LoadAssetAtPath<ComputeShader>(Root+"/Shaders/CompactGrassHandoff.compute");
            demo.StreamingShader=AssetDatabase.LoadAssetAtPath<ComputeShader>(Root+"/Shaders/CompactGrassStreaming.compute");
            string path=Root+"/CompactGrass.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null) {material=new Material(demo.GrassMaterial);AssetDatabase.CreateAsset(material,path);}
            else material.CopyPropertiesFromMaterial(demo.GrassMaterial);
            material.shader=AssetDatabase.LoadAssetAtPath<Shader>(Root+"/Shaders/CompactGrass.shader");
            demo.GrassMaterial=material;EditorUtility.SetDirty(material);
            EditorSceneManager.SaveScene(demo.gameObject.scene,ScenePath);AssetDatabase.SaveAssets();
        }
    }
}
