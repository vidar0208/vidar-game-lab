// Drives the box-projected reflections post. Two cameras see the same room from the same place; before each one
// renders, the director sets the global _GMode that BoxReflect reads (0 plain cubemap, 1 Unity's default box
// projection, 2 box projection + contact hardening). Both render into textures and a full-screen quad shows the split.
// Runs before FrameCapturer (execution order -100).
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using GameTech;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class BoxDirector : MonoBehaviour
    {
        public Camera camA, camB; public Material display; public ReflectionProbe probe; public Material[] reflMats;
        public Vector3 probePos, boxMin, boxMax; public float floorSmooth;
        public LabelSpec title, l1, l2, m1, m2;
        RenderTexture rtA, rtB; int phase = -1; RenderTexture cube;

        static float S(float x) { x = Mathf.Clamp01(x); return x * x * x * (x * (x * 6f - 15f) + 10f); }
        static RenderTexture RT(int w, int h) { var r = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { antiAliasing = 1 }; r.Create(); return r; }

        // camera keys: time, position, look-at point
        static readonly float[] kt = { 0f, 13f, 20f, 25f };
        static readonly Vector3[] kp = { new Vector3(-2.3f, 1.45f, -2.3f), new Vector3(0.2f, 0.95f, 0.1f), new Vector3(1.9f, 0.55f, 0.9f), new Vector3(-0.6f, 1.5f, -2.4f) };
        static readonly Vector3[] kl = { new Vector3(0.6f, 1.0f, 2.9f), new Vector3(1.3f, 0.6f, 3.0f), new Vector3(2.4f, 0.35f, 3.0f), new Vector3(0.6f, 1.1f, 3.0f) };

        void Start()
        {
            int w = int.Parse(FrameCapturer.Arg("-gtW", "1920")), h = int.Parse(FrameCapturer.Arg("-gtH", "1080"));
            rtA = RT(w, h); rtB = RT(w, h);
            camA.targetTexture = rtA; camA.enabled = false; camB.targetTexture = rtB; camB.enabled = false;
            display.SetTexture("_TexA", rtA); display.SetTexture("_TexB", rtB);
            Shader.SetGlobalVector("_GProbePos", probePos); Shader.SetGlobalVector("_GBoxMin", boxMin); Shader.SetGlobalVector("_GBoxMax", boxMax);
            // ambient set at runtime: the scene's ambient probe is not regenerated in a batch build
            var sh = new SphericalHarmonicsL2(); sh.AddAmbientLight(new Color(0.36f, 0.34f, 0.32f));
            sh.AddDirectionalLight(Vector3.up, new Color(0.12f, 0.13f, 0.16f), 1f); sh.AddDirectionalLight(Vector3.down, new Color(0.14f, 0.1f, 0.07f), 1f);
            RenderSettings.ambientMode = AmbientMode.Custom; RenderSettings.ambientProbe = sh;
            CaptureProbe();
            Debug.Log($"[gt] probe at {probePos}, box {boxMin} .. {boxMax}");
        }

        // Renders the probe's cubemap once, from the probe position, with the props' layer culled (like a baked probe that
        // only sees the room). Mips are generated from the result; they are box-filtered, not GGX-convolved.
        void CaptureProbe()
        {
            cube = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { dimension = TextureDimension.Cube, useMipMap = true, autoGenerateMips = false };
            cube.Create();
            var go = new GameObject("Probe capture cam"); var pc = go.AddComponent<Camera>();
            var data = go.AddComponent<UniversalAdditionalCameraData>(); data.renderPostProcessing = false; data.renderShadows = true;
            pc.enabled = false; pc.transform.position = probePos; pc.nearClipPlane = 0.05f; pc.farClipPlane = 80f;
            pc.cullingMask = probe.cullingMask; pc.clearFlags = CameraClearFlags.SolidColor; pc.backgroundColor = Color.black; pc.allowHDR = true;
            bool ok = pc.RenderToCubemap(cube, 63);
            cube.GenerateMips();
            foreach (var m in reflMats) m.SetTexture("_Cube", cube);
            Destroy(go);
            Debug.Log($"[gt] probe cubemap rendered {ok}, {cube.width} px, mips {cube.mipmapCount}");
        }

        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }

        void Update()
        {
            float t = FrameCapturer.T; int k = 0;
            while (k < kt.Length - 2 && t > kt[k + 1]) k++;
            float u = S((t - kt[k]) / (kt[k + 1] - kt[k]));
            var pos = Vector3.Lerp(kp[k], kp[k + 1], u); var look = Vector3.Lerp(kl[k], kl[k + 1], u);
            var rot = Quaternion.LookRotation(look - pos);
            foreach (var c in new[] { camA, camB }) c.transform.SetPositionAndRotation(pos, rot);
        }

        void LateUpdate()
        {
            if (rtA == null) return;
            float t = FrameCapturer.T;
            int p = t < 3f ? 0 : t < 13f ? 1 : t < 17f ? 2 : t < 22.5f ? 3 : 4;
            float modeA = p == 1 ? 0 : 1, modeB = 2, split = 0.5f, line = 1;
            Shader.SetGlobalFloat("_GDebugMip", p == 3 ? 1 : 0);
            Shader.SetGlobalFloat("_GMode", modeA); camA.Render();
            Shader.SetGlobalFloat("_GMode", modeB); camB.Render();
            string box = $"{boxMax.x - boxMin.x:0.#} x {boxMax.y - boxMin.y:0.#} x {boxMax.z - boxMin.z:0.#} m";
            switch (p)
            {
                case 0:
                    split = 0f; line = 0;
                    Labels("One reflection probe, box-projected", "Box projection + contact hardening", $"probe box {box}", "", "");
                    break;
                case 1:
                    split = Mathf.Lerp(0f, 0.5f, S((t - 3f) / 0.8f));
                    Labels("Same probe, same cubemap, same camera", "Plain cubemap", "room treated as infinitely far away", "Box projection + contact hardening", $"rays hit the probe box, {box}");
                    break;
                case 2:
                    Labels($"Floor smoothness {floorSmooth:0.##}", "Unity's default box projection", "one blur level for the whole floor", "+ contact hardening", "sharper where the reflected wall is close");
                    break;
                case 3:
                    Labels("Cubemap mip level used", "Default: same mip everywhere", "blue = sharp, red = blurry", "Contact hardening", "mip drops with the hit distance");
                    break;
                default:
                    split = 0f; line = 0;
                    Labels("Improved box-projected reflections", "Unity 6.3 URP, after frostbone25/Unity-Improved-Box-Projected-Reflections (MIT)", "one probe at the room centre, 256 px cubemap, captured once", "", "");
                    break;
            }
            display.SetFloat("_Split", split); display.SetFloat("_Line", line);
            if (p != phase) { phase = p; Debug.Log($"[gt] t {t:F2} phase {p} '{title.text}'"); }
        }

        void OnDestroy() { foreach (var r in new[] { rtA, rtB, cube }) if (r) r.Release(); }
    }
}
