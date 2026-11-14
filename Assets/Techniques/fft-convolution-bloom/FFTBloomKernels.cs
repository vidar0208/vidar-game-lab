// Procedural kernel images for the FFT bloom (the source takes a kernel texture from the user; none is shipped).
// 512 x 512, centre at (256, 256), units are pixels of the 512 x 512 FFT image (1 px = 3.75 px of a 1920 wide frame).
// Every kernel is scaled so its values sum to 512: the source FFT pair returns the convolution divided by 512
// (measured: 0.0020 of the input light with a kernel summing to 1), so with sum 512 the bloom spreads light without adding any.
using UnityEngine;

namespace GameTech.Posts
{
    public static class FFTBloomKernels
    {
        public const int N = 512;
        public static readonly string[] Names = { "glow", "cross", "star" };

        static float Spike(float dx, float dy, float angleDeg, float length, float width)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            float t = dx * Mathf.Cos(a) + dy * Mathf.Sin(a), d = -dx * Mathf.Sin(a) + dy * Mathf.Cos(a);
            return Mathf.Exp(-d * d / (2f * width * width)) * Mathf.Exp(-Mathf.Abs(t) / length);
        }

        public static float Value(string kind, float dx, float dy)
        {
            float r2 = dx * dx + dy * dy, r = Mathf.Sqrt(r2);
            float core = Mathf.Exp(-r2 / (2f * 1.5f * 1.5f));
            switch (kind)
            {
                case "glow": // core + medium halo + long exponential tail
                    return core + 0.06f * Mathf.Exp(-r2 / (2f * 8f * 8f)) + 0.004f * Mathf.Exp(-r / 30f);
                case "cross": // 4 spikes (0 and 90 degrees), 40 px decay length
                    return core + 0.035f * (Spike(dx, dy, 0, 40, 0.6f) + Spike(dx, dy, 90, 40, 0.6f)) + 0.015f * Mathf.Exp(-r2 / (2f * 10f * 10f));
                default: // "star": 6 spikes (0, 60, 120 degrees), 50 px decay length
                    return core + 0.025f * (Spike(dx, dy, 0, 50, 0.6f) + Spike(dx, dy, 60, 50, 0.6f) + Spike(dx, dy, 120, 50, 0.6f)) + 0.015f * Mathf.Exp(-r2 / (2f * 10f * 10f));
            }
        }

        // Kernel image for the FFT (linear float, sum 512) plus a 128 x 128 preview of the centre 256 x 256 (gamma 0.3).
        public static Texture2D Make(string kind, out Texture2D preview)
        {
            var v = new float[N * N]; double sum = 0; float max = 0;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++) { float k = Value(kind, x - N / 2, y - N / 2); v[y * N + x] = k; sum += k; max = Mathf.Max(max, k); }
            var px = new Color[N * N];
            for (int i = 0; i < px.Length; i++) { float k = (float)(v[i] / sum * N); px[i] = new Color(k, k, k, 0); }
            var tex = new Texture2D(N, N, TextureFormat.RGBAFloat, false, true) { name = "kernel " + kind, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels(px); tex.Apply(false);

            const int P = 128;
            var pp = new Color[P * P];
            for (int y = 0; y < P; y++)
                for (int x = 0; x < P; x++)
                {
                    float k = Mathf.Pow(Mathf.Clamp01(v[(N / 2 - P + 2 * y) * N + (N / 2 - P + 2 * x)] / max), 0.3f);
                    pp[y * P + x] = new Color(k, k, k, 1);
                }
            preview = new Texture2D(P, P, TextureFormat.RGBA32, false, true) { name = "preview " + kind, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            preview.SetPixels(pp); preview.Apply(false);
            return tex;
        }
    }
}
