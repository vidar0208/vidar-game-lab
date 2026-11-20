using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VirtualTexture
{
    // Port of the source's FeedbackReader: optional down-scale (keeping the finest mip of each block), then an
    // AsyncGPUReadback; finished readbacks go to the page table. Logic unchanged.
    // GameTech changes: linear targets (the default sRGB target would change the page numbers), explicit RGBA32
    // readback, the debug texture code left out.
    public class FeedbackReader : MonoBehaviour
    {
        public event Action<Texture2D> OnFeedbackReadComplete;
        public ScaleFactor m_ReadbackScale = ScaleFactor.Half;
        public Shader m_DownScaleShader;

        private Material m_DownScaleMaterial;
        private int m_DownScaleMaterialPass;
        private RenderTexture m_DownScaleTexture;
        private readonly Queue<AsyncGPUReadbackRequest> m_ReadbackRequests = new Queue<AsyncGPUReadbackRequest>();
        private Texture2D m_ReadbackTexture;
        private CommandBuffer cmd;
        public int ReadbacksDone { get; private set; }

        private void Start()
        {
            if (m_ReadbackScale != ScaleFactor.One)
            {
                m_DownScaleMaterial = new Material(m_DownScaleShader);
                switch (m_ReadbackScale)
                {
                    case ScaleFactor.Half: m_DownScaleMaterialPass = 0; break;
                    case ScaleFactor.Quarter: m_DownScaleMaterialPass = 1; break;
                    case ScaleFactor.Eighth: m_DownScaleMaterialPass = 2; break;
                }
            }
            GetComponent<FeedbackRendererURP>().OnFeedbackRenderComplete += NewRequest;
        }

        private void Update() { UpdateRequest(); }

        private void NewRequest(RenderTexture texture)
        {
            if (m_ReadbackRequests.Count > 8)
                return;
            var width = (int)(texture.width * m_ReadbackScale.ToFloat());
            var height = (int)(texture.height * m_ReadbackScale.ToFloat());
            if (m_ReadbackScale != ScaleFactor.One)
            {
                if (m_DownScaleTexture == null || m_DownScaleTexture.width != width || m_DownScaleTexture.height != height)
                {
                    m_DownScaleTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Point };
                }
                // full-screen triangle with a command buffer (GameTech: instead of Graphics.Blit)
                m_DownScaleMaterial.SetTexture("_MainTex", texture);
                m_DownScaleMaterial.SetVector("_SrcTexel", new Vector4(1f / texture.width, 1f / texture.height, 0, 0));
                m_DownScaleMaterial.SetVector("_BlitScaleBias", new Vector4(1, 1, 0, 0));
                if (cmd == null) cmd = new CommandBuffer { name = "VT feedback downscale" };
                cmd.Clear();
                cmd.SetRenderTarget(m_DownScaleTexture);
                cmd.DrawProcedural(Matrix4x4.identity, m_DownScaleMaterial, m_DownScaleMaterialPass, MeshTopology.Triangles, 3);
                Graphics.ExecuteCommandBuffer(cmd);
                texture = m_DownScaleTexture;
            }
            if (m_ReadbackTexture == null || m_ReadbackTexture.width != width || m_ReadbackTexture.height != height)
            {
                m_ReadbackTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
                m_ReadbackTexture.filterMode = FilterMode.Point;
                m_ReadbackTexture.wrapMode = TextureWrapMode.Clamp;
            }
            m_ReadbackRequests.Enqueue(AsyncGPUReadback.Request(texture, 0, TextureFormat.RGBA32));
        }

        private void UpdateRequest()
        {
            bool complete = false;
            while (m_ReadbackRequests.Count > 0)
            {
                var req = m_ReadbackRequests.Peek();
                if (req.hasError)
                {
                    m_ReadbackRequests.Dequeue();
                }
                else if (req.done)
                {
                    m_ReadbackTexture.GetRawTextureData<Color32>().CopyFrom(req.GetData<Color32>());
                    complete = true;
                    m_ReadbackRequests.Dequeue();
                }
                else
                {
                    break;
                }
            }
            if (complete)
            {
                ReadbacksDone++;
                OnFeedbackReadComplete?.Invoke(m_ReadbackTexture);
            }
        }
    }
}
