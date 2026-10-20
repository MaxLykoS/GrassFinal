using System;
using UnityEngine;

namespace GrassFinal.PBDGrass
{
    [ExecuteAlways]
    public sealed class GrassResponseDemo : MonoBehaviour
    {
        public Camera Cam;
        public ComputeShader Solver,ResponseShader;
        public Material GrassMaterial;
        public GrassResponseType[] Types;
        public bool EnableWind=true,RandomWindOnly;
        public int SelectedType=5;
        public PBDGrassPatchRenderer[] References { get; private set; }
        public GrassResponseProxy Proxy { get; private set; }
        public float Elapsed { get; private set; }
        public Vector3 CurrentWind { get; private set; }
        private Texture2D uniformWind;
        private double accumulator;
        private bool rebuild;
        private bool Ready => Cam!=null && Solver!=null && ResponseShader!=null && GrassMaterial!=null && Types!=null && Types.Length==8;
        public Vector3 Center(int type) => transform.position+new Vector3((type%4-1.5f)*4,0,(type/4)*5);
        public static Quaternion LeafYaw
        {
            get { float value=Mathf.Sin(67)*43758.5453f; return Quaternion.AngleAxis((value-Mathf.Floor(value))*360,Vector3.up); }
        }
        public Vector3 ReferenceRoot(int type) => Center(type)-(LeafYaw*Vector3.right)*.75f;
        public Vector3 ProxyRoot(int type) => Center(type)+(LeafYaw*Vector3.right)*.75f;
        private void OnEnable() { Camera.onPreCull-=Draw; Camera.onPreCull+=Draw; }
        private void OnValidate() => rebuild=true;
        public void Initialize()
        {
            if(References!=null || !Ready)return;
            foreach(var type in Types)if(type==null || !type.IsBaked)return;
            try
            {
                uniformWind=GrassResponseType.CreateUniformWindTexture(); References=new PBDGrassPatchRenderer[8]; var roots=new Vector3[8];
                // Explicit probe also keeps offscreen validation and the interactive scene equally lit.
                var ambient=new UnityEngine.Rendering.SphericalHarmonicsL2(); ambient.AddAmbientLight(new Color(.23f,.27f,.20f));
                RenderSettings.ambientProbe=ambient;
                for(int t=0;t<8;t++)
                {
                    var patch=GrassResponseType.CreatePatch(Types[t],ReferenceRoot(t));
                    try
                    {
                        References[t]=new PBDGrassPatchRenderer(patch,Solver,GrassMaterial,Cam,uniformWind,Types[t].Physics,null);
                        var pose=Types[t].Samples[Types[t].ZeroIndex]; References[t].InitializeFromPose(pose.Nodes,pose.Frames);
                        References[t].SetWind(Vector3.zero,0,Vector4.zero); roots[t]=ProxyRoot(t);
                    }
                    finally { GrassDemo.DestroyMesh(patch.PatchMesh); }
                }
                Proxy=new GrassResponseProxy(Types,roots,ResponseShader,GrassMaterial,Cam);
            }
            catch { Release(); throw; }
        }
        public static Vector3 WindAt(float time,bool randomOnly=false)
        {
            float phase=time%40;
            if(!randomOnly)
            {
                if(phase<3)return Vector3.zero;
                if(phase<9)return new Vector3(0,0,6*Mathf.SmoothStep(0,1,(phase-3)*2));
                if(phase<14)return Vector3.zero;
                if(phase<20)return new Vector3(0,0,-6*Mathf.SmoothStep(0,1,(phase-14)*2));
            }
            float u=time/1.8f; int key=Mathf.FloorToInt(u); float f=Mathf.SmoothStep(0,1,u-key);
            return Vector3.Lerp(RandomKnot(key),RandomKnot(key+1),f);
        }
        private static Vector3 RandomKnot(int key)
        {
            float a=Mathf.Repeat(Mathf.Sin(key*12.9898f+31.7f)*43758.5453f,1)*Mathf.PI*2;
            float magnitude=2+4*Mathf.Repeat(Mathf.Sin(key*7.312f+18.2f)*17423.127f,1);
            return new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*magnitude;
        }
        public void Step(Vector3 wind,float tick=1f/60)
        {
            Initialize(); if(Proxy==null)return; CurrentWind=wind;
            foreach(var renderer in References) { renderer.SetWind(wind,0,Vector4.zero); renderer.Advance(tick); }
            Proxy.Advance(wind,tick); Elapsed+=tick;
        }
        private void Update()
        {
            bool playing=Application.IsPlaying(gameObject);
            if(rebuild && !playing)Release(); rebuild=false;
            Initialize(); if(Proxy==null || !playing)return;
            for(int key=0;key<=8;key++)if(Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha0+key)))SetView(key-1);
            if(Input.GetKeyDown(KeyCode.Space))EnableWind=!EnableWind;
            if(Input.GetKeyDown(KeyCode.G))RandomWindOnly=!RandomWindOnly;
            if(Input.GetKeyDown(KeyCode.R))ResetAll();
            accumulator=Math.Min(accumulator+Time.deltaTime,4.0/60);
            while(accumulator+1e-8>=1.0/60)
            { Step(EnableWind?WindAt(Elapsed+1f/60,RandomWindOnly):Vector3.zero); accumulator-=1.0/60; }
        }
        public void SetView(int type)
        {
            if(Cam==null)return; SelectedType=type;
            Vector3 target=type>=0?Center(Mathf.Clamp(type,0,7))+Vector3.up*.65f:transform.position+new Vector3(0,.4f,2.5f);
            Cam.transform.position=type>=0?target+LeafYaw*new Vector3(1,1.6f,-4.1f):target+new Vector3(3,8,-12);
            Cam.transform.LookAt(target); Cam.fieldOfView=type>=0?44:53;
            // Parallel projection makes identical left/right curves appear identical too; perspective
            // otherwise changes their apparent lean simply because their roots are separated.
            Cam.orthographic=type>=0; Cam.orthographicSize=1.4f;
        }
        [ContextMenu("Reset Response Comparison")]
        public void ResetAll()
        {
            if(References==null)return;
            for(int t=0;t<8;t++)
            { var pose=Types[t].Samples[Types[t].ZeroIndex]; References[t].InitializeFromPose(pose.Nodes,pose.Frames); }
            Proxy.Reset(); Elapsed=0; accumulator=0; CurrentWind=Vector3.zero;
        }
        private void Draw(Camera target)
        {
            if(!isActiveAndEnabled || target==null || !Ready || (target!=Cam && target.cameraType!=CameraType.SceneView))return;
            Initialize(); if(Proxy==null)return;
            foreach(var renderer in References)renderer.Draw(target); Proxy.Draw(target);
        }
        public void DrawForValidation(Camera target)
        { foreach(var renderer in References)renderer.Draw(target); Proxy.Draw(target); }
        private void OnGUI()
        {
            if(Proxy==null || !Application.IsPlaying(gameObject))return;
            GUI.Box(new Rect(12,12,690,108),GUIContent.none);
            string title=SelectedType<0?"Eight grass types":(SelectedType+1)+"  "+Types[SelectedType].DisplayName;
            GUI.Label(new Rect(24,20,665,24),title+"     LEFT: XPBD     RIGHT: baked Bezier response");
            GUI.Label(new Rect(24,44,665,22),"1 - 8: focus type    0: overview    Space: wind on/off    G: random wind    R: reset");
            GUI.Label(new Rect(24,68,665,22),"Wind acceleration: "+CurrentWind.ToString("F1")+"     "+Elapsed.ToString("F1")+" seconds");
            GUI.Label(new Rect(24,92,665,22),"Same material and wind. Both start from the baked gravity equilibrium.");
            if(SelectedType<0)
            {
                for(int t=0;t<8;t++)
                {
                    Vector3 screen=Cam.WorldToScreenPoint(Center(t)+Vector3.up*1.9f);
                    if(screen.z>0)GUI.Label(new Rect(screen.x-130,Screen.height-screen.y,300,38),(t+1)+" "+Types[t].DisplayName+"\nXPBD     |     BEZIER");
                }
            }
        }
        private void Release()
        {
            if(References!=null)foreach(var renderer in References)renderer?.Dispose(); References=null;
            Proxy?.Dispose(); Proxy=null;
            if(uniformWind!=null) { if(Application.isPlaying)Destroy(uniformWind); else DestroyImmediate(uniformWind); uniformWind=null; }
        }
        private void OnDisable() { Camera.onPreCull-=Draw; Release(); }
        private void OnDestroy() { Camera.onPreCull-=Draw; Release(); }
    }
}
