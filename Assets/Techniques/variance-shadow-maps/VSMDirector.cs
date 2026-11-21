// Drives the VSM post: the stand-in mannequin walks out of the sun and under the pergola's slatted roof, turns and
// stands in the striped shade. Cameras move, and each camera picks which version of the shadow map it reads
// (unblurred with a plain depth compare, 3-tap box blur, 21-tap Gaussian) so the comparisons share one frame.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class VSMDirector : MonoBehaviour
    {
        public Camera camA, camB;
        public Transform character;
        public ShadowVSMURP vsm;
        public LabelSpec title, l1, l2, m1, m2;
        public Color sky = new Color(0.55f, 0.62f, 0.78f), ground = new Color(0.22f, 0.19f, 0.16f);
        public float skyIntensity = 1.9f;
        public float hardBias = 0.05f;

        readonly Dictionary<Transform, Quaternion> bind = new Dictionary<Transform, Quaternion>();
        readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        readonly Dictionary<Camera, ShadowVSMURP.Version> mode = new Dictionary<Camera, ShadowVSMURP.Version>();
        int logged = -1; float nextLog = 0f;

        void Awake()
        {
            foreach (var t in character.GetComponentsInChildren<Transform>(true)) { bones[t.name] = t; if (t != character) bind[t] = t.localRotation; }
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(ground);
            sh.AddDirectionalLight(Vector3.up, sky, skyIntensity);
            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = sh;
        }

        void OnEnable() { RenderPipelineManager.beginCameraRendering += OnCam; }
        void OnDisable() { RenderPipelineManager.beginCameraRendering -= OnCam; }
        void OnCam(ScriptableRenderContext ctx, Camera c)
        {
            var v = mode.TryGetValue(c, out var m) ? m : ShadowVSMURP.Version.Box3;
            var tex = vsm.Get(v);
            if (tex != null) Shader.SetGlobalTexture("VSM_ShadowTex", tex);
            Shader.SetGlobalFloat("_GT_VSMMode", v == ShadowVSMURP.Version.Unblurred ? 0f : 1f);
            Shader.SetGlobalFloat("_GT_VSMHardBias", hardBias);
        }

        void Turn(string bone, float deg, Vector3 axis)
        {
            if (!bones.TryGetValue(bone, out var b)) return;
            b.rotation = Quaternion.AngleAxis(deg, character.TransformDirection(axis)) * b.rotation;
        }

        static float Smooth(float a, float b, float t) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, t));

        public const float X0 = -4.6f, Z = 1.35f, Speed = 0.45f, WalkEnd = 13.5f;

        // 0-13.5 s walk along +x, 13.5-15 s turn to face the camera and stand, small idle sway after
        void Pose(float t)
        {
            float walkT = Mathf.Min(t, WalkEnd);
            float x = X0 + walkT * Speed;
            float gait = 1f - Smooth(WalkEnd - 0.4f, WalkEnd + 0.3f, t);
            float yaw = Mathf.Lerp(90f, 160f, Smooth(WalkEnd, WalkEnd + 1.5f, t));
            character.position = new Vector3(x, 0f, Z);
            character.rotation = Quaternion.Euler(0, yaw, 0);
            foreach (var kv in bind) kv.Key.localRotation = kv.Value;
            float ph = walkT * Speed / 0.62f * Mathf.PI;
            float sw = Mathf.Sin(ph) * gait;
            float idle = (1f - gait) * Mathf.Sin(t * 1.3f);
            Turn("Spine", 2f * idle, Vector3.forward);
            Turn("Upperarm_L", 72f, Vector3.forward); Turn("Upperarm_R", -72f, Vector3.forward);
            Turn("Upperarm_L", 22f * sw, Vector3.right); Turn("Upperarm_R", -22f * sw, Vector3.right);
            Turn("Lowerarm_L", -12f, Vector3.right); Turn("Lowerarm_R", -12f, Vector3.right);
            Turn("Upperleg_L", -26f * sw, Vector3.right); Turn("Upperleg_R", 26f * sw, Vector3.right);
            Turn("Lowerleg_L", 30f * Mathf.Max(0f, -Mathf.Cos(ph)) * gait, Vector3.right);
            Turn("Lowerleg_R", 30f * Mathf.Max(0f, Mathf.Cos(ph)) * gait, Vector3.right);
            Turn("Head", 8f * Mathf.Sin(t * 0.5f) - 18f * (1f - gait), Vector3.up);
        }

        static void Look(Camera c, Vector3 pos, Vector3 target) { c.transform.position = pos; c.transform.LookAt(target); }

        void Show(Camera c, bool on, Rect r, float fov, ShadowVSMURP.Version v)
        {
            c.enabled = on;
            if (!on) return;
            c.rect = r; c.fieldOfView = fov; c.aspect = r.width * 1920f / (r.height * 1080f);
            mode[c] = v;
        }

        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }

        static Vector3 Orbit(Vector3 target, float yawDeg, float dist, float height)
        {
            float a = yawDeg * Mathf.Deg2Rad;
            return target + new Vector3(Mathf.Sin(a) * dist, height, -Mathf.Cos(a) * dist);
        }

        void LateUpdate()
        {
            float t = FrameCapturer.T;
            Pose(t);
            var full = new Rect(0, 0, 1, 1);
            var h0 = new Rect(0, 0, 0.4985f, 1); var h1 = new Rect(0.5015f, 0, 0.4985f, 1);
            var chest = character.position + Vector3.up * 0.85f;
            string res = $"{vsm.resolution} px per cascade";
            int phase;
            if (t < 4f)
            {
                phase = 0;
                Show(camA, true, full, 38f, ShadowVSMURP.Version.Box3); Show(camB, false, full, 0, 0);
                var tgt = new Vector3(-0.6f, 1.0f, 1.4f);
                Look(camA, Orbit(tgt, Mathf.Lerp(-28f, -12f, t / 4f), 7.6f, 1.9f), tgt);
                Labels("Variance shadow maps", "Depth and depth squared in the shadow map, blurred, then a Chebyshev bound per pixel", "", "", "");
            }
            else if (t < 11f)
            {
                phase = 1;
                Show(camA, true, h0, 40f, ShadowVSMURP.Version.Unblurred); Show(camB, true, h1, 40f, ShadowVSMURP.Version.Box3);
                float u = Mathf.SmoothStep(0f, 1f, (t - 4f) / 7f);
                var tgt = new Vector3(chest.x + 0.2f, 0.75f, Z + 0.4f);
                foreach (var c in new[] { camA, camB }) Look(c, Orbit(tgt, Mathf.Lerp(-18f, 4f, u), 3.6f, 0.75f), tgt);
                Labels("Plain depth compare vs VSM", "Plain depth compare", $"same map, {res}, point sampled", "VSM", $"{res}, 3-tap box blur (source default)");
            }
            else if (t < 18f)
            {
                phase = 2;
                Show(camA, true, h0, 38f, ShadowVSMURP.Version.Box3); Show(camB, true, h1, 38f, ShadowVSMURP.Version.Gauss21);
                float u = Mathf.SmoothStep(0f, 1f, (t - 11f) / 7f);
                var tgt = new Vector3(chest.x, 0.8f, Z + 0.3f);
                foreach (var c in new[] { camA, camB }) Look(c, Orbit(tgt, Mathf.Lerp(10f, -14f, u), 3.4f, 0.9f), tgt);
                Labels("Blur size, same 512 px map", "3-tap box blur", "source default, 1 texel each side", "21-tap Gaussian blur", "source option, 10 texels each side: wall stripes merge into shade");
            }
            else if (t < 22f)
            {
                phase = 3;
                Show(camA, true, full, 30f, ShadowVSMURP.Version.Box3); Show(camB, false, full, 0, 0);
                float u = (t - 18f) / 4f;
                var tgt = new Vector3(Mathf.Lerp(-4.4f, -3.6f, u), 1.0f, 2.9f);
                Look(camA, tgt + new Vector3(0.6f, 0.25f, -2.6f), tgt);
                Labels("Close-up: lattice shadow on the wall", "VSM, 3-tap box blur, 512 px per cascade", "", "", "");
            }
            else
            {
                phase = 4;
                Show(camA, true, full, 38f, ShadowVSMURP.Version.Box3); Show(camB, false, full, 0, 0);
                float u = Mathf.SmoothStep(0f, 1f, (t - 22f) / 4f);
                var tgt = new Vector3(-0.2f, 1.0f, 1.4f);
                Look(camA, Orbit(tgt, Mathf.Lerp(8f, 22f, u), Mathf.Lerp(6.4f, 7.8f, u), 1.9f), tgt);
                Labels("Variance shadow maps", "Unity 6.3 URP, ported from arigo's ShadowVSM", "", "", "");
            }
            if (phase == 0 || phase >= 3) l1.anchor = new Vector2(0.012f, 0.03f);
            else { l1.anchor = new Vector2(0.012f, 0.058f); l2.anchor = new Vector2(0.012f, 0.018f); }
            m1.anchor = new Vector2(0.512f, 0.058f); m2.anchor = new Vector2(0.512f, 0.018f);
            if (phase != logged) { logged = phase; Debug.Log($"[gt] t {t:F2} phase {phase} '{title.text}' | '{l1.text}' '{l2.text}' | '{m1.text}' '{m2.text}'"); }
            if (t >= nextLog)
            {
                nextLog = t + 2f;
                Debug.Log($"[gt] t {t:F2} vsm updates {vsm.UpdatesDone}, caster draws per update {vsm.DrawCallsPerUpdate}, {vsm.resolution} px x {vsm.numCascades} cascades, {vsm.BytesPerVersion / 1048576f:F1} MB per version, char x {character.position.x:F2}");
            }
        }
    }
}
