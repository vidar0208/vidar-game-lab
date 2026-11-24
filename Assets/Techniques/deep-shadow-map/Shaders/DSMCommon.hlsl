// Deep shadow map lookup shared by the fibre and ground shaders. Mirrors the source's resolve kernel: for each light
// texel, multiply (1 - alpha) of every stored fragment that is closer to the light than the receiver. _GMode 0 uses
// the nearest depth only (an ordinary shadow map built from the same fragments), _GMode 1 uses all stored fragments.
// Both modes use the same 2x2 bilinear filter over neighbouring texels.
#ifndef GT_DSM_COMMON
#define GT_DSM_COMMON
StructuredBuffer<uint> _Count;
StructuredBuffer<uint> _Nearest;
StructuredBuffer<float2> _Frags;
uint _Dim, _MaxFrags;
float4 _LC, _LR, _LU, _LF;
float _GMode, _GDebug, _DsmBias;
float4 _GLightCol, _GAmb;

float3 DsmToLight(float3 p)
{
    float3 d = p - _LC.xyz;
    return float3(dot(d, _LR.xyz) / (2.0 * _LC.w) + 0.5, dot(d, _LU.xyz) / (2.0 * _LC.w) + 0.5, (dot(d, _LF.xyz) + _LF.w) / (2.0 * _LF.w));
}

float DsmTexel(int2 t, float d)
{
    t = clamp(t, 0, (int)_Dim - 1);
    uint idx = t.y * _Dim + t.x;
    if (_GMode < 0.5) return asfloat(_Nearest[idx]) < d ? 0.0 : 1.0;
    uint n = min(_Count[idx], _MaxFrags);
    float tr = 1.0;
    for (uint i = 0; i < n; i++)
    {
        float2 f = _Frags[idx * _MaxFrags + i];
        if (f.x < d) tr *= 1.0 - f.y;
    }
    return tr;
}

float DsmTransmittance(float3 p)
{
    float3 l = DsmToLight(p);
    if (any(l.xy <= 0.0) || any(l.xy >= 1.0)) return 1.0;
    float d = l.z - _DsmBias / (2.0 * _LF.w);
    float2 f = l.xy * _Dim - 0.5; int2 i = (int2)floor(f); float2 w = f - floor(f);
    float a = DsmTexel(i, d), b = DsmTexel(i + int2(1, 0), d), c = DsmTexel(i + int2(0, 1), d), e = DsmTexel(i + int2(1, 1), d);
    return lerp(lerp(a, b, w.x), lerp(c, e, w.x), w.y);
}

float DsmCount(float3 p)
{
    float3 l = DsmToLight(p);
    if (any(l.xy <= 0.0) || any(l.xy >= 1.0)) return 0.0;
    int2 t = (int2)(l.xy * _Dim);
    return (float)_Count[t.y * _Dim + t.x];
}

float3 DsmHeat(float x) { x = saturate(x); return saturate(float3(x * 2.0 - 0.6, 1.2 - abs(x * 2.2 - 1.1), 1.0 - x * 2.4)); }
#endif
