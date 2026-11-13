// Runs the halftone passes on the scene camera's image every frame and feeds the split-screen compose quad.
// The two passes and their parameters follow the source script (Halftone.cs): RGB to CMYK + dot threshold, then print.
// OnRenderImage does not exist in URP, so the same Graphics.Blit chain runs here in LateUpdate, before the frame is captured.
using UnityEngine;

namespace GameTech.Posts
{
    public struct HalftoneSettings
    {
        public float freq;                 // the source's "dot size": a frequency, dot pitch = 2 pi / freq pixels
        public Vector4 angles, curves;     // degrees and exponents per plate, C M Y K (the source uses 1, 1, 1, 0.15)
        public bool c, m, y, k, sourceMapping, smooth;
        public Vector2 offC, offM, offY, offK;

        public static HalftoneSettings Default() => new HalftoneSettings
        {
            freq = 0.75f, angles = new Vector4(15, 75, 0, 45), curves = new Vector4(1, 1, 1, 1),
            c = true, m = true, y = true, k = true,
        };
        public float Pitch => 2f * Mathf.PI / freq;
    }

    public class HalftoneChain
    {
        public readonly int W, H;
        readonly Material mat; readonly RenderTexture cmyk;

        public static RenderTexture RT(int w, int h)
        {
            var r = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
            r.Create(); return r;
        }

        public HalftoneChain(int w, int h, Material m) { W = w; H = h; mat = m; cmyk = RT(w, h); }

        public RenderTexture Run(Texture src, RenderTexture dst, HalftoneSettings s)
        {
            foreach (var n in new[] { "_CyanDotSize", "_MagentaDotSize", "_YellowDotSize", "_BlackDotSize" }) mat.SetFloat(n, s.freq);
            foreach (var n in new[] { "_CyanBias", "_MagentaBias", "_YellowBias", "_BlackBias" }) mat.SetFloat(n, 0f);
            mat.SetVector("_Angles", s.angles); mat.SetVector("_Curves", s.curves);
            mat.SetFloat("_SourceMapping", s.sourceMapping ? 1 : 0); mat.SetFloat("_Smooth", s.smooth ? 1 : 0);
            mat.SetInt("_PrintCyan", s.c ? 1 : 0); mat.SetInt("_PrintMagenta", s.m ? 1 : 0);
            mat.SetInt("_PrintYellow", s.y ? 1 : 0); mat.SetInt("_PrintBlack", s.k ? 1 : 0);
            mat.SetVector("_CyanOffset", s.offC); mat.SetVector("_MagentaOffset", s.offM);
            mat.SetVector("_YellowOffset", s.offY); mat.SetVector("_BlackOffset", s.offK);
            Graphics.Blit(src, cmyk, mat, 0);
            Graphics.Blit(cmyk, dst, mat, 1);
            return dst;
        }
    }

    [DefaultExecutionOrder(-100)]
    public class HalftoneDirector : MonoBehaviour
    {
        public Camera src;
        public Material halftone, compose;
        public Texture2D ramp;
        public LabelSpec left, mid3, right3, right2, top;
        public Transform torus;
        public Vector3 focus = new Vector3(0, 1.0f, 0);

        RenderTexture srcRT, outA, outB, outC, rampRT, rampA, rampB;
        HalftoneChain full, zoom;
        int W, H, logged = -1;

        void Start()
        {
            W = int.Parse(FrameCapturer.Arg("-gtW", "1920")); H = int.Parse(FrameCapturer.Arg("-gtH", "1080"));
            srcRT = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            srcRT.Create();
            src.targetTexture = srcRT; src.enabled = false;
            full = new HalftoneChain(W, H, halftone);
            zoom = new HalftoneChain(W / 4, H / 4, halftone);     // shown 4x enlarged
            outA = HalftoneChain.RT(W, H); outB = HalftoneChain.RT(W, H); outC = HalftoneChain.RT(W, H);
            rampRT = HalftoneChain.RT(W / 4, H / 4); rampA = HalftoneChain.RT(W / 4, H / 4); rampB = HalftoneChain.RT(W / 4, H / 4);
            Graphics.Blit(ramp, rampRT);   // linear input value 0 at the left edge to 1 at the right edge
        }

