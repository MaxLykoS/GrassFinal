using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GrassFinal.PBDGrass.Editor
{
    public static class GrassOuterCoverageSceneBuilder
    {
        public const string ScenePath="Assets/PBDGrass/Scenes/PBDGrassOuterCoverage.unity";
        [MenuItem("Tools/PBD Grass/Create Outer Coverage Experiment")]
        public static void Create()
        {
            var demo=Build();EditorSceneManager.SaveScene(demo.gameObject.scene,ScenePath);
            if(Application.isBatchMode)EditorApplication.Exit(0);
        }
        public static GrassHandoffDemo Build()
        {
            var demo=GrassTieredSceneBuilder.Build();
            demo.name="Outer coverage - full XPBD core + stateless perimeter";
            var outer=demo.gameObject.AddComponent<GrassOuterCoverage>(); outer.Demo=demo; demo.OuterCoverage=outer;
            outer.CullShader=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/PBDGrass/Shaders/GrassOuterCoverage.compute");
            outer.LeafShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/PBDGrass/Shaders/GrassOuterCoverage.shader");
            demo.OverheadFieldSize=256; demo.OverheadHeight=180; demo.ConfigureViewCamera();
            demo.Cam.farClipPlane=500;
            const string groundPath="Assets/PBDGrass/Materials/OuterCoverageGround.mat";
            var groundMaterial=AssetDatabase.LoadAssetAtPath<Material>(groundPath);
            if(groundMaterial==null)
            {
                groundMaterial=new Material(Shader.Find("Standard")); groundMaterial.color=new Color(.055f,.10f,.025f);
                groundMaterial.SetFloat("_Glossiness",0); AssetDatabase.CreateAsset(groundMaterial,groundPath);
            }
            var ground=GameObject.CreatePrimitive(PrimitiveType.Plane); ground.name="Coverage ground (render only)";
            Object.DestroyImmediate(ground.GetComponent<Collider>()); ground.transform.position=new Vector3(0,-.025f,0);
            ground.transform.localScale=new Vector3(25.6f,1,25.6f); ground.GetComponent<MeshRenderer>().sharedMaterial=groundMaterial;
            // No new colliders, ground physics or history assets are added for the perimeter.
            return demo;
        }
    }
}
