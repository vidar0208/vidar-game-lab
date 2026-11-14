// Drives the FFT convolution bloom post: moves the lantern, the ring and the camera, renders the scene camera into an
// HDR texture (and, for the comparison, once more with URP's own Bloom), runs one FFTBloomChain per band and hands the
// results to the display quad.
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class FFTBloomDirector : MonoBehaviour
    {
        public Camera src;
        public ComputeShader fft;
        public Material kernelGenerate, sourceGenerate, finalBlit, compose;
        public Transform lantern, ring;
        public LabelSpec title;
        public LabelSpec[] l1, l2;

        public const float Threshold = 1f, Intensity = 1f;

        RenderTexture hdrRT, urpRT;
        RenderTexture[] outRT = new RenderTexture[3];
        FFTBloomChain[] chains = new FFTBloomChain[3];
        Texture2D[] kernels = new Texture2D[3], previews = new Texture2D[3];
        int W, H, logged = -1;
        bool measured;

        void Start()
        {
            W = int.Parse(FrameCapturer.Arg("-gtW", "1920")); H = int.Parse(FrameCapturer.Arg("-gtH", "1080"));
            hdrRT = new RenderTexture(W, H, 24, RenderTextureFormat.ARGBHalf) { filterMode = FilterMode.Bilinear }; hdrRT.Create();
            urpRT = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { filterMode = FilterMode.Bilinear }; urpRT.Create();
            for (int i = 0; i < 3; i++)
            {
                outRT[i] = new RenderTexture(W, H, 0, RenderTextureFormat.ARGBHalf) { filterMode = FilterMode.Bilinear }; outRT[i].Create();
                chains[i] = new FFTBloomChain(fft, kernelGenerate, sourceGenerate, finalBlit) { threshold = Threshold, intensity = Intensity };
                kernels[i] = FFTBloomKernels.Make(FFTBloomKernels.Names[i], out previews[i]);
            }
            src.enabled = false;
            compose.SetTexture("_K0", previews[0]); compose.SetTexture("_K1", previews[1]); compose.SetTexture("_K2", previews[2]);
        }

        void RenderScene(RenderTexture target, bool urpPost)
        {
            var data = src.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = urpPost;
            src.targetTexture = target;
            src.Render();
        }

        // band i: scene + FFT bloom with kernel k, kernel scale s (intensity divided by s^2 so the total light stays the same)
        void Fft(int i, int k, float s)
        {
            Graphics.Blit(hdrRT, outRT[i]);
            chains[i].kernelSizeScale = new Vector2(s, s);
            chains[i].intensity = Intensity / (s * s);
            chains[i].Run(hdrRT, kernels[k], outRT[i]);
        }

        void Measure()
        {
            // sum of the thresholded 512 x 512 source vs the sum of the convolved result (left half of the frequency texture)
            float Sum(RenderTexture rt, int w, int h)
            {
                var tx = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true);
                RenderTexture.active = rt; tx.ReadPixels(new Rect(0, 0, w, h), 0, 0); tx.Apply(); RenderTexture.active = null;
                double s = 0; foreach (var c in tx.GetPixels()) s += c.r + c.g + c.b;
                Object.Destroy(tx); return (float)s;
            }
            float a = Sum(chains[0].SourceTexture, 512, 512), b = Sum(chains[0].SourceFrequency, 512, 512);
            Debug.Log($"[gt] energy: source {a:F1}, convolved {b:F1}, ratio {b / Mathf.Max(a, 1e-6f):F4}");
        }

        void Labels(string ttl, float s1, float s2, string[] a, string[] b)
        {
            title.text = ttl;
            float[] start = { 0f, s1, s2 };
            for (int i = 0; i < 3; i++)
            {
                l1[i].text = a != null && i < a.Length ? a[i] : ""; l1[i].anchor = new Vector2(start[i] + 0.012f, 0.058f);
                l2[i].text = b != null && i < b.Length ? b[i] : ""; l2[i].anchor = new Vector2(start[i] + 0.012f, 0.018f);
            }
            compose.SetFloat("_S1", s1); compose.SetFloat("_S2", s2);
        }

        void LateUpdate()
        {
            float t = FrameCapturer.T;
            lantern.position = new Vector3(2.4f * Mathf.Sin(t * 0.6f), 1.0f + 0.3f * Mathf.Sin(t * 1.2f), 0.6f + 0.8f * Mathf.Sin(t * 1.2f));
            ring.rotation = Quaternion.Euler(0, 25f * Mathf.Sin(t * 0.3f), 0);
            float a = (-14f + 22f * Mathf.Sin(t * 0.11f)) * Mathf.Deg2Rad;
            var target = new Vector3(0, 1.35f, 1.6f);
            src.transform.position = target + new Vector3(Mathf.Sin(a) * 7.2f, 0.25f, -Mathf.Cos(a) * 7.2f);
            src.transform.LookAt(target);

            RenderScene(hdrRT, false);
            string fftRes = "FFT 512 x 512", thr = $"threshold {Threshold:0.0}";
            int phase; float third = 1f / 3f;
            var ldr = Vector4.zero; var kon = Vector4.zero;
            if (t < 3f || t >= 23f)
            {
                phase = t < 3f ? 0 : 4;
                Fft(0, 2, 1f);
                compose.SetTexture("_B0", outRT[0]);
                kon = new Vector4(1, 0, 0, 0); compose.SetTexture("_K0", previews[2]);
                Labels("FFT convolution bloom: bright pixels convolved with a kernel image in the frequency domain", 1f, 1f,
                    new[] { "6-spike star kernel (top right), 512 x 512" }, new[] { $"{fftRes}, {thr}, intensity {Intensity:0.0}" });
            }
            else if (t < 10f)
            {
                phase = 1;
                RenderScene(urpRT, true);
                Fft(2, 2, 1f);
                compose.SetTexture("_B0", hdrRT); compose.SetTexture("_B1", urpRT); compose.SetTexture("_B2", outRT[2]);
                ldr = new Vector4(0, 1, 0, 0); kon = new Vector4(0, 0, 1, 0); compose.SetTexture("_K2", previews[2]);
                Labels("Same frame: no bloom, URP's mip-chain bloom, FFT convolution bloom", third, 2 * third,
                    new[] { "No bloom", "URP Bloom (downsample + upsample)", "FFT convolution, star kernel" },
                    new[] { "ACES tonemap only", "threshold 1.0, intensity 1, scatter 0.7", $"{fftRes}, {thr}" });
            }
            else if (t < 18f)
            {
                phase = 2;
                for (int i = 0; i < 3; i++) Fft(i, i, 1f);
                compose.SetTexture("_B0", outRT[0]); compose.SetTexture("_B1", outRT[1]); compose.SetTexture("_B2", outRT[2]);
                kon = new Vector4(1, 1, 1, 0);
                compose.SetTexture("_K0", previews[0]); compose.SetTexture("_K1", previews[1]); compose.SetTexture("_K2", previews[2]);
                Labels("Any kernel image works: the shape of the glow is the shape of the kernel", third, 2 * third,
                    new[] { "Glow kernel", "Cross kernel (4 spikes)", "Star kernel (6 spikes)" },
                    new[] { $"512 x 512 image, {thr}", $"512 x 512 image, {thr}", $"512 x 512 image, {thr}" });
            }
            else
            {
                phase = 3;
                float[] s = { 0.5f, 1f, 2f };
                for (int i = 0; i < 3; i++) Fft(i, 2, s[i]);
                compose.SetTexture("_B0", outRT[0]); compose.SetTexture("_B1", outRT[1]); compose.SetTexture("_B2", outRT[2]);
                kon = Vector4.zero;
                Labels("Kernel size scale (the kernel image is stretched before the FFT)", third, 2 * third,
                    new[] { "Kernel scale 0.5", "Kernel scale 1", "Kernel scale 2" },
                    new[] { $"intensity {Intensity / 0.25f:0.00} (1 / scale²), {thr}", $"intensity {Intensity:0.00}, {thr}", $"intensity {Intensity / 4f:0.00} (1 / scale²), {thr}" });
            }
            compose.SetVector("_LDR", ldr); compose.SetVector("_KOn", kon);
            if (!measured && t >= 0.5f) { measured = true; Measure(); }
            if (phase != logged) { logged = phase; Debug.Log($"[gt] t {t:F2} phase {phase} '{title.text}' | '{l1[0].text}' '{l2[0].text}' | '{l1[1].text}' '{l2[1].text}' | '{l1[2].text}' '{l2[2].text}'"); }
        }
    }
}
