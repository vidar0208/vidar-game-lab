// Port of the ConvolutionBloom render pass from AKGWSB/FFTConvolutionBloom (MIT). Same textures (512 x 512 source and
// kernel, 1024 x 512 frequency textures, ARGBFloat, random write), same parameters, same order of blits and compute
// dispatches, run with Graphics.Blit and ComputeShader.Dispatch from a script instead of a ScriptableRendererFeature.
using UnityEngine;

namespace GameTech.Posts
{
    public class FFTBloomChain
    {
        // ConvolitonBloomSettings, same defaults
        public float intensity = 1f, threshold = 0f;
        public Vector2 kernelPositionOffset = Vector2.zero, kernelSizeScale = Vector2.one;
        public float kernelDistanceExp = 0f, kernelDistanceExpClampMin = 1f, kernelDistanceExpScale = 1f;
        public bool kernelImageUseLuminanceAsRGB;
        public bool flipSource, flipRead; // GT: the source flips the camera target; see FFTBloomSource

        readonly ComputeShader cs;
        readonly Material kernelGenerate, sourceGenerate, finalBlit;
        readonly RenderTexture source, kernel, sourceFrequency, kernelFrequency;
        readonly int kFwdH, kInvH, kVert, kConv, kKernelTransform;

        public RenderTexture SourceTexture => source;
        public RenderTexture SourceFrequency => sourceFrequency;

        static RenderTexture RT(int w, int h)
        {
            var d = new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGBFloat, 0) { msaaSamples = 1, enableRandomWrite = true };
            var r = new RenderTexture(d) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            r.Create(); return r;
        }

        public FFTBloomChain(ComputeShader cs, Material kernelGenerate, Material sourceGenerate, Material finalBlit)
        {
            this.cs = cs; this.kernelGenerate = kernelGenerate; this.sourceGenerate = sourceGenerate; this.finalBlit = finalBlit;
            source = RT(512, 512); kernel = RT(512, 512);
            sourceFrequency = RT(1024, 512); kernelFrequency = RT(1024, 512);
            kFwdH = cs.FindKernel("TwoForOneFFTForwardHorizontal");
            kInvH = cs.FindKernel("TwoForOneFFTInverseHorizontal");
            kVert = cs.FindKernel("FFTVertical");
            kConv = cs.FindKernel("Convolution");
            kKernelTransform = cs.FindKernel("KernelTransform");
        }

        // target must already hold the scene: the final blit adds the bloom on top (Blend One One).
        public void Run(Texture sceneHdr, Texture kernelTexture, RenderTexture target)
        {
            Shader.SetGlobalFloat("FFTBloomIntensity", intensity);
            Shader.SetGlobalFloat("FFTBloomThreshold", threshold);
            Shader.SetGlobalVector("FFTBloomKernelGenParam", new Vector4(kernelPositionOffset.x, kernelPositionOffset.y, kernelSizeScale.x, kernelSizeScale.y));
            Shader.SetGlobalVector("FFTBloomKernelGenParam1", new Vector4(kernelDistanceExp, kernelDistanceExpClampMin, kernelDistanceExpScale, kernelImageUseLuminanceAsRGB ? 1f : 0f));
            Shader.SetGlobalVector("_GTSrcSize", new Vector4(sceneHdr.width, sceneHdr.height, flipSource ? 1 : 0, flipRead ? 1 : 0));

            // kernel image -> 512 x 512, then move its centre to the corner
            Graphics.Blit(kernelTexture, kernel, kernelGenerate);
            cs.SetTexture(kKernelTransform, "SourceTexture", kernel);
            cs.Dispatch(kKernelTransform, 512 / 8, 256 / 8, 1);

            // scene colour -> 512 x 512 (aspect kept, threshold applied)
            Graphics.Blit(sceneHdr, source, sourceGenerate);

            // FFT of the kernel: horizontal (two channels per complex FFT), then vertical
            cs.SetTexture(kFwdH, "SourceTexture", kernel);
            cs.SetTexture(kFwdH, "FrequencyTexture", kernelFrequency);
            cs.Dispatch(kFwdH, 512, 1, 1);
            cs.SetFloat("IsForward", 1f);
            cs.SetTexture(kVert, "FrequencyTexture", kernelFrequency);
            cs.Dispatch(kVert, 512, 1, 1);

            // FFT of the image
            cs.SetTexture(kFwdH, "SourceTexture", source);
            cs.SetTexture(kFwdH, "FrequencyTexture", sourceFrequency);
            cs.Dispatch(kFwdH, 512, 1, 1);
            cs.SetFloat("IsForward", 1f);
            cs.SetTexture(kVert, "FrequencyTexture", sourceFrequency);
            cs.Dispatch(kVert, 512, 1, 1);

            // convolution = complex multiply per frequency
            cs.SetTexture(kConv, "SourceFrequencyTexture", sourceFrequency);
            cs.SetTexture(kConv, "KernelFrequencyTexture", kernelFrequency);
            cs.Dispatch(kConv, 512 / 8, 512 / 8, 1);

            // inverse: vertical, then horizontal
            cs.SetFloat("IsForward", 0f);
            cs.SetTexture(kVert, "FrequencyTexture", sourceFrequency);
            cs.Dispatch(kVert, 512, 1, 1);
            cs.SetTexture(kInvH, "FrequencyTexture", sourceFrequency);
            cs.Dispatch(kInvH, 512, 1, 1);

            Graphics.Blit(sourceFrequency, target, finalBlit);
        }
    }
}
