// URP port of arigo's ShadowVSM component (MIT, licence in LICENSE.txt next to this file). Same steps and defaults:
// an orthographic box on the sun's rotation renders depth and depth squared for numCascades concentric boxes (each
// twice the size of the one before, first one firstCascadeLevelSize half-height), every cascade is blurred with the
// source's two-pass blur shader (vertical, then horizontal plus normalise), and the receivers get VSM_ShadowTex,
// VSM_LightMatrix, VSM_LightMatrixNormal and VSM_InvNumCascades. The box sits at a fixed position, as in the source's
// "_shadowCameraFollowsMainCamera = false" mode with SetShadowCameraPosition.
// Changes: URP has no Camera.RenderWithShader, so the casters are drawn with a CommandBuffer (DrawRenderer with the
// depth material) once per frame from URP's beginContextRendering; cascades go into the slices of a texture array
// instead of rows of one tall atlas; three versions of each cascade are kept for the comparison: unblurred, the
// source's default 3-tap box blur and the source's optional 21-tap Gaussian (LARGE_BLUR).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-50)]
    public class ShadowVSMURP : MonoBehaviour
    {
        public Shader depthShader, blurShader;
        public int resolution = 512;
        public int numCascades = 6;
        public float firstCascadeLevelSize = 8f;
        public float depthOfShadowRange = 1000f;
        public Light sun;
        public Vector3 shadowCamPosition;
        public Renderer[] casters;

        public enum Version { Unblurred = 0, Box3 = 1, Gauss21 = 2 }
        public RenderTexture Get(Version v) => outs[(int)v];
        public int UpdatesDone { get; private set; }
        public int DrawCallsPerUpdate { get; private set; }
        public long BytesPerVersion => (long)resolution * resolution * numCascades * 8;   // RGFloat

        RenderTexture target;
        readonly RenderTexture[] outs = new RenderTexture[3];
        Material depthMat;
        readonly Material[] blurY = new Material[3], blurX = new Material[3];
        CommandBuffer cmd;
        int lastFrame = -1;
        static readonly int TmpId = Shader.PropertyToID("_GT_VSMBlurTmp");
        static readonly int CascadeScaleId = Shader.PropertyToID("_GT_VSMCascadeScale");

        void OnEnable() { RenderPipelineManager.beginContextRendering += OnContext; }
        void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= OnContext;
            if (target) target.Release();
            foreach (var o in outs) if (o) o.Release();
            cmd?.Release(); cmd = null;
        }

        Material Blur(string kwPass, string kwSize)
        {
            var m = new Material(blurShader);
            m.EnableKeyword(kwPass);
            if (kwSize != null) m.EnableKeyword(kwSize);
            m.SetVector("BlurPixelSize", new Vector2(1f / resolution, 1f / resolution));
            m.SetColor("_Color", new Color(0, 0, 0, 0));
            return m;
        }

        void Init()
        {
            // target: 'res x res' ARGBFloat with 4x MSAA and a 24-bit depth buffer, like the source's CreateTarget
            var desc = new RenderTextureDescriptor(resolution, resolution, RenderTextureFormat.ARGBFloat, 24) { msaaSamples = 4 };
            target = new RenderTexture(desc) { wrapMode = TextureWrapMode.Clamp, name = "VSM target" };
            target.Create();
            for (int i = 0; i < 3; i++)
            {
                var d = new RenderTextureDescriptor(resolution, resolution, RenderTextureFormat.RGFloat, 0)
                { dimension = TextureDimension.Tex2DArray, volumeDepth = numCascades };
                outs[i] = new RenderTexture(d) { filterMode = i == 0 ? FilterMode.Point : FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "VSM " + (Version)i };
                outs[i].Create();
            }
            depthMat = new Material(depthShader);
            string[] size = { "GT_NO_BLUR", null, "LARGE_BLUR" };
            for (int i = 0; i < 3; i++) { blurY[i] = Blur("BLUR_Y", size[i]); blurX[i] = Blur("BLUR_LINEAR_AND_SQUARE_PART", size[i]); }
            cmd = new CommandBuffer { name = "ShadowVSM (URP)" };
        }

        void SetLightMatrices()
        {
            // same as the source's UpdateShadowCamTransformShaderValues (aspect 1, internal scale 1)
            var worldToLocal = Matrix4x4.TRS(shadowCamPosition, sun.transform.rotation, Vector3.one).inverse;
            Vector3 size;
            size.y = firstCascadeLevelSize * 2;
            size.x = size.y;
            size.z = depthOfShadowRange * 2;
            size.x = 1f / size.x; size.y = 1f / size.y; size.z = 128f / size.z;
            var lightMatrix = Matrix4x4.Scale(size) * worldToLocal;
            var lightMatrixNormal = Matrix4x4.Scale(Vector3.one * 1.2f / resolution) * worldToLocal;
            Shader.SetGlobalMatrix("VSM_LightMatrix", lightMatrix);
            Shader.SetGlobalMatrix("VSM_LightMatrixNormal", lightMatrixNormal);
            Shader.SetGlobalFloat("VSM_InvNumCascades", 1f / numCascades);
        }

        void OnContext(ScriptableRenderContext ctx, List<Camera> cams)
        {
            if (Time.frameCount == lastFrame || sun == null) return;
            lastFrame = Time.frameCount;
            if (cmd == null) Init();
            SetLightMatrices();

            cmd.Clear();
            int draws = 0;
            cmd.GetTemporaryRT(TmpId, resolution, resolution, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBFloat);
            for (int lvl = numCascades - 1; lvl >= 0; lvl--)
            {
                cmd.SetRenderTarget(target);
                cmd.ClearRenderTarget(true, true, new Color(0, 0, 0, 0));
                cmd.SetGlobalFloat(CascadeScaleId, Mathf.Pow(2, -lvl));
                foreach (var r in casters)
                {
                    if (r == null || !r.gameObject.activeInHierarchy) continue;
                    int n = r.sharedMaterials.Length;
                    for (int s = 0; s < n; s++) { cmd.DrawRenderer(r, depthMat, s, 0); draws++; }
                }
                for (int v = 0; v < 3; v++)
                {
                    cmd.Blit(target, TmpId, blurY[v], 0);
                    cmd.Blit(TmpId, outs[v], blurX[v], 0, lvl);
                }
            }
            cmd.ReleaseTemporaryRT(TmpId);
            ctx.ExecuteCommandBuffer(cmd);
            DrawCallsPerUpdate = draws;
            UpdatesDone++;
        }
    }
}
