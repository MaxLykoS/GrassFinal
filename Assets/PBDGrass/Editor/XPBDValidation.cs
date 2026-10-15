using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace GrassFinal.PBDGrass.Editor
{
    public static class XPBDValidation
    {
        [Serializable] private sealed class Metric { public string name; public float value; }
        [Serializable] private sealed class Report
        {
            public string gpu, graphicsApi;
            public bool passed;
            public string error;
            public List<Metric> metrics = new List<Metric>();
        }
        private static Report report;
        private static string output;
        private static Camera camera;
        private static ComputeShader solver;
        private static Material material;
        private static Texture2D wind;
        private static readonly List<string> errors = new List<string>();

        [MenuItem("Tools/PBD Grass/Validate XPBD and Capture Preview")]
        public static void Run()
        {
            output = Environment.GetEnvironmentVariable("XPBD_VALIDATION_OUTPUT");
            if (string.IsNullOrEmpty(output)) output = Path.GetFullPath("Assets/PBDGrass/Diagnostics");
            Directory.CreateDirectory(output);
            report = new Report { gpu = SystemInfo.graphicsDeviceName, graphicsApi = SystemInfo.graphicsDeviceType.ToString() };
            Scene previous = SceneManager.GetActiveScene();
            Scene testScene = Application.isBatchMode
                ? EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single)
                : SceneManager.CreateScene("XPBD validation (temporary)");
            SceneManager.SetActiveScene(testScene);
            errors.Clear();
            Application.logMessageReceived += CaptureError;
            try
            {
                Require(SystemInfo.supportsComputeShaders, "A graphics device with Compute Shader support is required.");
                solver = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/PBDGrass/Shaders/PBDSolverCS.compute");
                material = AssetDatabase.LoadAssetAtPath<Material>("Assets/PBDGrass/Materials/PBDGrassProcedualMaterial.mat");
                wind = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/PBDGrass/Textures/Wind.png");
                Require(solver != null && material != null && wind != null, "Missing workspace assets.");
                Require(solver.IsSupported(solver.FindKernel("XPBDSolverLOD0")), "XPBD compute kernel failed to compile.");
                foreach (var message in ShaderUtil.GetShaderMessages(material.shader))
                    Require(message.severity.ToString() != "Error", message.message);
                camera = new GameObject("Validation Camera").AddComponent<Camera>();
                camera.enabled = false;
                camera.transform.position = new Vector3(0, 2, -5);
                camera.transform.LookAt(Vector3.zero);
                camera.nearClipPlane = 0.03f;
                CheckRestAndFrames();
                CheckPoolAndOddGrid();
                CheckMaterialDirections();
                CheckBendingAndRecovery();
                CheckMovingContact();
                CheckSegmentProbe();
                CheckCapsuleContact();
                CheckGroundFriction();
                CheckContactFrameRates();
                Benchmark(32);
                Benchmark(16);
                BenchmarkContacts();
                CapturePreview();
                Require(errors.Count == 0, string.Join("\n", errors));
                report.passed = true;
                Debug.Log("XPBD_VALIDATION_PASS " + JsonUtility.ToJson(report));
            }
            catch (Exception exception)
            {
                report.error = exception.ToString();
                Debug.LogError("XPBD_VALIDATION_FAIL " + report.error);
            }
            finally
            {
                Application.logMessageReceived -= CaptureError;
                File.WriteAllText(Path.Combine(output, "results.json"), JsonUtility.ToJson(report, true));
                if (!Application.isBatchMode) EditorSceneManager.CloseScene(testScene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
            if (Application.isBatchMode) EditorApplication.Exit(report.passed ? 0 : 1);
        }

        private static void CaptureError(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(condition);
        }
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        private static void Record(string name, float value) => report.metrics.Add(new Metric { name = name, value = value });
        private static XPBDGrassSettings Settings() => new XPBDGrassSettings
        {
            Gravity = Vector3.zero, GroundCollision = false, NearDistance = 100, MaxDistance = 100,
            EnablePlasticity = false, // These legacy probes specifically validate reversible elasticity.
            SoftBendCompliance = 12, HardBendCompliance = .8f, TwistCompliance = 3, BaseBendCompliance = .5f
        };
        private static PBDGrassPatch Patch(int size)
        {
            UnityEngine.Random.State state = UnityEngine.Random.state;
            UnityEngine.Random.InitState(12345);
            try { return new PBDGrassPatch(Vector3.zero, size, 32); }
            finally { UnityEngine.Random.state = state; }
        }
        private static PBDGrassPatchRenderer Renderer(int size, XPBDGrassSettings settings, IList<Transform> colliders = null)
        {
            PBDGrassPatch patch = Patch(size);
            try { return new PBDGrassPatchRenderer(patch, solver, material, camera, wind, settings, colliders); }
            finally { Object.DestroyImmediate(patch.PatchMesh); }
        }
        private static void Ticks(PBDGrassPatchRenderer renderer, int count)
        {
            for (int i = 0; i < count; i++) renderer.Advance(1f / 60);
        }
        private static float Difference(BoneInfo[] a, BoneInfo[] b)
        {
            float maximum = 0;
            for (int i = 0; i < a.Length; i++) maximum = Mathf.Max(maximum, Vector3.Distance(a[i].Position, b[i].Position));
            return maximum;
        }
        private static float TipMovement(BoneInfo[] nodes)
        {
            float sum = 0;
            for (int i = 7; i < nodes.Length; i += 8) sum += Vector3.Distance(nodes[i].Position, nodes[i].OriginPos);
            return sum / (nodes.Length / 8);
        }
        private static float Angle(Vector3 a, Vector3 b, Vector3 c) => Vector3.Angle(b - a, c - b) * Mathf.Deg2Rad;
        private static float BendError(BoneInfo[] nodes)
        {
            float sum = 0;
            for (int b = 0; b < nodes.Length; b += 8)
            {
                Vector3 root = nodes[b].OriginPos, basePoint = 2 * root - nodes[b + 1].OriginPos;
                sum += Angle(basePoint, root, nodes[b + 1].Position);
                for (int i = 0; i < 6; i++)
                    sum += Mathf.Abs(Angle(nodes[b+i].Position, nodes[b+i+1].Position, nodes[b+i+2].Position)
                        - Angle(nodes[b+i].OriginPos, nodes[b+i+1].OriginPos, nodes[b+i+2].OriginPos));
            }
            return sum / (nodes.Length / 8 * 7);
        }
        private static float ValidateNodes(BoneInfo[] nodes)
        {
            float stretch = 0;
            for (int i = 0; i < nodes.Length; i++)
            {
                Vector3 p = nodes[i].Position;
                Require(!float.IsNaN(p.sqrMagnitude) && !float.IsInfinity(p.sqrMagnitude), "Non-finite node.");
                if (i % 8 == 0) Require(Vector3.Distance(p, nodes[i].OriginPos) < 1e-6, "Root moved.");
                else
                {
                    float rest = Vector3.Distance(nodes[i].OriginPos, nodes[i-1].OriginPos);
                    stretch = Mathf.Max(stretch, Mathf.Abs(Vector3.Distance(p, nodes[i-1].Position) / rest - 1));
                }
            }
            return stretch;
        }
        private static void CheckRestAndFrames()
        {
            using (PBDGrassPatchRenderer r = Renderer(8, Settings()))
            {
                r.SetWind(Vector3.zero, 0, Vector4.zero);
                BoneInfo[] before = r.ReadNodes();
                Ticks(r, 60);
                float drift = Difference(before, r.ReadNodes());
                Record("rest_drift_m", drift);
                Require(drift < 0.0005f, "Unforced rest shape drifts.");
            }
            BoneInfo[] reference = null;
            foreach (int fps in new[] { 30, 60, 120 })
            {
                using (PBDGrassPatchRenderer r = Renderer(8, Settings()))
                {
                    r.SetWind(new Vector3(10, 0, 3), 0.1f, new Vector4(.01f, .01f, 0, 1));
                    for (int i = 0; i < fps * 2; i++) r.Advance(1f / fps);
                    BoneInfo[] nodes = r.ReadNodes();
                    ValidateNodes(nodes);
                    Require(r.SimulationTicks == 120, "Render FPS changed simulated time.");
                    if (reference == null) reference = nodes;
                    float difference = Difference(reference, nodes);
                    Record("fps_" + fps + "_difference_m", difference);
                    Require(difference < 0.0001f, "Render FPS changed the trajectory.");
                }
            }
        }
        private static void CheckBendingAndRecovery()
        {
            float stiffBend = 0;
            foreach (float compliance in new[] { .001f, .01f })
            {
                XPBDGrassSettings settings = Settings();
                settings.SoftBendCompliance = compliance*1200;
                settings.HardBendCompliance = compliance*80;
                settings.TwistCompliance = compliance*300;
                settings.BaseBendCompliance = compliance*50;
                using (PBDGrassPatchRenderer r = Renderer(8, settings))
                {
                    r.SetWind(new Vector3(20, 0, 0), 0, Vector4.zero);
                    Ticks(r, 120);
                    BoneInfo[] pushed = r.ReadNodes();
                    float tip = TipMovement(pushed), error = BendError(pushed);
                    float stretch = ValidateNodes(pushed);
                    Record("compliance_" + compliance + "_tip_m", tip);
                    Record("compliance_" + compliance + "_bend_error_rad", error);
                    Record("compliance_" + compliance + "_max_stretch", stretch);
                    Require(stretch < .05f, "Unobstructed grass stretches excessively.");
                    if (compliance < .005f) stiffBend = error;
                    else
                    {
                        // Tip displacement includes free roll of an intrinsically curved, isotropic centerline.
                        // Measure the actual constrained angular strain to compare material stiffness.
                        Require(error > stiffBend * 1.2f, "Bending compliance does not change angular elasticity.");
                        r.SetWind(Vector3.zero, 0, Vector4.zero);
                        Ticks(r, 240);
                        float recovered = BendError(r.ReadNodes());
                        Record("loaded_bend_error_rad", error);
                        Record("recovered_bend_error_rad", recovered);
                        Require(recovered < error * .4f, "Intrinsic bending failed to recover after removing wind.");
                    }
                }
            }
        }
        private static void CheckPoolAndOddGrid()
        {
            XPBDGrassSettings small = Settings(); small.MaxActiveBlades=128; small.NearDistance=1.2f; small.PoolHysteresis=.5f;
            using (var r=Renderer(8,small))
            {
                r.SetWind(new Vector3(10,0,3),.1f,new Vector4(.01f,.01f,0,1)); Ticks(r,30);
                Require(r.ActiveBladeCount<=128,"Pool exceeded capacity.");
                int initialized=r.Pool.InitializationCount;
                var before=r.ReadNodes(); camera.transform.Rotate(0,180,0); Ticks(r,1);
                Require(initialized==r.Pool.InitializationCount,"Camera rotation reset physics state.");
                r.SimulationCenter=new Vector3(3,0,0); Ticks(r,1);
                Require(r.ActiveBladeCount<=128 && r.Pool.InitializationCount>initialized,"Pool did not migrate with interaction center.");
                r.SimulationCenter=Vector3.zero; Ticks(r,1); ValidateNodes(r.ReadNodes());
                Record("pool_capacity_blades",128); Record("pool_migration_initializations",r.Pool.InitializationCount);
                camera.transform.LookAt(Vector3.zero);
            }
            var cached=Settings();cached.MaxActiveBlades=512;cached.NearDistance=1.2f;cached.PoolHysteresis=0;
            using(var r=Renderer(8,cached))
            {
                r.SetWind(new Vector3(10,0,3),.1f,new Vector4(.01f,.01f,0,1));Ticks(r,30);
                var before=r.ReadNodes();r.SimulationCenter=new Vector3(2,0,0);Ticks(r,1);
                int initialized=r.Pool.InitializationCount;r.SimulationCenter=Vector3.zero;Ticks(r,1);
                Require(initialized==r.Pool.InitializationCount,"Cached state was reinitialized on re-entry.");
                var after=r.ReadNodes();Require(Difference(before,after)>.001f,"Cache test did not excite any motion.");
                Record("pool_cached_reentry_initializations",r.Pool.InitializationCount-initialized);
            }
            var patch=new PBDGrassPatch(new Vector3(3,2,-4),3,32);
            try
            {
                using(var r=new PBDGrassPatchRenderer(patch,solver,material,camera,wind,Settings(),null))
                {
                    r.SetWind(Vector3.zero,0,Vector4.zero); Ticks(r,10); var nodes=r.ReadNodes(); ValidateNodes(nodes);
                    for(int i=0;i<nodes.Length;i+=8)Require(Mathf.Abs(nodes[i].Position.y-2)<1e-6,"Translated root ignored.");
                    Require(r.BladeCount==288,"Odd grid layout wrong."); r.Advance(1);
                    Require(r.DroppedTime>.9f,"Catch-up cap failed."); Record("odd_grid_and_hitch_guard",1);
                }
            }
            finally { Object.DestroyImmediate(patch.PatchMesh); }
        }
        private static void CheckContactFrameRates()
        {
            BoneInfo[] reference=null;
            foreach(int fps in new[]{30,60,120})
            {
                var go=new GameObject("Contact frame-rate probe");go.transform.position=new Vector3(0,.65f,-3);
                using(var r=Renderer(8,Settings(),new[]{go.transform}))
                {
                    r.SetWind(Vector3.zero,0,Vector4.zero);
                    for(int frame=1;frame<=fps*2;frame++)
                    {go.transform.position=new Vector3(0,.65f,-3+3*frame/(float)fps);r.Advance(1f/fps);}
                    var nodes=r.ReadNodes();ValidateNodes(nodes);if(reference==null)reference=nodes;
                    float difference=Difference(reference,nodes);Record("moving_contact_fps_"+fps+"_difference_m",difference);
                    Require(difference<.002f,"Render FPS changed the moving-contact trajectory.");
                }
                Object.DestroyImmediate(go);
            }
        }
        private static void BenchmarkContacts()
        {
            var a=new GameObject("Benchmark capsule");var shape=a.AddComponent<GrassInteractionCollider>();
            a.transform.position=new Vector3(0,.65f,0);shape.Radius=.25f;shape.HalfSegment=.35f;
            var b=new GameObject("Benchmark sphere");b.transform.position=new Vector3(2,.65f,0);
            using(var r=Renderer(32,Settings(),new[]{a.transform,b.transform}))
            {
                r.SetWind(new Vector3(10,0,3),.1f,new Vector4(.01f,.01f,0,1));Ticks(r,4);r.ReadNodes();
                Stopwatch timer=Stopwatch.StartNew();
                for(int t=0;t<30;t++) {a.transform.position+=Vector3.right*.02f;b.transform.position+=Vector3.right*.02f;Ticks(r,1);}
                ValidateNodes(r.ReadNodes());timer.Stop();Record("blades_32768_two_contacts_synchronized_tick_ms",(float)timer.Elapsed.TotalMilliseconds/30);
            }
            Object.DestroyImmediate(a);Object.DestroyImmediate(b);
        }
        private static void CheckMaterialDirections()
        {
            float soft=0,hard=0;
            for(int axis=0;axis<2;axis++)
            {
                using(var r=Renderer(1,Settings()))
                {
                    r.SetWind(Vector3.zero,0,Vector4.zero);
                    Quaternion q=r.Data.Rods[0].Rotation;
                    Vector3 direction=q*(axis==0?Vector3.up:Vector3.right);
                    r.ImpulseBlade(0,direction*1.5f,Vector3.zero);
                    float movement=0;
                    for(int t=0;t<30;t++) { Ticks(r,1); var nodes=r.ReadNodes(); ValidateNodes(nodes);
                        movement=Mathf.Max(movement,Mathf.Abs(Vector3.Dot(nodes[7].Position-nodes[7].OriginPos,direction))); }
                    Record(axis==0?"soft_direction_peak_tip_m":"hard_direction_peak_tip_m",movement);
                    if(axis==0)soft=movement; else hard=movement;
                }
            }
            Require(soft>hard*1.15f,"Material bending directions have indistinguishable response.");
            using(var r=Renderer(1,Settings()))
            {
                r.SetWind(Vector3.zero,0,Vector4.zero);
                Vector3 tangent=r.Data.Rods[0].Rotation*Vector3.forward;
                r.ImpulseBlade(0,Vector3.zero,tangent*12);
                float peak=0;
                for(int t=0;t<60;t++) { Ticks(r,1); peak=Mathf.Max(peak,FrameError(r)); }
                Ticks(r,300); float recovered=FrameError(r);
                Record("twist_impulse_peak_frame_rad",peak); Record("twist_impulse_recovered_frame_rad",recovered);
                Require(peak>.01f && recovered<peak*.5f,"Material frames did not recover from a torsional impulse.");
            }
        }
        private static float FrameError(PBDGrassPatchRenderer r)
        {
            var f=r.ReadFrames(); float sum=0;
            for(int s=0;s<7;s++)sum+=Quaternion.Angle(f[s].Rotation,r.Data.Rods[s].Rotation)*Mathf.Deg2Rad;
            foreach(var frame in f)
            {
                float norm=frame.Rotation.x*frame.Rotation.x+frame.Rotation.y*frame.Rotation.y+frame.Rotation.z*frame.Rotation.z+frame.Rotation.w*frame.Rotation.w;
                Require(!float.IsNaN(norm) && Mathf.Abs(norm-1)<.001f,"Non-unit material frame.");
            }
            return sum/7;
        }
        private static void CheckMovingContact()
        {
            GameObject sphere = new GameObject("Contact Probe");
            sphere.transform.position = new Vector3(0, .65f, -3);
            XPBDGrassSettings settings = Settings();
            settings.GroundCollision = true;
            using (PBDGrassPatchRenderer r = Renderer(8, settings, new[] { sphere.transform }))
            {
                r.SetWind(Vector3.zero, 0, Vector4.zero);
                float penetration = 0;
                for (int tick = 0; tick < 120; tick++)
                {
                    sphere.transform.position = new Vector3(0, .65f, -3 + tick * .05f);
                    r.Advance(1f / 60);
                    if (tick % 5 != 0) continue;
                    BoneInfo[] nodes = r.ReadNodes();
                    ValidateNodes(nodes);
                    for(int i=0;i<nodes.Length;i++) if(i%8!=7)
                    {
                        Vector3 edge=nodes[i+1].Position-nodes[i].Position;
                        float t=Mathf.Clamp01(Vector3.Dot(sphere.transform.position-nodes[i].Position,edge)/edge.sqrMagnitude);
                        penetration=Mathf.Max(penetration,settings.ColliderRadius-Vector3.Distance(nodes[i].Position+edge*t,sphere.transform.position));
                    }
                }
                Record("moving_sphere_segment_penetration_m", penetration);
                Require(penetration < .005f, "Segments penetrate the moving sphere.");
                sphere.transform.position = new Vector3(0, 5, 0);
                Ticks(r, 240);
                Record("post_contact_max_stretch", ValidateNodes(r.ReadNodes()));
            }
            Object.DestroyImmediate(sphere);
        }
        private static float SegmentDistance(Vector3 p,Vector3 q,Vector3 a,Vector3 b)
        {
            Vector3 d=q-p,e=b-a,r=p-a; float dd=Vector3.Dot(d,d),ee=Vector3.Dot(e,e),de=Vector3.Dot(d,e),dr=Vector3.Dot(d,r),er=Vector3.Dot(e,r);
            float t,u;
            if(dd<1e-10f) { t=0; u=ee>1e-10f?Mathf.Clamp01(er/ee):0; }
            else if(ee<1e-10f) { u=0; t=Mathf.Clamp01(-dr/dd); }
            else { float denominator=dd*ee-de*de; t=denominator>1e-10f?Mathf.Clamp01((de*er-dr*ee)/denominator):0;
                u=(de*t+er)/ee;
                if(u<0) {u=0;t=Mathf.Clamp01(-dr/dd);} else if(u>1) {u=1;t=Mathf.Clamp01((de-dr)/dd);} }
            return Vector3.Distance(p+d*t,a+e*u);
        }
        private static void CheckSegmentProbe()
        {
            PBDGrassPatch patch=Patch(1); Vector3 a=patch.Bodies[0].OriginPos[3],b=patch.Bodies[0].OriginPos[4];
            Object.DestroyImmediate(patch.PatchMesh);
            var probe=new GameObject("Between nodes probe"); probe.transform.position=(a+b)*.5f;
            XPBDGrassSettings settings=Settings();settings.ColliderRadius=.012f;
            using(var r=Renderer(1,settings,new[]{probe.transform}))
            {
                r.SetWind(Vector3.zero,0,Vector4.zero);var before=r.ReadNodes();
                Require(Vector3.Distance(before[3].Position,probe.transform.position)>.012f &&
                    Vector3.Distance(before[4].Position,probe.transform.position)>.012f,"Probe touches a node; test does not isolate segment contact.");
                Ticks(r,1);var after=r.ReadNodes();
                float penetration=.012f-SegmentDistance(after[3].Position,after[4].Position,probe.transform.position,probe.transform.position);
                Record("between_nodes_probe_penetration_m",Mathf.Max(0,penetration));
                Require(penetration<.0005f && Difference(before,after)>.005f,"Between-node sphere failed to displace the segment.");
            }
            Object.DestroyImmediate(probe);
        }
        private static void CheckCapsuleContact()
        {
            var go=new GameObject("Moving capsule");var shape=go.AddComponent<GrassInteractionCollider>();
            shape.Radius=.25f;shape.HalfSegment=.35f;go.transform.position=new Vector3(0,.65f,-3);
            using(var r=Renderer(8,Settings(),new[]{go.transform}))
            {
                r.SetWind(Vector3.zero,0,Vector4.zero);float penetration=0,movement=0;
                for(int tick=0;tick<120;tick++)
                {
                    go.transform.position=new Vector3(0,.65f,-3+tick*.05f);r.Advance(1f/60);
                    if(tick%5!=0)continue;var nodes=r.ReadNodes();ValidateNodes(nodes);shape.Endpoints(out Vector3 a,out Vector3 b);
                    for(int i=0;i<nodes.Length;i++)if(i%8!=7)
                        penetration=Mathf.Max(penetration,shape.Radius-SegmentDistance(nodes[i].Position,nodes[i+1].Position,a,b));
                    movement=Mathf.Max(movement,TipMovement(nodes));
                }
                Record("moving_capsule_segment_penetration_m",penetration);Record("moving_capsule_mean_tip_peak_m",movement);
                Require(penetration<.005f && movement>.001f,"Capsule contact failed.");
            }
            Object.DestroyImmediate(go);
        }
        private static void CheckGroundFriction()
        {
            float free=0,rough=0;
            foreach(float friction in new[]{0f,.8f})
            {
                // Disable elasticity to isolate Coulomb ground friction from spring recovery and damping.
                var settings=Settings(); settings.GroundCollision=true;settings.Gravity=new Vector3(0,-9.81f,0);
                settings.Friction=friction;settings.VelocityDamping=0;settings.StretchCompliance=settings.AlignmentCompliance=1e9f;
                settings.SoftBendCompliance=settings.HardBendCompliance=settings.TwistCompliance=settings.BaseBendCompliance=1e9f;
                using(var r=Renderer(1,settings))
                {
                    r.SetWind(Vector3.zero,0,Vector4.zero);r.ImpulseBlade(0,Vector3.right*1.5f,Vector3.zero);Ticks(r,60);
                    var nodes=r.ReadNodes();float slip=nodes[7].Position.x-nodes[7].OriginPos.x;
                    Require(nodes[7].Position.y>=settings.CollisionMargin-1e-5f,"Ground contact penetrated.");
                    Record("ground_friction_"+friction+"_tip_slip_m",slip); if(friction==0)free=slip;else rough=slip;
                }
            }
            Require(rough<free*.8f,"Friction does not reduce tangential sliding.");
        }
        private static void Benchmark(int size)
        {
            using (PBDGrassPatchRenderer r = Renderer(size, Settings()))
            {
                r.SetWind(new Vector3(10, 0, 3), .1f, new Vector4(.01f, .01f, 0, 1));
                Ticks(r, 4); r.ReadNodes();
                Stopwatch timer = Stopwatch.StartNew();
                Ticks(r, 30); ValidateNodes(r.ReadNodes()); timer.Stop();
                Record("blades_" + r.BladeCount + "_synchronized_tick_ms", (float)timer.Elapsed.TotalMilliseconds / 30);
            }
        }
        public static void BuildNearDemoPrefab()
        {
            const string path="Assets/PBDGrass/Prefabs/PBDGrassDemo.prefab";
            GameObject root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                GrassDemo demo=root.GetComponent<GrassDemo>(); demo.GrassSize=32;demo.Physics=new XPBDGrassSettings { NearDistance=24 };
                if(demo.colliders.Count>0)
                {
                    Transform tr=demo.colliders[0];tr.position=new Vector3(-1,.65f,0);tr.localScale=new Vector3(.6f,.65f,.6f);
                    GameObject primitive=GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    tr.GetComponent<MeshFilter>().sharedMesh=primitive.GetComponent<MeshFilter>().sharedMesh;Object.DestroyImmediate(primitive);
                    var physicsCollider=tr.GetComponent<Collider>();if(physicsCollider!=null)Object.DestroyImmediate(physicsCollider);
                    var shape=tr.GetComponent<GrassInteractionCollider>();if(shape==null)shape=tr.gameObject.AddComponent<GrassInteractionCollider>();
                    shape.Radius=.5f;shape.HalfSegment=.5f;shape.Friction=.45f;tr.name="Capsule interaction";
                }
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            if(Application.isBatchMode)EditorApplication.Exit(0);
        }
        private static void CapturePreview()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.35f, .38f, .4f);
            Light light = new GameObject("Preview Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(45, -30, 0);
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = Vector3.one * .8f;
            ground.transform.position = Vector3.down * .02f;
            Material groundMat = new Material(Shader.Find("Standard"));
            groundMat.color = new Color(.1f, .12f, .07f);
            ground.GetComponent<Renderer>().sharedMaterial = groundMat;
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.localScale = Vector3.one;
            Object.DestroyImmediate(sphere.GetComponent<Collider>());
            Object.DestroyImmediate(sphere.GetComponent<MeshFilter>());
            var capsule=GameObject.CreatePrimitive(PrimitiveType.Capsule);
            sphere.GetComponent<MeshFilter>();
            var mesh=sphere.AddComponent<MeshFilter>(); mesh.sharedMesh=capsule.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(capsule);
            sphere.transform.localScale=new Vector3(.6f,.7f,.6f);
            var interaction=sphere.AddComponent<GrassInteractionCollider>();
            interaction.Radius=.5f; interaction.HalfSegment=.5f;
            sphere.transform.position = new Vector3(0, .65f, -3);
            Material sphereMat = new Material(Shader.Find("Standard"));
            sphereMat.color = new Color(.55f, .24f, .055f);
            sphere.GetComponent<Renderer>().sharedMaterial = sphereMat;
            camera.transform.position = new Vector3(2.8f, 1.8f, -4.5f);
            camera.transform.LookAt(new Vector3(0, .2f, 0));
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.18f, .27f, .34f);
            RenderTexture rt = new RenderTexture(960, 640, 24);
            camera.targetTexture = rt;
            Texture2D image = new Texture2D(960, 640, TextureFormat.RGB24, false);
            XPBDGrassSettings settings = new XPBDGrassSettings { NearDistance = 100, MaxDistance = 100 };
            using (PBDGrassPatchRenderer r = Renderer(8, settings, new[] { sphere.transform }))
            {
                r.SetWind(new Vector3(10, 0, 3), .1f, new Vector4(.05f, .05f, 0, 1));
                for (int frame = 0; frame < 70; frame++)
                {
                    if (frame >= 10 && frame < 35) sphere.transform.position = new Vector3(0, .65f, -3 + (frame - 10) * .24f);
                    if (frame >= 35)
                    {
                        sphere.transform.position = new Vector3(0, 5, 0);
                        r.SetWind(Vector3.zero, .1f, new Vector4(.05f, .05f, 0, 1));
                    }
                    Ticks(r, 6);
                    r.Draw();
                    camera.Render();
                    RenderTexture prior = RenderTexture.active;
                    RenderTexture.active = rt;
                    image.ReadPixels(new Rect(0, 0, 960, 640), 0, 0); image.Apply();
                    RenderTexture.active = prior;
                    File.WriteAllBytes(Path.Combine(output, "preview_" + frame.ToString("D3") + ".png"), image.EncodeToPNG());
                }
                Record("preview_visible_grids", r.VisibleGridCount());
                Require(r.VisibleGridCount() > 0, "Grass rendering culled every grid.");
                ValidateNodes(r.ReadNodes());
            }
            camera.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt); Object.DestroyImmediate(image);
            Object.DestroyImmediate(groundMat); Object.DestroyImmediate(sphereMat);
        }
    }
}
