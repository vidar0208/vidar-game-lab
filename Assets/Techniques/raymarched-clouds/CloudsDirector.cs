// Port of CloudMaster from SebLague/Clouds (MIT): generates the noise, feeds the cloud material every camera
// (the material runs in URP's Full Screen Pass Renderer Feature). Values are the repo's "Clouds Test" scene values.
// Added for the post: per-camera step size / lighting switch for the split screen, a frame-time benchmark, labels.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    public class CloudsDirector : MonoBehaviour
    {
        public Material mat;
        public ComputeShader noiseCompute;
        public Texture2D blueNoise;
        public Transform container;
        public Light sun;
        public Camera camFull, camL, camR, camBack;
        public LabelSpec title, l1, l2, m1, m2;

        struct Cfg { public float step; public bool lit; public Cfg(float s, bool l) { step = s; lit = l; } }
        readonly Dictionary<Camera, Cfg> cfg = new Dictionary<Camera, Cfg>();
        float msLit11, msDensity11, msLit88;
        int phaseShown = -1;
        Vector3 pos0; Quaternion rot0;

        void OnEnable() { RenderPipelineManager.beginCameraRendering += Begin; }
        void OnDisable() { RenderPipelineManager.beginCameraRendering -= Begin; }

        void Start()
        {
            var t0 = System.Diagnostics.Stopwatch.StartNew();
            var shape = CloudNoise.Generate(noiseCompute, 132, CloudNoise.Shape, "ShapeNoise");
            var detail = CloudNoise.Generate(noiseCompute, 64, CloudNoise.Detail, "DetailNoise");
            Debug.Log($"[gt] noise generated in {t0.ElapsedMilliseconds} ms");
            mat.SetTexture("NoiseTex", shape); mat.SetTexture("DetailNoiseTex", detail); mat.SetTexture("BlueNoise", blueNoise);
            // CloudMaster values from the Clouds Test scene
            mat.SetInteger("numStepsLight", 8); mat.SetFloat("rayOffsetStrength", 10f);
            mat.SetFloat("scale", 0.62f); mat.SetFloat("densityMultiplier", 1.07f); mat.SetFloat("densityOffset", -4.27f);
            mat.SetVector("shapeOffset", new Vector3(190.44f, 0, 0)); mat.SetVector("shapeNoiseWeights", new Vector4(1f, 0.48f, 0.15f, 0f));
            mat.SetFloat("detailNoiseScale", 3f); mat.SetFloat("detailNoiseWeight", 3.42f);
            mat.SetVector("detailWeights", new Vector3(1f, 0.5f, 0.5f)); mat.SetVector("detailOffset", new Vector3(51.25f, 0, 0));
            mat.SetFloat("lightAbsorptionThroughCloud", 0.75f); mat.SetFloat("lightAbsorptionTowardSun", 1.21f); mat.SetFloat("darknessThreshold", 0.15f);
            mat.SetVector("phaseParams", new Vector4(0.811f, 0.33f, 1f, 0.488f));   // forward, back scattering, base brightness, phase factor
            mat.SetFloat("timeScale", 1f); mat.SetFloat("baseSpeed", 0.5f); mat.SetFloat("detailSpeed", 1f);
            pos0 = camFull.transform.position; rot0 = camFull.transform.rotation;
            Bench();
        }

        // whole-frame time: render the full camera into a 1920x1080 target N times and wait for the GPU
        void Bench()
        {
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.DefaultHDR);
            float Measure(Cfg c)
            {
                cfg[camFull] = c; camFull.targetTexture = rt;
                for (int i = 0; i < 3; i++) camFull.Render();
                UnityEngine.Rendering.AsyncGPUReadback.Request(rt).WaitForCompletion();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                const int n = 20;
                for (int i = 0; i < n; i++) camFull.Render();
                UnityEngine.Rendering.AsyncGPUReadback.Request(rt).WaitForCompletion();
                camFull.targetTexture = null;
                return (float)sw.Elapsed.TotalMilliseconds / n;
            }
            msLit11 = Measure(new Cfg(11f, true)); msDensity11 = Measure(new Cfg(11f, false)); msLit88 = Measure(new Cfg(88f, true));
            msLit11 = Measure(new Cfg(11f, true));   // again, after warm-up of all variants
            rt.Release();
            Debug.Log($"[gt] bench ms: lit step11 {msLit11:F2}, density-only step11 {msDensity11:F2}, lit step88 {msLit88:F2}");
        }

        void Begin(ScriptableRenderContext ctx, Camera c)
        {
            var k = cfg.TryGetValue(c, out var v) ? v : new Cfg(11f, true);
            mat.SetFloat("stepSize", k.step); mat.SetInteger("lightingOn", k.lit ? 1 : 0);
            mat.SetFloat("cloudTime", FrameCapturer.T / 20f);   // the source used _Time.x (seconds / 20)
            mat.SetVector("sunDir", -sun.transform.forward); mat.SetColor("sunColor", sun.color * sun.intensity);
            mat.SetVector("boundsMin", container.position - container.localScale / 2);
            mat.SetVector("boundsMax", container.position + container.localScale / 2);
        }

        public static int Phase(float t) => t < 7f ? 0 : t < 14f ? 1 : t < 21f ? 2 : 3;

        void Update()
        {
            float t = FrameCapturer.T; int p = Phase(t);
            camFull.enabled = camL.enabled = camR.enabled = camBack.enabled = false;
            var fwd = rot0 * Vector3.forward;
            var pos = pos0 + fwd * (6f * t);
            var rot = Quaternion.AngleAxis(4f * Mathf.Sin(t * 0.2f), Vector3.up) * rot0;
            string per = "ms per 1920x1080 frame";
            if (p == 0)
            {
                Set(camFull, pos, rot); cfg[camFull] = new Cfg(11f, true);
                Labels("Raymarched clouds: a full-screen pass marching through 3D noise",
                    "every pixel steps through the cloud box; each sample marches again toward the sun",
                    "URP port of SebLague/Clouds; the Worley noise is built by its compute shader at startup", "", "");
            }
            else if (p == 1)
            {
                camBack.enabled = true; Set(camL, pos, rot); Set(camR, pos, rot);
                cfg[camL] = new Cfg(11f, false); cfg[camR] = new Cfg(11f, true);
                Labels("What the light march adds",
                    "density only: Beer's law along the view ray", $"no light march, no phase function. {msDensity11:F1} {per}",
                    "full: 8-step light march + phase function", $"{msLit11:F1} {per}");
            }
            else if (p == 2)
            {
                camBack.enabled = true; Set(camL, pos, rot); Set(camR, pos, rot);
                cfg[camL] = new Cfg(88f, true); cfg[camR] = new Cfg(11f, true);
                Labels("View-ray step size: fewer samples, less time",
                    "step 88 m (8x fewer samples)", $"{msLit88:F1} {per}",
                    "step 11 m (hard-coded in the source)", $"{msLit11:F1} {per}");
            }
            else
            {
                var r3 = Quaternion.AngleAxis(-22f * Mathf.SmoothStep(0, 1, (t - 21f) / 4f), Vector3.up) * rot;
                Set(camFull, pos, r3); cfg[camFull] = new Cfg(11f, true);
                Labels("Where the time goes (whole frame, 1920x1080)",
                    $"full {msLit11:F1} ms, density only {msDensity11:F1} ms, step 88 m {msLit88:F1} ms",
                    "the light march barely registers here; the steps along the view ray dominate. Apple M4, Metal, Unity 6.3 URP Editor", "", "");
            }
            if (p != phaseShown) { phaseShown = p; Debug.Log($"[gt] t {t:F2} phase {p} '{title.text}' | {l1.text} | {l2.text} | {m1.text} | {m2.text}"); }
        }

        static void Set(Camera c, Vector3 pos, Quaternion rot) { c.enabled = true; c.transform.SetPositionAndRotation(pos, rot); }

        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }
    }
}
