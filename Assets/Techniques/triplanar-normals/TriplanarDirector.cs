// Drives the triplanar normal mapping post: camera moves, split screens, and the blend method per camera
// (a global set just before each camera renders, read by GameTech/TriplanarLit).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class TriplanarDirector : MonoBehaviour
    {
        public RenderPipelineAsset pipeline;
        public Camera camA, camB, camC;
        public Transform rock, torus, block;
        public LabelSpec title, l1, l2, m1, m2, r1, r2;
        public float[] err;   // [1..5] mean angle error in degrees, [7..11] % of bumps flipped (CPU sphere test)

        const int None = 0, Naive = 1, UDN = 2, Whiteout = 3, RNM = 4, GPUGems3 = 5;
        RenderPipelineAsset previous;
        readonly Dictionary<Camera, int> mode = new Dictionary<Camera, int>();
        int logged = -1;

        void Awake() { previous = QualitySettings.renderPipeline; QualitySettings.renderPipeline = pipeline; }
        void OnEnable() { RenderPipelineManager.beginCameraRendering += Begin; }
        void OnDisable() { RenderPipelineManager.beginCameraRendering -= Begin; }
        void OnDestroy() { QualitySettings.renderPipeline = previous; }

        void Begin(ScriptableRenderContext ctx, Camera cam)
        {
            Shader.SetGlobalFloat("_GT_TriMode", mode.TryGetValue(cam, out var m) ? m : RNM);
        }

        static void Pose(Camera c, float yaw, float dist, float height, Vector3 target)
        {
            var d = Quaternion.Euler(0f, yaw, 0f) * Vector3.back;
            c.transform.position = target + d * dist + Vector3.up * (height - target.y);
            c.transform.LookAt(target);
        }

        void Show(Camera c, bool on, Rect r, float fov, int m)
        {
            c.enabled = on;
            if (!on) return;
            c.rect = r; c.fieldOfView = fov; c.aspect = r.width * 1920f / (r.height * 1080f);
            mode[c] = m;
        }

        void Labels(string t, string a1, string a2, string b1, string b2, string c1 = "", string c2 = "")
        {
            title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; r1.text = c1; r2.text = c2;
        }

        string E(int m) => $"{err[m]:F1}°";
        string F(int m) => $"{err[6 + m]:F0}%";

        void LateUpdate()
        {
            float t = FrameCapturer.T;
            var full = new Rect(0, 0, 1, 1);
            var h0 = new Rect(0, 0, 0.4985f, 1); var h1 = new Rect(0.5015f, 0, 0.4985f, 1);
            var mid = new Vector3(0.2f, 0.75f, 0.45f);
            int phase;
            float mx = 0.512f;
            if (t < 3.5f)
            {
                phase = 0;
                Show(camA, true, full, 38f, RNM); Show(camB, false, full, 0, 0); Show(camC, false, full, 0, 0);
                float u = t / 3.5f;
                Pose(camA, Mathf.Lerp(-30f, -18f, u), Mathf.Lerp(7.0f, 6.4f, u), 2.1f, mid);
                Labels("Triplanar normal mapping", "One normal map projected along X, Y and Z: no UVs on any of these meshes", "", "", "");
            }
            else if (t < 10.5f)
            {
                phase = 1;
                Show(camA, true, h0, 46f, Naive); Show(camB, true, h1, 46f, RNM); Show(camC, false, full, 0, 0);
                float u = Mathf.SmoothStep(0f, 1f, (t - 3.5f) / 7f);
                foreach (var c in new[] { camA, camB }) Pose(c, Mathf.Lerp(-25f, 0f, u), 5.2f, 1.7f, new Vector3(0.2f, 0.75f, 0.5f));
                Labels("Same normal map, same light (flip rate: test on a sphere)",
                    "Naive", $"blend 3 samples, use the mesh's tangents: {F(Naive)} of bumps flipped",
                    "Reoriented normal mapping (RNM)", $"each sample turned onto the surface: {F(RNM)} flipped");
            }
            else if (t < 16.5f)
            {
                phase = 2;
                float w = 0.3313f;
                Show(camA, true, new Rect(0, 0, w, 1), 36f, GPUGems3);
                Show(camB, true, new Rect(w + 0.003f, 0, w, 1), 36f, Whiteout);
                Show(camC, true, new Rect(2 * w + 0.006f, 0, w, 1), 36f, RNM);
                float u = Mathf.SmoothStep(0f, 1f, (t - 10.5f) / 6f);
                foreach (var c in new[] { camA, camB, camC }) Pose(c, Mathf.Lerp(-25f, 0f, u), 3.9f, 1.45f, torus.position + new Vector3(0f, -0.05f, 0f));
                Labels("Three correct blends: mean error vs a slope reference (sphere test)",
                    "GPU Gems 3", E(GPUGems3),
                    "Whiteout", E(Whiteout),
                    "RNM", E(RNM));
                mx = 0.346f;
            }
            else if (t < 22f)
            {
                phase = 3;
                Show(camA, true, h0, 42f, None); Show(camB, true, h1, 42f, RNM); Show(camC, false, full, 0, 0);
                float u = Mathf.SmoothStep(0f, 1f, (t - 16.5f) / 5.5f);
                foreach (var c in new[] { camA, camB }) Pose(c, Mathf.Lerp(-30f, -5f, u), 4.2f, 1.7f, block.position + new Vector3(-0.3f, -0.15f, 0f));
                Labels("Normal map off vs on",
                    "Vertex normals only", "same colours, no normal map",
                    "Triplanar normal map, RNM", "same colours with the cobble normal map");
            }
            else
            {
                phase = 4;
                Show(camA, true, full, 36f, RNM); Show(camB, false, full, 0, 0); Show(camC, false, full, 0, 0);
                float u = Mathf.SmoothStep(0f, 1f, (t - 22f) / 4f);
                Pose(camA, Mathf.Lerp(-5f, 18f, u), Mathf.Lerp(5.6f, 5.0f, u), Mathf.Lerp(1.5f, 1.3f, u), mid);
                Labels("Triplanar normals, RNM blend", "Unity 6.3 URP, ported from bgolus' triplanar shaders, procedural cobble normal map", "", "", "");
            }
            if (phase == 0 || phase == 4) l1.anchor = new Vector2(0.012f, 0.03f);
            else { l1.anchor = new Vector2(0.012f, 0.058f); l2.anchor = new Vector2(0.012f, 0.018f); }
            m1.anchor = new Vector2(mx, 0.058f); m2.anchor = new Vector2(mx, 0.018f);
            r1.anchor = new Vector2(0.68f, 0.058f); r2.anchor = new Vector2(0.68f, 0.018f);
            if (phase != logged) { logged = phase; Debug.Log($"[gt] t {t:F2} phase {phase} '{title.text}' | '{l1.text}' '{l2.text}' | '{m1.text}' '{m2.text}' | '{r1.text}' '{r2.text}'"); }
        }
    }
}
