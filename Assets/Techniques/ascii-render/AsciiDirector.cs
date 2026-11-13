// Runs the source's ASCII pipeline on the scene camera's image every frame and feeds the split-screen compose quad.
// The pass order and parameters follow the source script (ASCII.cs): luminance, difference of Gaussians, Sobel,
// colour + luminance pack, three point downscales, then the compute pass that picks one glyph per 8 x 8 tile.
// OnRenderImage does not exist in URP, so the same Graphics.Blit chain runs here in LateUpdate, before the frame is captured.
using UnityEngine;

namespace GameTech.Posts
{
    // One ASCII chain at a fixed resolution. Run it at 1920 x 1080 for 8 px tiles on screen, at 960 x 540 for 16 px tiles.
    public class AsciiChain
    {
        public readonly int W, H;
        // The effect reads linear colour; the source's own attenuation exponent at 1/2.2 brings tile luminance close to display brightness
        public static float Attenuation = 1f / 2.2f;
        readonly Material mat; readonly ComputeShader cs;
        readonly RenderTexture input, lum, ping, dog, sobel, d1, d2, d3;

        static RenderTexture RT(int w, int h, RenderTextureFormat f, RenderTextureReadWrite rw = RenderTextureReadWrite.Linear, bool random = false)
        {
            var r = new RenderTexture(w, h, 0, f, rw) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point, enableRandomWrite = random };
            r.Create(); return r;
        }

        public AsciiChain(int w, int h, Material m, ComputeShader c)
        {
            W = w; H = h; mat = m; cs = c;
            input = RT(w, h, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            lum = RT(w, h, RenderTextureFormat.RHalf);
            ping = RT(w, h, RenderTextureFormat.ARGBHalf);
            dog = RT(w, h, RenderTextureFormat.ARGBHalf);
            sobel = RT(w, h, RenderTextureFormat.ARGBHalf);
            d1 = RT(w / 2, h / 2, RenderTextureFormat.ARGBHalf);
            d2 = RT(w / 4, h / 4, RenderTextureFormat.ARGBHalf);
            d3 = RT(w / 8, h / 8, RenderTextureFormat.ARGBHalf);
        }

        public RenderTexture Output() => RT(W, H, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear, true);

        public RenderTexture Run(Texture src, RenderTexture result, Texture fillAtlas, Texture edgeAtlas,
                                 bool edges = true, bool fill = true, bool colour = false, bool debugEdges = false)
        {
            Texture s = src;
            if (src.width != W) { Graphics.Blit(src, input); s = input; }   // 2:1 bilinear downsample for the 16 px tile run

            // ASCII.cs defaults
            mat.SetFloat("_K", 1.6f); mat.SetFloat("_Sigma", 2f); mat.SetFloat("_Tau", 1f);
            mat.SetInt("_GaussianKernelSize", 2); mat.SetFloat("_Threshold", 0.005f); mat.SetInt("_Invert", 0);

            Graphics.Blit(s, lum, mat, 1);      // luminance
            Graphics.Blit(lum, ping, mat, 3);   // horizontal blur, two sigmas
            Graphics.Blit(ping, dog, mat, 4);   // vertical blur and difference of Gaussians
            mat.SetTexture("_LuminanceTex", lum);
            Graphics.Blit(dog, ping, mat, 5);   // Sobel horizontal
            Graphics.Blit(ping, sobel, mat, 6); // Sobel vertical: magnitude, angle, valid mask
            Graphics.Blit(s, ping, mat, 2);     // colour + luminance pack
            Graphics.Blit(ping, d1, mat, 0);    // three point downscales: one sample per 8 x 8 tile
            Graphics.Blit(d1, d2, mat, 0);
            Graphics.Blit(d2, d3, mat, 0);

            cs.SetTexture(0, "_SobelTex", sobel);
            cs.SetTexture(0, "_Result", result);
            cs.SetTexture(0, "_EdgeAsciiTex", edgeAtlas);
            cs.SetTexture(0, "_AsciiTex", fillAtlas);
            cs.SetTexture(0, "_LuminanceTex", d3);
            cs.SetInt("_ViewUncompressed", 0); cs.SetInt("_Grid", 0);
            cs.SetInt("_DebugEdges", debugEdges ? 1 : 0);
            cs.SetInt("_NoEdges", edges ? 0 : 1); cs.SetInt("_NoFill", fill ? 0 : 1);
            cs.SetInt("_Colorize", colour ? 1 : 0);
            cs.SetInt("_EdgeThreshold", 8); cs.SetFloat("_Exposure", 1f); cs.SetFloat("_Attenuation", Attenuation);
            // the source dispatches width / 8 groups in both x and y; y must use the height
            cs.Dispatch(0, Mathf.CeilToInt(W / 8f), Mathf.CeilToInt(H / 8f), 1);
            return result;
        }
    }

