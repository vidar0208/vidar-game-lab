// Helpers for 85: the three pixel-art effects of the source as Graphics.Blit chains, the generated palettes and an off-palette count.
// The final point-sampled upscale of the source is done by the compose quad, which point samples the low-resolution result.
using UnityEngine;

namespace GameTech.Posts
{
    public struct DitherSettings
    {
        public float spread;                       // the source's _Spread: how far the Bayer offset (-0.5 .. 0.5) pushes each value
        public int red, green, blue, bayerLevel;   // colour count per channel (2..16); Bayer level 0 = 2x2, 1 = 4x4, 2 = 8x8
        public bool gray;                          // GT: luminance first, so the result can feed the palette pass
        public bool gamma;                         // GT: dither and quantise in sRGB instead of linear

        // spread = one quantisation step, 1 / (levels - 1): the offsets then cover exactly the gap between two levels
        public static DitherSettings Levels(int n, int bayerLevel = 1, bool gray = false, bool gamma = true) =>
            new DitherSettings { spread = 1f / (n - 1), red = n, green = n, blue = n, bayerLevel = bayerLevel, gray = gray, gamma = gamma };
        public static readonly string[] BayerName = { "2x2", "4x4", "8x8" };
    }

    public class PixelChain
    {
        public readonly int downSamples, lowW, lowH;
        readonly Material pixelArt, dither, palette;
        readonly RenderTexture[] pointDown, averageDown;

        public static RenderTexture RT(int w, int h, FilterMode f)
        {
            var r = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { filterMode = f, wrapMode = TextureWrapMode.Clamp };
            r.Create(); return r;
        }

        public PixelChain(int w, int h, int downs, Material pixelArtMat, Material ditherMat, Material paletteMat)
        {
            downSamples = downs; pixelArt = pixelArtMat; dither = ditherMat; palette = paletteMat;
            pointDown = new RenderTexture[downs]; averageDown = new RenderTexture[downs];
            for (int i = 0; i < downs; i++) { w /= 2; h /= 2; pointDown[i] = RT(w, h, FilterMode.Point); averageDown[i] = RT(w, h, FilterMode.Bilinear); }
            lowW = w; lowH = h;
        }

        public RenderTexture NewLow() => RT(lowW, lowH, FilterMode.Point);

        // PixelArtFilter: halve downSamples times, each blit point sampled, so each step keeps one of every 2 x 2 pixels
        public RenderTexture Pixelate(Texture src)
        {
            Texture cur = src;
            for (int i = 0; i < downSamples; i++) { Graphics.Blit(cur, pointDown[i], pixelArt); cur = pointDown[i]; }
            return pointDown[downSamples - 1];
        }

        // Ditherer with pointFilterDown = false (its default): plain bilinear blits halve the image (each pixel = mean of 2 x 2),
        // then pass 0 of the dither shader runs at the low resolution
        public RenderTexture Dither(Texture src, DitherSettings s, RenderTexture dst)
        {
            Texture cur = src;
            for (int i = 0; i < downSamples; i++) { Graphics.Blit(cur, averageDown[i]); cur = averageDown[i]; }
            dither.SetFloat("_Spread", s.spread);
            dither.SetInteger("_RedColorCount", s.red); dither.SetInteger("_GreenColorCount", s.green); dither.SetInteger("_BlueColorCount", s.blue);
            dither.SetInteger("_BayerLevel", s.bayerLevel); dither.SetInteger("_Grayscale", s.gray ? 1 : 0); dither.SetInteger("_Gamma", s.gamma ? 1 : 0);
            Graphics.Blit(cur, dst, dither, 0);
            return dst;
        }

        // PaletteSwapper: reads the red channel as the grey level and looks it up in the palette; snap = the GT nearest-entry lookup
        public RenderTexture Palette(Texture gray, Texture2D pal, bool snap, RenderTexture dst)
        {
            palette.SetTexture("_ColorPalette", pal); palette.SetInteger("_Invert", 0); palette.SetInteger("_Snap", snap ? 1 : 0);
            Graphics.Blit(gray, dst, palette);
            return dst;
        }
    }

    public static class PixelPalettes
    {
        // Generated here as hue-shifted ramps from dark to light. Not taken from any game or published palette.
        public static Texture2D Ramp(string name, int n, float h0, float h1, float s0, float s1, float v0, float v1)
        {
            var t = new Texture2D(n, 1, TextureFormat.RGBA32, false, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = name };
            for (int i = 0; i < n; i++)
            {
                float k = i / (n - 1f);
                t.SetPixel(i, 0, Color.HSVToRGB(Mathf.Repeat(Mathf.Lerp(h0, h1, k), 1f), Mathf.Lerp(s0, s1, k), Mathf.Lerp(v0, v1, k)));
            }
            t.Apply(false, false);
            return t;
        }

        // share of pixels whose colour is not one of the palette entries (readback of a small render texture)
        public static float OffPalettePercent(RenderTexture rt, Texture2D pal)
        {
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false, true);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(false);
            RenderTexture.active = prev;
            var px = tex.GetPixels(); Object.Destroy(tex);
            var pc = pal.GetPixels(); for (int i = 0; i < pc.Length; i++) pc[i] = pc[i].linear;
            int off = 0;
            foreach (var p in px)
            {
                bool hit = false;
                foreach (var c in pc) if (Mathf.Abs(p.r - c.r) + Mathf.Abs(p.g - c.g) + Mathf.Abs(p.b - c.b) < 0.006f) { hit = true; break; }
                if (!hit) off++;
            }
            return 100f * off / px.Length;
        }
    }
}
