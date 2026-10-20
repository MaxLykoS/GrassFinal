using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace GrassFinal.PBDGrass.Editor
{
    public static class BakedGrassHandoffSceneBuilder
    {
        public const string ScenePath="Assets/PBDGrass/Scenes/PBDGrassBakedHandoff.unity";
        [MenuItem("Tools/PBD Grass/Create Baked Wind Plastic Handoff Scene")]
        public static void Create()
        {
            Build();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),ScenePath);
            if(Application.isBatchMode)EditorApplication.Exit(0);
        }
        internal static GrassHandoffDemo Build()
        {
            var scene=Application.isBatchMode?EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single)
                :UnityEngine.SceneManagement.SceneManager.CreateScene("Baked wind plastic grass handoff");
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.24f,.28f,.31f); RenderSettings.ambientEquatorColor=new Color(.13f,.16f,.12f);
            RenderSettings.ambientGroundColor=new Color(.06f,.07f,.04f);
            var sh=new SphericalHarmonicsL2(); sh.AddAmbientLight(new Color(.23f,.27f,.20f)); RenderSettings.ambientProbe=sh;
            var sun=new GameObject("Sun").AddComponent<Light>(); sun.type=LightType.Directional;
            sun.transform.rotation=Quaternion.Euler(55,-25,0); sun.intensity=1; RenderSettings.sun=sun;
            var camera=new GameObject("Main Camera").AddComponent<Camera>(); camera.tag="MainCamera";
            camera.transform.position=new Vector3(-5.76f,2.5f,-2.5f); camera.transform.LookAt(Vector3.up*.6f);
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.035f,.045f,.06f);
            camera.nearClipPlane=.04f; camera.farClipPlane=80; camera.fieldOfView=65;
            var free=camera.gameObject.AddComponent<FreeCam>(); free.movementSpeed=2; free.fastMovementSpeed=6; free.enabled=false;
            var contact=GameObject.CreatePrimitive(PrimitiveType.Sphere); contact.name="Pressure sphere (C)";
            Object.DestroyImmediate(contact.GetComponent<Collider>()); contact.transform.localScale=Vector3.one*.76f;
            contact.transform.position=new Vector3(-4.7f,.6f,-2);
            var shape=contact.AddComponent<GrassInteractionCollider>(); shape.Type=GrassInteractionCollider.Shape.Sphere; shape.Radius=.5f;
            var demo=new GameObject("Baked wind + plastic lodging - 2048 blades").AddComponent<GrassHandoffDemo>();
            var type=AssetDatabase.LoadAssetAtPath<GrassResponseType>(GrassResponseBaker.TypePath(5));
            demo.Cam=camera; demo.Interaction=contact.transform; demo.Handoff.BakedResponse=type;
            demo.Physics=JsonUtility.FromJson<XPBDGrassSettings>(JsonUtility.ToJson(type.Physics));
            demo.Physics.EnablePlasticity=true; demo.Physics.MaxActiveBlades=1024;
            demo.Physics.NearDistance=2.5f; demo.Physics.PoolHysteresis=.35f; demo.Physics.ColliderRadius=.38f;
            demo.Shape=JsonUtility.FromJson<GrassBladeShapeSettings>(JsonUtility.ToJson(type.Shape));
            demo.Wind=new Vector3(4,0,2); demo.RandomWind=true;
            demo.Solver=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/PBDGrass/Shaders/PBDSolverCS.compute");
            demo.HandoffShader=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/PBDGrass/Shaders/GrassHandoff.compute");
            demo.GrassMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/PBDGrass/Materials/PBDGrassProcedualMaterial.mat");
            demo.WindNoise=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/PBDGrass/Textures/Wind.png");
            Selection.activeGameObject=demo.gameObject;
            return demo;
        }
    }
}
