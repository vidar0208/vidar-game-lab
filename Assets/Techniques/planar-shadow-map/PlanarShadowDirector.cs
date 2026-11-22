// Drives the planar shadow post. Each frame, before the cameras render, a command buffer draws every caster mesh
// with the planar caster shader into two screen-aligned maps (high and low resolution) using the main camera's
// view-projection, the way dreamfairy/Unity_URP_Planear_Shadowmap's render feature does (DrawMesh per caster after
// the camera's culling). Camera A and camera B then render the same view; each sees its own copy of the ground
// (layer 8 / layer 9) whose material picks the shadow source, and a full-screen quad shows the two side by side.
// URP's own shadow settings are set for the comparison at start and restored on exit. Runs before FrameCapturer.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using GameTech;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class PlanarShadowDirector : MonoBehaviour
    {
        public Camera camA, camB; public Material display, caster, groundA, groundB;
        public Light sun; public Transform casterRoot, figure, ball;
        public Vector2Int hiSize = new Vector2Int(1024, 576), loSize = new Vector2Int(256, 144);
        public int urpResolution = 1024, urpCascades = 4; public float urpDistance = 60f;
        public LabelSpec title, l1, l2, m1, m2;
        RenderTexture rtA, rtB, mapHi, mapLo; CommandBuffer cmd; readonly List<Renderer> casters = new List<Renderer>();
        int oldRes, oldCascades; float oldDist; bool oldSoft; UniversalRenderPipelineAsset rp; int phase = -1;

        static float S(float x) { x = Mathf.Clamp01(x); return x * x * x * (x * (x * 6f - 15f) + 10f); }
        static RenderTexture RT(int w, int h, int d, RenderTextureFormat f)
        { var r = new RenderTexture(w, h, d, f, RenderTextureReadWrite.Linear) { antiAliasing = 1, filterMode = FilterMode.Point }; r.Create(); return r; }

        void Start()
        {
            int w = int.Parse(FrameCapturer.Arg("-gtW", "1920")), h = int.Parse(FrameCapturer.Arg("-gtH", "1080"));
            rtA = RT(w, h, 24, RenderTextureFormat.ARGBHalf); rtB = RT(w, h, 24, RenderTextureFormat.ARGBHalf);
            mapHi = RT(hiSize.x, hiSize.y, 0, RenderTextureFormat.RHalf); mapLo = RT(loSize.x, loSize.y, 0, RenderTextureFormat.RHalf);
            camA.targetTexture = rtA; camA.enabled = false; camB.targetTexture = rtB; camB.enabled = false;
            casters.AddRange(casterRoot.GetComponentsInChildren<Renderer>());
            display.SetTexture("_TexA", rtA); display.SetTexture("_TexB", rtB);
            rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (rp)
            {
                oldRes = rp.mainLightShadowmapResolution; oldCascades = rp.shadowCascadeCount; oldDist = rp.shadowDistance;
                rp.mainLightShadowmapResolution = urpResolution; rp.shadowCascadeCount = urpCascades; rp.shadowDistance = urpDistance;
            }
            cmd = new CommandBuffer { name = "PlanarShadow" };
            Debug.Log($"[gt] planar: {casters.Count} caster renderers, maps {hiSize} / {loSize}, URP shadows {urpResolution} x{urpCascades} {urpDistance} m, sun elevation {sun.transform.eulerAngles.x:F0}");
        }

        void Maps()
        {
            Vector3 L = -sun.transform.forward;
            Matrix4x4 view = camA.worldToCameraMatrix, proj = camA.projectionMatrix;
            cmd.Clear();
            cmd.SetGlobalMatrix("_PsmVP", GL.GetGPUProjectionMatrix(proj, true) * view);
            cmd.SetGlobalMatrix("_PsmUV", proj * view);
            cmd.SetGlobalVector("_PsmLightDir", L); cmd.SetGlobalFloat("_PsmPlaneY", 0f); cmd.SetGlobalFloat("_PsmBias", 0.02f);
            foreach (var (rt, name) in new[] { (mapHi, "_PsmMapHi"), (mapLo, "_PsmMapLo") })
            {
                cmd.SetRenderTarget(rt); cmd.ClearRenderTarget(false, true, new Color(1000f, 1000f, 1000f, 1000f));
                foreach (var r in casters)   // the source draws each caster MeshFilter with DrawMesh; DrawRenderer also covers the skinned figure
                    for (int sm = 0; sm < r.sharedMaterials.Length; sm++) cmd.DrawRenderer(r, caster, sm, 0);
                cmd.SetGlobalTexture(name, rt);
            }
            cmd.SetGlobalVector("_PsmSizeHi", new Vector4(hiSize.x, hiSize.y, 1f / hiSize.x, 1f / hiSize.y));
            cmd.SetGlobalVector("_PsmSizeLo", new Vector4(loSize.x, loSize.y, 1f / loSize.x, 1f / loSize.y));
            Graphics.ExecuteCommandBuffer(cmd);
        }

        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }

        void Update()
        {
            float t = FrameCapturer.T;
            // slow low dolly across the courtyard
            var pos = new Vector3(Mathf.Lerp(-1.6f, 1.4f, S(t / 25f)), 1.05f + 0.08f * Mathf.Sin(t * 0.3f), Mathf.Lerp(-4.6f, -4.1f, S(t / 25f)));
            foreach (var c in new[] { camA, camB }) { c.transform.position = pos; c.transform.LookAt(new Vector3(Mathf.Lerp(-0.4f, 0.4f, S(t / 25f)), 0.35f, 0.6f)); }
            figure.rotation = Quaternion.Euler(0, 200f + 40f * Mathf.Sin(t * 0.25f), 0);
            // a ball bouncing next to the figure: contact shadow shrinks and sharpens as it lands
            float ph = Mathf.Repeat(t * 0.9f, 1f); ball.position = new Vector3(1.15f, 0.16f + 1.1f * 4f * ph * (1f - ph), -0.2f);
        }

        void LateUpdate()
        {
            if (cmd == null) return;
            float t = FrameCapturer.T;
            Maps(); camA.Render(); camB.Render();
            int p = t < 3f ? 0 : t < 13f ? 1 : t < 18.5f ? 2 : t < 22.5f ? 3 : 4;
            float split = 0.5f, a = 1, b = 2, map = 0, line = 1;
            string hi = $"{hiSize.x}x{hiSize.y}", lo = $"{loSize.x}x{loSize.y}";
            switch (p)
            {
                case 0:
                    split = 0f; line = 0;
                    Labels("Camera-space planar shadow map", $"Planar shadow map, {hi}, 4-tap PCF", "sun from above, ground plane receiver", "", "");
                    break;
                case 1:
                    split = Mathf.Lerp(0f, 0.5f, S((t - 3f) / 0.8f)); a = 1; b = 2;
                    Labels("Same frame, same camera", $"URP cascaded shadows", $"{urpResolution}x{urpResolution} atlas, {urpCascades} cascades, {urpDistance:0} m, soft", "Planar shadow map", $"{hi} ({hiSize.x * hiSize.y / 1000}k texels), 4-tap PCF");
                    break;
                case 2:
                    a = 3; b = 2;
                    Labels("Resolution", $"Planar map {lo}", $"{loSize.x * loSize.y / 1000}k texels", $"Planar map {hi}", $"{hiSize.x * hiSize.y / 1000}k texels");
                    break;
                case 3:
                    map = 1; b = 2;
                    Labels("The map is the screen", $"Planar shadow map ({hi})", "casters slid along the light onto the ground", "Result", "every texel lands on ground you can see");
                    break;
                default:
                    split = 0f; line = 0;
                    Labels("Camera-space planar shadow map", "Unity 6.3 URP, after dreamfairy/Unity_URP_Planear_Shadowmap (MIT)", "light must come from above; one flat receiver plane", "", "");
                    break;
            }
            groundA.SetFloat("_Mode", a); groundB.SetFloat("_Mode", b);
            display.SetFloat("_Split", split); display.SetFloat("_LeftMap", map); display.SetFloat("_Line", line);
            if (p != phase) { phase = p; Debug.Log($"[gt] t {t:F2} phase {p} '{title.text}'"); }
        }

        void OnDestroy()
        {
            if (rp) { rp.mainLightShadowmapResolution = oldRes; rp.shadowCascadeCount = oldCascades; rp.shadowDistance = oldDist; }
            foreach (var r in new[] { rtA, rtB, mapHi, mapLo }) if (r) r.Release(); cmd?.Release();
        }
    }
}
