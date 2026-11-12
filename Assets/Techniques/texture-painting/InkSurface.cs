// One paintable object: the core of EsProgram/InkPainter (MIT, Copyright (c) 2017 Es_Program) rewritten for this
// scene. InkCanvas: one RenderTexture per map (colour, normal, height), copied from the object's own textures,
// painted at a hit UV with Blit + paint shader + Blit back (PaintUVDirect). HeightFluid: every frame the height map
// runs through HeightDrip, then HeightToColor and HeightToNormal write the moving ink into the colour and normal
// maps; 5 s after the last stamp, or when the ink colour changes, InvertAlpha ends the drip.
using UnityEngine;

namespace GameTech.Posts
{
    public class InkSurface : MonoBehaviour
    {
        public Texture2D baseColor, baseNormal;
        public Vector2 uvToMetres = Vector2.one;     // metres per UV unit along u and v (brush footprint in metres)
        public bool paintNormal = true, fluid = false;
        public int heightSize = 1024;
        public Material target;                      // URP Lit instance that shows the painted maps

        // HeightFluid settings (source defaults unless noted)
        public Vector2 flowDirection = Vector2.up;
        public float flowingForce = 1f, easeOfDripping = 1f, horizontalSpread = 0.01f, normalScaleFactor = 1f, adhesionBorder = 0.01f, fluidStopTime = 5f;

        public RenderTexture MainRT { get; private set; }
        public RenderTexture NormalRT { get; private set; }
        public RenderTexture HeightRT { get; private set; }
        public static int StampsThisFrame, StampsTotal; static int stampFrame = -1;
        public static int ThisFrame => Time.frameCount == stampFrame ? StampsThisFrame : 0;

        static Material paint;
        bool fluidOn; float lastPaint; Color lastColor = Color.clear;

        static RenderTexture Make(int w, int h, string name)
        {
            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, useMipMap = false };
            rt.Create(); return rt;
        }

        public void Init()
        {
            if (MainRT != null) return;
            if (paint == null) paint = new Material(Shader.Find("Hidden/GameTech/InkPaint"));
            MainRT = Make(baseColor.width, baseColor.height, name + " colour");
            Graphics.Blit(baseColor, MainRT);
            target.SetTexture("_BaseMap", MainRT);
            if (paintNormal)
            {
                NormalRT = Make(baseNormal.width, baseNormal.height, name + " normal");
                Graphics.Blit(baseNormal, NormalRT);
                target.SetTexture("_BumpMap", NormalRT); target.EnableKeyword("_NORMALMAP");
            }
            if (fluid)
            {
                HeightRT = Make(heightSize, heightSize * baseColor.height / baseColor.width, name + " height");
                Fill(HeightRT, Vector4.zero);
            }
        }

        void Fill(RenderTexture rt, Vector4 c) { paint.SetVector("_Color", c); Blit(rt, 7); }

