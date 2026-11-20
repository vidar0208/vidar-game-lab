// URP renderer feature port of Kino Motion (keijiro/KinoMotion, MIT): ReconstructionFilter + FrameBlendingFilter.
// The source runs in OnRenderImage on the built-in pipeline; here one render-graph pass runs before URP's post
// processing (so the blur works on HDR colour) and issues the same chain of blits:
// velocity setup -> tile max /2 /4 /8 -> tile max to tile size -> neighbour max -> reconstruction -> frame blending.
// Settings and the 4-frame history come from a KinoMotionCamera component on each camera.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace GameTech.KinoMotionURP
{
    public class KinoMotionFeature : ScriptableRendererFeature
    {
        public Shader shader;
        Material material;
        KinoMotionPass pass;

        public override void Create()
        {
            if (shader == null) shader = Shader.Find("Hidden/GameTech/KinoMotionURP");
            if (shader != null && material == null) material = CoreUtils.CreateEngineMaterial(shader);
            pass = new KinoMotionPass { renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var cam = renderingData.cameraData.camera;
            if (material == null || renderingData.cameraData.cameraType != CameraType.Game) return;
            var settings = cam.GetComponent<KinoMotionCamera>();
            if (settings == null || !settings.enabled) return;
            pass.Setup(material, settings);
            pass.ConfigureInput(ScriptableRenderPassInput.Motion | ScriptableRenderPassInput.Depth);
            pass.requiresIntermediateTexture = true;
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            pass?.Dispose();
            CoreUtils.Destroy(material);
        }
    }

    public class KinoMotionPass : ScriptableRenderPass
    {
        const float kMaxBlurRadius = 5;   // percent of the screen height, as in the source
        Material material;
        KinoMotionCamera settings;
        RTHandle colorCopy, recon, vbuffer, tile2, tile4, tile8, tile, neighborMax;

        public static int LastMaxBlurPixels, LastTileSize;   // for the post's log
        public static float[] LastWeights = new float[4];

        static readonly int idVelocityScale = Shader.PropertyToID("_VelocityScale"), idMaxBlur = Shader.PropertyToID("_MaxBlurRadius"),
            idRcpMaxBlur = Shader.PropertyToID("_RcpMaxBlurRadius"), idLoop = Shader.PropertyToID("_LoopCount"),
            idTileLoop = Shader.PropertyToID("_TileMaxLoop"), idTileOffs = Shader.PropertyToID("_TileMaxOffs"),
            idMain = Shader.PropertyToID("_KmMainTexel"), idVel = Shader.PropertyToID("_KmVelTexel"), idNbr = Shader.PropertyToID("_KmNbrTexel"),
            idVelTex = Shader.PropertyToID("_VelocityTex"), idNbrTex = Shader.PropertyToID("_NeighborMaxTex");
        static readonly int[] idHist = { Shader.PropertyToID("_History1Tex"), Shader.PropertyToID("_History2Tex"), Shader.PropertyToID("_History3Tex"), Shader.PropertyToID("_History4Tex") };
        static readonly int[] idWeight = { Shader.PropertyToID("_History1Weight"), Shader.PropertyToID("_History2Weight"), Shader.PropertyToID("_History3Weight"), Shader.PropertyToID("_History4Weight") };

        public void Setup(Material m, KinoMotionCamera s) { material = m; settings = s; }

        public void Dispose()
        {
            foreach (var h in new[] { colorCopy, recon, vbuffer, tile2, tile4, tile8, tile, neighborMax }) h?.Release();
        }

        class PassData
        {
            public Material material;
            public KinoMotionCamera settings;
            public TextureHandle color;
            public RTHandle colorCopy, recon, vbuffer, tile2, tile4, tile8, tile, neighborMax;
            public int width, height;
            public float time;
            public int frame;
        }

        static RenderTextureDescriptor Desc(int w, int h, RenderTextureFormat f)
        {
            var d = new RenderTextureDescriptor(Mathf.Max(1, w), Mathf.Max(1, h), f, 0) { msaaSamples = 1, sRGB = false, useMipMap = false };
            return d;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            if (resourceData.isActiveTargetBackBuffer) return;

            var desc = cameraData.cameraTargetDescriptor;
            desc.msaaSamples = 1; desc.depthBufferBits = 0; desc.depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None;
            int w = desc.width, h = desc.height;
            var packed = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGB2101010) ? RenderTextureFormat.ARGB2101010 : RenderTextureFormat.ARGB32;
            var vec = RenderTextureFormat.RGHalf;
            int maxBlurPixels = (int)(kMaxBlurRadius * h / 100);
            int tileSize = ((maxBlurPixels - 1) / 8 + 1) * 8;
            LastMaxBlurPixels = maxBlurPixels; LastTileSize = tileSize;

            RenderingUtils.ReAllocateHandleIfNeeded(ref colorCopy, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_KmColorCopy");
            RenderingUtils.ReAllocateHandleIfNeeded(ref recon, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_KmRecon");
            RenderingUtils.ReAllocateHandleIfNeeded(ref vbuffer, Desc(w, h, packed), FilterMode.Point, TextureWrapMode.Clamp, name: "_KmVelocity");
            RenderingUtils.ReAllocateHandleIfNeeded(ref tile2, Desc(w / 2, h / 2, vec), FilterMode.Point, TextureWrapMode.Clamp, name: "_KmTile2");
            RenderingUtils.ReAllocateHandleIfNeeded(ref tile4, Desc(w / 4, h / 4, vec), FilterMode.Point, TextureWrapMode.Clamp, name: "_KmTile4");
            RenderingUtils.ReAllocateHandleIfNeeded(ref tile8, Desc(w / 8, h / 8, vec), FilterMode.Point, TextureWrapMode.Clamp, name: "_KmTile8");
            RenderingUtils.ReAllocateHandleIfNeeded(ref tile, Desc(w / tileSize, h / tileSize, vec), FilterMode.Point, TextureWrapMode.Clamp, name: "_KmTile");
            RenderingUtils.ReAllocateHandleIfNeeded(ref neighborMax, Desc(w / tileSize, h / tileSize, vec), FilterMode.Point, TextureWrapMode.Clamp, name: "_KmNeighborMax");
            for (int i = 0; i < 4; i++)
                if (RenderingUtils.ReAllocateHandleIfNeeded(ref settings.history[i], desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_KmHistory" + i))
                    settings.historyValid[i] = false;

            using (var builder = renderGraph.AddUnsafePass<PassData>("Kino Motion (URP)", out var data))
            {
                data.material = material; data.settings = settings;
                data.color = resourceData.activeColorTexture;
                data.colorCopy = colorCopy; data.recon = recon; data.vbuffer = vbuffer; data.tile2 = tile2; data.tile4 = tile4;
                data.tile8 = tile8; data.tile = tile; data.neighborMax = neighborMax;
                data.width = w; data.height = h; data.time = Time.time; data.frame = Time.frameCount;
                builder.UseTexture(data.color, AccessFlags.ReadWrite);
                if (resourceData.motionVectorColor.IsValid()) builder.UseTexture(resourceData.motionVectorColor, AccessFlags.Read);
                if (resourceData.cameraDepthTexture.IsValid()) builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                foreach (var hnd in new[] { colorCopy, recon, vbuffer, tile2, tile4, tile8, tile, neighborMax })
                    builder.UseTexture(renderGraph.ImportTexture(hnd), AccessFlags.ReadWrite);
                for (int i = 0; i < 4; i++) builder.UseTexture(renderGraph.ImportTexture(settings.history[i]), AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((PassData d, UnsafeGraphContext ctx) => Execute(d, CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd)));
            }
        }

        static Vector4 Texel(RTHandle h) { var r = h.rt; return new Vector4(1f / r.width, 1f / r.height, r.width, r.height); }

        static void Blit(CommandBuffer cmd, RTHandle src, RTHandle dst, Material m, int pass)
        {
            cmd.SetGlobalVector(idMain, Texel(src));
            Blitter.BlitCameraTexture(cmd, src, dst, m, pass);
        }

        static void Execute(PassData d, CommandBuffer cmd)
        {
            var s = d.settings; var m = d.material;
            RTHandle color = d.color;
            float shutter = s.suppressFrames > 0 ? 0f : s.shutterAngle;
            Blitter.BlitCameraTexture(cmd, color, d.colorCopy);
            var current = d.colorCopy;

            if (shutter > 0)
            {
                // ReconstructionFilter.ProcessImage
                int maxBlurPixels = (int)(kMaxBlurRadius * d.height / 100);
                int tileSize = ((maxBlurPixels - 1) / 8 + 1) * 8;
                cmd.SetGlobalFloat(idVelocityScale, shutter / 360);
                cmd.SetGlobalFloat(idMaxBlur, maxBlurPixels);
                cmd.SetGlobalFloat(idRcpMaxBlur, 1.0f / maxBlurPixels);
                cmd.SetGlobalVector(idMain, Texel(d.vbuffer));
                Blitter.BlitCameraTexture(cmd, d.colorCopy, d.vbuffer, m, 0);   // velocity setup (reads motion vectors + depth)
                Blit(cmd, d.vbuffer, d.tile2, m, 1);
                Blit(cmd, d.tile2, d.tile4, m, 2);
                Blit(cmd, d.tile4, d.tile8, m, 2);
                cmd.SetGlobalVector(idTileOffs, Vector2.one * (tileSize / 8.0f - 1) * -0.5f);
                cmd.SetGlobalFloat(idTileLoop, tileSize / 8);
                Blit(cmd, d.tile8, d.tile, m, 3);
                Blit(cmd, d.tile, d.neighborMax, m, 4);
                cmd.SetGlobalFloat(idLoop, Mathf.Clamp(s.sampleCount, 2, 64) / 2);
                cmd.SetGlobalTexture(idNbrTex, d.neighborMax);
                cmd.SetGlobalTexture(idVelTex, d.vbuffer);
                cmd.SetGlobalVector(idVel, Texel(d.vbuffer));
                cmd.SetGlobalVector(idNbr, Texel(d.neighborMax));
                Blit(cmd, d.colorCopy, d.recon, m, 5);
                current = d.recon;
            }

            if (s.frameBlending > 0)
            {
                // FrameBlendingFilter.BlendFrames (raw path), then PushFrame
                for (int k = 1; k <= 4; k++)
                {
                    int slot = (d.frame + 4 - k) % 4;
                    cmd.SetGlobalTexture(idHist[k - 1], s.history[slot]);
                    float w = s.Weight(slot, s.frameBlending, d.time); LastWeights[k - 1] = w;
                    cmd.SetGlobalFloat(idWeight[k - 1], w);
                }
                Blit(cmd, current, color, m, 6);
                if (d.frame != s.lastPushFrame)
                {
                    int idx = d.frame % 4;
                    Blitter.BlitCameraTexture(cmd, current, s.history[idx]);
                    s.historyTime[idx] = d.time; s.historyValid[idx] = true; s.lastPushFrame = d.frame;
                }
            }
            else
            {
                Blitter.BlitCameraTexture(cmd, current, color);
            }
            if (s.suppressFrames > 0) s.suppressFrames--;
        }
    }
}
