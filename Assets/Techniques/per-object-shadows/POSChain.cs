// Runs the source's two URP passes from a script, after the scene camera has rendered:
//  pass 1 (PerObjectShadowPass): for every registered object, set the viewport to its atlas slice and its own
//         light-space view/projection, and draw its renderers with the material's DepthOnly pass into one
//         shadow-map atlas;
//  pass 2 (PerObjectShadowResolvePass): draw one instanced frustum cube per slice; the resolve shader rebuilds the
//         world position from the camera depth, projects it into the slice and blends the shadow over the target.
// The source passes are ScriptableRenderPasses built on URP's old RenderTargetHandle API, which URP 17 no longer
// has, so the same steps are recorded into a CommandBuffer here. The camera depth the resolve needs is drawn with
// the same DepthOnly pass (the source asks for a depth prepass or copy before the resolve).
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    public class POSChain
    {
        public readonly PerObjectShadowImpl impl;
        public readonly PerObjectShadowSettings settings;
        public RenderTexture atlas, sceneDepth, mask;
        public bool flipInvVP = true;
        readonly CommandBuffer cmd = new CommandBuffer { name = "GT per-object shadow" };
        readonly int W, H;

        public POSChain(PerObjectShadowSettings s, int w, int h)
        {
            settings = s; impl = new PerObjectShadowImpl(s); W = w; H = h;
            sceneDepth = new RenderTexture(w, h, 24, RenderTextureFormat.Depth) { name = "GT scene depth" }; sceneDepth.Create();
            mask = new RenderTexture(w, h, 0, RenderTextureFormat.R8) { name = "GT screen-space shadow mask" }; mask.Create();
        }

        public static int DepthPass(Material m) { int p = m.FindPass("DepthOnly"); return p >= 0 ? p : 3; }

        void Draw(Renderer r)
        {
            int n = r is SkinnedMeshRenderer sk ? sk.sharedMesh.subMeshCount : r.GetComponent<MeshFilter>().sharedMesh.subMeshCount;
            var m = r.sharedMaterial; // as in the source: the first material's depth pass for every submesh
            for (int i = 0; i < n; i++) cmd.DrawRenderer(r, m, i, DepthPass(m));
        }

        // pass 1: PerObjectShadowPass.Configure + Execute
        public void Atlas(Camera cam)
        {
            impl.SetupAtlasProperty();
            var res = impl.AtlasResolution;
            if (atlas == null || atlas.width != res.x || atlas.height != res.y)
            {
                if (atlas != null) atlas.Release();
                atlas = new RenderTexture(res.x, res.y, 16, RenderTextureFormat.Shadowmap) { name = PerObjectShadowImpl.s_PerObjectShadowAtlas, filterMode = FilterMode.Bilinear };
                atlas.Create();
            }
            impl.SetupRenderingData(cam);
            cmd.Clear();
            cmd.SetRenderTarget(atlas);
            cmd.ClearRenderTarget(true, true, Color.black);
            foreach (var s in impl.SliceDataList)
            {
                if (!s.ShouldRender) continue;
                var d = s.sliceDataPerFrame;
                cmd.SetViewport(new Rect(d.sliceOffset.x, d.sliceOffset.y, d.sliceResolution.x, d.sliceResolution.y));
                cmd.SetViewProjectionMatrices(d.shadowViewMatrix, d.shadowProjMatrix);
                foreach (var r in s.renderers) Draw(r);
            }
            cmd.SetViewProjectionMatrices(cam.worldToCameraMatrix, cam.projectionMatrix);
            Graphics.ExecuteCommandBuffer(cmd);
        }

        // camera depth for the resolve (the source relies on URP's depth prepass / copy depth)
        public void SceneDepth(Camera cam, Renderer[] all)
        {
            cmd.Clear();
            cmd.SetRenderTarget(sceneDepth);
            cmd.ClearRenderTarget(true, false, Color.black);
            cmd.SetViewProjectionMatrices(cam.worldToCameraMatrix, cam.projectionMatrix);
            foreach (var r in all) if (r.enabled && r.gameObject.activeInHierarchy) Draw(r);
            Graphics.ExecuteCommandBuffer(cmd);
        }

        // pass 2: PerObjectShadowResolvePass.Execute, into a colour target (blend into the scene) or into the R8 mask
        public void Resolve(Camera cam, Material mat, RenderTexture target, bool clearWhite)
        {
            cmd.Clear();
            cmd.SetRenderTarget(target);
            if (clearWhite) cmd.ClearRenderTarget(false, true, Color.white);
            cmd.SetViewProjectionMatrices(cam.worldToCameraMatrix, cam.projectionMatrix);
            var gpuP = GL.GetGPUProjectionMatrix(cam.projectionMatrix, flipInvVP);
            cmd.SetGlobalMatrix("unity_MatrixInvVP", (gpuP * cam.worldToCameraMatrix).inverse);
            cmd.SetGlobalVector("_ScaledScreenParams", new Vector4(W, H, 1f + 1f / W, 1f + 1f / H));
            cmd.SetGlobalTexture("_CameraDepthTexture", sceneDepth);
            cmd.SetGlobalTexture(PerObjectShadowImpl.s_PerObjectShadowAtlas, atlas);
            var uv = impl.WorldToAtlasUVMatrixArray;
            if (uv.Length > 0)
            {
                var model = impl.FrustumLocalToWorldMatrixArray;
                var block = new MaterialPropertyBlock(); // new each call: the slice count can change when an object is culled
                block.SetMatrixArray(PerObjectShadowImpl.s_WorldToUVMatrix, uv);
                block.SetVectorArray(PerObjectShadowImpl.s_SliceUVOffsetExtend, impl.SliceUVOffsetExtendArray);
                cmd.SetGlobalVector(PerObjectShadowImpl.s_TexelSize, impl.TexelSize);
                cmd.DrawMeshInstanced(PerObjectShadowImpl.FrustumCube, 0, mat, 0, model, model.Length, block);
            }
            Graphics.ExecuteCommandBuffer(cmd);
        }
    }
}
