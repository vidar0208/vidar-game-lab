// Drives the dithered transparency post. Every see-through object exists three times: a dithered copy (layer 9),
// an alpha-blended copy (layer 8) and a shadow-only dithered copy (layer 0), so the halves of a split frame can show
// blend vs dither with identical shadows. Dither settings (matrix or texture, texture scale, shadow dither on/off) are
// globals set per camera just before it renders. Zoom boxes copy the camera's own finished pixels and draw them 5x
// larger with point filtering, so the pattern is not lost in video compression.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class DitherDirector : MonoBehaviour
    {
        public RenderPipelineAsset pipeline;
        public Camera camA, camB, camC;
        public Texture2D ditherTex;
        public Material[] urnMats;
        public Transform star, curtains, urn;
        public LabelSpec title, l1, l2, m1, m2, r1, r2;

        const int LayerBlend = 8, LayerDither = 9;
        RenderPipelineAsset previous;
        readonly Dictionary<Camera, Vector3> settings = new Dictionary<Camera, Vector3>();   // mode, scale, shadow dither
        readonly Dictionary<Camera, Vector3> zoomAt = new Dictionary<Camera, Vector3>();      // world point to magnify
        RenderTexture copy;
        Material colored, textured;
        int logged = -1;
        float baseW = 1920f;

        void Awake()
        {
            previous = QualitySettings.renderPipeline;
            QualitySettings.renderPipeline = pipeline;
        }

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += Begin;
            RenderPipelineManager.endCameraRendering += End;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= Begin;
            RenderPipelineManager.endCameraRendering -= End;
        }

        void OnDestroy() { QualitySettings.renderPipeline = previous; }

        void Start()
        {
            Shader.SetGlobalTexture("_GT_DitherTex", ditherTex);
            baseW = float.Parse(FrameCapturer.Arg("-gtW", "1920"));
            Debug.Log($"[gt] pipeline {GraphicsSettings.currentRenderPipeline?.name}, shadow res {(GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset)?.mainLightShadowmapResolution}, dither tex {ditherTex.width}x{ditherTex.height} {ditherTex.filterMode}");
        }

        void Begin(ScriptableRenderContext ctx, Camera cam)
        {
            if (!settings.TryGetValue(cam, out var s)) s = new Vector3(0, 470, 1);
            Shader.SetGlobalFloat("_GT_DitherMode", s.x);
            Shader.SetGlobalFloat("_GT_DitherScale", s.y);
            Shader.SetGlobalFloat("_GT_ShadowDither", s.z);
        }

        // After a camera has rendered into the capture target: copy its pixels and draw a 5x point-filtered zoom box.
        void End(ScriptableRenderContext ctx, Camera cam)
        {
            var rt = cam.targetTexture;
            if (rt == null || !zoomAt.TryGetValue(cam, out var world)) return;
            float k = rt.width / 1920f;                   // virtual 1920x1080 layout -> actual pixels
            int region = Mathf.Max(8, Mathf.RoundToInt(60 * k));
            float disp = region * 5f;
            var sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0) return;
            float sx = Mathf.Round(sp.x - region * 0.5f), sy = Mathf.Round(sp.y - region * 0.5f);
            var vp = cam.pixelRect;
            float dx = vp.xMax - disp - 24 * k, dy = rt.height - 128 * k - disp;
            if (copy == null || copy.width != rt.width || copy.height != rt.height)
            {
                if (copy != null) copy.Release();
                copy = new RenderTexture(rt.width, rt.height, 0, rt.format, RenderTextureReadWrite.sRGB) { filterMode = FilterMode.Point };
                copy.Create();
            }
            Graphics.CopyTexture(rt, copy);
            if (colored == null)
            {
                colored = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
                colored.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); colored.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                colored.SetInt("_Cull", (int)CullMode.Off); colored.SetInt("_ZWrite", 0); colored.SetInt("_ZTest", (int)CompareFunction.Always);
                textured = new Material(Shader.Find("Hidden/GT/Zoom")) { hideFlags = HideFlags.HideAndDontSave };
            }
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Viewport(new Rect(0, 0, rt.width, rt.height));
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, rt.width, 0, rt.height);
            float b = Mathf.Max(2f, 3f * k);
            colored.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Color(new Color(1f, 1f, 1f, 0.95f));
            Frame(sx, sy, region, region, b * 0.7f);
            Rect(dx - b, dy - b, disp + 2 * b, disp + 2 * b);
            GL.End();
            textured.mainTexture = copy;
            textured.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Color(Color.white);
            float u0 = sx / rt.width, v0 = sy / rt.height, u1 = (sx + region) / rt.width, v1 = (sy + region) / rt.height;
            GL.TexCoord2(u0, v0); GL.Vertex3(dx, dy, 0);
            GL.TexCoord2(u1, v0); GL.Vertex3(dx + disp, dy, 0);
            GL.TexCoord2(u1, v1); GL.Vertex3(dx + disp, dy + disp, 0);
            GL.TexCoord2(u0, v1); GL.Vertex3(dx, dy + disp, 0);
            GL.End();
            GL.PopMatrix();
            RenderTexture.active = prev;
        }

        static void Rect(float x, float y, float w, float h)
        {
            GL.Vertex3(x, y, 0); GL.Vertex3(x + w, y, 0); GL.Vertex3(x + w, y + h, 0); GL.Vertex3(x, y + h, 0);
        }

        static void Frame(float x, float y, float w, float h, float t)
        {
            Rect(x - t, y - t, w + 2 * t, t); Rect(x - t, y + h, w + 2 * t, t);
            Rect(x - t, y, t, h); Rect(x + w, y, t, h);
        }

        static void Pose(Camera c, float yaw, float dist, float height, Vector3 target)
        {
            var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.back;
            c.transform.position = target + dir * dist + Vector3.up * (height - target.y);
            c.transform.LookAt(target);
        }

        void Show(Camera c, bool on, Rect r, int layer, float fov)
        {
            c.enabled = on;
            if (!on) return;
            c.rect = r; c.fieldOfView = fov;
            c.cullingMask = (1 << 0) | (1 << layer);
        }

        void Set(Camera c, float mode, float scale, float shadow) { settings[c] = new Vector3(mode, scale, shadow); }

        void Fade(float a) { foreach (var m in urnMats) m.SetFloat("_Fade", a); }

        void Labels(string t, string a1, string a2, string b1, string b2, string c1 = "", string c2 = "")
        {
            title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; r1.text = c1; r2.text = c2;
        }

        void LateUpdate()
        {
            float t = FrameCapturer.T;
            var full = new Rect(0, 0, 1, 1);
            var half0 = new Rect(0, 0, 0.4985f, 1); var half1 = new Rect(0.5015f, 0, 0.4985f, 1);
            zoomAt.Clear();
            Fade(1f);
            int phase;
            if (t < 3.5f)
            {
                phase = 0;
                Show(camA, true, full, LayerDither, 40f); Show(camB, false, full, 0, 0); Show(camC, false, full, 0, 0);
                Set(camA, 0, 470, 1);
                float u = t / 3.5f;
                Pose(camA, Mathf.Lerp(-16f, -8f, u), Mathf.Lerp(6.6f, 6.0f, u), 1.55f, new Vector3(-0.6f, 1.05f, 1.4f));
                Labels("Dithered transparency: keep or discard each pixel by alpha", "", "", "", "");
                l1.text = "Curtains, glass panes and leaves: all drawn in the opaque pass";
                m1.anchor = new Vector2(0.512f, 0.058f);
            }
            else if (t < 10.5f)
            {
                phase = 1;
                Show(camA, true, half0, LayerBlend, 50f); Show(camB, true, half1, LayerDither, 50f); Show(camC, false, full, 0, 0);
                Set(camA, 0, 470, 1); Set(camB, 0, 470, 1);
                float u = Mathf.SmoothStep(0f, 1f, (t - 3.5f) / 7f);
                var target = new Vector3(0.75f, 0.95f, 1.35f);
                foreach (var c in new[] { camA, camB }) Pose(c, Mathf.Lerp(30f, -15f, u), 3.5f, 1.45f, target);
                zoomAt[camA] = zoomAt[camB] = star.position + new Vector3(0f, 0.12f, 0f);
                Labels("Overlapping see-through surfaces (box: 5x zoom of the same pixels)",
                    "Alpha blend", "no depth write: draw order decides what is in front",
                    "Dithered, opaque pass", "depth write and test per pixel: right order from any angle");
                m1.anchor = new Vector2(0.512f, 0.058f); m2.anchor = new Vector2(0.512f, 0.018f);
            }
            else if (t < 16f)
            {
                phase = 2;
                Show(camA, true, half0, LayerDither, 52f); Show(camB, true, half1, LayerDither, 52f); Show(camC, false, full, 0, 0);
                Set(camA, 0, 470, 0); Set(camB, 0, 470, 1);
                float u = Mathf.SmoothStep(0f, 1f, (t - 10.5f) / 5.5f);
                var target = new Vector3(-1.75f, 0.25f, 3.3f);
                foreach (var c in new[] { camA, camB }) Pose(c, Mathf.Lerp(-22f, -6f, u), 2.9f, Mathf.Lerp(6.2f, 5.8f, u), target);
                Labels("Shadows of see-through surfaces",
                    "Ordinary shadow pass", "alpha ignored: everything casts a full shadow",
                    "Shadow pass dithered too", "shadow density follows alpha");
                m1.anchor = new Vector2(0.512f, 0.058f); m2.anchor = new Vector2(0.512f, 0.018f);
            }
            else if (t < 21f)
            {
                phase = 3;
                float w = 0.3313f;
                Show(camA, true, new Rect(0, 0, w, 1), LayerDither, 52f);
                Show(camB, true, new Rect(w + 0.003f, 0, w, 1), LayerDither, 52f);
                Show(camC, true, new Rect(2 * w + 0.006f, 0, w, 1), LayerDither, 52f);
                Set(camA, 0, 470, 1); Set(camB, 1, 470, 1); Set(camC, 1, 1880, 1);
                float u = Mathf.SmoothStep(0f, 1f, (t - 16f) / 5f);
                var target = new Vector3(-1.95f, 1.45f, 2.0f);
                foreach (var c in new[] { camA, camB, camC }) Pose(c, Mathf.Lerp(-10f, 8f, u), 2.7f, 1.55f, target);
                var zp = curtains.position + new Vector3(0.1f, -0.3f, 0f);
                zoomAt[camA] = zoomAt[camB] = zoomAt[camC] = zp;
                Labels("Threshold pattern (box: 5x zoom of the same pixels)",
                    "4x4 matrix", "17 alpha levels, regular grid",
                    "Blue-noise texture", "1 texel per pixel",
                    "Blue-noise texture", "4 pixels per texel");
                m1.anchor = new Vector2(0.346f, 0.058f); m2.anchor = new Vector2(0.346f, 0.018f);
            }
            else
            {
                phase = 4;
                Show(camA, true, full, LayerDither, 38f); Show(camB, false, full, 0, 0); Show(camC, false, full, 0, 0);
                Set(camA, 0, 470, 1);
                float u = Mathf.SmoothStep(0f, 1f, (t - 21f) / 5f);
                Pose(camA, Mathf.Lerp(24f, 8f, u), Mathf.Lerp(3.6f, 3.2f, u), Mathf.Lerp(1.35f, 1.2f, u), urn.position + new Vector3(-0.3f, 0.55f, 0.6f));
                float a = 1f - Mathf.SmoothStep(0f, 1f, (t - 21.8f) / 3.4f);
                Fade(a);
                Labels("Fade out without sorting", $"urn alpha {a:F2}: fewer pixels survive, its shadow thins too", "", "", "");
                l1.anchor = new Vector2(0.012f, 0.03f);
            }
            if (phase != 0 && phase != 4) { l1.anchor = new Vector2(0.012f, 0.058f); l2.anchor = new Vector2(0.012f, 0.018f); }
            if (phase == 0) l1.anchor = new Vector2(0.012f, 0.03f);
            float rx = phase == 3 ? 0.68f : 0.512f;
            r1.anchor = new Vector2(rx, 0.058f); r2.anchor = new Vector2(rx, 0.018f);
            if (phase != logged) { logged = phase; Debug.Log($"[gt] t {t:F2} phase {phase} '{title.text}' | '{l1.text}' '{l2.text}' | '{m1.text}' '{m2.text}' | '{r1.text}' '{r2.text}'"); }
        }
    }
}
