using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VirtualTexture
{
    // Replaces the source's FeedbackRenderer (a second camera with a replacement shader, built-in pipeline only).
    // Same output: the virtual-textured objects drawn from the watching camera into a small linear target,
    // each pixel = (page x, page y, mip) / 255, cleared to white (= nothing). Drawn with a command buffer.
    public class FeedbackRendererURP : MonoBehaviour
    {
        public event Action<RenderTexture> OnFeedbackRenderComplete;
        public ScaleFactor m_Scale = ScaleFactor.Quarter;
        public Shader m_FeedbackShader;
        public int m_MipmapBias = 0;
        public Camera targetCamera;
        public Renderer[] targets = new Renderer[0];
        // size of the final image (0 = the camera's own pixel size); the capture renders off screen at a size
        // the camera does not report during LateUpdate
        public Vector2Int outputSize;

        public RenderTexture TargetTexture { get; private set; }
        Material mat;
        CommandBuffer cmd;

        void LateUpdate()
        {
            var cam = targetCamera;
            if (cam == null || m_FeedbackShader == null) return;
            if (mat == null) { mat = new Material(m_FeedbackShader); GetComponent<VTBinding>().Add(mat); cmd = new CommandBuffer { name = "VT feedback" }; }

            var scale = m_Scale.ToFloat();
            float pw = outputSize.x > 0 ? outputSize.x * cam.rect.width : cam.pixelWidth, ph = outputSize.y > 0 ? outputSize.y * cam.rect.height : cam.pixelHeight;
            int width = Mathf.Max(8, (int)(pw * scale)), height = Mathf.Max(8, (int)(ph * scale));
            if (TargetTexture == null || TargetTexture.width != width || TargetTexture.height != height)
            {
                if (TargetTexture != null) TargetTexture.Release();
                TargetTexture = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                { useMipMap = false, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point, name = "VT feedback" };
                TargetTexture.Create();
                // x: page table size (pages), y: virtual texture size (pixels) x feedback scale, z: max mip, w: mip bias
                var table = GetComponent<PageTable>(); var tiles = GetComponent<TiledTexture>();
                GetComponent<VTBinding>().SetVector("_VTFeedbackParam", new Vector4(table.TableSize, table.TableSize * tiles.TileSize * scale, table.MaxMipLevel, m_MipmapBias));
            }

            cmd.Clear();
            cmd.SetRenderTarget(TargetTexture);
            cmd.ClearRenderTarget(true, true, Color.white);
            cmd.SetViewProjectionMatrices(cam.worldToCameraMatrix, cam.projectionMatrix);
            foreach (var r in targets) if (r != null) cmd.DrawRenderer(r, mat, 0, 0);
            Graphics.ExecuteCommandBuffer(cmd);
            OnFeedbackRenderComplete?.Invoke(TargetTexture);
        }
    }
}
