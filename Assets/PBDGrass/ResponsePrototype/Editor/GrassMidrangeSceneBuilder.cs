using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace GrassFinal.PBDGrass.Editor
{
    public static class GrassMidrangeSceneBuilder
    {
        public const string ScenePath="Assets/PBDGrass/Scenes/PBDGrassMidrange.unity";
        [MenuItem("Tools/PBD Grass/Create Middle Range Experiment")]
        public static void Create()
        {
            EditorSceneManager.OpenScene(GrassStreamingSceneBuilder.ScenePath);
            var demo=Object.FindObjectOfType<GrassHandoffDemo>();demo.name="Radial middle - stable handoff and local interaction";
            demo.PatchSize=96;demo.Physics.StablePoolSelection=true;demo.Physics.PromoteInteractions=true;
            demo.Physics.InteractionDistance=28;demo.Handoff.AdaptiveHistory=true;
            demo.OuterCoverage.RadialMiddle=true;demo.OuterCoverage.MiddleFullDistance=28;demo.OuterCoverage.MiddleOuterDistance=36;
            var ball=GameObject.CreatePrimitive(PrimitiveType.Sphere);ball.name="Middle interaction (I toggles contact)";
            Object.DestroyImmediate(ball.GetComponent<Collider>());ball.transform.localScale=Vector3.one*.76f;
            ball.transform.position=demo.Cam.transform.position+Vector3.forward*24;ball.transform.position=new Vector3(ball.transform.position.x,.4f,ball.transform.position.z);
            var collider=ball.AddComponent<GrassInteractionCollider>();collider.Type=GrassInteractionCollider.Shape.Sphere;collider.Radius=.5f;
            var motion=ball.AddComponent<GrassMidrangeInteractor>();motion.Demo=demo;demo.AdditionalInteractions.Add(ball.transform);
            EditorSceneManager.SaveScene(demo.gameObject.scene,ScenePath);
            if(Application.isBatchMode)EditorApplication.Exit(0);
        }
    }
}
