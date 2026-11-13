// Runs the source's pixel-art effects (PixelArtFilter, Ditherer, PaletteSwapper) on the scene camera's image every frame
// and feeds the band compose quad. OnRenderImage does not exist in URP, so the same Graphics.Blit chains run here in LateUpdate.
using UnityEngine;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class PixelDirector : MonoBehaviour
    {
        public Camera src;
        public Material pixelArt, dither, palette, compose;
        public Light sun;
        public Transform orb, torus;
        public LabelSpec title, zTop;
        public LabelSpec[] l1, l2;          // two label lines per band, four bands
        public Vector3 focus = new Vector3(0, 1.3f, 0);
        public const int Downs = 2, PalSize = 6;

        RenderTexture srcRT; PixelChain chain; RenderTexture[] low;
        Texture2D[] pals; readonly string[] palNames = { "dusk", "moss", "ice" };
        int W, H, logged = -1;
        float offSource = -1, offSnap = -1;

        void Start()
        {
            W = int.Parse(FrameCapturer.Arg("-gtW", "1920")); H = int.Parse(FrameCapturer.Arg("-gtH", "1080"));
            srcRT = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            srcRT.Create();
            src.targetTexture = srcRT; src.enabled = false;
            chain = new PixelChain(W, H, Downs, pixelArt, dither, palette);
            low = new RenderTexture[6]; for (int i = 0; i < low.Length; i++) low[i] = chain.NewLow();
            pals = new[]
            {
                PixelPalettes.Ramp("dusk", PalSize, 0.74f, 1.10f, 0.70f, 0.28f, 0.16f, 0.98f),
                PixelPalettes.Ramp("moss", PalSize, 0.52f, 0.17f, 0.72f, 0.38f, 0.12f, 0.95f),
                PixelPalettes.Ramp("ice", PalSize, 0.70f, 0.52f, 0.75f, 0.10f, 0.16f, 1.00f),
            };
            compose.SetTexture("_P0", pals[0]); compose.SetTexture("_P1", pals[1]); compose.SetTexture("_P2", pals[2]);
            compose.SetFloat("_PW", PalSize);
            Debug.Log($"[gt] chain {W}x{H} -> {chain.lowW}x{chain.lowH}");
            for (int p = 0; p < pals.Length; p++)
            {
                var s = "";
                foreach (var c in pals[p].GetPixels()) s += "#" + ColorUtility.ToHtmlStringRGB(c) + " ";
                Debug.Log($"[gt] palette {palNames[p]}: {s}");
            }
        }

        void Bands(Texture a, Texture b, Texture c, Texture d, float s1, float s2, float s3, string ttl, string[] line1, string[] line2, bool chips = false)
        {
            compose.SetFloat("_Mode", 0);
            compose.SetTexture("_A", a); compose.SetTexture("_B", b); compose.SetTexture("_C", c); compose.SetTexture("_D", d);
            compose.SetFloat("_S1", s1); compose.SetFloat("_S2", s2); compose.SetFloat("_S3", s3); compose.SetFloat("_Chips", chips ? 1 : 0);
            title.text = ttl; zTop.text = "";
            float[] start = { 0f, s1, s2, s3 };
            for (int i = 0; i < 4; i++)
            {
                l1[i].text = line1 != null && i < line1.Length ? line1[i] : ""; l1[i].anchor = new Vector2(start[i] + 0.012f, 0.058f);
                l2[i].text = line2 != null && i < line2.Length ? line2[i] : ""; l2[i].anchor = new Vector2(start[i] + 0.012f, 0.018f);
            }
        }

        void Zoom(Texture a, Texture b, Vector2 centre, float zoom, string ttl, string topLine, string bottomLine)
        {
            compose.SetFloat("_Mode", 1); compose.SetFloat("_Chips", 0);
            compose.SetTexture("_A", a); compose.SetTexture("_B", b); compose.SetFloat("_Zoom", zoom); compose.SetVector("_ZoomC", centre);
            title.text = ttl; zTop.text = topLine;
            for (int i = 0; i < 4; i++) { l1[i].text = ""; l2[i].text = ""; }
            l1[0].text = bottomLine; l1[0].anchor = new Vector2(0.012f, 0.022f);
        }

        static string Spread(float s) => s.ToString("0.00");

        void LateUpdate()
        {
            float t = FrameCapturer.T;

            float ang = (205f + t * 3.5f) * Mathf.Deg2Rad;
            src.transform.position = focus + new Vector3(Mathf.Sin(ang) * 11.5f, 3.0f + 0.4f * Mathf.Sin(t * 0.3f), Mathf.Cos(ang) * 11.5f);
            src.transform.LookAt(focus);
            sun.transform.rotation = Quaternion.Euler(40f + 8f * Mathf.Sin(t * 0.4f), 20f + t * 12f, 0);   // the sun orbits once in 30 s
            float oa = t * 1.4f;
            orb.position = new Vector3(Mathf.Cos(oa) * 3.4f, 1.7f + 0.3f * Mathf.Sin(t * 2f), Mathf.Sin(oa) * 3.4f);
            torus.rotation = Quaternion.Euler(70f, t * 30f, 0);
            src.Render();

            var d4 = DitherSettings.Levels(4, 1);
            var g6 = DitherSettings.Levels(PalSize, 1, true);
            string res = $"{chain.lowW} x {chain.lowH}";
            int phase;
            if (t < 2.5f)
            {
                phase = 0;
                Bands(srcRT, srcRT, srcRT, srcRT, 1, 1, 1, $"Original render (URP Lit), {W} x {H}", null, null);
            }
            else if (t < 8.5f)
            {
                phase = 1;
                float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 2.5f) / 0.8f));
                var pix = chain.Pixelate(srcRT);
                var dth = chain.Dither(srcRT, d4, low[0]);
                var pal = chain.Palette(chain.Dither(srcRT, g6, low[1]), pals[0], true, low[2]);
                Bands(srcRT, pix, dth, pal, Mathf.Lerp(1f, 0.25f, e), Mathf.Lerp(1f, 0.5f, e), Mathf.Lerp(1f, 0.75f, e),
                    "Pixel-art post-process: downsample, ordered dither, palette (levels spaced in sRGB)",
                    new[] { "Original", "Pixelate only", "Averaged down + dither", "+ Palette" },
                    new[] { $"{W} x {H}", $"point sampled, {res}", $"Bayer {DitherSettings.BayerName[d4.bayerLevel]}, {d4.red} levels per channel", $"luminance, {g6.red} levels, {PalSize} colours" });
            }
            else if (t < 12.5f)
            {
                phase = 2;
                var none = DitherSettings.Levels(2, 0); none.spread = 0;
                var b = new[] { DitherSettings.Levels(2, 0), DitherSettings.Levels(2, 1), DitherSettings.Levels(2, 2) };
                Bands(chain.Dither(srcRT, none, low[0]), chain.Dither(srcRT, b[0], low[1]), chain.Dither(srcRT, b[1], low[2]), chain.Dither(srcRT, b[2], low[3]),
                    0.25f, 0.5f, 0.75f, $"Bayer sizes in the source, 2 levels per channel in sRGB (8 colours), {res}",
                    new[] { "No dither", "Bayer " + DitherSettings.BayerName[0], "Bayer " + DitherSettings.BayerName[1], "Bayer " + DitherSettings.BayerName[2] },
                    new[] { "spread 0", "spread " + Spread(b[0].spread), "spread " + Spread(b[1].spread), "spread " + Spread(b[2].spread) });
            }
            else if (t < 16.5f)
            {
                phase = 3;
                var s = new[] { DitherSettings.Levels(2, 1), DitherSettings.Levels(4, 1), DitherSettings.Levels(8, 1) };
                var l1t = new string[3]; var l2t = new string[3];
                for (int i = 0; i < 3; i++) { l1t[i] = $"{s[i].red} levels per channel"; l2t[i] = $"spread {Spread(s[i].spread)}, up to {s[i].red * s[i].green * s[i].blue} colours"; }
                Bands(chain.Dither(srcRT, s[0], low[0]), chain.Dither(srcRT, s[1], low[1]), chain.Dither(srcRT, s[2], low[2]), srcRT,
                    1f / 3f, 2f / 3f, 1f, "Levels per channel in sRGB, Bayer 4x4, spread = 1 / (levels - 1)", l1t, l2t);
            }
            else if (t < 20f)
            {
                phase = 6;
                var lin = DitherSettings.Levels(4, 1, false, false); var srgb = DitherSettings.Levels(4, 1);
                Bands(chain.Dither(srcRT, lin, low[0]), chain.Dither(srcRT, srgb, low[1]), srcRT, srcRT, 0.5f, 1f, 1f,
                    $"Where the levels sit: {lin.red} levels per channel, Bayer 4x4, spread {Spread(lin.spread)}",
                    new[] { "Levels even in linear light (as in the source)", "Levels even in sRGB" },
                    new[] { "0, 0.33, 0.67, 1 linear = 0, 0.61, 0.84, 1 on screen", "0, 0.33, 0.67, 1 on screen" });
            }
            else if (t < 25f)
            {
                phase = 4;
                var gray = chain.Dither(srcRT, g6, low[0]);
                Bands(chain.Palette(gray, pals[0], true, low[1]), chain.Palette(gray, pals[1], true, low[2]), chain.Palette(gray, pals[2], true, low[3]), srcRT,
                    1f / 3f, 2f / 3f, 1f, $"Same frame, 3 generated palettes: luminance in sRGB, Bayer 4x4, {g6.red} levels",
                    new[] { $"Palette \"{palNames[0]}\"", $"Palette \"{palNames[1]}\"", $"Palette \"{palNames[2]}\"" },
                    new[] { $"{PalSize} generated colours", $"{PalSize} generated colours", $"{PalSize} generated colours" }, true);
            }
            else
            {
                phase = 5;
                var gray = chain.Dither(srcRT, g6, low[0]);
                var a = chain.Palette(gray, pals[0], false, low[1]);
                var b = chain.Palette(gray, pals[0], true, low[2]);
                if (offSource < 0)
                {
                    offSource = PixelPalettes.OffPalettePercent(a, pals[0]);
                    offSnap = PixelPalettes.OffPalettePercent(b, pals[0]);
                    var g7 = chain.Dither(srcRT, DitherSettings.Levels(PalSize + 1, 1, true), low[3]);
                    float off7 = PixelPalettes.OffPalettePercent(chain.Palette(g7, pals[0], false, low[4]), pals[0]);
                    Debug.Log($"[gt] off-palette: source lookup {g6.red} levels {offSource:F2}%, {PalSize + 1} levels {off7:F2}%, snapped {offSnap:F2}%");
                    a = chain.Palette(gray, pals[0], false, low[1]);
                }
                Zoom(a, b, new Vector2(0.5f, 0.42f), 2f, $"8x zoom: {g6.red} grey levels into a {PalSize}-colour palette",
                    $"Source lookup: blends neighbouring entries, {offSource:F1}% of pixels are not palette colours",
                    $"Snapped to the nearest entry: {offSnap:F1}% not palette colours");
            }
            if (phase == 4 && logged != 4)
            {
                for (int p = 0; p < 3; p++) Debug.Log($"[gt] snapped palette {palNames[p]} off-palette {PixelPalettes.OffPalettePercent(low[p + 1], pals[p]):F2}%");
            }
            if (phase != logged) { logged = phase; Debug.Log($"[gt] t {t:F2} phase {phase} '{title.text}' | '{l1[0].text}' '{l2[0].text}' | '{l1[1].text}' '{l2[1].text}' | '{l1[2].text}' '{l2[2].text}' | '{l1[3].text}' '{l2[3].text}' | '{zTop.text}'"); }
        }
    }
}
