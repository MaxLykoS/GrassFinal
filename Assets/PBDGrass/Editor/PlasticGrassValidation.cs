using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace GrassFinal.PBDGrass.Editor
{
    public static class PlasticGrassValidation
    {
        [Serializable] private sealed class Metric { public string name; public float value; }
        [Serializable] private sealed class Report
        {
            public bool passed;
            public string gpu, error;
            public List<Metric> metrics = new List<Metric>();
        }
        private static Report report;
        private static Camera camera;
        private static ComputeShader solver;
        private static Material material;
        private static Texture2D wind;
        private static string output;
        private static readonly List<string> errors = new List<string>();

        [MenuItem("Tools/PBD Grass/Validate Plastic Long Grass")]
        public static void Run()
        {
            output = Environment.GetEnvironmentVariable("PLASTIC_GRASS_OUTPUT");
            if (string.IsNullOrEmpty(output)) output = Path.GetFullPath("Assets/PBDGrass/Diagnostics/PlasticGrass");
            Directory.CreateDirectory(output);
            report = new Report { gpu = SystemInfo.graphicsDeviceName };
            var previous = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var scene = Application.isBatchMode ? EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single)
                : UnityEngine.SceneManagement.SceneManager.CreateScene("Plastic grass validation");
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            errors.Clear(); Application.logMessageReceived += CaptureError;
            try
            {
                solver = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/PBDGrass/Shaders/PBDSolverCS.compute");
                material = AssetDatabase.LoadAssetAtPath<Material>("Assets/PBDGrass/Materials/PBDGrassProcedualMaterial.mat");
                wind = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/PBDGrass/Textures/Wind.png");
                Require(solver != null && solver.IsSupported(solver.FindKernel("XPBDSolverLOD0")), "Solver does not compile.");
                camera = new GameObject("Plastic validation camera").AddComponent<Camera>(); camera.enabled = false;
                camera.nearClipPlane = .03f; camera.transform.position = new Vector3(3, 2.4f, -5);
                camera.transform.LookAt(new Vector3(0, .7f, 0));
                CheckShapeAndWeakWind();
                CheckLightContact();
                CheckContactAndPersistence();
                CheckContactFrameRates();
                CheckFullDemo();
                CaptureComparison();
                CheckDemoScene();
                Require(errors.Count == 0, string.Join("\n", errors)); report.passed = true;
                Debug.Log("PLASTIC_GRASS_PASS " + JsonUtility.ToJson(report));
            }
            catch (Exception e) { report.error = e.ToString(); Debug.LogError("PLASTIC_GRASS_FAIL " + e); }
            finally
            {
                Application.logMessageReceived -= CaptureError;
                File.WriteAllText(Path.Combine(output, "results.json"), JsonUtility.ToJson(report, true));
                if (!Application.isBatchMode) EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);
            }
            if (Application.isBatchMode) EditorApplication.Exit(report.passed ? 0 : 1);
        }
        private static void CaptureError(string message, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void Record(string name, float value) => report.metrics.Add(new Metric { name = name, value = value });
        private static XPBDGrassSettings Settings(bool plastic = true) => new XPBDGrassSettings
        { EnablePlasticity = plastic, Gravity = Vector3.zero, GroundCollision = false, NearDistance = 100, MaxDistance = 100 };
        private static PBDGrassPatch Patch(int size, bool longLeaf = true)
        {
            var state = UnityEngine.Random.state; UnityEngine.Random.InitState(12345);
            try { return new PBDGrassPatch(Vector3.zero, size, 32, longLeaf ? new GrassBladeShapeSettings() : null); }
            finally { UnityEngine.Random.state = state; }
        }
        private static PBDGrassPatchRenderer Renderer(int size, XPBDGrassSettings settings, Transform collider = null, bool longLeaf = true)
        {
            var patch = Patch(size, longLeaf);
            try { return new PBDGrassPatchRenderer(patch, solver, material, camera, wind, settings,
                collider == null ? null : new[] { collider }); }
            finally { Object.DestroyImmediate(patch.PatchMesh); }
        }
        private static void Ticks(PBDGrassPatchRenderer r, int count)
        { for (int t = 0; t < count; t++) r.Advance(1f / 60); }
        private static float PlasticPeak(Vector4[] values)
        { float peak = 0; foreach (var p in values) { Require(!float.IsNaN(p.sqrMagnitude), "Invalid plastic state."); peak = Mathf.Max(peak, p.magnitude); } return peak; }
        private static float Validate(PBDGrassPatchRenderer r)
        {
            float stretch = 0; var nodes = r.ReadNodes();
            for (int i = 0; i < nodes.Length; i++)
            {
                Require(!float.IsNaN(nodes[i].Position.sqrMagnitude) && !float.IsInfinity(nodes[i].Position.sqrMagnitude), "Invalid position.");
                if (i % 8 == 0) Require(Vector3.Distance(nodes[i].Position, nodes[i].OriginPos) < 1e-6f, "Root moved.");
                else
                {
                    float rest = Vector3.Distance(nodes[i].OriginPos, nodes[i - 1].OriginPos);
                    stretch = Mathf.Max(stretch, Mathf.Abs(Vector3.Distance(nodes[i].Position, nodes[i - 1].Position) / rest - 1));
                }
            }
            return stretch;
        }
        private static void CheckShapeAndWeakWind()
        {
            using (var r = Renderer(1, Settings()))
            {
                float min = float.MaxValue, max = 0;
                foreach (var b in r.Data.Blades) { min = Mathf.Min(min, b.Height); max = Mathf.Max(max, b.Height); }
                Record("rest_min_height_m", min); Record("rest_max_height_m", max);
                Require(min > 1 && max < 1.7f && r.Data.VerticesData.Length == 32 * 15, "Long leaf shape/topology wrong.");
                r.SetWind(Vector3.zero, 0, Vector4.zero); Ticks(r, 120);
                Require(PlasticPeak(r.ReadPlasticBends()) < 1e-6f, "Rest shape accumulated plasticity.");
                r.SetWind(new Vector3(1, 0, .3f), .1f, new Vector4(.01f, .01f, 0, 1)); Ticks(r, 240);
                float peak = PlasticPeak(r.ReadPlasticBends()); Record("weak_wind_plastic_angle_rad", peak);
                Require(peak < .001f, "Weak wind permanently deformed the field.");
                Record("weak_wind_max_stretch", Validate(r));
            }
        }
        private static void Press(PBDGrassPatchRenderer r, Transform collider, int blade, int ticks = 120)
        {
            Vector3 root = r.Data.GetBlade(blade).Root;
            Vector3 direction = r.Data.GetRod(blade * 7).Rotation * Vector3.up;
            for (int t = 0; t < ticks; t++)
            {
                float u = Mathf.Clamp01(t / (ticks * .5f));
                collider.position = root + Vector3.up * .65f + direction * Mathf.Lerp(1.2f, -.15f, u);
                r.Advance(1f / 60);
            }
        }
        private static void CheckLightContact()
        {
            var patch = Patch(1); Vector3 point = patch.Bodies[0].OriginPos[5];
            Vector3 direction = Vector3.Cross(patch.Bodies[0].OriginPos[6] - point, patch.Bodies[0].Offset[5]).normalized;
            for (int blade = 1; blade < patch.Bodies.Length; blade++)
                patch.Bodies[blade].Transform(Matrix4x4.Translate(new Vector3(100, 0, 0)));
            var go = new GameObject("Light contact"); go.transform.position = point + direction * .12f;
            var settings = Settings(); settings.ColliderRadius = .04f;
            using (var r = new PBDGrassPatchRenderer(patch, solver, material, camera, wind, settings, new[] { go.transform }))
            {
                r.SetWind(Vector3.zero, 0, Vector4.zero);
                for (int tick = 0; tick < 60; tick++)
                { go.transform.position = point + direction * Mathf.Lerp(.12f, .035f, Mathf.Clamp01(tick / 30f)); Ticks(r, 1); }
                var pressed = r.ReadNodes(); float movement = Vector3.Distance(pressed[5].Position, pressed[5].OriginPos);
                float plastic = PlasticPeak(r.ReadPlasticBends()); Record("light_contact_node_movement_m", movement);
                Record("light_contact_plastic_angle_rad", plastic);
                Require(movement > .002f && plastic < .001f, "Light contact failed to remain elastic.");
                go.SetActive(false); Ticks(r, 360);
                float residual = Vector3.Distance(r.ReadNodes()[7].Position, r.ReadNodes()[7].OriginPos);
                Record("light_contact_residual_tip_m", residual); Require(residual < .01f, "Light touch failed to recover.");
            }
            Object.DestroyImmediate(patch.PatchMesh);
            Object.DestroyImmediate(go);
        }
        private static void CheckContactAndPersistence()
        {
            var contact = new GameObject("Plastic pressure sphere"); contact.transform.position = Vector3.up * 10;
            float elasticResidual = 0, plasticResidual = 0;
            foreach (bool plastic in new[] { false, true })
            {
                var settings = Settings(plastic); settings.ColliderRadius = .38f;
                using (var r = Renderer(1, settings, contact.transform))
                {
                    r.SetWind(Vector3.zero, 0, Vector4.zero); Press(r, contact.transform, 0);
                    float pressed = Vector3.Distance(r.ReadNodes()[7].Position, r.ReadNodes()[7].OriginPos);
                    Record(plastic ? "plastic_pressed_tip_m" : "elastic_pressed_tip_m", pressed);
                    Record(plastic ? "plastic_contact_state_rad" : "elastic_contact_state_rad", PlasticPeak(r.ReadPlasticBends()));
                    contact.transform.position = Vector3.up * 10; Ticks(r, 360);
                    var nodes = r.ReadNodes(); float residual = Vector3.Distance(nodes[7].Position, nodes[7].OriginPos);
                    Record(plastic ? "plastic_residual_tip_m" : "elastic_residual_tip_m", residual);
                    Record(plastic ? "plastic_contact_max_stretch" : "elastic_contact_max_stretch", Validate(r));
                    if (plastic) { plasticResidual = residual; Require(residual < pressed, "Plastic leaf did not partially rebound."); }
                    else elasticResidual = residual;
                }
            }
            Require(plasticResidual > .05f && plasticResidual > elasticResidual + .04f, "Pressure left no meaningful permanent bend.");
            var poolSettings = Settings(); poolSettings.ColliderRadius = .38f;
            poolSettings.MaxActiveBlades = 32; poolSettings.NearDistance = 1; poolSettings.PoolHysteresis = 0;
            using (var r = Renderer(4, poolSettings, contact.transform))
            {
                r.SetWind(Vector3.zero, 0, Vector4.zero); r.SimulationCenter = r.Data.Grids[0].Pos; Ticks(r, 1);
                Press(r, contact.transform, 0); contact.transform.position = Vector3.up * 10; Ticks(r, 120);
                var before = r.ReadPlasticBends(); Require(PlasticPeak(before) > .01f, "Persistence probe never yielded.");
                r.SimulationCenter = r.Data.Grids[15].Pos; Ticks(r, 1);
                Require(r.Pool.SlotForGrid(0) < 0, "Pool probe did not evict the deformed grid.");
                var after = r.ReadPlasticBends(); float error = 0;
                for (int i = 0; i < 32 * 7; i++) error = Mathf.Max(error, Vector4.Distance(before[i], after[i]));
                Record("evicted_plastic_state_error", error); Require(error == 0, "Plastic state was attached to a recycled pool slot.");
                r.SimulationCenter = r.Data.Grids[0].Pos; r.RefreshSimulationSelection();
                var reentered = r.ReadNodes(); float residual = Vector3.Distance(reentered[7].Position, reentered[7].OriginPos);
                Record("reentered_tip_residual_m", residual); Require(residual > .05f, "Re-entered leaf forgot its natural shape.");
                r.ResetDeformation(); Require(PlasticPeak(r.ReadPlasticBends()) == 0, "Reset left plastic state.");
                Record("reset_max_stretch", Validate(r));
                Require(Vector3.Distance(r.ReadNodes()[7].Position, r.ReadNodes()[7].OriginPos) < .0001f, "Reset did not restore the original leaf.");
            }
            Object.DestroyImmediate(contact);
        }
        private static void CheckFullDemo()
        {
            var settings = Settings(); settings.NearDistance = 24; settings.Gravity = new Vector3(0, -9.81f, 0); settings.GroundCollision = true;
            using (var r = Renderer(32, settings))
            {
                r.SetWind(new Vector3(10, 0, 3), .1f, new Vector4(.01f, .01f, 0, 1)); Ticks(r, 180);
                Require(r.ActiveBladeCount == 32768, "Demo no longer simulates 32768 blades.");
                float stretch = Validate(r); Record("demo_32768_max_stretch", stretch);
                Record("demo_unpressed_plastic_angle_rad", PlasticPeak(r.ReadPlasticBends()));
                Require(stretch < .02f, "Long leaves stretch excessively in ordinary wind.");
                Require(PlasticPeak(r.ReadPlasticBends()) < .001f, "Ordinary demo wind caused permanent collapse.");
            }
        }
        private static void CheckContactFrameRates()
        {
            Vector4[] reference = null;
            foreach (int fps in new[] { 30, 60, 120 })
            {
                var go = new GameObject("Frame rate contact"); go.transform.position = Vector3.up * 10;
                var settings = Settings(); settings.ColliderRadius = .38f;
                var initial = Patch(1); Vector3 initialRoot = initial.Bodies[0].OriginPos[0];
                Vector3 initialDirection = Vector3.Cross(initial.Bodies[0].OriginPos[1] - initialRoot, initial.Bodies[0].Offset[0]).normalized;
                go.transform.position = initialRoot + Vector3.up * .65f + initialDirection * 1.2f;
                Object.DestroyImmediate(initial.PatchMesh);
                using (var r = Renderer(1, settings, go.transform))
                {
                    r.SetWind(Vector3.zero, 0, Vector4.zero);
                    Vector3 root = r.Data.GetBlade(0).Root, direction = r.Data.GetRod(0).Rotation * Vector3.up;
                    for (int frame = 1; frame <= fps * 2; frame++)
                    {
                        go.transform.position = root + Vector3.up * .65f + direction * Mathf.Lerp(1.2f, -.15f, Mathf.Clamp01(frame / (float)fps));
                        r.Advance(1f / fps);
                    }
                    var state = r.ReadPlasticBends(); if (reference == null) reference = state;
                    float difference = 0;
                    for (int i = 0; i < state.Length; i++) difference = Mathf.Max(difference, Vector4.Distance(reference[i], state[i]));
                    Record("contact_fps_" + fps + "_plastic_error_rad", difference);
                    Require(difference < .002f, "Plastic history depends on render FPS.");
                }
                Object.DestroyImmediate(go);
            }
        }
        private static void Capture(string name, PBDGrassPatchRenderer r)
        {
            var rt = new RenderTexture(960, 640, 24); camera.targetTexture = rt;
            r.Draw(camera); camera.Render(); var previous = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(960, 640, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 960, 640), 0, 0); tex.Apply(); RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(output, name + ".png"), tex.EncodeToPNG());
            camera.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        }
        private static void CaptureComparison()
        {
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.3f, .35f, .4f);
            var sh = new SphericalHarmonicsL2(); sh.AddAmbientLight(RenderSettings.ambientLight); RenderSettings.ambientProbe = sh;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .18f, .23f);
            var light = new GameObject("Validation sun").AddComponent<Light>(); light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(55, -30, 0); light.intensity = 1;
            using (var r = Renderer(8, Settings(false), null, false)) Capture("short_reference", r);
            using (var r = Renderer(8, Settings())) Capture("long_rest", r);
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.localScale = Vector3.one * .76f; go.transform.position = Vector3.up * 10;
            var settings = Settings(); settings.ColliderRadius = .38f;
            using (var r = Renderer(4, settings, go.transform))
            {
                r.SetWind(Vector3.zero, 0, Vector4.zero); Press(r, go.transform, 0);
                Capture("long_pressed", r); go.transform.position = Vector3.up * 10; Ticks(r, 360);
                Capture("long_residual", r);
            }
            Object.DestroyImmediate(go);
            CaptureSingleLeaf();
        }
        private static void CaptureSingleLeaf()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.localScale = Vector3.one * .76f;
            Vector3 savedPosition = camera.transform.position; Quaternion savedRotation = camera.transform.rotation;
            foreach (bool plastic in new[] { false, true })
            {
                var patch = Patch(1);
                for (int blade = 1; blade < patch.Bodies.Length; blade++)
                    patch.Bodies[blade].Transform(Matrix4x4.Translate(new Vector3(100, 0, 0)));
                var settings = Settings(plastic); settings.ColliderRadius = .38f; go.transform.position = Vector3.up * 10;
                using (var r = new PBDGrassPatchRenderer(patch, solver, material, camera, wind, settings, new[] { go.transform }))
                {
                    Vector3 root = r.Data.GetBlade(0).Root;
                    Vector3 view = r.Data.GetRod(0).Rotation * new Vector3(.55f, 1, 0).normalized;
                    camera.transform.position = root + Vector3.up * .7f + view * 2.8f;
                    camera.transform.LookAt(root + Vector3.up * .7f); camera.fieldOfView = 35;
                    foreach (var light in Object.FindObjectsOfType<Light>())
                        light.transform.rotation = Quaternion.LookRotation(-view + Vector3.down * .7f);
                    r.SetWind(Vector3.zero, 0, Vector4.zero);
                    if (plastic) Capture("single_rest", r);
                    Press(r, go.transform, 0); Capture(plastic ? "single_plastic_pressed" : "single_elastic_pressed", r);
                    go.SetActive(false); Ticks(r, 360);
                    Capture(plastic ? "single_plastic_released" : "single_elastic_released", r); go.SetActive(true);
                }
                Object.DestroyImmediate(patch.PatchMesh);
            }
            camera.transform.SetPositionAndRotation(savedPosition, savedRotation); camera.fieldOfView = 60;
            Object.DestroyImmediate(go);
        }
        private static void CheckDemoScene()
        {
            foreach (var light in Object.FindObjectsOfType<Light>()) light.enabled = false;
            var scene = EditorSceneManager.OpenScene("Assets/PBDGrass/Scenes/PBDGrassDemo.unity", OpenSceneMode.Additive);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            GrassDemo demo = null;
            foreach (var root in scene.GetRootGameObjects()) { demo = root.GetComponentInChildren<GrassDemo>(); if (demo != null) break; }
            Require(demo != null, "Demo scene has no GrassDemo."); demo.enabled = false; demo.Initialize();
            Require(demo.Renderer.BladeCount == 32768 && demo.Renderer.ActiveBladeCount == 32768, "Saved scene no longer has the full benchmark.");
            Camera previousCamera = camera; camera = demo.Cam; camera.enabled = false;
            // Keep the designated culling projection fixed while the two offscreen targets are swapped.
            demo.Cam.aspect = 960f / 640;
            Capture("demo_main", demo.Renderer); uint mainVisible = demo.Renderer.VisibleGridCount();
            var probe = new GameObject("Scene camera").AddComponent<Camera>(); probe.enabled = false; probe.cameraType = CameraType.SceneView;
            probe.transform.position = new Vector3(18, 16, -22); probe.transform.LookAt(Vector3.up * .7f);
            probe.clearFlags = CameraClearFlags.SolidColor; probe.backgroundColor = Color.black;
            camera = probe; Capture("demo_scene", demo.Renderer); uint sceneVisible = demo.Renderer.VisibleGridCount();
            Record("demo_main_visible_grids", mainVisible); Record("demo_scene_visible_grids", sceneVisible);
            Record("demo_edit_preview_ticks", demo.Renderer.SimulationTicks);
            Require(mainVisible > 0 && sceneVisible == mainVisible && demo.Renderer.SimulationTicks == 0, "Scene camera changed culling or advanced physics.");
            demo.Cam.transform.position = new Vector3(0, 5, -22); demo.Cam.transform.rotation = Quaternion.LookRotation(Vector3.back);
            Capture("demo_scene_main_away", demo.Renderer);
            Require(demo.Renderer.VisibleGridCount() == 0, "Scene camera used its own culling camera.");
            demo.ResetDeformation(); demo.enabled = true; demo.enabled = false;
            Object.DestroyImmediate(probe.gameObject); camera = previousCamera; EditorSceneManager.CloseScene(scene, true);
        }
    }
}