    [DefaultExecutionOrder(-100)]
    public class AsciiDirector : MonoBehaviour
    {
        public Camera src;
        public Material ascii, compose;
        public ComputeShader cs;
        public Texture2D fill10, fill5, edgeAtlas;
        public LabelSpec left, right;
        public Transform torus, cube;
        public Light orbitLight;
        public Vector3 focus = new Vector3(0, 1.0f, 0);

        RenderTexture srcRT, outA, outB, outHalf;
        AsciiChain full, half;
        int W, H, logged = -1;

        void Start()
        {
            W = int.Parse(FrameCapturer.Arg("-gtW", "1920")); H = int.Parse(FrameCapturer.Arg("-gtH", "1080"));
            srcRT = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            srcRT.Create();
            src.targetTexture = srcRT; src.enabled = false;
            full = new AsciiChain(W, H, ascii, cs);
            half = new AsciiChain(W / 2, H / 2, ascii, cs);
            outA = full.Output(); outB = full.Output(); outHalf = half.Output();
        }

        void Show(Texture a, Texture b, float split, string l, string r)
        {
            compose.SetTexture("_A", a); compose.SetTexture("_B", b); compose.SetFloat("_Split", split);
            left.text = l; right.text = r;
        }

        void LateUpdate()
        {
            float t = FrameCapturer.T;

            // animate: torus spins, cube turns, a warm light circles the objects, the camera drifts on an arc
            torus.rotation = Quaternion.Euler(18f + t * 22f, t * 31f, 0);
            cube.rotation = Quaternion.Euler(0, 30f + t * 12f, 0);
            orbitLight.transform.position = new Vector3(Mathf.Cos(t * 0.7f) * 3.2f, 1.8f, Mathf.Sin(t * 0.7f) * 3.2f);
            float ang = (200f + t * 2.2f) * Mathf.Deg2Rad;
            src.transform.position = focus + new Vector3(Mathf.Sin(ang) * 7.0f, 2.3f + 0.3f * Mathf.Sin(t * 0.3f), Mathf.Cos(ang) * 7.0f);
            src.transform.LookAt(focus);
            src.Render();

            float slide = Mathf.SmoothStep(1f, 0.5f, Mathf.Clamp01((t - 2.5f) / 0.7f));
            int phase;
            if (t < 2.5f) { phase = 0; Show(srcRT, srcRT, 1f, "Original render (URP Lit)", ""); }
            else if (t < 7f)
            {
                phase = 1;
                Show(srcRT, full.Run(srcRT, outA, fill10, edgeAtlas), slide, "Original", "ASCII: one glyph per 8 x 8 px tile");
            }
            else if (t < 15f)
            {
                int k = t < 9.7f ? 0 : t < 12.3f ? 1 : 2;
                phase = 10 + k;
                var r = full.Run(srcRT, outA, fill10, edgeAtlas, edges: k >= 1, colour: k == 2);
                string[] l = {
                    "Step 1: tile luminance picks 1 of 10 glyphs",
                    "Step 2: + edge glyphs from DoG + Sobel",
                    "Step 3: + glyphs tinted by the tile colour" };
                Show(r, r, 1f, l[k], "");
            }
            else if (t < 17.5f)
            {
                phase = 20;
                var a = full.Run(srcRT, outA, fill10, edgeAtlas);
                var b = full.Run(srcRT, outB, fill10, edgeAtlas, debugEdges: true);
                Show(a, b, 0.5f, "ASCII", "Edge tiles: red | green - cyan/yellow diagonals");
            }
            else if (t < 21f)
            {
                phase = 30;
                var a = full.Run(srcRT, outA, fill10, edgeAtlas);
                var b = half.Run(srcRT, outHalf, fill10, edgeAtlas);
                Show(a, b, 0.5f, $"8 px tiles: {W / 8} x {H / 8} glyphs", $"16 px tiles: {W / 16} x {Mathf.CeilToInt(H / 16f)} glyphs");
            }
            else
            {
                phase = 40;
                var a = full.Run(srcRT, outA, fill10, edgeAtlas, edges: false);
                var b = full.Run(srcRT, outB, fill5, edgeAtlas, edges: false);
                Show(a, b, 0.5f, "Fill only, 10 glyphs:  . : - = + * # % @", "Fill only, 5 glyphs:  : + # @");
            }
            if (phase != logged) { logged = phase; Debug.Log($"[gt] t {t:F2} phase {phase} left '{left.text}' right '{right.text}'"); }
        }
    }
}
