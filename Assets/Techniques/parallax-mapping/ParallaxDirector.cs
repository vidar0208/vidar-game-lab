// Timeline for the parallax mapping post. Generates the hole noise, hole mask and mountain noise from code and
// drives the material properties of GameTech/ParallaxMarch every frame from the capture clock.
using UnityEngine;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(200)]
    public class ParallaxDirector : MonoBehaviour
    {
        public Camera camL, camR, camFull;
        public Material wallL, wallR, groundL, groundR, winL, winR;

        // values used in the video (the labels quote these)
        public const float BrickDepth = 0.05f;            // metres
        public const int BrickSteps = 300; public const float BrickStep = 0.001f;     // source BricksParallax
        public const float HoleDepth = 1.5f;               // exp2(1.5) - 1 = 1.83 m at the bottom
        public const int HoleSteps = 500; public const float HoleStep = 0.005f;
        public const int FewSteps = 12; public const float FewStep = 0.16f;
        public const int MountainSteps = 300; public const float MountainStep = 0.01f; public const float MountainHeight = 0.97f;   // source MountainInPlane

        void Start()
        {
            var noise = Voronoi(512, 9, 7);
            var mask = Mask(256);
            var mountain = Fbm(512, 21);
            foreach (var m in new[] { groundL, groundR }) { m.SetTexture("_HeightTex", noise); m.SetTexture("_MaskTex", mask); }
            foreach (var m in new[] { winL, winR }) m.SetTexture("_HeightTex", mountain);
            Debug.Log($"[gt] parallax: hole noise {noise.width}, mask {mask.width}, mountain noise {mountain.width}");
        }

        // ---- generated textures
        static float Hash(int x, int y, int s)
        {
            unchecked { uint h = (uint)(x * 374761393 + y * 668265263 + s * 2147483647); h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16; return (h & 0xFFFFFF) / 16777216f; }
        }
        static int Mod(int a, int n) => ((a % n) + n) % n;
        static float Value(float u, float v, int period, int seed)
        {
            float x = u * period, y = v * period; int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy; fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float a = Hash(Mod(ix, period), Mod(iy, period), seed), b = Hash(Mod(ix + 1, period), Mod(iy, period), seed);
            float c = Hash(Mod(ix, period), Mod(iy + 1, period), seed), d = Hash(Mod(ix + 1, period), Mod(iy + 1, period), seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }
        static Texture2D Make(int S, System.Func<float, float, float> f, string name)
        {
            var px = new byte[S * S];
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) px[y * S + x] = (byte)(Mathf.Clamp01(f((x + 0.5f) / S, (y + 0.5f) / S)) * 255f);
            var t = new Texture2D(S, S, TextureFormat.R8, true, true) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            t.SetPixelData(px, 0); t.Apply(true); return t;
        }
        // cracked stone: distance to the nearest cell border (dark cracks), plus a little value noise
        static Texture2D Voronoi(int S, int cells, int seed) => Make(S, (u, v) =>
        {
            float x = u * cells, y = v * cells; int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y); float f1 = 9, f2 = 9;
            for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++)
            {
                int gx = cx + i, gy = cy + j;
                float px = gx + Hash(Mod(gx, cells), Mod(gy, cells), seed), py = gy + Hash(Mod(gx, cells), Mod(gy, cells), seed + 3);
                float d = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                if (d < f1) { f2 = f1; f1 = d; } else if (d < f2) f2 = d;
            }
            float border = Mathf.Clamp01((f2 - f1) / 0.35f);
            return 0.25f + 0.6f * Mathf.Sqrt(border) + 0.15f * (Value(u, v, 16, seed + 9) - 0.5f);
        }, "hole_noise");
        // round hole mask with a ragged edge, 1 in the middle
        static Texture2D Mask(int S)
        {
            var t = Make(S, (u, v) =>
            {
                float dx = u - 0.5f, dy = v - 0.5f, r = Mathf.Sqrt(dx * dx + dy * dy), a = Mathf.Atan2(dy, dx);
                float wob = 0.035f * Mathf.Sin(a * 5 + 1.3f) + 0.02f * Mathf.Sin(a * 11 + 0.4f);
                return Mathf.Clamp01((0.42f + wob - r) / 0.30f);
            }, "hole_mask");
            t.wrapMode = TextureWrapMode.Clamp; return t;
        }
        static Texture2D Fbm(int S, int seed) => Make(S, (u, v) =>
        {
            float n = 0.5f * Value(u, v, 4, seed) + 0.25f * Value(u, v, 8, seed + 1) + 0.13f * Value(u, v, 16, seed + 2) + 0.07f * Value(u, v, 32, seed + 3) + 0.05f * Value(u, v, 64, seed + 4);
            return (n - 0.26f) / 0.48f;
        }, "mountain_noise");

        // ---- timeline
        static float S(float a, float b, float t) { float x = Mathf.Clamp01((t - a) / (b - a)); return x * x * (3 - 2 * x); }
        static void March(Material m, bool on, int steps, float step, bool interp, int debug = 0)
        {
            m.SetFloat("_March", on ? 1 : 0); m.SetFloat("_Steps", steps); m.SetFloat("_StepDistance", step);
            m.SetFloat("_Interp", interp ? 1 : 0); m.SetFloat("_Debug", debug);
        }
        void Look(Camera c, Vector3 from, Vector3 at) { c.transform.position = from; c.transform.LookAt(at); }

        void Update()
        {
            float t = FrameCapturer.T;
            bool split = t < 8f || (t >= 19f && t < 21.6f);
            camL.enabled = camR.enabled = split; camFull.enabled = !split;

            // defaults: right / full = full effect with the source's values
            March(wallR, true, BrickSteps, BrickStep, false); March(wallL, false, BrickSteps, BrickStep, false);
            March(groundR, true, HoleSteps, HoleStep, true); March(groundL, false, HoleSteps, HoleStep, true);
            March(winR, true, MountainSteps, MountainStep, true); March(winL, false, MountainSteps, MountainStep, true);
            float hole = 1f, scroll = t * 0.25f;     // the source scrolls the mountain by _Time.x * 5 = t / 4

            if (t < 8f)
            {   // A: normal map only | ray-marched parallax, grazing dolly along the wall
                float k = S(0f, 8f, t);
                var from = Vector3.Lerp(new Vector3(-3.1f, 1.05f, -1.0f), new Vector3(-2.2f, 1.35f, -0.55f), k);
                var at = Vector3.Lerp(new Vector3(1.2f, 1.1f, -2.0f), new Vector3(2.0f, 1.0f, -2.0f), k);
                Look(camL, from, at); Look(camR, from, at);
            }
            else if (t < 14.5f)
            {   // B: step count and interpolation, on the hole
                if (t < 10.2f) March(groundR, true, FewSteps, FewStep, false);
                else if (t < 12.4f) March(groundR, true, FewSteps, FewStep, true);
                float k = S(8f, 14.5f, t), a = Mathf.Lerp(-40f, 15f, k) * Mathf.Deg2Rad;
                var c = new Vector3(-1.2f, 0f, 1.0f);
                Look(camFull, c + new Vector3(Mathf.Sin(a) * 2.3f, 2.1f, Mathf.Cos(a) * 2.3f), c + new Vector3(0, -0.5f, 0));
            }
            else if (t < 19f)
            {   // C: the landscape window, camera slides sideways
                float k = S(14.5f, 19f, t);
                Look(camFull, Vector3.Lerp(new Vector3(0.2f, 1.75f, 0.2f), new Vector3(3.0f, 1.25f, 0.6f), k), new Vector3(1.6f, 1.5f, -2f));
            }
            else if (t < 21.6f)
            {   // D1: geometry | height map
                March(wallL, false, BrickSteps, BrickStep, false, 2); March(groundL, false, HoleSteps, HoleStep, true, 2); March(winL, false, MountainSteps, MountainStep, true, 2);
                March(wallR, false, BrickSteps, BrickStep, false, 1); March(groundR, false, HoleSteps, HoleStep, true, 1); March(winR, false, MountainSteps, MountainStep, true, 1);
                float k = S(19f, 21.6f, t);
                var from = Vector3.Lerp(new Vector3(-0.2f, 2.6f, 4.6f), new Vector3(0.3f, 2.4f, 4.2f), k);
                Look(camL, from, new Vector3(0.0f, 0.9f, -0.6f)); Look(camR, from, new Vector3(0.0f, 0.9f, -0.6f));
            }
            else
            {   // D2: the hole opens in the same flat ground quad
                hole = S(21.7f, 23.8f, t);
                float k = S(21.6f, 24f, t);
                Look(camFull, Vector3.Lerp(new Vector3(0.5f, 2.3f, 3.3f), new Vector3(0.0f, 2.0f, 2.9f), k), new Vector3(-1.2f, -0.3f, 0.9f));
            }
            foreach (var m in new[] { groundL, groundR }) m.SetFloat("_HoleAnim", hole);
            foreach (var m in new[] { winL, winR }) m.SetFloat("_Scroll", scroll);
        }
    }
}
