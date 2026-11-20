// Drives the capsule occlusion post: the stand-in mannequin walks along the back wall, turns, stands by the pillar,
// crouches and stands up; its 18 occlusion capsules ride on its bones. Cameras move, and each camera's
// CapsuleOcclusionURP switches (on/off, on the figure, view mode) set up the comparisons on the same frame.
using System.Collections.Generic;
using CapsuleOcclusion;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class CapsuleDirector : MonoBehaviour
    {
        public Camera camA, camB, camC;
        public Transform character;
        public LabelSpec title, l1, l2, m1, m2, r1, r2;
        public Color sky = new Color(0.55f, 0.62f, 0.75f), ground = new Color(0.16f, 0.15f, 0.14f);
        public float skyIntensity = 2.6f;

        readonly Dictionary<Transform, Quaternion> bind = new Dictionary<Transform, Quaternion>();
        readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        int logged = -1; float nextLog = 0f;
        const int CharLayer = 24, ProxyLayer = 25;

        void Awake()
        {
            foreach (var t in character.GetComponentsInChildren<Transform>(true)) { bones[t.name] = t; if (t != character) bind[t] = t.localRotation; }
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(ground);
            sh.AddDirectionalLight(Vector3.up, sky, skyIntensity);
            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = sh;
        }

        void Turn(string bone, float deg, Vector3 axis)
        {
            if (!bones.TryGetValue(bone, out var b)) return;
            b.rotation = Quaternion.AngleAxis(deg, character.TransformDirection(axis)) * b.rotation;
        }

        static float Smooth(float a, float b, float t) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, t));

        // 0-4 s walk along the back wall (+x), 4-5 s turn to face the camera, stand; 10.5-12 s crouch,
        // 12-16 s hold, 16-17.5 s stand up
        void Pose(float t)
        {
            const float speed = 0.4f, x0 = -0.75f, z = 1.42f;
            float walkT = Mathf.Min(t, 4f);
            float x = x0 + walkT * speed;
            float gait = 1f - Smooth(3.7f, 4.3f, t);
            float yaw = Mathf.Lerp(90f, 180f, Smooth(4f, 5f, t));
            float crouch = Smooth(10.5f, 12f, t) * (1f - Smooth(16f, 17.5f, t));
            character.position = new Vector3(x, -0.40f * crouch, z);
            character.rotation = Quaternion.Euler(0, yaw, 0);
            foreach (var kv in bind) kv.Key.localRotation = kv.Value;
            float ph = walkT * speed / 0.62f * Mathf.PI;
            float sw = Mathf.Sin(ph) * gait;
            Turn("Spine", 30f * crouch, Vector3.right);
            Turn("Upperarm_L", 72f, Vector3.forward); Turn("Upperarm_R", -72f, Vector3.forward);
            Turn("Upperarm_L", 22f * sw - 45f * crouch, Vector3.right); Turn("Upperarm_R", -22f * sw - 45f * crouch, Vector3.right);
            Turn("Lowerarm_L", -12f - 40f * crouch, Vector3.right); Turn("Lowerarm_R", -12f - 40f * crouch, Vector3.right);
            Turn("Upperleg_L", -26f * sw - 100f * crouch, Vector3.right); Turn("Upperleg_R", 26f * sw - 100f * crouch, Vector3.right);
            Turn("Lowerleg_L", 30f * Mathf.Max(0f, -Mathf.Cos(ph)) * gait + 130f * crouch, Vector3.right);
            Turn("Lowerleg_R", 30f * Mathf.Max(0f, Mathf.Cos(ph)) * gait + 130f * crouch, Vector3.right);
            Turn("Foot_L", -30f * crouch, Vector3.right); Turn("Foot_R", -30f * crouch, Vector3.right);
            Turn("Head", 6f * Mathf.Sin(t * 0.6f) - 10f * crouch, Vector3.up);
        }

        static void Look(Camera c, Vector3 pos, Vector3 target) { c.transform.position = pos; c.transform.LookAt(target); }

        void Show(Camera c, bool on, Rect r, float fov, bool occ, bool onChar, int view)
        {
            c.enabled = on;
            if (!on) return;
            c.rect = r; c.fieldOfView = fov; c.aspect = r.width * 1920f / (r.height * 1080f);
            var co = c.GetComponent<CapsuleOcclusionURP>(); co.occlusion = occ; co.onCharacter = onChar; co.view = view;
            c.cullingMask = view == 2 ? ~(1 << CharLayer) : ~(1 << ProxyLayer);
        }

        void Labels(string t, string a1, string a2, string b1, string b2, string c1 = "", string c2 = "")
        {
            title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; r1.text = c1; r2.text = c2;
        }

        static Vector3 Orbit(Vector3 c, float deg, float dist, float y) => c + new Vector3(Mathf.Sin(deg * Mathf.Deg2Rad) * dist, y, -Mathf.Cos(deg * Mathf.Deg2Rad) * dist);

        // CPU copy of the source's capOcclusionFast summed over all capsules, as the shader does for a point
        static float AoAt(Vector3 p, Vector3 n, float intensity, float rangeMul)
        {
            float occ = 0;
            foreach (var c in OcclusionCapsule.instances)
            {
                Vector3 a = c.point1, b = c.point2; float r = c.radius, maxD = r * rangeMul, r2 = r * r;
                Vector3 ba = b - a, pa = p - a;
                float h = Mathf.Clamp01(Vector3.Dot(pa, ba) / Mathf.Max(Vector3.Dot(ba, ba), 1e-8f));
                Vector3 d = pa - h * ba; float l = Vector3.Dot(d, d);
                float o = -Vector3.Dot(d, n) * r2 * r / (l * l);
                float s = Mathf.Clamp01(1f - (l - r2) / (maxD * maxD)); s = s * s * (3 - 2 * s);
                occ += Mathf.Clamp01(o * s);
            }
            return 1f / (Mathf.Max(0, occ * intensity) + 1f);
        }

        void LateUpdate()
        {
            float t = FrameCapturer.T;
            Pose(t);
            var full = new Rect(0, 0, 1, 1);
            var h0 = new Rect(0, 0, 0.4985f, 1); var h1 = new Rect(0.5015f, 0, 0.4985f, 1);
            int n = OcclusionCapsule.instances.Count;
            var focus = new Vector3(0.5f, 0.6f, 1.3f);
            int phase; float mx = 0.512f;
            if (t < 4f)
            {
                phase = 0;
                Show(camA, true, full, 40f, true, true, 0); Show(camB, false, full, 0, false, false, 0); Show(camC, false, full, 0, false, false, 0);
                Look(camA, Orbit(focus, Mathf.Lerp(-30f, -12f, t / 4f), 3.4f, 1.0f), focus);
                Labels("Capsule occlusion", $"Soft ambient occlusion from {n} capsules riding on the figure's bones", "", "", "");
            }
            else if (t < 10f)
            {
                phase = 1;
                Show(camA, true, h0, 44f, false, true, 0); Show(camB, true, h1, 44f, true, true, 0); Show(camC, false, full, 0, false, false, 0);
                float u = Mathf.SmoothStep(0f, 1f, (t - 4f) / 6f);
                var f = new Vector3(0.85f, 0.55f, 1.4f);
                foreach (var c in new[] { camA, camB }) Look(c, Orbit(f, Mathf.Lerp(-8f, 10f, u), 2.3f, 0.35f), f);
                Labels("Capsule occlusion off vs on",
                    "Off", "ambient light ignores the figure",
                    "On", "floor, wall and pillar darken near the body");
            }
            else if (t < 16f)
            {
                phase = 2;
                Show(camA, true, h0, 40f, true, false, 0); Show(camB, true, h1, 40f, true, true, 0); Show(camC, false, full, 0, false, false, 0);
                float u = Mathf.SmoothStep(0f, 1f, (t - 10f) / 6f);
                var f = new Vector3(0.85f, 0.48f, 1.4f);
                foreach (var c in new[] { camA, camB }) Look(c, Orbit(f, Mathf.Lerp(-26f, -38f, u), Mathf.Lerp(2.8f, 2.5f, u), 0.4f), f);
                Labels("On the figure itself",
                    "Floor and walls only", "the figure gets no occlusion",
                    "Also on the figure", "normal-based: legs, arms and torso darken each other");
            }
            else if (t < 22f)
            {
                phase = 3;
                float w = 0.3313f;
                Show(camA, true, new Rect(0, 0, w, 1), 46f, true, true, 0);
                Show(camB, true, new Rect(w + 0.003f, 0, w, 1), 46f, true, true, 1);
                Show(camC, true, new Rect(2 * w + 0.006f, 0, w, 1), 46f, true, true, 2);
                float u = Mathf.SmoothStep(0f, 1f, (t - 16f) / 6f);
                var f = new Vector3(0.85f, 0.65f, 1.4f);
                foreach (var c in new[] { camA, camB, camC }) Look(c, Orbit(f, Mathf.Lerp(-12f, 6f, u), 2.6f, 0.55f), f);
                var co = camA.GetComponent<CapsuleOcclusionURP>();
                Labels($"Shaded, ambient only, debug ({co.clusterSize.x}x{co.clusterSize.y}x{co.clusterSize.z} clusters)",
                    "Final", "", "Ambient only", "occlusion acts on ambient light",
                    "Debug", "capsules + count per cluster");
                mx = 0.346f;
            }
            else
            {
                phase = 4;
                Show(camA, true, full, 40f, true, true, 0); Show(camB, false, full, 0, false, false, 0); Show(camC, false, full, 0, false, false, 0);
                float u = Mathf.SmoothStep(0f, 1f, (t - 22f) / 4f);
                Look(camA, Orbit(focus, Mathf.Lerp(14f, 30f, u), 3.3f, 1.1f), focus);
                Labels("Capsule occlusion", "Unity 6.3 URP, ported from Fewes' CapsuleOcclusion: analytic capsule AO, clustered", "", "", "");
            }
            if (phase == 0 || phase == 4) l1.anchor = new Vector2(0.012f, 0.03f);
            else { l1.anchor = new Vector2(0.012f, 0.058f); l2.anchor = new Vector2(0.012f, 0.018f); }
            m1.anchor = new Vector2(mx, 0.058f); m2.anchor = new Vector2(mx, 0.018f);
            r1.anchor = new Vector2(0.68f, 0.058f); r2.anchor = new Vector2(0.68f, 0.018f);
            if (phase != logged) { logged = phase; Debug.Log($"[gt] t {t:F2} phase {phase} '{title.text}' | '{l1.text}' '{l2.text}' | '{m1.text}' '{m2.text}' | '{r1.text}' '{r2.text}'"); }
            if (t >= nextLog)
            {
                nextLog = t + 2f;
                var co = camA.GetComponent<CapsuleOcclusionURP>();
                Vector3 feet = new Vector3(character.position.x, 0.002f, character.position.z);
                Vector3 wall = new Vector3(character.position.x, 0.9f, 1.599f);
                Debug.Log($"[gt] t {t:F2} capsules {n} visible {CapsuleOcclusionURP.LastVisible} cluster entries {CapsuleOcclusionURP.LastEntries} " +
                    $"| AO floor under figure {AoAt(feet, Vector3.up, co.intensity, co.capsuleRangeMultiplier):F3} floor 0.5 m away {AoAt(feet + new Vector3(0, 0, -0.5f), Vector3.up, co.intensity, co.capsuleRangeMultiplier):F3} wall behind {AoAt(wall, Vector3.back, co.intensity, co.capsuleRangeMultiplier):F3} " +
                    $"| buffers {co.GetByteCount() / 1048576f:F1} MB per camera, range {co.maxCameraRange} m, figure y {character.position.y:F2} feet y {bones["Foot_L"].position.y:F3} {bones["Foot_R"].position.y:F3}");
            }
        }
    }
}
