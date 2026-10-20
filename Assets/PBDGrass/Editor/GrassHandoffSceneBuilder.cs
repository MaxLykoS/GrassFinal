using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace GrassFinal.PBDGrass.Editor
{
    public static class GrassHandoffSceneBuilder
    {
        [MenuItem("Tools/PBD Grass/Create Handoff Prototype Scene")]
        public static void Create()
        {
            // Menu creates a new additive scene; never replaces the user's open scene or the benchmark.
            var scene=Application.isBatchMode?EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single)
                :UnityEngine.SceneManagement.SceneManager.CreateScene("PBDGrass Handoff");
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.212f,.227f,.259f);
            RenderSettings.ambientEquatorColor=new Color(.114f,.125f,.133f);
            RenderSettings.ambientGroundColor=new Color(.047f,.043f,.035f);
            var camera=new GameObject("Main Camera").AddComponent<Camera>(); camera.tag="MainCamera";
            camera.transform.position=new Vector3(-5.76f,2.5f,-2.5f); camera.transform.LookAt(new Vector3(0,.6f,0));
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.black;
            camera.nearClipPlane=.05f; camera.farClipPlane=80; camera.fieldOfView=65;
            var free=camera.gameObject.AddComponent<FreeCam>(); free.movementSpeed=2; free.fastMovementSpeed=6; free.enabled=false;
            var sun=new GameObject("Sun").AddComponent<Light>(); sun.type=LightType.Directional;
            sun.transform.rotation=Quaternion.Euler(60,0,0); sun.intensity=1; RenderSettings.sun=sun;
            var contact=GameObject.CreatePrimitive(PrimitiveType.Sphere); contact.name="Contact (C to toggle)";
            Object.DestroyImmediate(contact.GetComponent<Collider>()); contact.transform.localScale=Vector3.one*.76f;
            contact.transform.position=new Vector3(-4.7f,.6f,-2);
            var shape=contact.AddComponent<GrassInteractionCollider>(); shape.Type=GrassInteractionCollider.Shape.Sphere;
            shape.Radius=.5f; // local scale .76 -> world radius .38 meters, matching the visible sphere.
            var demo=new GameObject("XPBD Bezier Handoff - 2048 blades").AddComponent<GrassHandoffDemo>();
            demo.Cam=camera; demo.Interaction=contact.transform;
            demo.Solver=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/PBDGrass/Shaders/PBDSolverCS.compute");
            demo.HandoffShader=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/PBDGrass/Shaders/GrassHandoff.compute");
            demo.GrassMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/PBDGrass/Materials/PBDGrassProcedualMaterial.mat");
            demo.WindNoise=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/PBDGrass/Textures/Wind.png");
            Selection.activeGameObject=demo.gameObject;
            EditorSceneManager.SaveScene(scene,"Assets/PBDGrass/Scenes/PBDGrassHandoff.unity");
            if(Application.isBatchMode)EditorApplication.Exit(0);
        }
    }
}
