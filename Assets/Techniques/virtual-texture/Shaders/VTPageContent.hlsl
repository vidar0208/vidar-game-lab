// The content of the virtual texture: a procedural map (hillshaded height noise, colour bands, contour lines)
// with a grid line and a "column row" number on every page, so pages, mips and cache misses can be seen.
// p: virtual uv (0..1), texel: virtual uv per output pixel (detail finer than about 3 pixels is faded out).
#ifndef GT_PAGE_CONTENT_INCLUDED
#define GT_PAGE_CONTENT_INCLUDED

float GTHash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float GTNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = GTHash(i), b = GTHash(i + float2(1, 0)), c = GTHash(i + float2(0, 1)), d = GTHash(i + float2(1, 1));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

float GTHeight(float2 p, float texel)
{
    float h = 0.0, amp = 0.5, f = 5.0;
    for (int o = 0; o < 9; o++)
    {
        float cyclePx = 1.0 / (f * texel);                  // output pixels per noise cell
        float w = saturate((cyclePx - 3.0) / 3.0);
        h += amp * w * (GTNoise(p * f + o * 17.31) - 0.5);
        amp *= 0.5; f *= 2.03;
    }
    return saturate(0.5 + h * 1.4);
}

float GTBox(float2 q, float2 lo, float2 hi)
{
    float2 d = max(lo - q, q - hi);
    return max(d.x, d.y);
}

// seven-segment digit in a 0.6 x 1 box; returns coverage
float GTDigit(int d, float2 q, float px)
{
    const int masks[10] = { 63, 6, 91, 79, 102, 109, 125, 7, 127, 111 };
    int m = masks[clamp(d, 0, 9)];
    const float t = 0.17, w = 0.6;
    float dist = 1e5;
    if (m & 1)  dist = min(dist, GTBox(q, float2(0, 1 - t), float2(w, 1)));
    if (m & 2)  dist = min(dist, GTBox(q, float2(w - t, 0.5), float2(w, 1)));
    if (m & 4)  dist = min(dist, GTBox(q, float2(w - t, 0), float2(w, 0.5)));
    if (m & 8)  dist = min(dist, GTBox(q, float2(0, 0), float2(w, t)));
    if (m & 16) dist = min(dist, GTBox(q, float2(0, 0), float2(t, 0.5)));
    if (m & 32) dist = min(dist, GTBox(q, float2(0, 0.5), float2(t, 1)));
    if (m & 64) dist = min(dist, GTBox(q, float2(0, 0.5 - t * 0.5), float2(w, 0.5 + t * 0.5)));
    return saturate(0.5 - dist / px);
}

float3 GTPageContent(float2 p, float texel, float pages)
{
    float h = GTHeight(p, texel);
    float e = max(texel, 1.0 / 16384.0);
    float hx = GTHeight(p + float2(e, 0), texel), hy = GTHeight(p + float2(0, e), texel);
    float2 grad = float2(hx - h, hy - h) / e;                  // height per virtual uv

    // hillshade (relief scaled down so the map reads, light from the upper left)
    float3 n = normalize(float3(-grad * 0.035, 1.0));
    float shade = saturate(dot(n, normalize(float3(-0.55, 0.6, 0.58)))) * 0.85 + 0.3;

    // colour bands: deep water, shallow water, sand, grass, forest, rock, snow (linear colours)
    float3 deep = float3(0.02, 0.06, 0.14), shallow = float3(0.05, 0.2, 0.3), sand = float3(0.55, 0.45, 0.26);
    float3 grass = float3(0.16, 0.3, 0.07), forest = float3(0.05, 0.15, 0.04), rock = float3(0.3, 0.27, 0.24), snow = float3(0.8, 0.82, 0.85);
    float3 col = lerp(deep, shallow, smoothstep(0.25, 0.42, h));
    col = lerp(col, sand, smoothstep(0.42, 0.435, h));
    col = lerp(col, grass, smoothstep(0.445, 0.47, h));
    col = lerp(col, forest, smoothstep(0.55, 0.6, h));
    col = lerp(col, rock, smoothstep(0.64, 0.68, h));
    col = lerp(col, snow, smoothstep(0.7, 0.72, h));
    bool land = h > 0.43;
    col *= land ? shade : lerp(1.0, shade, 0.3);

    // contour lines every 0.02 (minor) and 0.1 (major), about 1.2 px wide, faded when closer than 4 px
    float gl = max(length(grad) * texel, 1e-6);               // height change per pixel
    float lineMask = 0.0;
    for (int k = 0; k < 2; k++)
    {
        float step_ = k == 0 ? 0.02 : 0.1;
        float c = h / step_;
        float dist = abs(frac(c + 0.5) - 0.5) * step_ / gl;   // pixels to the nearest contour
        float spacingPx = step_ / gl;
        float a = (1.0 - smoothstep(0.6, 1.2, dist)) * saturate((spacingPx - 4.0) / 4.0);
        lineMask = max(lineMask, a * (k == 0 ? 0.35 : 0.6));
    }
    if (land) col *= 1.0 - lineMask;

    // page grid: thin light lines on page edges, faded when a page is smaller than 24 px
    float2 q = p * pages;
    float pagePx = 1.0 / (texel * pages);
    float2 gd = min(frac(q), 1.0 - frac(q)) * pagePx;         // pixels to the page edge
    float grid = (1.0 - smoothstep(0.75, 1.75, min(gd.x, gd.y))) * saturate((pagePx - 24.0) / 24.0);
    col = lerp(col, float3(0.85, 0.8, 0.7), grid * 0.7);

    // number plate in the page's corner: column and row, two digits each
    int2 cell = (int2)floor(q);
    float2 lq = frac(q);
    float digitPx = 0.16 * pagePx;
    float labelA = saturate((digitPx - 7.0) / 7.0);
    if (labelA > 0.0)
    {
        float plate = GTBox(lq, float2(0.04, 0.70), float2(0.56, 0.96));
        float plateA = saturate(0.5 - plate * pagePx) * 0.72 * labelA;
        col = lerp(col, float3(0.015, 0.015, 0.02), plateA);
        int digits[4] = { (cell.x / 10) % 10, cell.x % 10, (cell.y / 10) % 10, cell.y % 10 };
        float ink = 0.0;
        for (int j = 0; j < 4; j++)
        {
            float x0 = 0.07 + j * 0.115 + (j >= 2 ? 0.06 : 0.0);
            float2 dq = (lq - float2(x0, 0.74)) / 0.16;
            ink = max(ink, GTDigit(digits[j], dq, 1.0 / digitPx));
        }
        col = lerp(col, float3(1.0, 0.78, 0.35), ink * labelA);
    }
    return col;
}

#endif
