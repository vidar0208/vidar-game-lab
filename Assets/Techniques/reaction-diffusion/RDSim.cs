// One Gray-Scott grid: two RGHalf textures (u, v) swapped every step, as the source's double-buffered
// Custom Render Texture. Du = 1, Dv = 1/3 and the default feed/kill are the values in the source's material.
using UnityEngine;

namespace GameTech.Posts
{
    public class RDSim
    {
        public RenderTexture a, b;
        public float feed = 0.0547f, kill = 0.0632f, du = 1f, dv = 1f / 3f, pop = 0.1f;
        public bool react = true;
        public int steps;
        readonly ComputeShader cs; readonly int kInit, kStep; readonly int W, H;

        public RDSim(ComputeShader cs, int w, int h)
        {
            this.cs = cs; W = w; H = h; kInit = cs.FindKernel("Init"); kStep = cs.FindKernel("Step");
            a = Make(); b = Make();
        }

        RenderTexture Make()
        {
            var r = new RenderTexture(W, H, 0, RenderTextureFormat.RGHalf, RenderTextureReadWrite.Linear)
            { enableRandomWrite = true, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            r.Create(); return r;
        }

        public void Init(uint seed)
        {
            cs.SetInt("W", W); cs.SetInt("H", H); cs.SetInt("Seed", (int)seed); cs.SetFloat("Pop", pop);
            cs.SetTexture(kInit, "Dst", a);
            cs.Dispatch(kInit, (W + 7) / 8, (H + 7) / 8, 1);
            steps = 0;
        }

        public void Step(int n)
        {
            cs.SetInt("W", W); cs.SetInt("H", H);
            cs.SetFloat("Du", du); cs.SetFloat("Dv", dv); cs.SetFloat("Feed", feed); cs.SetFloat("Kill", kill); cs.SetInt("React", react ? 1 : 0);
            for (int i = 0; i < n; i++)
            {
                cs.SetTexture(kStep, "Src", a); cs.SetTexture(kStep, "Dst", b);
                cs.Dispatch(kStep, (W + 7) / 8, (H + 7) / 8, 1);
                var t = a; a = b; b = t;
            }
            steps += n;
        }
    }
}
