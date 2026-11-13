// Runs the three Kuwahara filters on the scene camera's image every frame and feeds the split-screen compose quad.
// The passes and their parameters follow the source scripts (Kuwahara, GeneralizedKuwahara, AnisotropicKuwahara);
// OnRenderImage does not exist in URP, so the same Graphics.Blit chain runs here in LateUpdate, before the frame is captured.
using UnityEngine;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class KuwaharaDirector : MonoBehaviour
    {
        public Camera src;
        public Material basic, general, aniso, tensorView, compose;
        public LabelSpec left, right;
        public Vector3 focus = new Vector3(0, 0.9f, 0);

        RenderTexture srcRT, st, e1, e2, rBasic, rGen, rView;
        readonly RenderTexture[] rAni = new RenderTexture[3];
        int W, H, logged = -1;

        RenderTexture Make(RenderTextureFormat f, RenderTextureReadWrite rw, int depth = 0)
        {
            var r = new RenderTexture(W, H, depth, f, rw) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            r.Create();
            return r;
        }

        void Start()
        {
            W = int.Parse(FrameCapturer.Arg("-gtW", "1920")); H = int.Parse(FrameCapturer.Arg("-gtH", "1080"));
            srcRT = Make(RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB, 24);
            src.targetTexture = srcRT; src.enabled = false;
            st = Make(RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            e1 = Make(RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            e2 = Make(RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            rBasic = Make(RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            rGen = Make(RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            rView = Make(RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            for (int i = 0; i < 3; i++) rAni[i] = Make(RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        }

        // Kuwahara.cs: _KernelSize is the quadrant size, the window is 2k+1 pixels wide
        RenderTexture Basic(int k)
        {
            basic.SetInt("_KernelSize", k); basic.SetInt("_MinKernelSize", 1); basic.SetInt("_AnimateSize", 0); basic.SetInt("_AnimateOrigin", 0);
            Graphics.Blit(srcRT, rBasic, basic);
            return rBasic;
        }

        // GeneralizedKuwahara.cs: kernelSize is the diameter, zeta = 2 / radius unless set
        RenderTexture Generalized(int kernel, float sharpness = 8f, float hardness = 8f)
        {
            general.SetInt("_KernelSize", kernel); general.SetInt("_N", 8);
            general.SetFloat("_Q", sharpness); general.SetFloat("_Hardness", hardness);
            general.SetFloat("_ZeroCrossing", 0.58f); general.SetFloat("_Zeta", 2.0f / (kernel / 2.0f));
            Graphics.Blit(srcRT, rGen, general);
            return rGen;
        }

        // AnisotropicKuwahara.cs: structure tensor, two blur passes (the second one also solves the eigenvectors), then the filter
        RenderTexture Anisotropic(int kernel, int passes = 1, float sharpness = 8f, float hardness = 8f, float alpha = 1f)
        {
            aniso.SetInt("_KernelSize", kernel); aniso.SetInt("_N", 8);
            aniso.SetFloat("_Q", sharpness); aniso.SetFloat("_Hardness", hardness); aniso.SetFloat("_Alpha", alpha);
            aniso.SetFloat("_ZeroCrossing", 0.58f); aniso.SetFloat("_Zeta", 2.0f / 2.0f / (kernel / 2.0f));
            Graphics.Blit(srcRT, st, aniso, 0);
            Graphics.Blit(st, e1, aniso, 1);
            Graphics.Blit(e1, e2, aniso, 2);
            aniso.SetTexture("_TFM", e2);
            Graphics.Blit(srcRT, rAni[0], aniso, 3);
            for (int i = 1; i < passes; i++) Graphics.Blit(rAni[i - 1], rAni[i], aniso, 3);
            return rAni[passes - 1];
        }

        RenderTexture Tensor() { tensorView.SetTexture("_ST", st); tensorView.SetFloat("_Gain", 4f); Graphics.Blit(e2, rView, tensorView); return rView; }

        void Show(Texture a, Texture b, float split, string l, string r)
        {
            compose.SetTexture("_A", a); compose.SetTexture("_B", b); compose.SetFloat("_Split", split);
            left.text = l; right.text = r;
        }

        void LateUpdate()
        {
            float t = FrameCapturer.T;

            // slow arc around the courtyard
            float ang = (-24f + t * 1.1f) * Mathf.Deg2Rad;
            var pos = focus + new Vector3(Mathf.Sin(ang) * 7.2f, 1.05f + 0.15f * Mathf.Sin(t * 0.25f), -Mathf.Cos(ang) * 7.2f);
            src.transform.position = pos;
            src.transform.LookAt(focus);
            src.Render();

            float slide = Mathf.SmoothStep(1f, 0.5f, Mathf.Clamp01((t - 2.5f) / 0.7f));
            int phase;
            if (t < 2.5f) { phase = 0; Show(srcRT, srcRT, 1f, "Original render (URP Lit)", ""); }
            else if (t < 7.5f) { phase = 1; Show(srcRT, Basic(10), slide, "Original", "Basic Kuwahara: 4 squares, radius 10 px"); }
            else if (t < 11f) { phase = 2; Show(Basic(10), Generalized(20), 0.5f, "Basic: 4 square sectors", "Generalized: 8 sectors, radius 10 px"); }
            else if (t < 14.5f) { phase = 3; Show(Generalized(20), Anisotropic(20), 0.5f, "Generalized: sectors on a circle", "Anisotropic: sectors on an ellipse"); }
            else if (t < 17.5f)
            {
                phase = 4; var a = Anisotropic(20);
                Show(a, Tensor(), 0.5f, "Anisotropic, kernel size 20", "Tensor: hue = direction, light = edge x anisotropy");
            }
            else if (t < 21f)
            {
                int k = 4 + 2 * Mathf.Min(10, Mathf.FloorToInt((t - 17.5f) / 3.5f * 11f));
                phase = 100 + k;
                Show(Anisotropic(k), srcRT, 1f, $"Anisotropic, kernel size {k}, sharpness 8, hardness 8", "");
            }
            else
            {
                int p = Mathf.Min(3, 1 + Mathf.FloorToInt((t - 21f) / 1.4f));
                phase = 200 + p;
                Show(Anisotropic(20, p), srcRT, 1f, $"Anisotropic, kernel size 20, {p} pass" + (p > 1 ? "es" : ""), "");
            }
            if (phase != logged) { logged = phase; Debug.Log($"[gt] t {t:F2} phase {phase} left '{left.text}' right '{right.text}'"); }
        }
    }
}
