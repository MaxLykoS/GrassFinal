using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GrassFinal.PBDGrass.Editor
{
    public static class GrassTieredSceneBuilder
    {
        public const string ScenePath="Assets/PBDGrass/Scenes/PBDGrassTieredState.unity";
        [MenuItem("Tools/PBD Grass/Create Tiered State Experiment")]
        public static void Create()
        {
            var demo=Build();
            EditorSceneManager.SaveScene(demo.gameObject.scene,ScenePath);
            if(Application.isBatchMode)EditorApplication.Exit(0);
        }
        public static GrassHandoffDemo Build()
        {
            var demo=BakedGrassHandoffSceneBuilder.Build();
            demo.name="Tiered state - 32768 XPBD + 98304 Bezier";
            demo.PatchSize=64; demo.AutoTourHalfSpan=12;
            demo.Physics.MaxActiveBlades=32768; demo.Physics.NearDistance=20;
            demo.Physics.PoolHysteresis=.35f; demo.Physics.MaxDistance=100;
            demo.Handoff.DebugLodBaseColors=true; demo.Handoff.UseTieredState=true;
            demo.Handoff.HistoryBandWidth=2; demo.Handoff.MaxHistoryBlades=65536;
            demo.HandoffShader=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/PBDGrass/Shaders/TieredGrassHandoff.compute");
            demo.Cam.farClipPlane=160; demo.Cam.transform.position=demo.TourPosition(0);
            demo.Cam.transform.LookAt(demo.transform.position+Vector3.up*.6f);
            demo.Interaction.position=new Vector3(-10.9f,.6f,-2.5f);
            var focus=new GameObject("Simulation Focus (ground tour)").transform;
            focus.SetParent(demo.transform,true); focus.position=new Vector3(-12,0,-2.5f);
            demo.SimulationFocus=focus;
            demo.OverheadView=true; demo.OverheadHeight=70; demo.ConfigureViewCamera();
            return demo;
        }
    }
}
