// Drives the virtual texture post: camera moves, which ground copy each camera sees (virtual texture with a
// 64-tile cache, the same with a 16-tile cache, or one regular 2048 texture), the debug panels and the labels.
// Every number on screen is computed here from the systems' settings or read from their counters.
using System.Globalization;
using GameTech;
using UnityEngine;
using UnityEngine.Rendering;
using VirtualTexture;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class VTDirector : MonoBehaviour
    {
        public Camera camA, camB, overlay;
        public GameObject sys64, sys16;
        public Material mat64, matReg, pageMat;
        public Renderer[] panels;              // feedback, page table, tile cache
        public LabelSpec title, l1, l2, m1, m2, p1, p2, p3;
        public const int L64 = 25, L16 = 26, LReg = 27;
        public const int RegularSize = 2048;

        PageTable pt64, pt16; TiledTexture tt64, tt16; FeedbackRendererURP fb64, fb16; FeedbackReader rd64;
        int logged = -1; float nextLog = 0f; int loads64At, loads16At;

        static string N(double v) => v.ToString("N0", CultureInfo.InvariantCulture);
        static string MB(double bytes) => (bytes / 1048576.0).ToString(bytes < 10 * 1048576.0 ? "0.0" : "0.0", CultureInfo.InvariantCulture) + " MB";

        void Start()
        {
            pt64 = sys64.GetComponent<PageTable>(); pt16 = sys16.GetComponent<PageTable>();
            tt64 = sys64.GetComponent<TiledTexture>(); tt16 = sys16.GetComponent<TiledTexture>();
            fb64 = sys64.GetComponent<FeedbackRendererURP>(); fb16 = sys16.GetComponent<FeedbackRendererURP>();
            rd64 = sys64.GetComponent<FeedbackReader>();
            var fc = FindFirstObjectByType<FrameCapturer>();
            var size = fc != null ? new Vector2Int(fc.width, fc.height) : new Vector2Int(Screen.width, Screen.height);
            fb64.outputSize = fb16.outputSize = size;

            // the regular texture: the same procedural map drawn once at 2048 x 2048, with mips
            var rt = new RenderTexture(RegularSize, RegularSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            { useMipMap = true, autoGenerateMips = false, anisoLevel = 8, filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp, name = "Regular 2048" };
            rt.Create();
            var mpb = new MaterialPropertyBlock();
            mpb.SetVector("_PageRect", new Vector4(0, 0, 1, 1));
            mpb.SetVector("_PageInfo", new Vector4(1f / RegularSize, 0, 0, 0));
            mpb.SetVector("_BlitScaleBias", new Vector4(1, 1, 0, 0));
            var cmd = new CommandBuffer { name = "Regular texture" };
            cmd.SetRenderTarget(rt);
            cmd.DrawProcedural(Matrix4x4.identity, pageMat, 0, MeshTopology.Triangles, 3, 1, mpb);
            cmd.GenerateMips(rt);
            Graphics.ExecuteCommandBuffer(cmd);
            matReg.SetTexture("_RegularTex", rt);
            Debug.Log($"[gt] async readback supported {SystemInfo.supportsAsyncGPUReadback}, feedback output size {size}");
        }

        static void Look(Camera c, Vector3 pos, Vector3 target) { c.transform.position = pos; c.transform.LookAt(target); }

        static void Show(Camera c, bool on, Rect r, float fov, int layer)
        {
            c.enabled = on;
            if (!on) return;
            c.rect = r; c.fieldOfView = fov; c.cullingMask = 1 << layer; c.aspect = r.width * 1920f / (r.height * 1080f);
        }

        void Labels(string t, string a1, string a2, string b1, string b2)
        {
            title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2;
            p1.text = p2.text = p3.text = "";
        }

        void LateUpdate()
        {
            float t = FrameCapturer.T;
            var full = new Rect(0, 0, 1, 1);
            var h0 = new Rect(0, 0, 0.4985f, 1); var h1 = new Rect(0.5015f, 0, 0.4985f, 1);
            int table = pt64.TableSize, tile = tt64.TileSize, tilePad = tt64.TileSizeWithPadding, virt = table * tile;
            double reg = RegularSize * (double)RegularSize * 4 * 4 / 3, c64 = tt64.Texture != null ? tt64.Texture.width * (double)tt64.Texture.height * 4 : 0;
            double c16 = tt16.Texture != null ? tt16.Texture.width * (double)tt16.Texture.height * 4 : 0;
            float pxPerM64 = virt / 64f, pxPerMReg = RegularSize / 64f;
            int phase; float mx = 0.512f;
            mat64.SetFloat("_Mode", 0);
            overlay.enabled = false;
            foreach (var p in panels) p.enabled = false;

            if (t < 4f)
            {
                phase = 0;
                Show(camA, true, full, 50f, L64); Show(camB, false, full, 0, 0);
                fb64.targetCamera = camA; fb16.targetCamera = camA;
                float u = Mathf.SmoothStep(0f, 1f, t / 4f);
                var pos = new Vector3(Mathf.Lerp(-6f, -2f, u), Mathf.Lerp(16f, 3.2f, u), Mathf.Lerp(-28f, -15f, u));
                Look(camA, pos, pos + new Vector3(0.5f, Mathf.Lerp(-9f, -2.2f, u), 8f));
                Labels("Virtual texturing",
                    $"One {N(virt)} x {N(virt)} px texture on a 64 m ground, in {table} x {table} pages: only visible pages stay in a {tt64.TileCount}-tile cache", "", "", "");
            }
            else if (t < 10f)
            {
                phase = 1;
                Show(camA, true, h0, 56f, LReg); Show(camB, true, h1, 56f, L64);
                fb64.targetCamera = camB; fb16.targetCamera = camB;
                float u = Mathf.SmoothStep(0f, 1f, (t - 4f) / 6f);
                var pos = new Vector3(Mathf.Lerp(-0.5f, 2.0f, u), 1.3f, Mathf.Lerp(-19f, -16.5f, u));
                foreach (var c in new[] { camA, camB }) Look(c, pos, pos + new Vector3(1.4f, -1.1f, 3f));
                Labels("Regular texture vs virtual texture",
                    $"Regular {N(RegularSize)} x {N(RegularSize)}", $"{MB(reg)} with mips, {pxPerMReg:0} px per metre",
                    $"Virtual {N(virt)} x {N(virt)}", $"{tt64.TileCount}-tile cache {MB(c64)}, {pxPerM64:0} px per metre");
            }
            else if (t < 17f)
            {
                phase = 2;
                Show(camA, true, new Rect(0, 0, 0.6185f, 1), 52f, L64); Show(camB, false, full, 0, 0);
                overlay.enabled = true; overlay.rect = new Rect(0.6215f, 0, 0.3785f, 1);
                fb64.targetCamera = camA; fb16.targetCamera = camA;
                mat64.SetFloat("_Mode", 2);
                float u = Mathf.SmoothStep(0f, 1f, (t - 10f) / 7f);
                var pos = new Vector3(Mathf.Lerp(1.5f, 7f, u), Mathf.Lerp(5f, 7f, u), Mathf.Lerp(-14f, -6f, u));
                Look(camA, pos, pos + new Vector3(1.5f, -4.2f, 8f));
                Labels("What the camera asks for, what is kept",
                    "Ground tinted by the mip level in the page table",
                    $"{pt64.LastRequestedPages} pages in the last feedback, {pt64.ResidentPages} in the cache", "", "");
                p1.text = "Feedback: page and mip per pixel";
                p2.text = $"Page table {table} x {table}";
                p3.text = $"Tile cache {tt64.RegionSize.x} x {tt64.RegionSize.y} x {tilePad} px";
                for (int i = 0; i < panels.Length; i++)
                {
                    panels[i].enabled = true;
                    Texture tex = i == 0 ? fb64.TargetTexture : i == 1 ? (Texture)pt64.LookupTexture : tt64.Texture;
                    if (tex != null) panels[i].sharedMaterial.SetTexture("_MainTex", tex);
                }
                if (fb64.TargetTexture != null)
                {
                    float aspect = fb64.TargetTexture.width / (float)fb64.TargetTexture.height;
                    panels[0].transform.localScale = new Vector3(436f * aspect, 436f, 1f);
                }
            }
            else if (t < 22f)
            {
                phase = 3;
                if (logged != 3) { loads64At = pt64.TotalLoads; loads16At = pt16.TotalLoads; }
                Show(camA, true, h0, 56f, L16); Show(camB, true, h1, 56f, L64);
                fb16.targetCamera = camA; fb64.targetCamera = camB;
                float u = (t - 17f) / 5f;
                var pos = new Vector3(Mathf.Lerp(3f, 6f, u), 2.2f, Mathf.Lerp(-8f, -4f, u));
                float yaw = Mathf.Lerp(-70f, 70f, Mathf.SmoothStep(0f, 1f, u));
                var fwd = Quaternion.Euler(0, yaw, 0) * new Vector3(0, -0.45f, 1f);
                foreach (var c in new[] { camA, camB }) Look(c, pos, pos + fwd);
                Labels("Cache size during a fast pan",
                    $"{tt16.TileCount} tiles ({tt16.Texture.width} x {tt16.Texture.height}, {MB(c16)})", $"{pt16.LastRequestedPages} pages wanted, {pt16.TotalLoads - loads16At} page draws in this shot",
                    $"{tt64.TileCount} tiles ({tt64.Texture.width} x {tt64.Texture.height}, {MB(c64)})", $"{pt64.LastRequestedPages} pages wanted, {pt64.TotalLoads - loads64At} page draws in this shot");
            }
            else
            {
                phase = 4;
                Show(camA, true, full, 50f, L64); Show(camB, false, full, 0, 0);
                fb64.targetCamera = camA; fb16.targetCamera = camA;
                float u = Mathf.SmoothStep(0f, 1f, (t - 22f) / 4f);
                var pos = new Vector3(Mathf.Lerp(2f, -2f, u), Mathf.Lerp(2.5f, 11f, u), Mathf.Lerp(-12f, -24f, u));
                Look(camA, pos, new Vector3(0f, 0f, Mathf.Lerp(-4f, 4f, u)));
                Labels("Virtual texturing", "Unity 6.3 URP, ported from jintiao's VirtualTexture: feedback pass, page table, LRU tile cache", "", "", "");
            }

            if (phase == 0 || phase == 4) l1.anchor = new Vector2(0.012f, 0.03f);
            else { l1.anchor = new Vector2(0.012f, 0.058f); l2.anchor = new Vector2(0.012f, 0.018f); }
            m1.anchor = new Vector2(mx, 0.058f); m2.anchor = new Vector2(mx, 0.018f);

            if (phase != logged)
            {
                logged = phase;
                Debug.Log($"[gt] t {t:F2} phase {phase} '{title.text}' | '{l1.text}' '{l2.text}' | '{m1.text}' '{m2.text}' | '{p1.text}' '{p2.text}' '{p3.text}'");
            }
            if (t >= nextLog)
            {
                nextLog = t + 1f;
                Debug.Log($"[gt] t {t:F2} vt64 wanted {pt64.LastRequestedPages} resident {pt64.ResidentPages} loads {pt64.TotalLoads} pending {sys64.GetComponent<ProceduralLoader>().Pending} readbacks {rd64.ReadbacksDone} | vt16 wanted {pt16.LastRequestedPages} resident {pt16.ResidentPages} loads {pt16.TotalLoads} pending {sys16.GetComponent<ProceduralLoader>().Pending} | regular {MB(reg)} cache64 {MB(c64)} cache16 {MB(c16)} fb {fb64.TargetTexture?.width}x{fb64.TargetTexture?.height}");
            }
        }
    }
}
