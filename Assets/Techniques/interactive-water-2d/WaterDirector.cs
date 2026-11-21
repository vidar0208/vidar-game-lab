// Drives the 2D water post: drops bodies into the pool on a fixed schedule, floats them (simple buoyancy, mine;
// the source water does not push back on bodies), runs a skimming hull along the surface, moves the cameras and
// sets the per-camera switches. Two water instances share the same bodies: one with the wave solve on (layer 20)
// and one with it off (layer 21); each camera sees one of them.
using System.Collections.Generic;
using Ruccho;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class WaterDirector : MonoBehaviour
    {
        public Camera camA, camB;
        public WaterRWCompute waterOn, waterOff;
        public Rigidbody2D[] bodies;
        public float[] dropTimes, dropX;
        public Rigidbody2D skimmer;
        public LabelSpec title, l1, l2, m1, m2;

        readonly Dictionary<Camera, float> rr = new Dictionary<Camera, float>();
        static readonly int RR = Shader.PropertyToID("_GT_WaterRR");
        int logged = -1; float nextLog = 0f; int dropped;
        const float SurfaceY = 0f;

        void OnEnable() { RenderPipelineManager.beginCameraRendering += OnCam; }
        void OnDisable() { RenderPipelineManager.beginCameraRendering -= OnCam; }
        void OnCam(ScriptableRenderContext ctx, Camera c) { Shader.SetGlobalFloat(RR, rr.TryGetValue(c, out var v) ? v : 1f); }

        void Awake()
        {
            waterOff.simulate = false;
            foreach (var b in bodies) b.gameObject.SetActive(false);
            skimmer.gameObject.SetActive(false);
        }

        void FixedUpdate()
        {
            float t = FrameCapturer.T;
            while (dropped < bodies.Length && t >= dropTimes[dropped])
            {
                var b = bodies[dropped];
                b.gameObject.SetActive(true);
                b.position = new Vector2(dropX[dropped], 4.2f); b.rotation = 25f * dropped;
                b.linearVelocity = new Vector2(0, -4f); b.angularVelocity = (dropped % 2 == 0 ? 1 : -1) * 60f;
                dropped++;
            }
            // buoyancy (mine): lift by the submerged fraction of the body's height, more drag in the water
            foreach (var b in bodies)
            {
                if (!b.gameObject.activeSelf) continue;
                float h = b.GetComponent<Collider2D>().bounds.extents.y;
                float sub = Mathf.Clamp01((SurfaceY - (b.position.y - h)) / (2 * h));
                b.AddForce(Vector2.up * sub * b.mass * 9.81f * 1.9f);
                b.linearDamping = 0.05f + 2.5f * sub; b.angularDamping = 0.05f + 2f * sub;
            }
            // skimming hull 16.2 .. 19.8 s: kinematic, moves right along the surface line
            bool skim = t >= 16.2f && t < 19.8f;
            if (skim && !skimmer.gameObject.activeSelf) { skimmer.gameObject.SetActive(true); skimmer.position = new Vector2(-5.2f, -0.06f); }
            if (skimmer.gameObject.activeSelf) skimmer.linearVelocity = skim ? new Vector2(2.8f, 0f) : Vector2.zero;
            if (!skim && skimmer.gameObject.activeSelf && t >= 19.8f) skimmer.gameObject.SetActive(false);
        }

        static void Look(Camera c, Vector3 pos, Vector3 target) { c.transform.position = pos; c.transform.LookAt(target); }

        void Show(Camera c, bool on, Rect r, float fov, bool solver, bool refl)
        {
            c.enabled = on;
            if (!on) return;
            c.rect = r; c.fieldOfView = fov; c.aspect = r.width * 1920f / (r.height * 1080f);
            c.cullingMask = ~((1 << 20) | (1 << 21)) | (1 << (solver ? 20 : 21));
            rr[c] = refl ? 1f : 0f;
        }

        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }

        void LateUpdate()
        {
            float t = FrameCapturer.T;
            var full = new Rect(0, 0, 1, 1);
            var h0 = new Rect(0, 0, 0.4985f, 1); var h1 = new Rect(0.5015f, 0, 0.4985f, 1);
            var w = waterOn;
            string solverText = $"c {w.C:0.0#}, decay {w.Decay:0.0#}, {w.BufferPixels}-cell buffer, {1f / w.StepSeconds:0} steps/s";
            int phase;
            if (t < 4f)
            {
                phase = 0;
                Show(camA, true, full, 22f, true, true); Show(camB, false, full, 0, true, true);
                float u = t / 4f;
                Look(camA, new Vector3(Mathf.Lerp(-1.2f, 0.4f, u), 1.4f, -27f), new Vector3(Mathf.Lerp(-1.2f, 0.4f, u), 0.6f, 0f));
                Labels("2D interactive water", "1D wave equation in a compute shader; bodies crossing the surface push it", "", "", "");
            }
            else if (t < 10f)
            {
                phase = 1;
                Show(camA, true, h0, 24f, false, true); Show(camB, true, h1, 24f, true, true);
                float u = Mathf.SmoothStep(0f, 1f, (t - 4f) / 6f);
                foreach (var c in new[] { camA, camB }) Look(c, new Vector3(Mathf.Lerp(-0.6f, 0.4f, u), 1.0f, -17f), new Vector3(Mathf.Lerp(-0.6f, 0.4f, u), 0.3f, 0f));
                Labels("Wave solve off vs on", "Off", "the surface stays flat", "On", solverText);
            }
            else if (t < 16f)
            {
                phase = 2;
                Show(camA, true, h0, 24f, true, false); Show(camB, true, h1, 24f, true, true);
                float u = Mathf.SmoothStep(0f, 1f, (t - 10f) / 6f);
                foreach (var c in new[] { camA, camB }) Look(c, new Vector3(Mathf.Lerp(0.6f, -0.4f, u), 1.3f, -17f), new Vector3(Mathf.Lerp(0.6f, -0.4f, u), 0.4f, 0f));
                Labels("Reflection and refraction", "Wave only", "flat water colour", "Reflection + refraction", "screen image mirrored and offset at the surface");
            }
            else if (t < 21f)
            {
                phase = 3;
                Show(camA, true, full, 22f, true, true); Show(camB, false, full, 0, true, true);
                float sx = skimmer.gameObject.activeSelf ? skimmer.position.x : 4.9f;
                float fx = Mathf.Clamp(sx - 0.6f, -3.5f, 4.5f);
                Look(camA, new Vector3(fx, 0.9f, -13f), new Vector3(fx, 0.15f, 0f));
                Labels("Close-up: a skimming hull", "Bodies on the surface line write their velocity into the wave buffer", "", "", "");
            }
            else
            {
                phase = 4;
                Show(camA, true, full, 22f, true, true); Show(camB, false, full, 0, true, true);
                float u = Mathf.SmoothStep(0f, 1f, (t - 21f) / 4f);
                Look(camA, new Vector3(Mathf.Lerp(0.2f, -0.6f, u), Mathf.Lerp(1.0f, 1.6f, u), Mathf.Lerp(-22f, -27f, u)), new Vector3(Mathf.Lerp(0.2f, -0.6f, u), 0.5f, 0f));
                Labels("2D interactive water", "Unity 6.3 URP, ported from ruccho's WaterRW", "", "", "");
            }
            if (phase == 0 || phase == 3 || phase == 4) l1.anchor = new Vector2(0.012f, 0.03f);
            else { l1.anchor = new Vector2(0.012f, 0.058f); l2.anchor = new Vector2(0.012f, 0.018f); }
            m1.anchor = new Vector2(0.512f, 0.058f); m2.anchor = new Vector2(0.512f, 0.018f);
            if (phase != logged) { logged = phase; Debug.Log($"[gt] t {t:F2} phase {phase} '{title.text}' | '{l1.text}' '{l2.text}' | '{m1.text}' '{m2.text}'"); }
            if (t >= nextLog)
            {
                nextLog = t + 2f;
                Debug.Log($"[gt] t {t:F2} steps on {waterOn.StepsDone} off {waterOff.StepsDone} | crossing items {waterOn.InteractionsThisStep} | bodies dropped {dropped} | {solverText}, spatial scale {w.SpatialScale}, {w.PixelsPerUnit} cells per unit");
            }
        }
    }
}
