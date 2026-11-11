// Shared functions of the digital rain post. text(), rain(), rain_colored(), the hash functions and the two
// transition functions follow IRCSS/MatrixVFX (MIT, Copyright 2020 Shahriar Shahrabi): TriPlanarMatrix shader,
// RandomLib and Transition includes. Changes are marked "changed:".
#ifndef GT_MATRIX_COMMON
#define GT_MATRIX_COMMON

TEXTURE2D(global_white_noise);      // 512 x 512 animated white noise, written by the source's compute shader
TEXTURE2D(global_font_texture);     // 16 x 16 glyph atlas (changed: generated from code, not the repo's font texture)
SamplerState gt_trilinear_repeat_sampler;   // sampler_PointRepeat comes from the URP core library

float global_colored;               // 1 = coloured variant
uint _session_rand_seed;            // seed of the coloured variant
float _MX_Time;                     // seconds on the video timeline (source: _Time.y)
float _MX_Scale;                    // source: #define scale 0.2 (triplanar)

// ---- hash, as in RandomLib
uint triple32(uint x)
{
    x ^= x >> 17; x *= 0xed5ad4bbU;
    x ^= x >> 11; x *= 0xac4c1b51U;
    x ^= x >> 15; x *= 0x31848babU;
    x ^= x >> 14;
    return x;
}
float wang_rnd(uint seed) { return ((float)triple32(seed)) / float(0xFFFFFFFF); }
// changed: floats go through int first, so negative world coordinates do not all collapse to one seed
float rnd(float seed, uint constant) { return wang_rnd((uint)(int)seed + triple32(_session_rand_seed) * constant); }

// ---- one glyph per 16 x 16 block, picked by the white noise texture
float text(float2 coord)
{
    float2 c = coord / 16.0;
    // changed: explicit gradients, so the jump of the atlas uv at block borders does not pick a wrong mip level
    float2 gx = ddx(c) * 0.7 * 0.0625, gy = ddy(c) * 0.7 * 0.0625;
    float2 uv = frac(c);
    float2 block = floor(c);
    uv = uv * 0.7 + 0.1;
    // changed: + 0.5 samples the centre of the noise texel
    float2 r = SAMPLE_TEXTURE2D_LOD(global_white_noise, sampler_PointRepeat, (block + 0.5) / 512.0, 0).xy;
    r = floor(r * 16.0);
    uv += r;
    uv *= 0.0625;
    uv.x = -uv.x;
    return SAMPLE_TEXTURE2D_GRAD(global_font_texture, gt_trilinear_repeat_sampler, uv, gx, gy).r;
}

// ---- falling brightness per column
float3 rain_len(float2 fragCoord, float dropLength)
{
    fragCoord.x = floor(fragCoord.x / 16.0);
    float offset = sin(fragCoord.x * 15.0);
    float speed = cos(fragCoord.x * 3.0) * 0.15 + 0.35;
    float y = frac(fragCoord.y / dropLength + _MX_Time * speed + offset);
    y = max(y, 0.002);                       // changed: no division by zero at the head of a drop
    return float3(0.1, 1.0, 0.35) / (y * 20.0);
}

// Lab (0..1 per channel as in the source) to linear RGB. Own implementation of the standard CIE formulas.
float LabInv(float f) { return f > 0.206897 ? f * f * f : (f - 16.0 / 116.0) / 7.787; }
float3 LabToLinearRGB(float3 c)
{
    float L = 100.0 * c.x, a = 254.0 * (c.y - 0.5), b = 254.0 * (c.z - 0.5);
    float fy = (L + 16.0) / 116.0, fx = a / 500.0 + fy, fz = fy - b / 200.0;
    float3 xyz = float3(0.95047 * LabInv(fx), LabInv(fy), 1.08883 * LabInv(fz));
    float3 rgb = float3(dot(xyz, float3(3.2406, -1.5372, -0.4986)), dot(xyz, float3(-0.9689, 1.8758, 0.0415)), dot(xyz, float3(0.0557, -0.2040, 1.0570)));
    return saturate(rgb);
}

float3 rain_colored_len(float2 fragCoord, float dropLength)
{
    fragCoord.x = floor(fragCoord.x / 16.0);
    float offset = rnd(fragCoord.x * 521.0, 612);
    float speed = rnd(fragCoord.x * 612.0, 951) * 0.15 + 0.35;
    speed *= 0.4;
    float y = frac(fragCoord.y / dropLength + _MX_Time * speed + offset);
    y = max(y, 0.002);
    float randomSeed = (fragCoord.x + floor(fragCoord.y / dropLength + _MX_Time * speed + offset)) * 51.0;
    float3 col = float3(rnd(randomSeed, 21), frac(rnd(randomSeed, 712) + 0.8), frac(rnd(randomSeed, 61) + 0.2));
    col = LabToLinearRGB(col);
    return col / (y * 20.0);
}

#define MX_DROP_LENGTH 512.0
float3 MatrixEffect(float2 coord)
{
    float2 c = coord * float2(MX_DROP_LENGTH, MX_DROP_LENGTH) * _MX_Scale;
    float glyph = text(c);
    float3 rain_col = global_colored > 0.5 ? rain_colored_len(c, MX_DROP_LENGTH) : rain_len(c, MX_DROP_LENGTH);
    return glyph * rain_col;
}

// ---- transitions, as in the source's Transition include
float _Global_Transition_value;     // 0 = plain surface, 1 = rain over the surface, 2 = rain only
float3 _Global_Effect_center;       // camera position

float spread_from_center(float3 center, float3 coord, float transition, float shift)
{
    float distance_to_center = distance(coord, center);
    float control_value = saturate(transition - shift);
    if (control_value * 60.0 < distance_to_center) return 0;
    else return 1.0;
}

float split_from_midle(float coordx, float transition, float shift)
{
    float distance_to_center = abs(coordx - 0.5) * 2.0;
    float control_value = saturate(transition - shift);
    if (control_value < distance_to_center) return 0;
    else return 1.0;
}
#endif
