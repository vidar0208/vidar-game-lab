// URP renderer feature port of VolumetricLightRenderer.cs + VolumetricLight.cs of SlightlyMad/VolumetricLights
// (BSD-3-Clause, Copyright (c) 2016, Michal Skalsky). The source fills a half-resolution light buffer from the
// built-in pipeline's command buffers (one light-volume mesh per light), blurs it and adds it in OnRenderImage.
// Here one render-graph pass after the opaque objects does the same chain at half resolution:
// depth downsample -> one full-screen ray march per volumetric light (shadows from URP's shadow maps) ->
// bilateral blur H + V -> bilateral upsample and add (the source's BlitAdd).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace GameTech.VolumetricLightsURP
{
    public class VolumetricLightFeature : ScriptableRendererFeature
    {
        public Shader shader;
        Material material;
        VolumetricLightPass pass;
        public static Texture DitherTex, NoiseTex;

        public override void Create()
        {
            if (shader == null) shader = Shader.Find("Hidden/GameTech/VolumetricLightURP");
            if (shader != null && material == null) material = CoreUtils.CreateEngineMaterial(shader);
            pass = new VolumetricLightPass { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (material == null || renderingData.cameraData.cameraType != CameraType.Game) return;
            var cam = renderingData.cameraData.camera;
            var settings = cam.GetComponent<VolumetricCamera>();
            if (settings == null || !settings.volumetric || VolumetricLight.All.Count == 0) return;
            pass.Setup(material, settings);
            pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            pass.requiresIntermediateTexture = true;
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing) { pass?.Dispose(); CoreUtils.Destroy(material); }
    }

    public class VolumetricLightPass : ScriptableRenderPass
    {
        Material material;
        VolumetricCamera settings;
        RTHandle halfDepth, halfVolume, halfTemp, colorCopy;
        public static int LastLights, LastWidth, LastHeight;

        static readonly int idPos = Shader.PropertyToID("_VL_Pos"), idAxis = Shader.PropertyToID("_VL_Axis"), idSpot = Shader.PropertyToID("_VL_Spot"),
            idColor = Shader.PropertyToID("_VL_Color"), idVL = Shader.PropertyToID("_VolumetricLight"), idMie = Shader.PropertyToID("_MieG"),
            idNoise = Shader.PropertyToID("_NoiseData"), idNoiseVel = Shader.PropertyToID("_NoiseVelocity"), idSamples = Shader.PropertyToID("_SampleCount"),
            idMaxRay = Shader.PropertyToID("_MaxRayLength"), idSrc = Shader.PropertyToID("_VL_Src"), idHalfDepth = Shader.PropertyToID("_VL_HalfDepth"),
            idHalfTexel = Shader.PropertyToID("_VL_HalfTexel"), idDither = Shader.PropertyToID("_GT_DitherTex"), idNoiseTex = Shader.PropertyToID("_GT_NoiseTex");

        public void Setup(Material m, VolumetricCamera s) { material = m; settings = s; }
        public void Dispose() { foreach (var h in new[] { halfDepth, halfVolume, halfTemp, colorCopy }) h?.Release(); }

        struct LightDraw
        {
            public int pass;
            public Vector4 pos, axis, spot, color, vl, mie, noise, noiseVel;
            public float samples, maxRay;
        }

        class PassData
        {
            public Material material;
            public TextureHandle color;
            public RTHandle halfDepth, halfVolume, halfTemp, colorCopy;
            public List<LightDraw> lights = new List<LightDraw>();
            public Vector4 halfTexel;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var lightData = frameData.Get<UniversalLightData>();
            if (resourceData.isActiveTargetBackBuffer) return;

            var desc = cameraData.cameraTargetDescriptor;
            desc.msaaSamples = 1; desc.depthBufferBits = 0; desc.depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None;
            int hw = Mathf.Max(1, desc.width / 2), hh = Mathf.Max(1, desc.height / 2);
            var half = new RenderTextureDescriptor(hw, hh, RenderTextureFormat.ARGBHalf, 0) { msaaSamples = 1, sRGB = false, useMipMap = false };
            var halfD = new RenderTextureDescriptor(hw, hh, RenderTextureFormat.RFloat, 0) { msaaSamples = 1, sRGB = false, useMipMap = false };
            RenderingUtils.ReAllocateHandleIfNeeded(ref halfDepth, halfD, FilterMode.Point, TextureWrapMode.Clamp, name: "_VL_HalfDepth");
            RenderingUtils.ReAllocateHandleIfNeeded(ref halfVolume, half, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_VL_HalfVolume");
            RenderingUtils.ReAllocateHandleIfNeeded(ref halfTemp, half, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_VL_HalfTemp");
            RenderingUtils.ReAllocateHandleIfNeeded(ref colorCopy, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_VL_ColorCopy");
            LastWidth = hw; LastHeight = hh;

            using (var builder = renderGraph.AddUnsafePass<PassData>("Volumetric Lights (URP)", out var data))
            {
                data.material = material; data.color = resourceData.activeColorTexture;
                data.halfDepth = halfDepth; data.halfVolume = halfVolume; data.halfTemp = halfTemp; data.colorCopy = colorCopy;
                data.halfTexel = new Vector4(1f / hw, 1f / hh, hw, hh);

                // VolumetricLight.SetupSpotLight / SetupPointLight / SetupDirectionalLight
                data.lights.Clear();   // pass data objects are pooled between frames
                var visible = lightData.visibleLights;
                foreach (var vl in VolumetricLight.All)
                {
                    var l = vl.Light;
                    if (l == null || !l.enabled) continue;
                    if (settings.skipDirectional && l.type == LightType.Directional) continue;
                    int visibleIndex = -1;
                    for (int i = 0; i < visible.Length; i++) if (visible[i].light == l) { visibleIndex = i; break; }
                    if (visibleIndex < 0) continue;   // culled
                    // additional-light index used by URP's shadow data: visible index without the main light
                    int addIndex = visibleIndex - (lightData.mainLightIndex >= 0 && visibleIndex > lightData.mainLightIndex ? 1 : 0);
                    bool shadows = settings.shadows && l.shadows != LightShadows.None;
                    var d = new LightDraw
                    {
                        color = l.color.linear * l.intensity,
                        vl = new Vector4(vl.ScatteringCoef, vl.ExtinctionCoef, l.range, 0),
                        mie = vl.MieVector,
                        noise = new Vector4(vl.NoiseScale, vl.NoiseIntensity, vl.NoiseIntensityOffset, vl.Noise ? 1 : 0),
                        noiseVel = new Vector4(vl.NoiseVelocity.x, vl.NoiseVelocity.y) * vl.NoiseScale,
                        samples = settings.sampleOverride > 0 ? settings.sampleOverride : vl.SampleCount,
                        maxRay = vl.MaxRayLength
                    };
                    var fwd = l.transform.forward;
                    if (l.type == LightType.Directional)
                    {
                        d.pass = 1; d.axis = new Vector4(fwd.x, fwd.y, fwd.z, 2); d.spot = new Vector4(0, 0, 0, shadows ? 1 : 0);
                    }
                    else
                    {
                        d.pass = 0;
                        d.pos = new Vector4(l.transform.position.x, l.transform.position.y, l.transform.position.z, l.range);
                        bool spot = l.type == LightType.Spot;
                        d.axis = new Vector4(fwd.x, fwd.y, fwd.z, spot ? 0 : 1);
                        float cosOuter = Mathf.Cos((l.spotAngle + 1) * 0.5f * Mathf.Deg2Rad);   // the source's _CosAngle
                        float cosInner = Mathf.Cos(Mathf.Max(l.innerSpotAngle, l.spotAngle * 0.6f) * 0.5f * Mathf.Deg2Rad);
                        d.spot = new Vector4(cosOuter, cosInner, addIndex, shadows ? 1 : 0);
                    }
                    data.lights.Add(d);
                }
                LastLights = data.lights.Count;

                builder.UseTexture(data.color, AccessFlags.ReadWrite);
                if (resourceData.cameraDepthTexture.IsValid()) builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                if (resourceData.mainShadowsTexture.IsValid()) builder.UseTexture(resourceData.mainShadowsTexture, AccessFlags.Read);
                if (resourceData.additionalShadowsTexture.IsValid()) builder.UseTexture(resourceData.additionalShadowsTexture, AccessFlags.Read);
                foreach (var hnd in new[] { halfDepth, halfVolume, halfTemp, colorCopy })
                    builder.UseTexture(renderGraph.ImportTexture(hnd), AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((PassData d, UnsafeGraphContext ctx) => Execute(d, CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd)));
            }
        }

        static void Execute(PassData d, CommandBuffer cmd)
        {
            var m = d.material;
            RTHandle color = d.color;
            cmd.SetGlobalTexture(idDither, VolumetricLightFeature.DitherTex);
            if (VolumetricLightFeature.NoiseTex != null) cmd.SetGlobalTexture(idNoiseTex, VolumetricLightFeature.NoiseTex);
            cmd.SetGlobalVector(idHalfTexel, d.halfTexel);
            var sb = new Vector4(1, 1, 0, 0);

            // half-resolution depth (BilateralBlur pass "downsample depth")
            cmd.SetRenderTarget(d.halfDepth, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            Blitter.BlitTexture(cmd, d.halfVolume, sb, m, 2);

            // light buffer: cleared to (0, 0, 0, 1) as in the source, then one ray-march draw per light
            cmd.SetRenderTarget(d.halfVolume, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            cmd.ClearRenderTarget(false, true, new Color(0, 0, 0, 1));
            foreach (var l in d.lights)
            {
                cmd.SetGlobalVector(idPos, l.pos); cmd.SetGlobalVector(idAxis, l.axis); cmd.SetGlobalVector(idSpot, l.spot);
                cmd.SetGlobalVector(idColor, l.color); cmd.SetGlobalVector(idVL, l.vl); cmd.SetGlobalVector(idMie, l.mie);
                cmd.SetGlobalVector(idNoise, l.noise); cmd.SetGlobalVector(idNoiseVel, l.noiseVel);
                cmd.SetGlobalFloat(idSamples, l.samples); cmd.SetGlobalFloat(idMaxRay, l.maxRay);
                Blitter.BlitTexture(cmd, d.halfDepth, sb, m, l.pass);
            }

            // bilateral blur at half resolution, horizontal then vertical
            cmd.SetGlobalTexture(idHalfDepth, d.halfDepth);
            Blitter.BlitCameraTexture(cmd, d.halfVolume, d.halfTemp, m, 3);
            Blitter.BlitCameraTexture(cmd, d.halfTemp, d.halfVolume, m, 4);

            // bilateral upsample + add: scene * volume.a + volume.rgb
            Blitter.BlitCameraTexture(cmd, color, d.colorCopy);
            cmd.SetGlobalTexture(idSrc, d.halfVolume);
            Blitter.BlitCameraTexture(cmd, d.colorCopy, color, m, 5);
        }
    }
}
