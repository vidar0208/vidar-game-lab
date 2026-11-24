// Drives the erosion post. At Start it makes a fractal-noise height map (7 octaves, like the source's
// HeightMapGenerator, here with Unity's Perlin noise), then runs the source's droplet erosion on the GPU:
// two full runs of 1,048,576 droplets (brush radius 3 and radius 1, timed) and one copy that is eroded a few thousand
// droplets per frame so the video can show the count grow. The terrain shader reads whichever height buffer is set
// as the global _GMap before each camera renders. Runs before FrameCapturer (execution order -100).
using UnityEngine;
using UnityEngine.Rendering;
using GameTech;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class ErosionDirector : MonoBehaviour
    {
        public Camera camA, camB; public Material display, terrain; public ComputeShader erosion;
        public int N = 256, border = 3, droplets = 1 << 17, seed = 7;
        public float scale = 20f, elev = 12f;
        [Header("Source defaults")]
        public int maxLifetime = 30;
        public float inertia = 0.3f, sedimentCapacityFactor = 3, minSedimentCapacity = 0.01f, depositSpeed = 0.3f, erodeSpeed = 0.3f;
        public float evaporateSpeed = 0.01f, gravity = 4, startSpeed = 1, startWater = 1;
        public LabelSpec title, l1, l2, m1, m2;

        RenderTexture rtA, rtB; ComputeBuffer orig, full3, full1, prog, rnd;
        ComputeBuffer[] brushIdx = new ComputeBuffer[4], brushW = new ComputeBuffer[4]; int[] brushLen = new int[4];
        float[] hFull; int W, progDone, phase = -1; float ms3, ms1;

        static float S(float x) { x = Mathf.Clamp01(x); return x * x * x * (x * (x * 6f - 15f) + 10f); }
        static RenderTexture RT(int w, int h) { var r = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { antiAliasing = 4 }; r.Create(); return r; }

        float[] HeightMap()
        {
            // the source's octave loop (scale 2, lacunarity 2, persistence 0.5, 7 octaves), normalised to 0..1, then ^1.8
            var rng = new System.Random(seed); var off = new Vector2[7];
            for (int o = 0; o < 7; o++) off[o] = new Vector2((float)rng.NextDouble() * 1000f, (float)rng.NextDouble() * 1000f);
            var m = new float[W * W]; float lo = 1e9f, hi = -1e9f;
            for (int y = 0; y < W; y++) for (int x = 0; x < W; x++)
            {
                float s = 2f, w = 1f, v = 0;
                for (int o = 0; o < 7; o++) { v += (Mathf.PerlinNoise(x / (float)W * s + off[o].x, y / (float)W * s + off[o].y) * 2f - 1f) * w; s *= 2f; w *= 0.5f; }
                m[y * W + x] = v; lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v);
            }
            for (int i = 0; i < m.Length; i++) m[i] = Mathf.Pow((m[i] - lo) / (hi - lo), 1.8f); // my change: sharper peaks, wider valleys
            return m;
        }

        void MakeBrush(int r)
        {
            // the source's round brush, with offsets in the bordered map's row width
            var idx = new System.Collections.Generic.List<int>(); var wt = new System.Collections.Generic.List<float>(); float sum = 0;
            for (int by = -r; by <= r; by++) for (int bx = -r; bx <= r; bx++)
            {
                float d2 = bx * bx + by * by;
                if (d2 < r * r) { idx.Add(by * W + bx); float w = 1 - Mathf.Sqrt(d2) / r; sum += w; wt.Add(w); }
            }
            if (idx.Count == 0) { idx.Add(0); wt.Add(1); sum = 1; }
            for (int i = 0; i < wt.Count; i++) wt[i] /= sum;
            brushIdx[r] = new ComputeBuffer(idx.Count, 4); brushIdx[r].SetData(idx);
            brushW[r] = new ComputeBuffer(wt.Count, 4); brushW[r].SetData(wt); brushLen[r] = idx.Count;
        }

        void Erode(ComputeBuffer map, int radius, int offset, int count)
        {
            erosion.SetBuffer(0, "map", map); erosion.SetBuffer(0, "randomIndices", rnd);
            erosion.SetBuffer(0, "brushIndices", brushIdx[radius]); erosion.SetBuffer(0, "brushWeights", brushW[radius]);
            erosion.SetInt("mapSize", W); erosion.SetInt("brushLength", brushLen[radius]); erosion.SetInt("borderSize", border);
            erosion.SetInt("maxLifetime", maxLifetime); erosion.SetFloat("inertia", inertia);
            erosion.SetFloat("sedimentCapacityFactor", sedimentCapacityFactor); erosion.SetFloat("minSedimentCapacity", minSedimentCapacity);
            erosion.SetFloat("depositSpeed", depositSpeed); erosion.SetFloat("erodeSpeed", erodeSpeed); erosion.SetFloat("evaporateSpeed", evaporateSpeed);
            erosion.SetFloat("gravity", gravity); erosion.SetFloat("startSpeed", startSpeed); erosion.SetFloat("startWater", startWater);
            for (int done = 0; done < count; done += 32768)
            {
                int c = Mathf.Min(32768, count - done);
                erosion.SetInt("_Offset", offset + done); erosion.SetInt("_Count", c);
                erosion.Dispatch(0, (c + 1023) / 1024, 1, 1);
            }
        }

        float FullRun(ComputeBuffer map, float[] h, int radius)
        {
            map.SetData(h); var probe = new float[1]; map.GetData(probe, 0, 0, 1);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Erode(map, radius, 0, droplets);
            map.GetData(probe, 0, 0, 1);
            return (float)sw.Elapsed.TotalMilliseconds;
        }

        void Start()
        {
            int w = int.Parse(FrameCapturer.Arg("-gtW", "1920")), hh = int.Parse(FrameCapturer.Arg("-gtH", "1080"));
            rtA = RT(w, hh); rtB = RT(w, hh);
            camA.targetTexture = rtA; camA.enabled = false; camB.targetTexture = rtB; camB.enabled = false;
            display.SetTexture("_TexA", rtA); display.SetTexture("_TexB", rtB);
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(new Color(0.12f, 0.14f, 0.19f));
            sh.AddDirectionalLight(Vector3.up, new Color(0.45f, 0.58f, 0.85f), 0.8f);
            RenderSettings.ambientMode = AmbientMode.Custom; RenderSettings.ambientProbe = sh;

            W = N + 2 * border;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var h = HeightMap(); float noiseMs = (float)sw.Elapsed.TotalMilliseconds;
            var rng = new System.Random(seed + 1); var idx = new int[droplets];
            for (int i = 0; i < droplets; i++) idx[i] = (border + rng.Next(N)) * W + border + rng.Next(N);
            rnd = new ComputeBuffer(droplets, 4); rnd.SetData(idx);
            MakeBrush(3); MakeBrush(1);
            orig = new ComputeBuffer(W * W, 4); orig.SetData(h);
            full3 = new ComputeBuffer(W * W, 4); full1 = new ComputeBuffer(W * W, 4); prog = new ComputeBuffer(W * W, 4); prog.SetData(h);
            FullRun(full3, h, 3); // warm-up (first use compiles the kernel)
            ms3 = FullRun(full3, h, 3); ms1 = FullRun(full1, h, 1);
            hFull = new float[W * W]; full3.GetData(hFull);
            float lost = 0, sum0 = 0, sum1 = 0; for (int i = 0; i < h.Length; i++) { sum0 += h[i]; sum1 += hFull[i]; }
            lost = (sum0 - sum1) / sum0;
            Debug.Log($"[gt] erosion: map {N}+2x{border}, noise {noiseMs:F0} ms, {droplets} droplets: radius 3 {ms3:F0} ms, radius 1 {ms1:F0} ms (GPU, waited); total height change {lost * 100:F2}%");

            terrain.SetInt("_GN", N); terrain.SetInt("_GBorder", border); terrain.SetFloat("_GScale", scale); terrain.SetFloat("_GElev", elev);
            Shader.SetGlobalInt("_GN", N); Shader.SetGlobalInt("_GBorder", border); Shader.SetGlobalFloat("_GScale", scale); Shader.SetGlobalFloat("_GElev", elev);
            Shader.SetGlobalBuffer("_GMap0", orig);
        }

        float Ground(float x, float z)
        {
            int ix = Mathf.Clamp(Mathf.RoundToInt((x / scale + 1) * 0.5f * (N - 1)), 0, N - 1), iz = Mathf.Clamp(Mathf.RoundToInt((z / scale + 1) * 0.5f * (N - 1)), 0, N - 1);
            float m = 0;
            for (int dz = -4; dz <= 4; dz++) for (int dx = -4; dx <= 4; dx++)
            {
                int xx = Mathf.Clamp(ix + dx, 0, N - 1), zz = Mathf.Clamp(iz + dz, 0, N - 1);
                m = Mathf.Max(m, hFull[(zz + border) * W + xx + border]);
            }
            return m * elev;
        }

        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }

        void LateUpdate()
        {
            if (rtA == null) return;
            float t = FrameCapturer.T;
            int p = t < 3f ? 0 : t < 12f ? 1 : t < 18.5f ? 2 : t < 22.5f ? 3 : 4;

            // progressive run: 0 -> all droplets between 3.6 s and 11.4 s
            int target = Mathf.RoundToInt(droplets * S((t - 3.6f) / 7.8f));
            if (target > progDone) { Erode(prog, 3, progDone, target - progDone); progDone = target; }

            // camera: aerial orbit, swoop down a valley and back up, then pull out
            float ang = Mathf.Lerp(200f, 245f, S(t / 12f));
            Vector3 cp = Quaternion.Euler(0, ang, 0) * new Vector3(0, 0, -17f) + Vector3.up * 15f, ct = new Vector3(0, 3f, 0);
            float sw = S((t - 11.6f) / 1.8f), sw2 = S((t - 13.4f) / 4.6f), up = S((t - 17.8f) / 2.4f);
            var a0 = new Vector3(-13f, 13f, 16f); var a1 = new Vector3(2f, 0f, 2f);
            var low = Vector3.Lerp(a0, a1, sw2); low.y = Mathf.Lerp(a0.y, Ground(low.x, low.z) + 2.6f, S((t - 12.6f) / 3.2f));
            var lowT = low + new Vector3(1.0f, -0.25f, -1.0f) * 10f;
            cp = Vector3.Lerp(cp, low, sw); ct = Vector3.Lerp(ct, lowT, sw);
            var hi = new Vector3(9f, 16f, 13f);
            cp = Vector3.Lerp(cp, hi, up); ct = Vector3.Lerp(ct, new Vector3(0, 1.5f, 0), up);
            float ce = S((t - 22.2f) / 2.6f);
            cp = Vector3.Lerp(cp, Quaternion.Euler(0, 170f, 0) * new Vector3(0, 0, -19f) + Vector3.up * 17f, ce);
            foreach (var c in new[] { camA, camB }) c.transform.SetPositionAndRotation(cp, Quaternion.LookRotation(ct - cp));

            ComputeBuffer ba = full3, bb = full3; float ma = 0, mb = 0, split = 0.5f, line = 1;
            string par = $"inertia {inertia}, capacity x{sedimentCapacityFactor}, erode {erodeSpeed}, deposit {depositSpeed}, {maxLifetime} steps";
            switch (p)
            {
                case 0:
                    split = 0f; line = 0;
                    Labels($"Hydraulic erosion: {droplets:N0} raindrops", $"{N} x {N} height map, droplets simulated on the GPU", par, "", "");
                    break;
                case 1:
                    ba = orig; bb = prog; split = Mathf.Lerp(0f, 0.5f, S((t - 3f) / 0.8f));
                    Labels("Same height map, same sun", "Fractal noise only", "7 octaves, no erosion", "Eroded by droplets", $"{progDone:N0} droplets so far");
                    break;
                case 2:
                    ba = full1; bb = full3;
                    Labels($"Same {droplets:N0} droplets, one setting changed", "Erosion brush radius 1", "each step cuts one narrow spot", "Erosion brush radius 3 (source default)", "wider, smoother valleys");
                    break;
                case 3:
                    mb = 1;
                    Labels("Where the material went", "Eroded terrain", "colour: slope + sediment", "Height change", "orange = carried away, teal = dropped");
                    break;
                default:
                    split = 0f; line = 0;
                    Labels("Droplet hydraulic erosion", "Unity 6.3 URP, after SebLague/Hydraulic-Erosion (MIT)", $"{droplets:N0} droplets in {ms3:F0} ms on an Apple M4 Mac mini", "", "");
                    break;
            }
            Shader.SetGlobalBuffer("_GMap", ba); Shader.SetGlobalFloat("_GMode", ma); camA.Render();
            Shader.SetGlobalBuffer("_GMap", bb); Shader.SetGlobalFloat("_GMode", mb); camB.Render();
            display.SetFloat("_Split", split); display.SetFloat("_Line", line);
            if (p != phase) { phase = p; Debug.Log($"[gt] t {t:F2} phase {p} '{title.text}' prog {progDone}"); }
        }

        void OnDestroy()
        {
            foreach (var b in new[] { orig, full3, full1, prog, rnd }) b?.Release();
            foreach (var b in brushIdx) b?.Release(); foreach (var b in brushW) b?.Release();
            foreach (var r in new[] { rtA, rtB }) if (r) r.Release();
        }
    }
}