        // Blit through a pass into a temporary, then copy back, as the source does
        static void Blit(RenderTexture rt, int pass)
        {
            var tmp = RenderTexture.GetTemporary(rt.width, rt.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(rt, tmp, paint, pass);
            Graphics.Blit(tmp, rt);
            RenderTexture.ReleaseTemporary(tmp);
        }

        public void Paint(InkBrush b, Vector2 uv, Color color, float size, float rotate)
        {
            Init();
            if (Time.frameCount != stampFrame) { stampFrame = Time.frameCount; StampsThisFrame = 0; }
            StampsThisFrame++; StampsTotal++;
            if (fluid)
            {   // HeightFluid.OnPaintStart: a new ink colour stops the old fluid; painting (re)starts it
                if (lastColor != color) { lastColor = color; StopFluid(); }
                fluidOn = true; lastPaint = Time.time;
            }
            paint.SetVector("_PaintUV", uv);
            paint.SetVector("_UVToMetres", uvToMetres);
            paint.SetFloat("_BrushSize", size);           // half width of the brush square, metres
            paint.SetFloat("_BrushRotate", rotate);
            paint.SetTexture("_Brush", b.alpha); paint.SetTexture("_BrushNormal", b.normal); paint.SetTexture("_BrushHeight", b.height);
            var lc = color.linear;   // the RenderTextures hold linear values
            paint.SetVector("_ControlColor", lc);
            paint.SetFloat("_Alpha", fluid ? 1 : 0);            // ALPHA_ONLY while the fluid owns the colour
            Blit(MainRT, 0);
            if (paintNormal) { paint.SetFloat("_NormalBlend", 1f); Blit(NormalRT, 1); }
            if (fluid) { paint.SetFloat("_HeightBlend", 1f); paint.SetVector("_Color", lc); Blit(HeightRT, 2); }
        }

        void StopFluid() { if (HeightRT != null) Blit(HeightRT, 6); }

        // HeightFluid.OnWillRenderObject, run once per frame
        public void Step()
        {
            if (!fluid || HeightRT == null) return;
            if (fluidOn && Time.time - lastPaint > fluidStopTime) { StopFluid(); fluidOn = false; }
            if (!fluidOn) return;
            paint.SetFloat("_ScaleFactor", flowingForce); paint.SetFloat("_Viscosity", easeOfDripping);
            paint.SetFloat("_HorizontalSpread", horizontalSpread);
            paint.SetVector("_FlowDirection", flowDirection.normalized); paint.SetVector("_FixedColor", lastColor.linear);
            paint.SetTexture("_NormalMap", baseNormal);
            Blit(HeightRT, 3);
            var tmp = RenderTexture.GetTemporary(MainRT.width, MainRT.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            paint.SetTexture("_ColorMap", MainRT); paint.SetTexture("_BaseColorTex", baseColor);
            paint.SetFloat("_Alpha", 1f); paint.SetFloat("_Border", adhesionBorder);
            Graphics.Blit(HeightRT, tmp, paint, 4); Graphics.Blit(tmp, MainRT);
            RenderTexture.ReleaseTemporary(tmp);
            if (paintNormal)
            {
                tmp = RenderTexture.GetTemporary(NormalRT.width, NormalRT.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                paint.SetTexture("_BumpMap", NormalRT); paint.SetFloat("_NormalScaleFactor", normalScaleFactor);
                Graphics.Blit(HeightRT, tmp, paint, 5); Graphics.Blit(tmp, NormalRT);
                RenderTexture.ReleaseTemporary(tmp);
            }
        }

        void OnDestroy() { foreach (var rt in new[] { MainRT, NormalRT, HeightRT }) if (rt != null) rt.Release(); }
    }

    // Brush textures generated from code: alpha = splat shape with droplets, normal and height from a domed thickness.
    public class InkBrush
    {
        public Texture2D alpha, normal, height;

        static float H(int x, int y, int s) { unchecked { uint h = (uint)(x * 374761393 + y * 668265263 + s * 1442695041); h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16; return (h & 0xFFFFFF) / 16777216f; } }
        static float N(float x, float y, int s)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y); float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            return Mathf.Lerp(Mathf.Lerp(H(ix, iy, s), H(ix + 1, iy, s), fx), Mathf.Lerp(H(ix, iy + 1, s), H(ix + 1, iy + 1, s), fx), fy);
        }

        public static InkBrush Make(int seed, int S = 256)
        {
            var thick = new float[S * S];
            var rnd = new System.Random(seed);
            // droplets around the main blob
            int nd = 9; var dx = new float[nd]; var dy = new float[nd]; var dr = new float[nd];
            for (int i = 0; i < nd; i++)
            {
                float a = (float)rnd.NextDouble() * 6.283f, d = 0.52f + 0.38f * (float)rnd.NextDouble();
                dx[i] = Mathf.Cos(a) * d; dy[i] = Mathf.Sin(a) * d; dr[i] = 0.025f + 0.05f * (float)rnd.NextDouble();
            }
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S * 2 - 1, v = (y + 0.5f) / S * 2 - 1;
                float r = Mathf.Sqrt(u * u + v * v), a = Mathf.Atan2(v, u);
                float edge = 0.46f + 0.09f * Mathf.Sin(a * 3 + seed) + 0.06f * Mathf.Sin(a * 7 + seed * 2.1f) + 0.12f * (N(u * 4 + 9, v * 4 + 9, seed) - 0.5f);
                float t = Mathf.Clamp01((edge - r) / 0.06f);
                float fing = Mathf.Pow(Mathf.Abs(Mathf.Sin(a * 4.5f + seed)), 18f);       // a few fingers
                t = Mathf.Max(t, Mathf.Clamp01((edge + 0.22f * fing - r) / 0.04f) * (r < 0.78f ? 1 : 0));
                for (int i = 0; i < nd; i++)
                {
                    float ddx = u - dx[i], ddy = v - dy[i];
                    t = Mathf.Max(t, Mathf.Clamp01((dr[i] - Mathf.Sqrt(ddx * ddx + ddy * ddy)) / 0.015f));
                }
                thick[y * S + x] = t;
            }
            var b = new InkBrush();
            var ap = new Color32[S * S]; var np = new Color32[S * S]; var hp = new Color32[S * S];
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
            {
                float T(int i, int j) => thick[Mathf.Clamp(j, 0, S - 1) * S + Mathf.Clamp(i, 0, S - 1)];
                float t = T(x, y);
                float dome = Mathf.Sqrt(Mathf.Max(0, (T(x, y) + T(x - 2, y) + T(x + 2, y) + T(x, y - 2) + T(x, y + 2)) / 5f));
                float hx = (T(x + 3, y) - T(x - 3, y)), hy = (T(x, y + 3) - T(x, y - 3));
                var n = new Vector3(-hx * 1.2f, -hy * 1.2f, 1).normalized;
                byte A = (byte)(Mathf.Clamp01(t) * 255);
                ap[y * S + x] = new Color32(255, 255, 255, A);
                np[y * S + x] = new Color32((byte)((n.x * 0.5f + 0.5f) * 255), (byte)((n.y * 0.5f + 0.5f) * 255), (byte)((n.z * 0.5f + 0.5f) * 255), 255);
                byte hgt = (byte)(Mathf.Clamp01(dome) * 255);
                hp[y * S + x] = new Color32(hgt, hgt, hgt, 255);
            }
            b.alpha = Tex(S, ap, "brush_" + seed); b.normal = Tex(S, np, "brush_n_" + seed); b.height = Tex(S, hp, "brush_h_" + seed);
            return b;
        }
        static Texture2D Tex(int S, Color32[] px, string name)
        {
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false, true) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            t.SetPixels32(px); t.Apply(false); return t;
        }
    }
}
