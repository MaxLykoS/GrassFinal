using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GrassFinal.PBDGrass.Editor
{
    public static class GrassCombinedBaselineSceneBuilder
    {
        public const string ScenePath="Assets/PBDGrass/Scenes/PBDGrassCombinedBaseline.unity";
        [MenuItem("Tools/PBD Grass/Create Combined 32768 XPBD Baseline")]
        public static void Create()
        {
            var demo=BakedGrassHandoffSceneBuilder.Build();
            demo.name="Combined baseline - 32768 XPBD + 98304 Bezier";
            demo.PatchSize=64;
            demo.Handoff.DebugLodBaseColors=true;
            demo.AutoTourHalfSpan=12;
            demo.Physics.MaxActiveBlades=32768;
            // The radius supplies more than 1024 candidate grids. The pool keeps the nearest
            // 1024, maintaining the full workload while identities change along the route.
            demo.Physics.NearDistance=20;
            demo.Physics.PoolHysteresis=.35f;
            demo.Physics.MaxDistance=100;
            demo.Cam.farClipPlane=160;
            demo.Cam.transform.position=demo.TourPosition(0);
            demo.Cam.transform.LookAt(demo.transform.position+Vector3.up*.6f);
            demo.Interaction.position=new Vector3(-10.9f,.6f,-2.5f);
            EditorSceneManager.SaveScene(demo.gameObject.scene,ScenePath);
            if(Application.isBatchMode)EditorApplication.Exit(0);
        }
    }
}
