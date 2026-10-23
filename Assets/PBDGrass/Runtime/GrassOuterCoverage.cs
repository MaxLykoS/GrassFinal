using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace GrassFinal.PBDGrass
{
    // Separate, stateless outer representation. Only compact current-view instances
    // survive a dispatch; no collider, XPBD pool or lodging state is referenced.
    [ExecuteAlways]
    public sealed class GrassOuterCoverage : MonoBehaviour
    {
        public GrassHandoffDemo Demo;
        public ComputeShader CullShader;
        public Shader LeafShader;
        [Min(80)] public int FieldSize=256;
        [Range(1,8)] public float BlendWidth=4;
        [Tooltip("Use a radial compatible middle field with a presentation-only morph to the stateless outer shape.")]
        public bool RadialMiddle;
        [Min(1)] public float MiddleFullDistance=28;
        [Min(1)] public float MiddleOuterDistance=36;
        [Min(40)] public float SparseDistance=72;
        [Min(1)] public float DensityFadeDistance=20;
        [Range(1,4)] public float FarWidthScale=2;
        public int CandidateCount { get; private set; }
        public int TileCount { get; private set; }
        public long AllocatedGpuBytes => tiles==null?0:(long)tiles.count*tiles.stride+(long)instances.count*instances.stride+16+75*16;
        public ComputeBuffer DiagnosticInstances => instances;
        public ComputeBuffer DiagnosticArguments => arguments;
        [StructLayout(LayoutKind.Sequential)] private struct Tile
        { public Vector2 Origin; public int Count,Padding; }
        private ComputeShader compute;
        private Material material;
        private ComputeBuffer tiles,instances,arguments,controls;
        private GrassResponseType bakedResponse;
        private readonly Vector4[] planes=new Vector4[6];
        private Bounds bounds;
        private int kernel;
        private bool rebuild;
        private Vector3 origin;
        public Vector4 CoverageTransition
        {
            get
            {
                Vector3 p=Demo.StreamWorld?Demo.Cam.transform.position:Demo.transform.position;
                // The 8m resident guard band hides page turnover. Visual LOD follows
                // the continuous camera position, not the snapped residency anchor.
                return RadialMiddle?new Vector4(p.x,p.z,MiddleOuterDistance,MiddleOuterDistance-MiddleFullDistance)
                    :new Vector4(p.x,p.z,Demo.PatchSize*.5f-(Demo.StreamWorld?8:0),BlendWidth);
            }
        }
        private void OnEnable() { Camera.onPreCull-=Draw; Camera.onPreCull+=Draw; }
        private void OnValidate() => rebuild=true;
        public void Initialize()
        {
            if(instances!=null || Demo==null || CullShader==null || LeafShader==null || Demo.Handoff.BakedResponse==null)return;
            if(RadialMiddle && (!Demo.StreamWorld || MiddleOuterDistance<=MiddleFullDistance || MiddleOuterDistance>Demo.PatchSize*.5f-8))
                throw new System.InvalidOperationException("Radial middle requires streaming, increasing distances and an 8m resident guard outside its outer radius.");
            var bank=Demo.Handoff.BakedResponse;
            if(Demo.StreamWorld)Demo.Initialize();
            if(!bank.IsBaked || bank.GridSize!=5)throw new System.InvalidOperationException("Outer grass requires the accepted 5x5 response bank.");
            origin=Demo.transform.position; int side=Mathf.CeilToInt(Mathf.Max(Demo.PatchSize+16,FieldSize)/16f)*2;
            float core=Demo.PatchSize*.5f;
            var data=new List<Tile>(); CandidateCount=0;
            for(int z=-side/2;z<side/2;z++)for(int x=-side/2;x<side/2;x++)
            {
                var start=new Vector2(x*8,z*8);
                float farEdge=Mathf.Max(Mathf.Abs(start.x),Mathf.Abs(start.x+8),Mathf.Abs(start.y),Mathf.Abs(start.y+8));
                if(!Demo.StreamWorld && farEdge<=core-BlendWidth)continue;
                float nearEdge=Mathf.Max(Mathf.Max(0,Mathf.Max(start.x,-start.x-8)),Mathf.Max(0,Mathf.Max(start.y,-start.y-8)));
                int count=nearEdge<core+8?2048:512;
                data.Add(new Tile { Origin=start,Count=count }); CandidateCount+=count;
            }
            TileCount=data.Count;
            tiles=new ComputeBuffer(data.Count,16); tiles.SetData(data);
            // Capacity is a hard upper bound even when every tile is visible and dense.
            instances=new ComputeBuffer(CandidateCount,32,ComputeBufferType.Append);
            arguments=new ComputeBuffer(4,4,ComputeBufferType.IndirectArguments); arguments.SetData(new uint[] {15,0,0,0});
            var response=new Vector4[75];
            for(int s=0;s<25;s++)
            { response[s*3]=bank.Samples[s].C1; response[s*3+1]=bank.Samples[s].C2; response[s*3+2]=bank.Samples[s].Tip; }
            controls=new ComputeBuffer(75,16); controls.SetData(response);
            bakedResponse=bank;
            compute=Instantiate(CullShader); kernel=compute.FindKernel("BuildVisible");
            compute.SetBuffer(kernel,"Tiles",tiles); compute.SetBuffer(kernel,"Visible",instances);
            material=new Material(LeafShader); material.CopyPropertiesFromMaterial(Demo.GrassMaterial);
            material.SetBuffer("OuterInstances",instances); material.SetBuffer("OuterControls",controls);
            material.SetTexture("OuterWindNoise",Demo.WindNoise); material.SetFloat("OuterMaxWind",bank.MaxWind);
            material.SetFloat("OuterHalfWidth",bank.Shape.HalfWidth);
            bounds=new Bounds(origin+Vector3.up*2,new Vector3(side*8+8,12,side*8+8));
        }
        public void SetBakedResponse(GrassResponseType response)
        {
            if(controls==null)return;
            if(response==bakedResponse)return;
            GrassLodHandoff.ValidateResponseReplacement(bakedResponse,response);
            var values=new Vector4[75];
            for(int sample=0;sample<25;sample++)
            {values[sample*3]=response.Samples[sample].C1;values[sample*3+1]=response.Samples[sample].C2;values[sample*3+2]=response.Samples[sample].Tip;}
            controls.SetData(values);
            material.SetFloat("OuterMaxWind",response.MaxWind);material.SetFloat("OuterHalfWidth",response.Shape.HalfWidth);
            bakedResponse=response;
        }
        private void Update()
        {
            if(rebuild) { Release(); rebuild=false; }
            if(Application.IsPlaying(gameObject) && Demo!=null && Input.GetKeyDown(KeyCode.B))Demo.Handoff.DebugLodBaseColors=!Demo.Handoff.DebugLodBaseColors;
        }
        public void PrepareVisible()
        {
            Initialize(); if(instances==null || Demo.Cam==null)return;
            if(bakedResponse!=Demo.Handoff.BakedResponse)SetBakedResponse(Demo.Handoff.BakedResponse);
            Demo.Initialize(); Demo.Renderer.SetOuterCoverage(CoverageTransition,RadialMiddle);
            compute.SetInt("RadialMiddle",RadialMiddle?1:0);
            compute.SetInt("StreamWorld",Demo.StreamWorld?1:0);
            compute.SetFloat("OuterWindowHalf",Mathf.Ceil(Mathf.Max(Demo.PatchSize+16,FieldSize)/16f)*8);
            if(Demo.StreamWorld)
            {
                origin=Demo.WorldLayout.Center;
                bounds.center=origin+Vector3.up*2;
            }
            compute.SetBuffer(kernel,"CoreStates",Demo.Renderer.Handoff.ClassificationStates);
            compute.SetInt("CoreSize",Demo.PatchSize);
            compute.SetFloat("GrassLifecycleTime",Demo.Renderer.SimulationTime);
            compute.SetFloat("GrassLifecycleEntrySeconds",Mathf.Max(.05f,Demo.Handoff.TransitionSeconds));
            compute.SetFloat("GrassLifecycleExitSeconds",Mathf.Max(.05f,Demo.Physics.ContactFadeSeconds));
            var frustum=GeometryUtility.CalculateFrustumPlanes(Demo.Cam);
            for(int i=0;i<6;i++)planes[i]=new Vector4(frustum[i].normal.x,frustum[i].normal.y,frustum[i].normal.z,frustum[i].distance);
            compute.SetVectorArray("FrustumPlanes",planes); compute.SetVector("FieldOrigin",origin);
            compute.SetVector("Focus",Demo.SimulationFocus!=null?Demo.SimulationFocus.position:Demo.Cam.transform.position);
            compute.SetVector("CoreBlend",CoverageTransition); compute.SetFloat("SparseDistance",SparseDistance);
            compute.SetFloat("DensityFadeDistance",DensityFadeDistance); compute.SetFloat("FarWidthScale",FarWidthScale);
            compute.SetInt("TileCount",TileCount); instances.SetCounterValue(0);
            compute.Dispatch(kernel,TileCount,1,1); ComputeBuffer.CopyCount(instances,arguments,4);
            float time=Demo.Renderer.SimulationTime;
            Vector3 wind=Demo.RandomWind && Demo.Wind.sqrMagnitude>0?GrassResponseDemo.WindAt(time,true)*(Demo.Wind.magnitude/6):Demo.Wind;
            material.SetVector("OuterWind",wind); material.SetFloat("OuterTime",time);
            if(RadialMiddle)Demo.Renderer.SetOuterShape(controls,Demo.WindNoise,wind,time,Demo.Handoff.BakedResponse.MaxWind,Demo.Handoff.BakedResponse.Shape.HalfWidth);
            if(Demo.Handoff.DebugLodBaseColors)material.EnableKeyword("GRASS_LOD_BASE_COLORS"); else material.DisableKeyword("GRASS_LOD_BASE_COLORS");
        }
        public void RenderPrepared(Camera camera)
        {
            if(instances!=null)Graphics.DrawProceduralIndirect(material,bounds,MeshTopology.Triangles,arguments,0,camera,null,ShadowCastingMode.Off,true);
        }
        private void Draw(Camera camera)
        {
            if(!isActiveAndEnabled || Demo==null || (camera!=Demo.Cam && camera.cameraType!=CameraType.SceneView))return;
            Demo.ConfigureViewCamera(); if(Demo.OuterCoverage!=this)PrepareVisible(); RenderPrepared(camera);
        }
        private void OnGUI()
        {
            if(Application.IsPlaying(gameObject) && instances!=null)
            {
                GUI.Box(new Rect(12,150,850,54),GUIContent.none);
                GUI.Label(new Rect(24,154,830,24),"Outer candidates before culling: "+CandidateCount+". No collision or lodging history. B: lighting / tier colors");
                GUI.Label(new Rect(24,178,830,24),"Outer: 7 vertices / density 32 -> 8 -> 2 per m2. GPU visible count is inspected by the offline validator.");
            }
        }
        private void Release()
        {
            Demo?.Renderer?.SetOuterCoverage(Vector4.zero);
            foreach(var buffer in new[] {tiles,instances,arguments,controls})buffer?.Dispose();
            tiles=instances=arguments=controls=null;
            bakedResponse=null;
            if(Application.isPlaying) { if(compute!=null)Destroy(compute); if(material!=null)Destroy(material); }
            else { if(compute!=null)DestroyImmediate(compute); if(material!=null)DestroyImmediate(material); }
            compute=null; material=null;
        }
        private void OnDisable() { Camera.onPreCull-=Draw; Release(); }
        private void OnDestroy() { Camera.onPreCull-=Draw; Release(); }
    }
}
