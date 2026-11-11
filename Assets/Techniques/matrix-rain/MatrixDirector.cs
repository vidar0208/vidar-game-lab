// Timeline and manager for the digital rain post. Does the job of the source's TriplanarManager and
// ScreenSpaceMatrix scripts (IRCSS/MatrixVFX, MIT): creates the 512 x 512 white noise texture, runs the source's
// compute shader on it with a seed that changes 3 times per second (source: 6), and feeds the global values of the shaders.
// The key press of the source is replaced by a timeline; the transition speed is the source's 0.4 per second.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(200)]
    public class MatrixDirector : MonoBehaviour
    {
        public Camera camL, camR, camFull;
        public ComputeShader whiteNoiseGenerator;
        public GameObject screenQuad;
        public const float Scale = 0.45f, TransitionSpeed = 0.4f, RerollPerSecond = 3f;   // source: scale 0.2, re-roll 6 per second

        RenderTexture whiteNoise;
        Texture2D glyphs;
        float trL, trR, trFull, modeL;

        void OnEnable() { RenderPipelineManager.beginCameraRendering += Begin; }
        void OnDisable() { RenderPipelineManager.beginCameraRendering -= Begin; }

        void Start()
        {
            whiteNoise = new RenderTexture(512, 512, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = "white_noise", enableRandomWrite = true, useMipMap = false,
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat
            };
            whiteNoise.Create();
            whiteNoiseGenerator.SetTexture(0, "_white_noise", whiteNoise);
            glyphs = MakeGlyphAtlas();
            Shader.SetGlobalTexture("global_font_texture", glyphs);
            Shader.SetGlobalTexture("global_white_noise", whiteNoise);
            Shader.SetGlobalInteger("_session_rand_seed", 1234567);
            Shader.SetGlobalFloat("_MX_Sharpness", 10f);
            Debug.Log($"[gt] matrix: glyph atlas {glyphs.width}x{glyphs.height}, white noise {whiteNoise.width}x{whiteNoise.height}");
        }

        // 256 made-up glyphs: 4 to 6 strokes between the nodes of a 3 x 4 lattice. No font is used.
        static Texture2D MakeGlyphAtlas()
        {
            const int N = 16, C = 64, S = N * C;
            var px = new byte[S * S];
            var rng = new System.Random(75);
            var nodes = new Vector2[12];
            for (int j = 0; j < 4; j++) for (int i = 0; i < 3; i++)
                nodes[j * 3 + i] = new Vector2(Mathf.Lerp(0.24f, 0.66f, i / 2f), Mathf.Lerp(0.17f, 0.73f, j / 3f)) * C;
            var segs = new List<Vector2Int>();
            for (int j = 0; j < 4; j++) for (int i = 0; i < 3; i++)
            {
                int a = j * 3 + i;
                if (i < 2) segs.Add(new Vector2Int(a, a + 1));
                if (j < 3) segs.Add(new Vector2Int(a, a + 3));
                if (i < 2 && j < 3) { segs.Add(new Vector2Int(a, a + 4)); segs.Add(new Vector2Int(a + 1, a + 3)); }
            }
            var pick = new List<Vector2Int>();
            for (int g = 0; g < N * N; g++)
            {
                pick.Clear();
                int count = 4 + rng.Next(3);
                while (pick.Count < count) { var s = segs[rng.Next(segs.Count)]; if (!pick.Contains(s)) pick.Add(s); }
                int ox = (g % N) * C, oy = (g / N) * C;
                for (int y = 0; y < C; y++) for (int x = 0; x < C; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float d = 1e9f;
                    foreach (var s in pick)
                    {
                        Vector2 a = nodes[s.x], b = nodes[s.y], ab = b - a;
                        float h = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Vector2.Dot(ab, ab));
                        d = Mathf.Min(d, (p - a - ab * h).magnitude);
                    }
                    float v = Mathf.Clamp01((4.3f - d) / 1.6f);
                    px[(oy + y) * S + ox + x] = (byte)(v * 255f);
                }
            }
            var tex = new Texture2D(S, S, TextureFormat.R8, true, true) { name = "glyph_atlas", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            tex.SetPixelData(px, 0);
            tex.Apply(true);
            return tex;
        }

        void Begin(ScriptableRenderContext ctx, Camera cam)
        {
            Shader.SetGlobalFloat("_Global_Transition_value", cam == camL ? trL : cam == camR ? trR : trFull);
            Shader.SetGlobalFloat("_MX_Mode", cam == camL ? modeL : 0f);
            Shader.SetGlobalVector("_Global_Effect_center", cam.transform.position);
            Shader.SetGlobalFloat("_MX_ScreenW", cam.pixelWidth);
            Shader.SetGlobalFloat("_MX_ScreenH", cam.pixelHeight);
        }

        static float S(float a, float b, float t) { t = Mathf.Clamp01((t - a) / (b - a)); return t * t * (3 - 2 * t); }

        void Orbit(Camera c, float azDeg, float r, float h, Vector3 at)
        {
            float a = azDeg * Mathf.Deg2Rad;
            c.transform.position = new Vector3(Mathf.Sin(a) * r, h, Mathf.Cos(a) * r);
            c.transform.LookAt(at);
        }

        void Update()
        {
            float t = FrameCapturer.T;
            // source: seed = ceil(time * 6), every glyph re-rolled 6 times per second. Here 3 times per second.
            whiteNoiseGenerator.SetInt("_session_rand_seed", Mathf.CeilToInt(t * RerollPerSecond));
            whiteNoiseGenerator.Dispatch(0, 512 / 8, 512 / 8, 1);
            Shader.SetGlobalFloat("_MX_Time", t);

            bool split = t < 6.5f || (t >= 13f && t < 18f);
            camL.enabled = camR.enabled = split; camFull.enabled = !split;
            float scale = Scale, colored = 0f; modeL = 0f;
            bool quad = false;
            var at = new Vector3(0, 0.82f, 0);

            if (t < 6.5f)
            {   // A: plain material | rain over the surface
                trL = 0f; trR = 1f;
                float az = Mathf.Lerp(-18f, 18f, S(0f, 6.5f, t));
                Orbit(camL, az, 3.3f, 1.05f, at); Orbit(camR, az, 3.3f, 1.05f, at);
            }
            else if (t < 13f)
            {   // B: the source's two-step switch, 0.4 per second
                trFull = Mathf.Clamp((t - 7f) * TransitionSpeed, 0f, 1f) + Mathf.Clamp((t - 9.6f) * TransitionSpeed, 0f, 1f);
                Orbit(camFull, Mathf.Lerp(18f, 8f, S(6.5f, 13f, t)), Mathf.Lerp(3.7f, 3.5f, S(6.5f, 13f, t)), 1.0f, at);
            }
            else if (t < 15.5f)
            {   // C: one projection | three projections, rain over the surface
                trL = trR = 1f; modeL = 1f;
                float az = Mathf.Lerp(25f, 36f, S(13f, 15.5f, t));
                var look = new Vector3(0.3f, 0.6f, -0.6f);
                Orbit(camL, az, 3.4f, 1.9f, look); Orbit(camR, az, 3.4f, 1.9f, look);
            }
            else if (t < 18f)
            {   // D: triplanar rain only | screen-space rain
                trL = 2f; trR = 2f; quad = true;
                float az = Mathf.Lerp(30f, 14f, S(15.5f, 18f, t));
                Orbit(camL, az, 3.6f, 1.0f, at); Orbit(camR, az, 3.6f, 1.0f, at);
            }
            else
            {   // E: the coloured variant, rain only
                trFull = 2f; colored = 1f;
                Orbit(camFull, Mathf.Lerp(14f, 4f, S(18f, 20f, t)), 3.6f, 1.0f, at);
            }
            Shader.SetGlobalFloat("_MX_Scale", scale);
            Shader.SetGlobalFloat("global_colored", colored);
            if (screenQuad.activeSelf != quad) screenQuad.SetActive(quad);
        }

        void OnDestroy() { if (whiteNoise != null) whiteNoise.Release(); }
    }
}
