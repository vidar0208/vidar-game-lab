// Port of NoiseGenerator.UpdateNoise from SebLague/Clouds (MIT, Copyright (c) 2019 Sebastian Lague):
// each channel of a tiling 3D texture gets three octaves of inverted Worley noise from NoiseGenCompute.compute
// (unchanged), then a normalise pass. Settings below are the repo's "Clouds Test" noise settings assets.
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace GameTech.Posts
{
    public static class CloudNoise
    {
        public struct Worley { public int seed, a, b, c; public float persistence; public Worley(int s, int a, int b, int c, float p) { seed = s; this.a = a; this.b = b; this.c = c; persistence = p; } }

        public static readonly Worley[] Shape = { new Worley(0, 3, 7, 11, 0.65f), new Worley(1, 9, 15, 23, 0.33f), new Worley(2, 13, 28, 42, 0.58f), new Worley(3, 20, 31, 45, 0.74f) };
        public static readonly Worley[] Detail = { new Worley(0, 8, 18, 20, 0.76f), new Worley(1, 13, 24, 28, 0.5f), new Worley(2, 20, 28, 32, 0.5f) };

        public static RenderTexture Generate(ComputeShader cs, int resolution, Worley[] channels, string name)
        {
            var tex = new RenderTexture(resolution, resolution, 0) { graphicsFormat = GraphicsFormat.R16G16B16A16_UNorm, volumeDepth = resolution, enableRandomWrite = true, dimension = UnityEngine.Rendering.TextureDimension.Tex3D, name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            tex.Create();
            int groups = Mathf.CeilToInt(resolution / 8f);
            for (int ch = 0; ch < channels.Length; ch++)
            {
                var s = channels[ch];
                var mask = new Vector4(ch == 0 ? 1 : 0, ch == 1 ? 1 : 0, ch == 2 ? 1 : 0, ch == 3 ? 1 : 0);
                cs.SetFloat("persistence", s.persistence);
                cs.SetInt("resolution", resolution);
                cs.SetVector("channelMask", mask);
                var prng = new System.Random(s.seed);
                var bA = Points(prng, s.a); var bB = Points(prng, s.b); var bC = Points(prng, s.c);
                cs.SetBuffer(0, "pointsA", bA); cs.SetBuffer(0, "pointsB", bB); cs.SetBuffer(0, "pointsC", bC);
                cs.SetInt("numCellsA", s.a); cs.SetInt("numCellsB", s.b); cs.SetInt("numCellsC", s.c);
                cs.SetBool("invertNoise", true); cs.SetInt("tile", 1);
                var minMax = new ComputeBuffer(2, sizeof(int)); minMax.SetData(new[] { int.MaxValue, 0 });
                cs.SetBuffer(0, "minMax", minMax);
                cs.SetTexture(0, "Result", tex);
                cs.Dispatch(0, groups, groups, groups);
                cs.SetBuffer(1, "minMax", minMax);
                cs.SetTexture(1, "Result", tex);
                cs.Dispatch(1, groups, groups, groups);
                var mm = new int[2]; minMax.GetData(mm);   // wait for the GPU before releasing the buffers
                bA.Release(); bB.Release(); bC.Release(); minMax.Release();
            }
            return tex;
        }

        // one random feature point per cell, same order of random draws as the source
        static ComputeBuffer Points(System.Random prng, int n)
        {
            var points = new Vector3[n * n * n];
            float cell = 1f / n;
            for (int x = 0; x < n; x++) for (int y = 0; y < n; y++) for (int z = 0; z < n; z++)
                    {
                        var r = new Vector3((float)prng.NextDouble(), (float)prng.NextDouble(), (float)prng.NextDouble()) * cell;
                        points[x + n * (y + z * n)] = new Vector3(x, y, z) * cell + r;
                    }
            var b = new ComputeBuffer(points.Length, sizeof(float) * 3);
            b.SetData(points);
            return b;
        }
    }
}