        void Show(Texture a, Texture b, Texture c, float s1, float s2, bool vertical, string l, string m3 = "", string r3 = "", string r2 = "", string t = "")
        {
            compose.SetTexture("_A", a); compose.SetTexture("_B", b); compose.SetTexture("_C", c);
            compose.SetFloat("_Split", s1); compose.SetFloat("_Split2", s2); compose.SetFloat("_Vertical", vertical ? 1 : 0);
            left.text = l; mid3.text = m3; right3.text = r3; right2.text = r2; top.text = t;
        }

        void LateUpdate()
        {
            float t = FrameCapturer.T;

            torus.rotation = Quaternion.Euler(70f, t * 24f, 0);
            float ang = (150f + t * 3f) * Mathf.Deg2Rad;
            src.transform.position = focus + new Vector3(Mathf.Sin(ang) * 7.5f, 1.5f + 0.25f * Mathf.Sin(t * 0.35f), Mathf.Cos(ang) * 7.5f);
            src.transform.LookAt(focus);
            src.Render();

            var d = HalftoneSettings.Default();
            int phase;
            if (t < 2.5f) { phase = 0; Show(srcRT, srcRT, srcRT, 1f, 1f, false, "Original render (URP Lit)"); }
            else if (t < 8.5f)
            {
                phase = 1;
                float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 2.5f) / 0.8f));
                var k = d; k.c = k.m = k.y = false;
                Show(srcRT, full.Run(srcRT, outA, k), full.Run(srcRT, outB, d), Mathf.Lerp(1f, 1f / 3f, e), Mathf.Lerp(1f, 2f / 3f, e), false,
                     "Original", "Black plate only", $"CMYK, dot pitch {d.Pitch:F1} px");
            }
            else if (t < 13f)
            {
                int[] a = { 0, 15, 30, 45, 60, 75, 90 };
                int i = Mathf.Min(a.Length - 1, Mathf.FloorToInt((t - 8.5f) / 4.5f * a.Length));
                phase = 100 + a[i];
                var k = d; k.c = k.m = k.y = false; k.angles.w = a[i];
                Show(full.Run(srcRT, outA, k), srcRT, srcRT, 1f, 1f, false, $"Black plate, grid angle {a[i]} deg, dot pitch {d.Pitch:F1} px");
            }
            else if (t < 17.5f)
            {
                float[] f = { 0.5f, 0.75f, 1f, 1.5f, 2f, 2.5f };
                int i = Mathf.Min(f.Length - 1, Mathf.FloorToInt((t - 13f) / 4.5f * f.Length));
                phase = 200 + i;
                var s = d; s.freq = f[i];
                Show(full.Run(srcRT, outA, s), srcRT, srcRT, 1f, 1f, false, $"CMYK, dot pitch {s.Pitch:F1} px (frequency {f[i]:0.##})");
            }
            else if (t < 20f)
            {
                phase = 300;
                var s = d; s.offC = new Vector2(4, 0); s.offM = new Vector2(0, 4); s.offY = new Vector2(-3, -3);
                Show(full.Run(srcRT, outA, d), full.Run(srcRT, outB, s), srcRT, 0.5f, 1f, false, "Plates aligned", "", "", "Plate offsets px: C (4, 0) M (0, 4) Y (-3, -3)");
            }
            else if (t < 22.75f)
            {
                phase = 400;
                var s = d; s.sourceMapping = true; s.curves.w = 0.15f;
                Show(zoom.Run(rampRT, rampA, s), zoom.Run(rampRT, rampB, d), srcRT, 1f, 1f, true,
                     "Remapped: dot where pattern < 2 x ink - 1, black curve 1", t: "Source: dot where pattern < ink, black curve 0.15. Input 0 left, 1 right, 4x zoom");
            }
            else
            {
                phase = 500;
                var s = d; s.smooth = true;
                Show(zoom.Run(rampRT, rampA, d), zoom.Run(rampRT, rampB, s), srcRT, 1f, 1f, true,
                     "Smoothed over fwidth: about 1 px soft edge", t: "Hard threshold (input 0 left, 1 right, 4x zoom)");
            }
            if (phase != logged) { logged = phase; Debug.Log($"[gt] t {t:F2} phase {phase} '{left.text}' '{mid3.text}' '{right3.text}' '{right2.text}' '{top.text}'"); }
        }
    }
}
