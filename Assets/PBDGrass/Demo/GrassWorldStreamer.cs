using UnityEngine;

namespace GrassFinal.PBDGrass
{
    // Camera is the streaming and simulation focus in this demo. Scene cameras
    // never move this focus; rotating the main camera alone does not stream tiles.
    [DefaultExecutionOrder(-150)]
    public sealed class GrassWorldStreamer : MonoBehaviour
    {
        public GrassHandoffDemo Demo;
        public Transform Ground;
        public float Speed=10,FastSpeed=30;
        public int OuterTilesLoaded { get; private set; }
        public int OuterTilesUnloaded { get; private set; }
        public long TotalOuterLoads { get; private set; }
        private bool auto,started;
        private Vector3 travelStart;
        private float travelTime;
        private Vector2Int anchor;
        private int version=-1;
        private void Update()
        {
            if(Demo==null || Demo.Cam==null)return;
            var camera=Demo.Cam.transform;
            if(Demo.AutoTour)
            {
                if(!auto) {travelStart=camera.position;travelTime=0;}
                travelTime+=Time.deltaTime;var p=travelStart+new Vector3(travelTime*6,0,Mathf.Sin(travelTime*.22f)*24);p.y=camera.position.y;camera.position=p;
            }
            else
            {
                Vector3 forward=Vector3.ProjectOnPlane(camera.forward,Vector3.up).normalized;
                if(forward.sqrMagnitude<.01f)forward=Vector3.forward;
                Vector3 right=Vector3.Cross(Vector3.up,forward);
                Vector3 move=forward*Input.GetAxisRaw("Vertical")+right*Input.GetAxisRaw("Horizontal");
                camera.position+=Vector3.ClampMagnitude(move,1)*(Input.GetKey(KeyCode.LeftShift)?FastSpeed:Speed)*Time.deltaTime;
                if(!Demo.OverheadView && Input.GetMouseButton(1))
                {
                    Vector3 e=camera.eulerAngles;float pitch=e.x>180?e.x-360:e.x;
                    camera.rotation=Quaternion.Euler(Mathf.Clamp(pitch-Input.GetAxis("Mouse Y")*2,-60,80),e.y+Input.GetAxis("Mouse X")*2,0);
                }
            }
            auto=Demo.AutoTour;
        }
        private void LateUpdate() => RefreshStatistics();
        public void RefreshStatistics()
        {
            if(Demo==null || Demo.WorldLayout==null)return;
            var layout=Demo.WorldLayout;
            if(Ground!=null)Ground.position=layout.Center-Vector3.up*.025f;
            if(version==layout.Version && started)return;
            var next=new Vector2Int((int)layout.Center.x/8,(int)layout.Center.z/8);
            int side=Mathf.CeilToInt(Demo.OuterCoverage.FieldSize/16f)*2;
            OuterTilesLoaded=!started?side*side:side*side-Mathf.Max(0,side-Mathf.Abs(next.x-anchor.x))*Mathf.Max(0,side-Mathf.Abs(next.y-anchor.y));
            OuterTilesUnloaded=started?OuterTilesLoaded:0;TotalOuterLoads+=OuterTilesLoaded;anchor=next;version=layout.Version;started=true;
        }
        private void OnGUI()
        {
            if(Demo==null || Demo.WorldLayout==null)return;
            var w=Demo.WorldLayout;GUI.Box(new Rect(12,212,850,76),GUIContent.none);
            GUI.Label(new Rect(24,216,830,24),"WASD: move world camera. Shift: fast. RMB: look. T: travel. V: overhead. B: tier colors.");
            GUI.Label(new Rect(24,240,830,24),"World window "+w.Center+"  shift #"+w.Version+"  core cells loaded: "+w.LastLoadedCells);
            GUI.Label(new Rect(24,264,830,24),"Outer logical tiles entered / exited: "+OuterTilesLoaded+" / "+OuterTilesUnloaded+". Unloaded lodging is discarded.");
        }
    }
}
