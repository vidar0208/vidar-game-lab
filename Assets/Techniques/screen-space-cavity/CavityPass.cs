// Runs the cavity port from a script. The source (ScreenSpaceCavity.cs, a URP 10 ScriptableRendererFeature) asks URP
// for the depth and DepthNormals textures, draws one full-screen pass into an R8 texture before opaques and publishes
// it as _ScreenSpaceCavityTexture for the lit shader. URP 17 dropped the RenderTargetHandle API it is built on, so the
// same steps are recorded into a CommandBuffer here: (1) draw the view-space normal + linear depth buffer, (2) the
// full-screen cavity pass with the source's parameter packing and keywords, (3) the lit shader reads the result.
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    // Same fields and defaults as ScreenSpaceCavitySettings in the source.
    [System.Serializable]
    public class CavitySettings
    {
        public enum CavityType { Both = 0, Curvature = 1, Cavity = 2 }
        public CavityType cavityType = CavityType.Both;
        public float curvatureScale = 1.0f;
        public float curvatureRidge = 0.25f;
        public float curvatureValley = 0.25f;
        public float cavityDistance = 0.25f;
        public float cavityAttenuation = 0.015625f;
        public float cavityRidge = 1.25f;
        public float cavityValley = 1.25f;
        public int cavitySamples = 4;

        public CavitySettings Copy() => (CavitySettings)MemberwiseClone();
    }

    public class CavityPass
    {
        public readonly RenderTexture normalDepth;
        public readonly RenderTexture[] cavity = new RenderTexture[3];
        readonly Material mat;
        readonly CommandBuffer cmd = new CommandBuffer { name = "GT screen-space cavity" };
        readonly int W, H;

        public CavityPass(Shader shader, int w, int h)
        {
            W = w; H = h; mat = new Material(shader);
            normalDepth = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBFloat) { name = "GT view normal + depth", filterMode = FilterMode.Point };
            normalDepth.Create();
            for (int i = 0; i < 3; i++)
            {
                cavity[i] = new RenderTexture(w, h, 0, RenderTextureFormat.R8) { name = "GT cavity " + i, filterMode = FilterMode.Bilinear };
                cavity[i].Create();
            }
        }

        // (1) stands in for URP 10's depth + DepthNormals textures
        public void NormalDepth(Camera cam, Renderer[] all)
        {
            cmd.Clear();
            cmd.SetRenderTarget(normalDepth);
            cmd.ClearRenderTarget(true, true, new Color(0, 0, 1, 10000f));
            cmd.SetViewProjectionMatrices(cam.worldToCameraMatrix, cam.projectionMatrix);
            foreach (var r in all)
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                var mats = r.sharedMaterials;
                int n = r is SkinnedMeshRenderer sk ? sk.sharedMesh.subMeshCount : r.GetComponent<MeshFilter>().sharedMesh.subMeshCount;
                for (int i = 0; i < n; i++)
                {
                    var m = mats[Mathf.Min(i, mats.Length - 1)];
                    int p = m.FindPass("GTViewNormalDepth");
                    if (p >= 0) cmd.DrawRenderer(r, m, i, p);
                }
            }
            Graphics.ExecuteCommandBuffer(cmd);
        }

        // (2) ScreenSpaceCavityPass.OnCameraSetup + Execute
        public RenderTexture Cavity(int slot, Camera cam, CavitySettings s)
        {
            mat.SetVector("_CurvatureParams", new Vector4(
                s.curvatureScale,
                s.cavitySamples,
                0.5f / Mathf.Max(Mathf.Sqrt(s.curvatureRidge), 1e-4f),
                0.7f / Mathf.Max(Mathf.Sqrt(s.curvatureValley), 1e-4f)));
            mat.SetVector("_CavityParams", new Vector4(s.cavityDistance, s.cavityAttenuation, s.cavityRidge, s.cavityValley));
            bool curv = s.cavityType != CavitySettings.CavityType.Cavity, cav = s.cavityType != CavitySettings.CavityType.Curvature;
            if (curv) mat.EnableKeyword("_TYPE_CURVATURE"); else mat.DisableKeyword("_TYPE_CURVATURE");
            if (cav) mat.EnableKeyword("_TYPE_CAVITY"); else mat.DisableKeyword("_TYPE_CAVITY");
            mat.SetVector("_SourceSize", new Vector4(W, H, 1f / W, 1f / H));
            mat.SetMatrix("_SSCProj", cam.projectionMatrix);
            mat.SetTexture("_SSCNormalDepth", normalDepth);

            cmd.Clear();
            cmd.SetRenderTarget(cavity[slot]);
            cmd.DrawProcedural(Matrix4x4.identity, mat, 0, MeshTopology.Triangles, 3);
            Graphics.ExecuteCommandBuffer(cmd);
            return cavity[slot];
        }
    }
}
