using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DepthTexMaker : MonoBehaviour
{
    public Material DepthMat;
    private RenderTexture DepthTex;
    private Camera cam;

    private void Start()
    {
        cam = GetComponent<Camera>();
        cam.depthTextureMode |= DepthTextureMode.Depth;

        DepthTex = new RenderTexture(1024, 1024, 0, RenderTextureFormat.RHalf);
        DepthTex.autoGenerateMips = false;

        DepthTex.useMipMap = true;
        DepthTex.filterMode = FilterMode.Point;
        DepthTex.Create();

        GrassInstancing.DepthTex = DepthTex;
        PBDGrassPatchRenderer.DepthTex = DepthTex;
    }

    int ID_DepthTexture;
    int ID_InvSize;

#if UNITY_EDITOR
    void Update()
    {
#else
    void OnPreRender()
    {
#endif
        int w = DepthTex.width;
        int h = DepthTex.height;
        int level = 0;

        RenderTexture lastRt = null;
        if (ID_DepthTexture == 0)
        {
            ID_DepthTexture = Shader.PropertyToID("_DepthTexture");
            ID_InvSize = Shader.PropertyToID("_InvSize");
        }
        RenderTexture tempRT;
        while (h > 8)
        {
            DepthMat.SetVector(ID_InvSize, new Vector4(1.0f / w, 1.0f / h, 0, 0));

            tempRT = RenderTexture.GetTemporary(w, h, 0, DepthTex.format);
            tempRT.filterMode = FilterMode.Point;
            if (lastRt == null)
            {
                Graphics.Blit(Shader.GetGlobalTexture("_CameraDepthTexture"), tempRT);
            }
            else
            {
                DepthMat.SetTexture(ID_DepthTexture, lastRt);
                Graphics.Blit(null, tempRT, DepthMat);
                RenderTexture.ReleaseTemporary(lastRt);
            }

            Graphics.CopyTexture(tempRT, 0, 0, DepthTex, 0, level);
            lastRt = tempRT;

            w /= 2;
            h /= 2;
            ++level;
        }

        RenderTexture.ReleaseTemporary(lastRt);
    }

    private void OnDestroy()
    {
        if (DepthTex != null)
            DepthTex.Release();
        Destroy(DepthTex);
    }
}
