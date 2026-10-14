using System.Collections.Generic;
using UnityEngine;

namespace GrassFinal.PBDGrass
{
    [ExecuteAlways]
    public sealed class GrassDemo : MonoBehaviour
    {
        [Min(1)] public int GrassSize = 32;
        public Material GrassProcedrualMaterial;
        public List<Transform> colliders = new List<Transform>();
        public ComputeShader PBDSolverCS;
        [Tooltip("World-space wind acceleration; old values such as 3000 are obsolete.")]
        public Vector3 WindForce = new Vector3(10, 0, 3);
        public Camera Cam;
        [Tooltip("Player or interaction center. Defaults to the grass patch center, independently of camera.")]
        public Transform SimulationFocus;
        public Texture2D WindNoiseTex;
        public float Frequency = 0.1f;
        public Vector4 TileAndOffset = new Vector4(0.01f, 0.01f, 0, 1);
        public int RandomSeed = 12345;
        public XPBDGrassSettings Physics = new XPBDGrassSettings();
        public PBDGrassPatchRenderer Renderer { get; private set; }

        private Vector3 savedWind;
        private int generatedGrassSize;
        private bool previewNeedsRebuild;
        private bool ResourcesAssigned => GrassProcedrualMaterial != null && PBDSolverCS != null
            && WindNoiseTex != null && Cam != null && gameObject.scene.IsValid() && gameObject.scene.isLoaded;

        private void OnEnable()
        {
            Camera.onPreCull -= DrawForCamera;
            Camera.onPreCull += DrawForCamera;
        }

        // OnValidate may run during deserialization. Defer GPU resource work to Update.
        private void OnValidate() => previewNeedsRebuild = true;

        public void Initialize()
        {
            if (Renderer != null) return;
            savedWind = WindForce;
            generatedGrassSize = Mathf.Max(1, GrassSize);
            if (GrassProcedrualMaterial == null || PBDSolverCS == null || WindNoiseTex == null || Cam == null)
            {
                Debug.LogError("Assign the grass material, compute shader, wind texture and camera.", this);
                enabled = false;
                return;
            }
            Random.State randomState = Random.state;
            PBDGrassPatch patch = null;
            try
            {
                Random.InitState(RandomSeed);
                patch = new PBDGrassPatch(transform.position, Mathf.Max(1, GrassSize), 32);
                Renderer = new PBDGrassPatchRenderer(patch, PBDSolverCS, GrassProcedrualMaterial,
                    Cam, WindNoiseTex, Physics, colliders);
                Renderer.SetWind(WindForce, Frequency, TileAndOffset);
                Renderer.SimulationCenter = SimulationFocus != null ? SimulationFocus.position : transform.position;
            }
            finally
            {
                Random.state = randomState;
                if (patch != null) DestroyMesh(patch.PatchMesh);
            }
        }

        private void Update()
        {
            bool playing = Application.IsPlaying(gameObject);
            if ((previewNeedsRebuild && !playing) || (Renderer != null && generatedGrassSize != Mathf.Max(1, GrassSize)))
                ReleaseRenderer();
            previewNeedsRebuild = false;
            if (!ResourcesAssigned) return;
            Initialize();
            Renderer.SetWind(WindForce, Frequency, TileAndOffset);
            Renderer.SimulationCenter = SimulationFocus != null ? SimulationFocus.position : transform.position;
            if (!playing) return;
            if (Input.GetKeyDown(KeyCode.Space))
            {
                if (WindForce.sqrMagnitude > 0) { savedWind = WindForce; WindForce = Vector3.zero; }
                else WindForce = savedWind;
                Renderer.SetWind(WindForce, Frequency, TileAndOffset);
            }
            // Render callbacks never advance physics, even with several Scene view cameras.
            Renderer.Advance(Time.deltaTime);
        }

        private void DrawForCamera(Camera renderCamera)
        {
            if (!isActiveAndEnabled || !ResourcesAssigned || renderCamera == null) return;
            if (renderCamera != Cam && renderCamera.cameraType != CameraType.SceneView) return;
            Initialize();
            Renderer.SimulationCenter = SimulationFocus != null ? SimulationFocus.position : transform.position;
            if (!Application.IsPlaying(gameObject)) Renderer.RefreshSimulationSelection();
            Renderer.Draw(renderCamera);
        }

        private void OnDisable()
        {
            Camera.onPreCull -= DrawForCamera;
            ReleaseRenderer();
        }

        private void OnDestroy()
        {
            Camera.onPreCull -= DrawForCamera;
            ReleaseRenderer();
        }

        private void ReleaseRenderer()
        {
            Renderer?.Dispose();
            Renderer = null;
        }

        public static void DestroyMesh(Mesh mesh)
        {
            if (mesh == null) return;
            if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
        }
    }
}
