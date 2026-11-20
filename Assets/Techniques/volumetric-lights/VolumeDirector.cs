// Drives the volumetric lights post: the mannequin walks back and forth under the cool spot, the cameras move,
// and each camera's VolumetricCamera switches (on/off, volume shadows, sample count) set up the comparisons.
using System.Collections.Generic;
using GameTech.VolumetricLightsURP;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class VolumeDirector : MonoBehaviour
    {
        public RenderPipelineAsset pipeline;
        public Camera camA, camB, camC;
        public Transform character;
        public Texture2D dither;
        public Texture3D noise;
        public VolumetricLight[] vols;
        public LabelSpec title, l1, l2, m1, m2, r1, r2;

        RenderPipelineAsset previous;
        readonly Dictionary<Transform, Quaternion> bind = new Dictionary<Transform, Quaternion>();
        readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        int logged = -1; float nextLog = 0f;

        void Awake()
        {
            previous = QualitySettings.renderPipeline;
            QualitySettings.renderPipeline = pipeline;
            VolumetricLightFeature.DitherTex = dither; VolumetricLightFeature.NoiseTex = noise;
            foreach (var t in character.GetComponentsInChildren<Transform>(true)) { bones[t.name] = t; if (t != character) bind[t] = t.localRotation; }
        }

        void OnDestroy() { QualitySettings.renderPipeline = previous; }

        void Turn(string bone, float deg, Vector3 axis)
        {
            if (!bones.TryGetValue(bone, out var b)) return;
            b.rotation = Quaternion.AngleAxis(deg, character.TransformDirection(axis)) * b.rotation;
        }

        // walk along x under the cool spot (x 1.2 .. 4.8 at z 4.4), turning at the ends; simple gait
        void Walk(float t)
        {
            const float speed = 0.9f, x0 = 1.2f, x1 = 4.8f, len = x1 - x0;
            float s = Mathf.Repeat(t * speed, 2 * len);
            bool back = s > len;
            float x = back ? x1 - (s - len) : x0 + s;
            character.position = new Vector3(x, 0f, 4.4f);
            character.rotation = Quaternion.Euler(0, back ? -90f : 90f, 0);
            foreach (var kv in bind) kv.Key.localRotation = kv.Value;
            float ph = t * speed / 0.62f * Mathf.PI;   // one stride = 1.24 m
            float sw = Mathf.Sin(ph);
            Turn("Upperarm_L", 72f, Vector3.forward); Turn("Upperarm_R", -72f, Vector3.forward);
            Turn("Upperarm_L", 22f * sw, Vector3.right); Turn("Upperarm_R", -22f * sw, Vector3.right);
            Turn("Lowerarm_L", -12f, Vector3.right); Turn("Lowerarm_R", -12f, Vector3.right);
            Turn("Upperleg_L", -26f * sw, Vector3.right); Turn("Upperleg_R", 26f * sw, Vector3.right);
            Turn("Lowerleg_L", 30f * Mathf.Max(0f, -Mathf.Cos(ph)), Vector3.right); Turn("Lowerleg_R", 30f * Mathf.Max(0f, Mathf.Cos(ph)), Vector3.right);
            Turn("Head", 4f * Mathf.Sin(t * 0.7f), Vector3.up);
        }

        static void Look(Camera c, Vector3 pos, Vector3 target) { c.transform.position = pos; c.transform.LookAt(target); }

        void Show(Camera c, bool on, Rect r, float fov, bool vol, bool shadows, int samples)
        {
            c.enabled = on;
            if (!on) return;
            c.rect = r; c.fieldOfView = fov; c.aspect = r.width * 1920f / (r.height * 1080f);
            var v = c.GetComponent<VolumetricCamera>(); v.volumetric = vol; v.shadows = shadows; v.sampleOverride = samples; v.skipDirectional = false;
        }

        void Labels(string t, string a1, string a2, string b1, string b2, string c1 = "", string c2 = "")
        {
            title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; r1.text = c1; r2.text = c2;
        }

        void LateUpdate()
        {
            float t = FrameCapturer.T;
            Walk(t);
            var full = new Rect(0, 0, 1, 1);
            var h0 = new Rect(0, 0, 0.4985f, 1); var h1 = new Rect(0.5015f, 0, 0.4985f, 1);
            int phase; float mx = 0.512f;
            if (t < 4f)
            {
                phase = 0;
                Show(camA, true, full, 50f, true, true, 0); Show(camB, false, full, 0, false, false, 0); Show(camC, false, full, 0, false, false, 0);
                float u = t / 4f;
                Look(camA, new Vector3(Mathf.Lerp(-1.6f, -0.6f, u), 1.7f, -1.6f), new Vector3(0.2f, 2.0f, 5.5f));
                Labels("Volumetric lights", "Ray marched inside each light's volume: sun through windows, two spots, a lantern", "", "", "");
            }
            else if (t < 10f)
            {
                phase = 1;
                Show(camA, true, h0, 56f, false, true, 0); Show(camB, true, h1, 56f, true, true, 0); Show(camC, false, full, 0, false, false, 0);
                float u = Mathf.SmoothStep(0f, 1f, (t - 4f) / 6f);
                foreach (var c in new[] { camA, camB }) Look(c, new Vector3(Mathf.Lerp(-0.4f, 0.8f, u), 1.6f, -1.4f), new Vector3(0.4f, 1.9f, 5f));
                Labels("Volumetric lights off vs on",
                    "Off", "light only shows where it hits a surface",
                    "On", "light scattered by the air along each view ray");
            }
            else if (t < 16f)
            {
                phase = 2;
                Show(camA, true, h0, 52f, true, false, 0); Show(camB, true, h1, 52f, true, true, 0); Show(camC, false, full, 0, false, false, 0);
                camA.GetComponent<VolumetricCamera>().skipDirectional = true; camB.GetComponent<VolumetricCamera>().skipDirectional = true;
                float u = Mathf.SmoothStep(0f, 1f, (t - 10f) / 6f);
                foreach (var c in new[] { camA, camB }) Look(c, new Vector3(Mathf.Lerp(0.8f, 0.3f, u), 1.6f, Mathf.Lerp(1.0f, 1.5f, u)), new Vector3(-2.6f, 1.5f, 3.4f));
                Labels("Volume shadows (spot light only, sun volume left out of this shot)",
                    "Shadows off in the volume", "the beam ignores the slats",
                    "Shadows on", "the spot's shadow map is read at every step");
            }
            else if (t < 22f)
            {
                phase = 3;
                float w = 0.3313f;
                Show(camA, true, new Rect(0, 0, w, 1), 46f, true, true, 4);
                Show(camB, true, new Rect(w + 0.003f, 0, w, 1), 46f, true, true, 8);
                Show(camC, true, new Rect(2 * w + 0.006f, 0, w, 1), 46f, true, true, 32);
                float u = Mathf.SmoothStep(0f, 1f, (t - 16f) / 6f);
                foreach (var c in new[] { camA, camB, camC }) Look(c, new Vector3(Mathf.Lerp(-0.2f, -0.6f, u), 1.4f, 2.0f), new Vector3(-2.6f, 1.4f, 3.3f));
                Labels("Samples per ray (8x8 dither + blur hide the steps)",
                    "4 samples", "", "8 samples", "source default", "32 samples", "");
                mx = 0.346f;
            }
            else
            {
                phase = 4;
                Show(camA, true, full, 46f, true, true, 0); Show(camB, false, full, 0, false, false, 0); Show(camC, false, full, 0, false, false, 0);
                float u = Mathf.SmoothStep(0f, 1f, (t - 22f) / 4f);
                Look(camA, new Vector3(Mathf.Lerp(1.0f, 2.0f, u), 1.4f, Mathf.Lerp(0.4f, 1.0f, u)), new Vector3(3.0f, 1.6f, 4.6f));
                Labels("Volumetric lights", "Unity 6.3 URP, ported from SlightlyMad's VolumetricLights: half-res ray march, bilateral blur and upsample", "", "", "");
            }
            if (phase == 0 || phase == 4) l1.anchor = new Vector2(0.012f, 0.03f);
            else { l1.anchor = new Vector2(0.012f, 0.058f); l2.anchor = new Vector2(0.012f, 0.018f); }
            m1.anchor = new Vector2(mx, 0.058f); m2.anchor = new Vector2(mx, 0.018f);
            r1.anchor = new Vector2(0.68f, 0.058f); r2.anchor = new Vector2(0.68f, 0.018f);
            if (phase != logged) { logged = phase; Debug.Log($"[gt] t {t:F2} phase {phase} '{title.text}' | '{l1.text}' '{l2.text}' | '{m1.text}' '{m2.text}' | '{r1.text}' '{r2.text}'"); }
            if (t >= nextLog) { nextLog = t + 2f; Debug.Log($"[gt] t {t:F2} volumetric lights drawn {VolumetricLightPass.LastLights}, half buffer {VolumetricLightPass.LastWidth}x{VolumetricLightPass.LastHeight}"); }
        }
    }
}
