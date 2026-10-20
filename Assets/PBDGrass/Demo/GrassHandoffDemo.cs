using System.Collections.Generic;
using UnityEngine;

namespace GrassFinal.PBDGrass
{
    [ExecuteAlways]
    public sealed class GrassHandoffDemo : MonoBehaviour
    {
        public Camera Cam;
        public ComputeShader Solver, HandoffShader;
        public Material GrassMaterial;
        public Texture2D WindNoise;
        public Transform Interaction;
        public List<Transform> AdditionalInteractions=new List<Transform>();
        [Min(2)] public int PatchSize=8;
        public bool AutoTour=true;
        [Tooltip("Automatic camera travel in meters from the field center; zero uses the original size-based tour.")]
        [Min(0)] public float AutoTourHalfSpan;
        [Tooltip("Optional ground focus, independent of the observing camera. Auto tour moves this object when assigned.")]
        public Transform SimulationFocus;
        public GrassOuterCoverage OuterCoverage;
        public bool StreamWorld;
        public ComputeShader StreamingShader;
        public GrassWorldLayout WorldLayout { get; private set; }
        [Tooltip("Show the entire field from above. V toggles the overhead view at runtime.")]
        public bool OverheadView;
        [Min(10)] public float OverheadHeight=70;
        [Min(0)] public float OverheadFieldSize;
        public bool ContactEnabled=true;
        public bool RandomWind;
        public Vector3 Wind=new Vector3(10,0,3);
        public GrassBladeShapeSettings Shape=new GrassBladeShapeSettings();
        public XPBDGrassSettings Physics=new XPBDGrassSettings
        { MaxActiveBlades=1024, NearDistance=2.5f, PoolHysteresis=.35f, MaxDistance=40 };
        public GrassHandoffSettings Handoff=new GrassHandoffSettings();
        public PBDGrassPatchRenderer Renderer { get; private set; }
        private bool rebuild;
        private float tourTime;
        private Vector3 savedWind;
        private bool appliedOverhead;
        private bool Ready => Cam!=null && Solver!=null && HandoffShader!=null && GrassMaterial!=null && WindNoise!=null;
        private void OnEnable() { Camera.onPreCull-=Draw; Camera.onPreCull+=Draw; }
        private void OnValidate() => rebuild=true;
        public void Initialize()
        {
            if(Renderer!=null || !Ready)return;
            var random=Random.state; PBDGrassPatch patch=null;
            try
            {
                Random.InitState(12345);
                if(StreamWorld)WorldLayout=new GrassWorldLayout(PatchSize,Focus(),transform.position.y);
                patch=StreamWorld?WorldLayout.CreatePatch(Handoff.BakedResponse):Handoff.BakedResponse!=null?GrassResponseType.CreateHandoffPatch(Handoff.BakedResponse,transform.position,Mathf.Max(2,PatchSize))
                    :new PBDGrassPatch(transform.position,Mathf.Max(2,PatchSize),32,Shape);
                var interactions=new List<Transform>();if(Interaction!=null)interactions.Add(Interaction);
                if(AdditionalInteractions!=null)interactions.AddRange(AdditionalInteractions);
                Renderer=new PBDGrassPatchRenderer(patch,Solver,GrassMaterial,Cam,WindNoise,Physics,
                    interactions,Handoff,HandoffShader,WorldLayout,StreamingShader);
                Renderer.SetWind(Wind,.1f,new Vector4(.01f,.01f,0,1));
                Renderer.SimulationCenter=Focus(); Renderer.RefreshSimulationSelection(); Renderer.ResetDeformation();
                savedWind=Wind;
            }
            finally { Random.state=random; if(patch!=null)GrassDemo.DestroyMesh(patch.PatchMesh); }
        }
        private Vector3 Focus() { var p=SimulationFocus!=null?SimulationFocus.position:Cam.transform.position; p.y=transform.position.y; return p; }
        public void ConfigureViewCamera()
        {
            if(Cam==null)return;
            if(OverheadView)
            {
                float extent=Mathf.Max(2,PatchSize,OverheadFieldSize)*.5f+4,height=Mathf.Max(10,OverheadHeight);
                Cam.orthographic=true; Cam.orthographicSize=extent/Mathf.Min(1,Mathf.Max(.01f,Cam.aspect));
                Vector3 center=StreamWorld?new Vector3(Cam.transform.position.x,transform.position.y,Cam.transform.position.z):transform.position;
                Cam.transform.SetPositionAndRotation(center+Vector3.up*height,Quaternion.Euler(90,0,0));
                Cam.farClipPlane=Mathf.Max(Cam.farClipPlane,height+extent+10);
                Cam.useOcclusionCulling=false;
                Physics.MaxDistance=Mathf.Max(Physics.MaxDistance,Mathf.Sqrt(height*height+2*extent*extent)+5);
            }
            else if(appliedOverhead)
            {
                Cam.orthographic=false;
                if(StreamWorld) { var p=Cam.transform.position;p.y=transform.position.y+2.5f;Cam.transform.SetPositionAndRotation(p,Quaternion.Euler(8,0,0)); }
                else { Cam.transform.position=TourPosition(tourTime);Cam.transform.LookAt(transform.position+Vector3.up*.6f); }
            }
            appliedOverhead=OverheadView;
        }
        private void Update()
        {
            bool playing=Application.IsPlaying(gameObject);
            if(rebuild && !playing) Release(); rebuild=false;
            if(!Ready)return;
            Initialize();
            if(playing)
            {
                if(Input.GetKeyDown(KeyCode.T))AutoTour=!AutoTour;
                if(Input.GetKeyDown(KeyCode.V))OverheadView=!OverheadView;
                if(Input.GetKeyDown(KeyCode.C))ContactEnabled=!ContactEnabled;
                if(Input.GetKeyDown(KeyCode.G))RandomWind=!RandomWind;
                if(Input.GetKeyDown(KeyCode.R))Renderer.ResetDeformation();
                if(Input.GetKeyDown(KeyCode.Space))
                { if(Wind.sqrMagnitude>0) { savedWind=Wind; Wind=Vector3.zero; } else Wind=savedWind; }
                var free=Cam.GetComponent<FreeCam>(); if(free!=null)free.enabled=!AutoTour && !OverheadView && !StreamWorld;
                if(AutoTour && !StreamWorld)
                {
                    tourTime+=Time.deltaTime;
                    if(SimulationFocus!=null) { var position=TourPosition(tourTime); position.y=transform.position.y; SimulationFocus.position=position; }
                    if(!OverheadView)
                    {
                        Cam.transform.position=TourPosition(tourTime);
                        Cam.transform.LookAt(transform.position+new Vector3(0,.6f,0));
                    }
                }
                if(Interaction!=null)
                {
                    var shape=Interaction.GetComponent<GrassInteractionCollider>(); if(shape!=null)shape.enabled=ContactEnabled;
                    var mesh=Interaction.GetComponent<MeshRenderer>(); if(mesh!=null)mesh.enabled=ContactEnabled;
                    if(ContactEnabled)
                    {
                        Vector3 forward=Vector3.ProjectOnPlane(Cam.transform.forward,Vector3.up).normalized;
                        if(forward.sqrMagnitude<.01f)forward=StreamWorld?Vector3.forward:Vector3.ProjectOnPlane(transform.position-Focus(),Vector3.up).normalized;
                        if(forward.sqrMagnitude<.01f)forward=Vector3.forward;
                        Interaction.position=Focus()+forward*1.1f+Vector3.up*.6f;
                    }
                }
            }
            ConfigureViewCamera();
            Vector3 currentWind=RandomWind && Wind.sqrMagnitude>0?GrassResponseDemo.WindAt(Renderer.SimulationTime,true)*(Wind.magnitude/6):Wind;
            Renderer.SetWind(currentWind,.1f,new Vector4(.01f,.01f,0,1)); Renderer.SimulationCenter=Focus();
            if(StreamWorld)Renderer.StreamTo(Focus());
            if(playing)Renderer.Advance(Time.deltaTime);
        }
        public Vector3 TourPosition(float time) => transform.position+new Vector3(
            -Mathf.Cos(time*Mathf.PI/12)*(AutoTourHalfSpan>0?AutoTourHalfSpan:PatchSize*.72f),2.5f,-2.5f);
        private void Draw(Camera target)
        {
            if(!isActiveAndEnabled || !Ready || (target!=Cam && target.cameraType!=CameraType.SceneView))return;
            ConfigureViewCamera(); Initialize(); Renderer.SimulationCenter=Focus();
            if(StreamWorld)Renderer.StreamTo(Focus());
            if(!Application.IsPlaying(gameObject))Renderer.RefreshSimulationSelection();
            if(OuterCoverage!=null && OuterCoverage.isActiveAndEnabled)OuterCoverage.PrepareVisible();
            Renderer.Draw(target);
        }
        private void OnGUI()
        {
            if(!Application.IsPlaying(gameObject) || Renderer==null)return;
            GUI.Box(new Rect(12,12,540,Handoff.UseTieredState?134:110),GUIContent.none);
            GUI.Label(new Rect(24,20,520,24),(StreamWorld?"Resident ":OuterCoverage!=null?"Core ":"Generated ")+Renderer.BladeCount+": "+Renderer.ActiveBladeCount+" XPBD + "+(Renderer.BladeCount-Renderer.ActiveBladeCount)+" Bezier");
            GUI.Label(new Rect(24,44,520,22),"V: overhead "+(OverheadView?"ON":"OFF")+"   T: tour "+(AutoTour?"ON":"OFF")+"   C: contact   Space: wind   R: reset");
            GUI.Label(new Rect(24,68,520,22),SimulationFocus!=null?
                "Simulation Focus drives XPBD; the camera observes independently.":
                "Manual: WASD + right mouse. Move away, then revisit the lodged trail.");
            GUI.Label(new Rect(24,92,520,22),"G: random wind "+(RandomWind?"ON":"OFF")+"   "+(Handoff.BakedResponse!=null?"Baked wind + plastic lodging":"Legacy curve"));
            if(Handoff.UseTieredState)
                GUI.Label(new Rect(24,116,520,22),"History: "+Renderer.Handoff.HistoryResidentBlades+" / "+Renderer.Handoff.HistoryCapacityBlades
                    +" blades   transitioning: "+Renderer.Handoff.TransitionBlades);
        }
        private void OnDrawGizmosSelected()
        {
            if(Cam==null)return; Gizmos.color=Color.cyan; Vector3 center=Focus()+Vector3.up*.02f;
            const int count=96;
            for(int i=0;i<count;i++)
            {
                float a=i*2*Mathf.PI/count,b=(i+1)*2*Mathf.PI/count;
                Gizmos.DrawLine(center+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*Physics.NearDistance,
                    center+new Vector3(Mathf.Cos(b),0,Mathf.Sin(b))*Physics.NearDistance);
            }
        }
        [ContextMenu("Reset Grass Deformation")] public void ResetDeformation() => Renderer?.ResetDeformation();
        private void Release() { Renderer?.Dispose(); Renderer=null; WorldLayout=null; }
        private void OnDisable() { Camera.onPreCull-=Draw; Release(); }
        private void OnDestroy() { Camera.onPreCull-=Draw; Release(); }
    }
}
