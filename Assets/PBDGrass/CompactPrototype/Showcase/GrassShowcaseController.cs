using System;
using UnityEngine;

namespace GrassFinal.PBDGrass
{
    // Scene-only choreography. The accepted renderer, solver and baked assets are reused.
    [DefaultExecutionOrder(-200)]
    public sealed class GrassShowcaseController : MonoBehaviour
    {
        public enum Motion { Sweep, Circle, Stamp, Fixed }
        [Serializable] public sealed class Actor
        {
            public GrassInteractionCollider Collider;
            public Vector3 Origin,Axis=Vector3.right;
            public Motion Movement;
            public float Travel=2,Period=10,Phase;
            public Renderer[] Visuals;
        }
        public GrassHandoffDemo Demo;
        public Actor[] Actors=Array.Empty<Actor>();
        public Transform[] Labels=Array.Empty<Transform>();
        public bool Paused;
        public bool ShowLabels=true;
        public float MotionTime { get; private set; }
        private float lastSimulationTime,tourTime;
        private bool touring;
        private Vector3 tourPosition;
        private Quaternion tourRotation;
        private bool tourOrthographic;

        private void Update()
        {
            if(Demo==null || Demo.Cam==null)return;
            float now=Demo.Renderer!=null?Demo.Renderer.SimulationTime:0;
            float dt=Mathf.Clamp(now-lastSimulationTime,0,.15f);lastSimulationTime=now;
            if(Input.GetKeyDown(KeyCode.P))Paused=!Paused;
            if(Input.GetKeyDown(KeyCode.H))ShowLabels=!ShowLabels;
            if(Input.GetKeyDown(KeyCode.R))MotionTime=0;
            if(Input.GetKeyDown(KeyCode.Alpha1))SetView(1);
            if(Input.GetKeyDown(KeyCode.Alpha2))SetView(2);
            if(Input.GetKeyDown(KeyCode.Alpha3))SetView(3);
            if(Input.GetKeyDown(KeyCode.Alpha4))SetView(4);
            if(!Paused && !Demo.AutoTour)MotionTime+=dt;
            ApplyActors(MotionTime,Demo.ContactEnabled && !Demo.AutoTour);
            UpdateCamera(dt);
        }
        public void ApplyActors(float time,bool contact)
        {
            foreach(var a in Actors)
            {
                if(a.Collider==null)continue;
                float cycle=(time+a.Phase)/Mathf.Max(1,a.Period);
                float angle=cycle*Mathf.PI*2;
                Vector3 p=a.Origin;
                switch(a.Movement)
                {
                    case Motion.Sweep:p+=a.Axis.normalized*(Mathf.Sin(angle)*a.Travel);break;
                    case Motion.Circle:p+=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*a.Travel;break;
                    case Motion.Stamp:
                        // Start raised, descend smoothly, hold pressure, lift, then leave
                        // time to observe elastic recovery and the remaining plastic bend.
                        float phase=Mathf.Repeat(cycle,1),lift;
                        if(phase<.18f)lift=1-Mathf.SmoothStep(0,1,phase/.18f);
                        else if(phase<.40f)lift=0;
                        else if(phase<.58f)lift=Mathf.SmoothStep(0,1,(phase-.40f)/.18f);
                        else lift=1;
                        p+=Vector3.up*(lift*3);break;
                }
                a.Collider.transform.position=p;
                a.Collider.enabled=contact;
                foreach(var visual in a.Visuals)if(visual!=null)visual.enabled=contact;
            }
        }
        public void SetView(int view)
        {
            if(Demo==null || Demo.Cam==null)return;
            Demo.AutoTour=false;touring=false;Demo.OverheadView=false;Demo.ConfigureViewCamera();
            var cam=Demo.Cam;cam.orthographic=view==3 || view==4;
            if(view==4)
            {
                cam.orthographicSize=Demo.OuterCoverage.MiddleOuterDistance+8;
                cam.transform.SetPositionAndRotation(new Vector3(0,100,0),Quaternion.Euler(90,0,0));
            }
            else if(view==3)
            {
                cam.orthographicSize=18;
                cam.transform.SetPositionAndRotation(new Vector3(0,32,0),Quaternion.Euler(90,0,0));
            }
            else
            {
                cam.transform.position=view==2?new Vector3(0,3,-12):new Vector3(0,16,-14);
                cam.transform.LookAt(new Vector3(0,.6f,1));
            }
        }
        private void UpdateCamera(float dt)
        {
            var cam=Demo.Cam;
            if(Demo.AutoTour)
            {
                if(!touring)
                {
                    tourPosition=cam.transform.position;tourRotation=cam.transform.rotation;
                    tourOrthographic=cam.orthographic;tourTime=0;touring=true;
                    Demo.OverheadView=false;Demo.ConfigureViewCamera();cam.orthographic=false;
                }
                tourTime+=dt;float phase=Mathf.Repeat(tourTime,24),distance;
                if(phase<8)distance=Mathf.SmoothStep(0,36,phase/8);
                else if(phase<12)distance=36;
                else if(phase<20)distance=Mathf.SmoothStep(36,0,(phase-12)/8);
                else distance=0;
                cam.transform.position=tourPosition+Vector3.right*distance;
                cam.transform.LookAt(new Vector3(0,.6f,1));
                return;
            }
            if(touring)
            {
                cam.transform.SetPositionAndRotation(tourPosition,tourRotation);cam.orthographic=tourOrthographic;touring=false;
            }
            if(Demo.OverheadView)return;
            Vector3 forward=Vector3.ProjectOnPlane(cam.transform.forward,Vector3.up).normalized;
            if(forward.sqrMagnitude<.01f)forward=Vector3.forward;
            Vector3 move=forward*Input.GetAxisRaw("Vertical")+Vector3.Cross(Vector3.up,forward)*Input.GetAxisRaw("Horizontal");
            if(Input.GetKey(KeyCode.E))move+=Vector3.up;
            if(Input.GetKey(KeyCode.Q))move-=Vector3.up;
            cam.transform.position+=Vector3.ClampMagnitude(move,1)*(Input.GetKey(KeyCode.LeftShift)?20:7)*Time.deltaTime;
            var position=cam.transform.position;position.y=Mathf.Max(1.8f,position.y);cam.transform.position=position;
            if(Input.GetMouseButton(1))
            {
                cam.orthographic=false;
                var e=cam.transform.eulerAngles;float pitch=e.x>180?e.x-360:e.x;
                cam.transform.rotation=Quaternion.Euler(Mathf.Clamp(pitch-Input.GetAxis("Mouse Y")*2,-60,85),e.y+Input.GetAxis("Mouse X")*2,0);
            }
        }
        private void LateUpdate()
        {
            if(Demo==null || Demo.Cam==null)return;
            foreach(var label in Labels)if(label!=null)
            {
                label.gameObject.SetActive(ShowLabels);
                label.rotation=Demo.Cam.transform.rotation;
            }
        }
        private void OnGUI()
        {
            if(Demo==null || Demo.Renderer==null)return;
            GUI.Box(new Rect(12,Screen.height-112,Mathf.Min(1020,Screen.width-24),100),GUIContent.none);
            GUI.Label(new Rect(24,Screen.height-108,990,22),"GRASS CONTACT LAB   12 colliders   1: elevated   2: close   3: top   4: tier overview   WASD / Q E: move / height   RMB: look");
            var outer=Demo.OuterCoverage;
            var pool=Demo.Renderer.Pool;
            GUI.Label(new Rect(24,Screen.height-84,990,22),"XPBD "+pool.ActiveBlades+" / "+Demo.Physics.MaxActiveBlades+"   contact "+pool.InteractionBlades+"   settling "+pool.SettlingBlades+"   fading "+pool.FadingBlades+"   GREEN: XPBD   BLUE: Bezier   ORANGE: outer");
            GUI.Label(new Rect(24,Screen.height-60,930,22),"P: pause actors ["+(Paused?"PAUSED":"RUNNING")+"]   C: remove / restore colliders   R: reset grass + motion   Space: wind   G: gusts   B: LOD colors");
            GUI.Label(new Rect(24,Screen.height-36,930,22),"T: 24s LOD round trip ["+(Demo.AutoTour?"ON - contact suspended, tracks preserved":"OFF")+"]   V: whole field   H: labels   Raised stamps reveal rebound and lodging.");
        }
    }
}
