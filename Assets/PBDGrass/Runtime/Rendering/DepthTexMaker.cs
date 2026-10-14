using UnityEngine;

namespace GrassFinal.PBDGrass
{
    [RequireComponent(typeof(Camera))]
    public sealed class DepthTexMaker : MonoBehaviour
    {
        public Material DepthMat;
        public RenderTexture DepthTexture { get; private set; }
        private Material runtimeMaterial;
        private Matrix4x4 capturedVP;
        private bool valid;
        private void OnEnable()
        {
            GetComponent<Camera>().depthTextureMode |= DepthTextureMode.Depth;
            if (DepthMat == null) return;
            runtimeMaterial = new Material(DepthMat);
            DepthTexture = new RenderTexture(1024, 1024, 0, RenderTextureFormat.RFloat)
            {
                autoGenerateMips = false, useMipMap = true, filterMode = FilterMode.Point,
                name = "XPBD Grass Hi-Z"
            };
            DepthTexture.Create();
        }
        public bool CanCull(Camera camera)
        {
            if (!valid || DepthTexture == null) return false;
            Matrix4x4 current = camera.projectionMatrix * camera.worldToCameraMatrix;
            for (int i = 0; i < 16; i++) if (Mathf.Abs(current[i] - capturedVP[i]) > 0.00001f) return false;
            return true;
        }
        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            Texture depth = Shader.GetGlobalTexture("_CameraDepthTexture");
            if (DepthTexture != null && runtimeMaterial != null && depth != null)
            {
                RenderTexture last = null;
                int size = DepthTexture.width, level = 0;
                try
                {
                    while (size >= 1)
                    {
                        RenderTexture next = RenderTexture.GetTemporary(size, size, 0, DepthTexture.format);
                        next.filterMode = FilterMode.Point;
                        if (last == null) Graphics.Blit(depth, next);
                        else
                        {
                            runtimeMaterial.SetTexture("_DepthTexture", last);
                            runtimeMaterial.SetVector("_InvSize", new Vector4(1f / size, 1f / size, 0, 0));
                            Graphics.Blit(null, next, runtimeMaterial);
                            RenderTexture.ReleaseTemporary(last);
                        }
                        last = next;
                        Graphics.CopyTexture(last, 0, 0, DepthTexture, 0, level++);
                        size /= 2;
                    }
                    Camera camera = GetComponent<Camera>();
                    capturedVP = camera.projectionMatrix * camera.worldToCameraMatrix;
                    valid = true;
                }
                finally { if (last != null) RenderTexture.ReleaseTemporary(last); }
            }
            Graphics.Blit(source, destination);
        }
        private void OnDisable()
        {
            valid = false;
            if (DepthTexture != null)
            {
                DepthTexture.Release();
                if (Application.isPlaying) Destroy(DepthTexture); else DestroyImmediate(DepthTexture);
                DepthTexture = null;
            }
            if (runtimeMaterial != null)
            {
                if (Application.isPlaying) Destroy(runtimeMaterial); else DestroyImmediate(runtimeMaterial);
                runtimeMaterial = null;
            }
        }
    }
}
