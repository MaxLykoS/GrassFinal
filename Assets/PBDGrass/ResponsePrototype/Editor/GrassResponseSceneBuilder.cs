using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace GrassFinal.PBDGrass.Editor
{
    public static class GrassResponseSceneBuilder
    {
        [MenuItem("Tools/PBD Grass/Create Eight Type Comparison")]
        public static void Create()
        {
            var scene=Application.isBatchMode?EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single)
                :UnityEngine.SceneManagement.SceneManager.CreateScene("Eight type grass response");
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.27f,.30f,.34f);
            RenderSettings.ambientEquatorColor=new Color(.15f,.17f,.18f);
            RenderSettings.ambientGroundColor=new Color(.07f,.08f,.06f);
            var sun=new GameObject("Sun").AddComponent<Light>(); sun.type=LightType.Directional;
            sun.transform.rotation=Quaternion.Euler(50,-25,0); sun.intensity=1.1f; RenderSettings.sun=sun;
            var camera=new GameObject("Main Camera").AddComponent<Camera>(); camera.tag="MainCamera";
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.035f,.045f,.06f);
            camera.nearClipPlane=.04f; camera.farClipPlane=80;
            var free=camera.gameObject.AddComponent<FreeCam>(); free.movementSpeed=2; free.fastMovementSpeed=6;
            var demo=new GameObject("Eight Types - XPBD left - Baked Bezier right").AddComponent<GrassResponseDemo>();
            demo.Cam=camera;
            demo.Solver=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/PBDGrass/Shaders/PBDSolverCS.compute");
            demo.ResponseShader=AssetDatabase.LoadAssetAtPath<ComputeShader>(GrassResponseBaker.Root+"/GrassResponse.compute");
            demo.GrassMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/PBDGrass/Materials/PBDGrassProcedualMaterial.mat");
            demo.Types=new GrassResponseType[8];
            string materialPath=GrassResponseBaker.Root+"/ComparisonPlatform.mat";
            var platform=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if(platform==null)
            {
                platform=new Material(Shader.Find("Standard")) { color=new Color(.055f,.07f,.06f) };
                platform.SetFloat("_Glossiness",.15f); AssetDatabase.CreateAsset(platform,materialPath);
            }
            for(int t=0;t<8;t++)
            {
                demo.Types[t]=AssetDatabase.LoadAssetAtPath<GrassResponseType>(GrassResponseBaker.TypePath(t));
                var ground=GameObject.CreatePrimitive(PrimitiveType.Cube); ground.name=(t+1)+" "+demo.Types[t].DisplayName;
                Object.DestroyImmediate(ground.GetComponent<Collider>());
                ground.transform.position=demo.Center(t)+new Vector3(0,-.045f,.25f); ground.transform.localScale=new Vector3(3.4f,.08f,2.3f);
                ground.transform.rotation=GrassResponseDemo.LeafYaw;
                ground.GetComponent<MeshRenderer>().sharedMaterial=platform;
            }
            demo.SetView(5); AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene,GrassResponseBaker.Root+"/GrassResponseComparison.unity");
            Selection.activeGameObject=demo.gameObject;
            if(Application.isBatchMode)EditorApplication.Exit(0);
        }
    }
}
