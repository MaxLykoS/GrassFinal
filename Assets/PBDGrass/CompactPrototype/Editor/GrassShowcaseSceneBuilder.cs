using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace GrassFinal.PBDGrass.Editor
{
    public static class GrassShowcaseSceneBuilder
    {
        public const string Root="Assets/PBDGrass/CompactPrototype/Showcase";
        public const string ScenePath="Assets/PBDGrass/Scenes/PBDGrassContactLab.unity";
        [MenuItem("Tools/PBD Grass/Create Contact Lab Scene")]
        public static void Create()
        {
            if(!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            Build();if(Application.isBatchMode)EditorApplication.Exit(0);
        }
        public static GrassShowcaseController Build()
        {
            EditorSceneManager.OpenScene(GrassCompactSceneBuilder.ScenePath);
            var demo=Object.FindObjectOfType<GrassHandoffDemo>();demo.enabled=false;
            demo.name="Grass Contact Lab - contact XPBD, 32768 capacity";
            if(demo.Interaction!=null)Object.DestroyImmediate(demo.Interaction.gameObject);
            foreach(var tr in demo.AdditionalInteractions)if(tr!=null)Object.DestroyImmediate(tr.gameObject);
            demo.Interaction=null;demo.AdditionalInteractions.Clear();
            var streamer=demo.GetComponent<GrassWorldStreamer>();if(streamer!=null)Object.DestroyImmediate(streamer);
            foreach(var free in Object.FindObjectsOfType<FreeCam>())Object.DestroyImmediate(free);
            demo.SimulationFocus=null;demo.AutoTour=false;demo.ContactEnabled=true;
            demo.OverheadView=false;demo.Cam.orthographic=false;demo.Cam.fieldOfView=58;
            demo.PatchSize=144;
            demo.Physics.PromoteInteractions=true;demo.Physics.InteractionsAcrossMiddle=true;
            demo.Physics.ContactDrivenSimulation=true;demo.Physics.ContactSettleSeconds=2;demo.Physics.ContactFadeSeconds=1;
            demo.OuterCoverage.MiddleFullDistance=52;demo.OuterCoverage.MiddleOuterDistance=64;
            demo.Handoff.DebugLodBaseColors=true;demo.RandomWind=true;demo.Wind=new Vector3(4,0,2);
            var controller=demo.gameObject.AddComponent<GrassShowcaseController>();controller.Demo=demo;
            var parent=new GameObject("Contact stations").transform;
            var actors=new List<GrassShowcaseController.Actor>();var labels=new List<Transform>();
            var amber=Material("Amber",new Color(.95f,.43f,.10f));
            var blue=Material("Blue",new Color(.12f,.55f,.95f));
            var coral=Material("Coral",new Color(.92f,.20f,.23f));
            var silver=Material("Silver",new Color(.57f,.66f,.72f));
            Add(actors,labels,parent,demo,"01 SWEEP",new Vector3(0,.50f,-5),.55f,0,Quaternion.identity,GrassShowcaseController.Motion.Sweep,Vector3.right,4.5f,10,0,amber);
            Add(actors,labels,parent,demo,"02 CIRCLE",new Vector3(-7,.60f,-2),.75f,0,Quaternion.identity,GrassShowcaseController.Motion.Circle,Vector3.right,1.8f,12,0,amber);
            Add(actors,labels,parent,demo,"03 WALKER",new Vector3(7,1,-2),.38f,.65f,Quaternion.identity,GrassShowcaseController.Motion.Sweep,Vector3.forward,3,8,0,blue);
            Add(actors,labels,parent,demo,"04 ROLLER",new Vector3(-2,.48f,3),.48f,1.5f,Quaternion.Euler(0,0,90),GrassShowcaseController.Motion.Sweep,Vector3.forward,2,12,0,blue);
            Add(actors,labels,parent,demo,"05 PRESS S",new Vector3(-8,.30f,7),.45f,0,Quaternion.identity,GrassShowcaseController.Motion.Stamp,Vector3.zero,0,9,0,coral);
            Add(actors,labels,parent,demo,"06 PRESS M",new Vector3(-4,.45f,7),.70f,0,Quaternion.identity,GrassShowcaseController.Motion.Stamp,Vector3.zero,0,11,1.5f,coral);
            Add(actors,labels,parent,demo,"07 PRESS L",new Vector3(0,.60f,7),.95f,0,Quaternion.identity,GrassShowcaseController.Motion.Stamp,Vector3.zero,0,13,3,coral);
            Add(actors,labels,parent,demo,"08 STATIC",new Vector3(7,.75f,6),1,0,Quaternion.identity,GrassShowcaseController.Motion.Fixed,Vector3.zero,0,10,0,silver);
            Add(actors,labels,parent,demo,"09 TIP BRUSH",new Vector3(-36,1.55f,14),.4f,0,Quaternion.identity,GrassShowcaseController.Motion.Sweep,Vector3.forward,1.5f,9,0,amber);
            Add(actors,labels,parent,demo,"10 CROSS A",new Vector3(-2,.7f,-1),.32f,.45f,Quaternion.identity,GrassShowcaseController.Motion.Sweep,Vector3.right,3,7,0,blue);
            Add(actors,labels,parent,demo,"11 CROSS B",new Vector3(2,.7f,.4f),.32f,.45f,Quaternion.identity,GrassShowcaseController.Motion.Sweep,Vector3.right,3,8,2,blue);
            Add(actors,labels,parent,demo,"12 POST",new Vector3(38,.9f,20),.42f,.5f,Quaternion.identity,GrassShowcaseController.Motion.Fixed,Vector3.zero,0,10,0,silver);
            controller.Actors=actors.ToArray();controller.Labels=labels.ToArray();
            controller.SetView(1);controller.ApplyActors(0,true);
            foreach(var label in labels)label.rotation=demo.Cam.transform.rotation;
            demo.enabled=true;
            EditorSceneManager.SaveScene(demo.gameObject.scene,ScenePath);AssetDatabase.SaveAssets();
            return controller;
        }
        private static Material Material(string name,Color color)
        {
            string path=Root+"/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null) {material=new Material(Shader.Find("Standard"));AssetDatabase.CreateAsset(material,path);}
            material.color=color;material.SetFloat("_Metallic",.18f);material.SetFloat("_Glossiness",.45f);
            EditorUtility.SetDirty(material);return material;
        }
        private static void Add(List<GrassShowcaseController.Actor> actors,List<Transform> labels,Transform parent,GrassHandoffDemo demo,
            string name,Vector3 origin,float radius,float half,Quaternion rotation,GrassShowcaseController.Motion motion,Vector3 axis,float travel,float period,float phase,Material material)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.SetPositionAndRotation(origin,rotation);
            var shape=go.AddComponent<GrassInteractionCollider>();shape.Type=half>0?GrassInteractionCollider.Shape.Capsule:GrassInteractionCollider.Shape.Sphere;
            shape.Radius=radius;shape.HalfSegment=half;shape.Friction=.45f;
            var visuals=new List<Renderer>();
            if(half>0)
            {
                visuals.Add(Visual(go.transform,PrimitiveType.Cylinder,Vector3.zero,new Vector3(radius*2,half,radius*2),material));
                visuals.Add(Visual(go.transform,PrimitiveType.Sphere,Vector3.up*half,Vector3.one*(radius*2),material));
                visuals.Add(Visual(go.transform,PrimitiveType.Sphere,Vector3.down*half,Vector3.one*(radius*2),material));
            }
            else visuals.Add(Visual(go.transform,PrimitiveType.Sphere,Vector3.zero,Vector3.one*(radius*2),material));
            var label=new GameObject(name+" label").AddComponent<TextMesh>();label.transform.SetParent(parent,false);
            label.transform.position=new Vector3(origin.x,2.3f,origin.z+1.2f);
            label.text=name;label.fontSize=64;label.characterSize=.08f;label.anchor=TextAnchor.MiddleCenter;label.color=new Color(1,.96f,.78f);
            labels.Add(label.transform);
            actors.Add(new GrassShowcaseController.Actor {Collider=shape,Origin=origin,Axis=axis,Movement=motion,Travel=travel,Period=period,Phase=phase,Visuals=visuals.ToArray()});
            demo.AdditionalInteractions.Add(go.transform);
        }
        private static Renderer Visual(Transform parent,PrimitiveType type,Vector3 position,Vector3 scale,Material material)
        {
            var go=GameObject.CreatePrimitive(type);go.name="Visible contact shape";Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=scale;
            var renderer=go.GetComponent<Renderer>();renderer.sharedMaterial=material;return renderer;
        }
    }
}
