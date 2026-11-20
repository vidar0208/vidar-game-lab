// Drives the screen-space path tracing post. The source's renderer feature (ScreenSpacePathTracingAccumulation) runs
// unchanged; the pipeline copy holds three deferred renderers: 0 = no path tracing, 1 and 2 = one feature instance each,
// so the two halves of a split frame keep separate accumulation histories. Each camera reads its own path tracing volume
// (layer 20 for the left camera, 21 for the right one). Sample counts on the labels are read from the feature's own counter.
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class SSPTDirector : MonoBehaviour
    {
        public RenderPipelineAsset pipeline;
        public Camera camL, camR;
        public Volume volA, volB;
        public ScriptableRendererFeature featA, featB;
        public Material matA, matB;
        public ReflectionProbe probe;
        public LabelSpec title, l1, l2, r1, r2;

        RenderPipelineAsset previous;
        ScreenSpacePathTracing ssA, ssB;
        UniversalAdditionalCameraData dataL, dataR;
        static readonly FieldInfo passField = typeof(ScreenSpacePathTracingAccumulation).GetField("m_AccumulationPass", BindingFlags.NonPublic | BindingFlags.Instance);
        int logged = -1, lastB = -1, stopFrame = -1, warm;
        RenderTexture warmRT;

        void Awake()
        {
            previous = QualitySettings.renderPipeline;
            QualitySettings.renderPipeline = pipeline;
        }

        void OnDestroy() { QualitySettings.renderPipeline = previous; }

        void Start()
        {
            volA.profile.TryGet(out ssA); volB.profile.TryGet(out ssB);
            dataL = camL.GetUniversalAdditionalCameraData(); dataR = camR.GetUniversalAdditionalCameraData();
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black; RenderSettings.ambientIntensity = 0f;
            probe.RenderProbe();
            Debug.Log($"[gt] pipeline {GraphicsSettings.currentRenderPipeline?.name}, probe rt {probe.realtimeTexture != null}, bounds {probe.bounds.min} {probe.bounds.max}, ssA {ssA != null} ssB {ssB != null}");
        }

        void ProbeToMaterials()
        {
            // Same values the source's PathTracingSetReflectionProbe script writes for the deferred path.
            foreach (var m in new[] { matA, matB })
            {
                m.SetTexture("_SpecCube0", probe.realtimeTexture);
                m.SetVector("_SpecCube0_HDR", probe.textureHDRDecodeValues);
                m.SetVector("_SpecCube0_BoxMin", probe.bounds.min);
                m.SetVector("_SpecCube0_BoxMax", probe.bounds.max);
                var p = probe.transform.position;
                m.SetVector("_SpecCube0_ProbePosition", new Vector4(p.x, p.y, p.z, probe.boxProjection ? 1f : 0f));
                m.SetFloat("_ProbeWeight", 0f);
            }
        }

        int Samples(ScriptableRendererFeature f)
        {
            var pass = passField?.GetValue(f);
            if (pass == null) return -1;
            return (int)pass.GetType().GetField("sample").GetValue(pass);
        }

        static void Pose(Camera c, float yaw, float dist, float height, Vector3 target)
        {
            var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.back;
            c.transform.position = target + dir * dist + Vector3.up * (height - target.y);
            c.transform.LookAt(target);
        }

        void Full(Camera on, Camera off)
        {
            on.enabled = true; on.rect = new Rect(0, 0, 1, 1); off.enabled = false;
        }

        void Split()
        {
            camL.enabled = camR.enabled = true;
            camL.rect = new Rect(0f, 0f, 0.4985f, 1f); camR.rect = new Rect(0.5015f, 0f, 0.4985f, 1f);
        }

        void Labels(string t, string a1, string a2, string b1, string b2, bool split)
        {
            title.text = t;
            l1.text = a1; l2.text = a2; r1.text = b1; r2.text = b2;
            l1.anchor = new Vector2(0.012f, 0.058f); l2.anchor = new Vector2(0.012f, 0.018f);
            float x = split ? 0.512f : 0.512f;
            r1.anchor = new Vector2(x, 0.058f); r2.anchor = new Vector2(x, 0.018f);
        }

        void LateUpdate()
        {
            float t = FrameCapturer.T;
            ProbeToMaterials();
            var target = new Vector3(0f, 1.05f, 4.1f);
            int phase;
            int nB = Samples(featB), nA = Samples(featA);
            if (t < 3f)
            {
                phase = 0; Full(camR, camL); dataR.SetRenderer(2);
                ssB.denoiser.value = ScreenSpacePathTracing.DenoiserType.Temporal;
                matB.SetFloat("_ProbeSet", 1f);
                camR.fieldOfView = 52f; Pose(camR, Mathf.Lerp(-28f, -20f, t / 3f), 4.0f, 1.45f, target);
                Labels("Screen-space path tracing: paths bounce between surfaces the camera sees", "", "", "", "", false);
            }
            else if (t < 10f)
            {
                phase = 1; Split(); dataL.SetRenderer(0); dataR.SetRenderer(2);
                ssB.denoiser.value = ScreenSpacePathTracing.DenoiserType.Temporal;
                matB.SetFloat("_ProbeSet", 1f);
                float u = Mathf.SmoothStep(0f, 1f, (t - 3f) / 7f);
                foreach (var c in new[] { camL, camR }) { c.fieldOfView = 62f; Pose(c, Mathf.Lerp(-20f, 18f, u), 3.6f, 1.4f, target); }
                Labels("Same frame, same lights", "Path tracing off", "direct light only", "Path tracing on", "4 bounces, 1 path per pixel per frame + probe fallback", true);
            }
            else if (t < 17f)
            {
                phase = 2; Split(); dataL.SetRenderer(1); dataR.SetRenderer(2);
                ssA.denoiser.value = ScreenSpacePathTracing.DenoiserType.Temporal;
                ssB.denoiser.value = ScreenSpacePathTracing.DenoiserType.Offline;
                matA.SetFloat("_ProbeSet", 1f); matB.SetFloat("_ProbeSet", 1f);
                float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 10f) / 2.2f));
                foreach (var c in new[] { camL, camR }) { c.fieldOfView = 62f; Pose(c, Mathf.Lerp(18f, 4f, u), Mathf.Lerp(3.6f, 3.1f, u), Mathf.Lerp(1.4f, 1.2f, u), target); }
                bool moving = t < 12.2f;
                if (!moving && stopFrame < 0) stopFrame = Time.frameCount;
                Labels("Camera moves, then stops", "Real-time: temporal reprojection", "1 new path per pixel each frame, history reused",
                    "Offline accumulation", moving ? $"restarts while the camera moves: {Mathf.Max(nB, 0)} sample" : $"{nB} of 128 samples accumulated", true);
            }
            else if (t < 22f)
            {
                phase = 3; Split(); dataL.SetRenderer(1); dataR.SetRenderer(2);
                ssA.denoiser.value = ScreenSpacePathTracing.DenoiserType.Temporal;
                ssB.denoiser.value = ScreenSpacePathTracing.DenoiserType.Temporal;
                matA.SetFloat("_ProbeSet", 0f); matB.SetFloat("_ProbeSet", 1f);
                float u = Mathf.SmoothStep(0f, 1f, (t - 17f) / 5f);
                foreach (var c in new[] { camL, camR }) { c.fieldOfView = 62f; Pose(c, Mathf.Lerp(4f, -14f, u), 3.1f, Mathf.Lerp(1.2f, 0.75f, u), target); }
                Labels("Paths that leave the screen", "Probe fallback off", "they return black", "Probe fallback on", "they read a reflection probe rendered at start", true);
            }
            else
            {
                phase = 4; Full(camR, camL); dataR.SetRenderer(2);
                ssB.denoiser.value = ScreenSpacePathTracing.DenoiserType.Temporal;
                matB.SetFloat("_ProbeSet", 1f);
                float u = Mathf.SmoothStep(0f, 1f, (t - 22f) / 4f);
                camR.fieldOfView = 52f; Pose(camR, Mathf.Lerp(-14f, -30f, u), Mathf.Lerp(3.1f, 3.6f, u), Mathf.Lerp(0.75f, 1.0f, u), target);
                Labels("Screen-space path tracing on", "", "", "", "", false);
                l1.text = "Unity 6.3 URP, deferred, 4 bounces, temporal accumulation"; l1.anchor = new Vector2(0.012f, 0.018f);
            }
            // Warm-up frames (before the first captured frame) are not rendered by the capturer, so the opening shot would start
            // with an empty temporal history. Render the opening camera here during warm-up so the history is filled.
            if (FrameCapturer.T == 0f && phase == 0 && warm < 30)
            {
                if (warmRT == null) warmRT = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
                camR.targetTexture = warmRT; camR.Render(); camR.targetTexture = null; warm++;
            }
            if (phase != logged) { logged = phase; Debug.Log($"[gt] t {t:F2} phase {phase} '{title.text}' | '{l1.text}' '{l2.text}' | '{r1.text}' '{r2.text}'"); }
            if (nB != lastB && (phase == 2 || Time.frameCount % 30 == 0)) { lastB = nB; Debug.Log($"[gt] t {t:F2} frame {Time.frameCount} samples A {nA} B {nB} stop {stopFrame}"); }
        }
    }
}
