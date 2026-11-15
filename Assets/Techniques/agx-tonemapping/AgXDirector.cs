// Drives the AgX post. At startup the quality level's render pipeline is switched to this post's copy of the URP asset
// (HDR colour grading, renderer with the Custom Tone Mapping renderer feature) and restored when play mode ends. Up to
// three band cameras render the same view each frame; each band camera reads the volume layer of one tone mapper, so
// every band is the same HDR frame through a different curve. Output is SDR.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class AgXDirector : MonoBehaviour
    {
        public RenderPipelineAsset pipeline;
        public Camera[] cams;
        public Volume[] vols;                 // 0 URP ACES, 1 AgX, 2 ACES 2.0, 3 AgX low contrast, 4 AgX high contrast
        public Material compose;
        public LabelSpec title;
        public LabelSpec[] l1, l2;
        public Color ambient;
        public Transform character;
        readonly System.Collections.Generic.Dictionary<string, Transform> bones = new();
        readonly System.Collections.Generic.Dictionary<Transform, Quaternion> bind = new();

        public const float BaseExposure = 0.3f;
        const int Aces = 0, Agx = 1, Aces2 = 2, AgxLow = 3, AgxHigh = 4;
        RenderPipelineAsset previous;
        readonly RenderTexture[] rt = new RenderTexture[3];
        ColorAdjustments[] adjust;
        int W, H, logged = -1;

        void Awake()
        {
            previous = QualitySettings.renderPipeline;
            QualitySettings.renderPipeline = pipeline;
        }

        void OnDestroy() { QualitySettings.renderPipeline = previous; }

        void Start()
        {
            W = int.Parse(FrameCapturer.Arg("-gtW", "1920")); H = int.Parse(FrameCapturer.Arg("-gtH", "1080"));
            for (int i = 0; i < 3; i++) { rt[i] = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); rt[i].Create(); }
            foreach (var c in cams) c.enabled = false;
            var sh = new SphericalHarmonicsL2(); sh.AddAmbientLight(ambient);
            RenderSettings.ambientMode = AmbientMode.Custom; RenderSettings.ambientProbe = sh;
            foreach (var tr in character.GetComponentsInChildren<Transform>(true))
            {
                if (!bones.ContainsKey(tr.name)) bones[tr.name] = tr;
                if (tr != character) bind[tr] = tr.localRotation;
            }
            adjust = new ColorAdjustments[vols.Length];
            for (int k = 0; k < vols.Length; k++) vols[k].profile.TryGet(out adjust[k]);   // runtime copies of the profiles
            Debug.Log($"[gt] pipeline {GraphicsSettings.currentRenderPipeline?.name}");
        }

        void Turn(string bone, float deg, Vector3 axis)
        {
            if (!bones.TryGetValue(bone, out var b)) return;
            b.rotation = Quaternion.AngleAxis(deg, character.TransformDirection(axis)) * b.rotation;
        }

        // stand-in mannequin: arms down, slow breathing and head turn
        void Idle(float t)
        {
            foreach (var kv in bind) kv.Key.localRotation = kv.Value;
            Turn("Upperarm_L", 77f, Vector3.forward); Turn("Upperarm_R", -77f, Vector3.forward);
            Turn("Upperarm_L", 5f, Vector3.right); Turn("Upperarm_R", 5f, Vector3.right);
            Turn("Lowerarm_L", -10f, Vector3.right); Turn("Lowerarm_R", -10f, Vector3.right);
            Turn("Chest", 1.2f * Mathf.Sin(t * 1.6f), Vector3.right);
            Turn("Head", 8f * Mathf.Sin(t * 0.3f), Vector3.up);
            Turn("Upperleg_L", 3f, Vector3.forward); Turn("Upperleg_R", -3f, Vector3.forward);
        }

        void Shot(int i, int mode)
        {
            var c = cams[i];
            c.GetUniversalAdditionalCameraData().volumeLayerMask = 1 << vols[mode].gameObject.layer;
            c.targetTexture = rt[i]; c.Render();
            compose.SetTexture("_B" + i, rt[i]);
        }

        void Labels(string ttl, float s1, float s2, string[] a, string[] b)
        {
            title.text = ttl;
            float[] start = { 0f, s1, s2 };
            for (int i = 0; i < 3; i++)
            {
                l1[i].text = a != null && i < a.Length ? a[i] : ""; l1[i].anchor = new Vector2(start[i] + 0.012f, 0.058f);
                l2[i].text = b != null && i < b.Length ? b[i] : ""; l2[i].anchor = new Vector2(start[i] + 0.012f, 0.018f);
            }
            compose.SetFloat("_S1", s1); compose.SetFloat("_S2", s2);
        }

        void Exposure(float ev)
        {
            foreach (var a in adjust) if (a != null) a.postExposure.Override(BaseExposure + ev);
        }

        void LateUpdate()
        {
            float t = FrameCapturer.T;
            Idle(t);
            // slow push from the back of the room towards the window
            float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 26f));
            Vector3 pos = Vector3.Lerp(new Vector3(-0.9f, 1.6f, -4.3f), new Vector3(0.1f, 1.45f, -1.2f), u);
            Vector3 look = Vector3.Lerp(new Vector3(0.1f, 1.45f, 4f), new Vector3(0.2f, 1.25f, 3f), u);
            foreach (var c in cams) { c.transform.position = pos; c.transform.LookAt(look); }

            float third = 1f / 3f; int phase; float ev = 0f;
            if (t < 3f)
            {
                phase = 0; Exposure(0f); Shot(0, Agx);
                compose.SetVector("_Off", Vector4.zero);
                Labels("AgX tone mapping in URP through a renderer feature (SDR output)", 1f, 1f,
                    new[] { "AgX" }, new[] { "Custom Tone Mapping renderer feature, look None" });
            }
            else if (t < 10f)
            {
                phase = 1; Exposure(0f); Shot(0, Aces); Shot(1, Agx);
                compose.SetVector("_Off", new Vector4(0.25f, -0.25f, 0, 0));
                Labels("Same HDR frame, same exposure, two curves (SDR, centre of the frame)", 0.5f, 1f,
                    new[] { "URP ACES", "AgX" }, new[] { "URP's built-in Tonemapping, mode ACES", "renderer feature, look None" });
            }
            else if (t < 16f)
            {
                phase = 2; Exposure(0f); Shot(0, Aces); Shot(1, Aces2); Shot(2, Agx);
                compose.SetVector("_Off", new Vector4(third, 0, -third, 0));
                Labels("Three highlight roll-offs on the same frame (SDR)", third, 2 * third,
                    new[] { "URP ACES", "ACES 2.0", "AgX" }, new[] { "URP built-in", "Academy output transform", "look None" });
            }
            else if (t < 21f)
            {
                phase = 3; Exposure(0f); Shot(0, AgxLow); Shot(1, Agx); Shot(2, AgxHigh);
                compose.SetVector("_Off", new Vector4(third, 0, -third, 0));
                Labels("AgX looks on top of the same base curve (SDR)", third, 2 * third,
                    new[] { "AgX Low Contrast", "AgX", "AgX High Contrast" }, new[] { "look preset, intensity 1", "look None", "look preset, intensity 1" });
            }
            else
            {
                phase = 4; ev = 3f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(21.5f, 25.5f, t));
                Exposure(ev); Shot(0, Aces); Shot(1, Agx);
                compose.SetVector("_Off", new Vector4(0.25f, -0.25f, 0, 0));
                string e = $"post exposure +{ev:F1} EV";
                Labels("Push the exposure up: where each curve runs out (SDR)", 0.5f, 1f,
                    new[] { "URP ACES", "AgX" }, new[] { e, e });
            }
            if (phase != logged) { logged = phase; Debug.Log($"[gt] t {t:F2} phase {phase} '{title.text}' | '{l1[0].text}' '{l2[0].text}' | '{l1[1].text}' '{l2[1].text}' | '{l1[2].text}' '{l2[2].text}'"); }
        }
    }
}
