using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GrassFinal.PBDGrass.Editor
{
    public static class GrassStreamingSceneBuilder
    {
        public const string ScenePath="Assets/PBDGrass/Scenes/PBDGrassStreaming.unity";
        [MenuItem("Tools/PBD Grass/Create Streaming Experiment")]
        public static void Create()
        {
            var demo=GrassOuterCoverageSceneBuilder.Build();demo.name="Streaming world - fixed XPBD budget";
            if(demo.SimulationFocus!=null)Object.DestroyImmediate(demo.SimulationFocus.gameObject);
            demo.SimulationFocus=null;demo.StreamWorld=true;demo.AutoTour=false;
            demo.StreamingShader=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/PBDGrass/Shaders/GrassStreaming.compute");
            demo.OverheadView=false;demo.Cam.orthographic=false;
            demo.Cam.transform.SetPositionAndRotation(new Vector3(-12,2.5f,-2.5f),Quaternion.Euler(8,0,0));
            var streamer=demo.gameObject.AddComponent<GrassWorldStreamer>();streamer.Demo=demo;
            foreach(var root in demo.gameObject.scene.GetRootGameObjects())if(root.name=="Coverage ground (render only)")streamer.Ground=root.transform;
            EditorSceneManager.SaveScene(demo.gameObject.scene,ScenePath);
            if(Application.isBatchMode)EditorApplication.Exit(0);
        }
    }
}
