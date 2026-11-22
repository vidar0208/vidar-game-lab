// Drives the OIT post. URP port of candycat1992/OIT_Lab WeightedBlendedManager (MIT):
//   the source renders opaque objects into a texture, clears an accumulation target to 0 and draws the transparent
//   layer with WB_Accumulate (Blend One One), clears a revealage target to 1 and draws it with WB_Revealage
//   (Blend Zero OneMinusSrcAlpha), then blends both over the opaque image with WB_Blend.
// Here: camera A renders everything with URP's normal sorted transparent queue (ODT). Camera B renders only the
// opaque scene. A command buffer draws an eye depth texture of the opaque renderers, then the glass renderers into
// accumulation (RGBA16F, weighted and unweighted) and revealage (R16F) targets with the same formats and blend states
// as the source. A full-screen quad shows two of the modes side by side. Runs before FrameCapturer (order -100).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using GameTech;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class OITDirector : MonoBehaviour
    {
        public Camera camOdt, camOpaque; public Material display, eyeDepth;
        public Light key, fill; public Color ambient = new Color(0.12f, 0.14f, 0.18f);
        public LabelSpec title, l1, l2, m1, m2;
        public Transform orbitTarget;
        RenderTexture odt, opaque, depth, accW, accU, reveal; CommandBuffer cmd;
        readonly List<Renderer> glass = new List<Renderer>(), solids = new List<Renderer>();
        int phase = -1;

        static float S(float x) { x = Mathf.Clamp01(x); return x * x * x * (x * (x * 6f - 15f) + 10f); }
        static RenderTexture RT(int w, int h, int d, RenderTextureFormat f)
        {
            var r = new RenderTexture(w, h, d, f, RenderTextureReadWrite.Linear) { antiAliasing = 1, filterMode = FilterMode.Point }; r.Create(); return r;
        }

        void Start()
        {
            int w = int.Parse(FrameCapturer.Arg("-gtW", "1920")), h = int.Parse(FrameCapturer.Arg("-gtH", "1080"));
            odt = RT(w, h, 24, RenderTextureFormat.ARGBHalf); opaque = RT(w, h, 24, RenderTextureFormat.ARGBHalf);
            depth = RT(w, h, 24, RenderTextureFormat.RFloat);
            accW = RT(w, h, 0, RenderTextureFormat.ARGBHalf); accU = RT(w, h, 0, RenderTextureFormat.ARGBHalf);
            reveal = RT(w, h, 0, RenderTextureFormat.RHalf);
            camOdt.targetTexture = odt; camOdt.enabled = false; camOpaque.targetTexture = opaque; camOpaque.enabled = false;
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (r.gameObject.layer == 8) glass.Add(r);
                else if (r.gameObject.layer == 0 && r.sharedMaterial.renderQueue < 2500 && r.gameObject.name != "Backdrop") solids.Add(r);
            }
            display.SetTexture("_OdtTex", odt); display.SetTexture("_OpaqueTex", opaque);
            display.SetTexture("_AccumW", accW); display.SetTexture("_AccumU", accU); display.SetTexture("_Reveal", reveal);
            cmd = new CommandBuffer { name = "OIT" };
            Debug.Log($"[gt] oit: {glass.Count} glass renderers, {solids.Count} opaque renderers in depth pass");
        }

        void SetLights()
        {
            Shader.SetGlobalVector("_GKeyDir", -key.transform.forward); Shader.SetGlobalVector("_GKeyCol", key.color * key.intensity);
            Shader.SetGlobalVector("_GFillDir", -fill.transform.forward); Shader.SetGlobalVector("_GFillCol", fill.color * fill.intensity);
            Shader.SetGlobalVector("_GAmb", ambient);
            Shader.SetGlobalVector("_GCamPos", camOdt.transform.position);
        }

        void Oit()
        {
            Matrix4x4 v = camOpaque.worldToCameraMatrix, vp = GL.GetGPUProjectionMatrix(camOpaque.projectionMatrix, true) * v;
            cmd.Clear();
            cmd.SetGlobalMatrix("_OitV", v); cmd.SetGlobalMatrix("_OitVP", vp);
            cmd.SetRenderTarget(depth); cmd.ClearRenderTarget(true, true, new Color(1e4f, 1e4f, 1e4f, 1e4f));
            foreach (var r in solids) cmd.DrawRenderer(r, eyeDepth, 0, 0);
            cmd.SetGlobalTexture("_OitSceneDepth", depth);
            // accumulate: clear to 0, Blend One One (weighted = source "BlendedAndWeighted", Weight1)
            cmd.SetGlobalFloat("_OitWeighted", 1f);
            cmd.SetRenderTarget(accW); cmd.ClearRenderTarget(false, true, Color.clear);
            foreach (var r in glass) cmd.DrawRenderer(r, r.sharedMaterial, 0, 1);
            // same pass, unweighted (source "Blended")
            cmd.SetGlobalFloat("_OitWeighted", 0f);
            cmd.SetRenderTarget(accU); cmd.ClearRenderTarget(false, true, Color.clear);
            foreach (var r in glass) cmd.DrawRenderer(r, r.sharedMaterial, 0, 1);
            // revealage: clear to 1, Blend Zero OneMinusSrcAlpha
            cmd.SetRenderTarget(reveal); cmd.ClearRenderTarget(false, true, Color.white);
            foreach (var r in glass) cmd.DrawRenderer(r, r.sharedMaterial, 0, 2);
            Graphics.ExecuteCommandBuffer(cmd);
        }

        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }

        void Update()
        {
            float t = FrameCapturer.T;
            float ang = Mathf.Lerp(-38f, 30f, S(t / 25f)), rad = Mathf.Lerp(4.4f, 4.05f, S(t / 25f));
            Vector3 c = orbitTarget.position;
            var pos = c + Quaternion.Euler(0, ang, 0) * new Vector3(0, 0.55f + 0.12f * Mathf.Sin(t * 0.3f), -rad);
            foreach (var cam in new[] { camOdt, camOpaque }) { cam.transform.position = pos; cam.transform.LookAt(c + new Vector3(0, -0.05f, 0)); }
        }

        void LateUpdate()
        {
            if (cmd == null) return;
            float t = FrameCapturer.T;
            SetLights();
            camOdt.Render(); camOpaque.Render(); Oit();
            int p = t < 3f ? 0 : t < 14f ? 1 : t < 18.5f ? 2 : t < 22.5f ? 3 : 4;
            float split = 0.5f, left = 0, right = 1, line = 1;
            switch (p)
            {
                case 0:
                    split = 0f; line = 0;
                    Labels("Order-independent transparency", "Weighted blended OIT", "4 crossing panes + 3 two-sided bottles, no sorting", "", "");
                    break;
                case 1:
                    split = Mathf.Lerp(0f, 0.5f, S((t - 3f) / 0.8f));
                    Labels("Same frame, same camera", "Default transparent queue", "sorted per object, drawn back to front", "Weighted blended OIT", "accumulate + revealage, one composite");
                    break;
                case 2:
                    left = 2; right = 1;
                    Labels("The weight", "Blended, weight = 1", "every layer counts the same", "Weighted, w(z, alpha)", "nearer and more opaque layers count more");
                    break;
                case 3:
                    left = 3; right = 4;
                    Labels("The two buffers", "Accumulation (RGBA16F)", "sum of colour x alpha x w, divided by sum of alpha x w", "Revealage (R16F)", "product of (1 - alpha): how much background shows");
                    break;
                default:
                    split = 0f; line = 0;
                    Labels("Weighted blended OIT", "Unity 6.3 URP, port of candycat1992/OIT_Lab (MIT)", "an approximation: colour order inside a pixel is blended, not exact", "", "");
                    break;
            }
            display.SetFloat("_Split", split); display.SetFloat("_LeftMode", left); display.SetFloat("_RightMode", right); display.SetFloat("_Line", line);
            if (p != phase) { phase = p; Debug.Log($"[gt] t {t:F2} phase {p} '{title.text}'"); }
        }

        void OnDestroy() { foreach (var r in new[] { odt, opaque, depth, accW, accU, reveal }) if (r) r.Release(); cmd?.Release(); }
    }
}
