// Shared code for GameTech/DitheredLit and GameTech/BlendLit.
// The dither functions are a URP/HLSL port of "Dither Functions.cginc" from
// gkjohnson/unity-dithered-transparency-shader (MIT, Copyright (c) 2017 Garrett Johnson).
#ifndef GT_DITHER_COMMON
#define GT_DITHER_COMMON

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST;
half4 _BaseColor;
half _Metallic;
half _Smoothness;
half _Fade;     // multiplies alpha (used for the fade-out)
half _Wind;     // vertex sway for cloth, 0 = static
CBUFFER_END

TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

// Per-camera settings, set as globals before each camera renders.
float _GT_DitherMode;     // 0 = 4x4 matrix, 1 = texture
float _GT_DitherScale;    // texture mode: screen pixels covered by one repeat of the texture
float _GT_ShadowDither;   // 1 = shadow caster runs the same dither clip, 0 = solid shadow caster
TEXTURE2D(_GT_DitherTex);
SamplerState gt_point_repeat_sampler;

// isDithered(pos, alpha) of the source; pos is the pixel position (SV_Position.xy)
float IsDitheredMatrix(float2 pix, float alpha)
{
    const float DITHER_THRESHOLDS[16] =
    {
        1.0 / 17.0,  9.0 / 17.0,  3.0 / 17.0, 11.0 / 17.0,
        13.0 / 17.0,  5.0 / 17.0, 15.0 / 17.0,  7.0 / 17.0,
        4.0 / 17.0, 12.0 / 17.0,  2.0 / 17.0, 10.0 / 17.0,
        16.0 / 17.0,  8.0 / 17.0, 14.0 / 17.0,  6.0 / 17.0
    };
    uint index = (uint(pix.x) % 4) * 4 + uint(pix.y) % 4;
    return alpha - DITHER_THRESHOLDS[index];
}

// isDithered(pos, alpha, tex, scale) of the source: the texture is centred on the screen and repeats every 'scale' pixels
float IsDitheredTex(float2 pix, float alpha)
{
    float2 p = (pix - _ScreenParams.xy * 0.5) / _GT_DitherScale;
    return alpha - SAMPLE_TEXTURE2D(_GT_DitherTex, gt_point_repeat_sampler, p).r - 0.0001 * (1 - ceil(alpha));
}

void DitherClip(float2 pix, float alpha)
{
    clip(_GT_DitherMode > 0.5 ? IsDitheredTex(pix, alpha) : IsDitheredMatrix(pix, alpha));
}

struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; };
struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float2 uv : TEXCOORD2;
    half4 color : COLOR;
};

float3 WindWS(float3 positionOS)
{
    float3 wp = TransformObjectToWorld(positionOS);
    if (_Wind > 0)
    {
        float hang = saturate((2.75 - wp.y) / 2.6);   // pinned at the rail, free at the hem
        wp.z += _Wind * hang * hang * sin(_Time.y * 1.3 + wp.x * 2.1);
        wp.x += _Wind * 0.4 * hang * sin(_Time.y * 0.9 + wp.y * 1.7);
    }
    return wp;
}

Varyings Vert(Attributes i)
{
    Varyings o;
    o.positionWS = WindWS(i.positionOS.xyz);
    o.positionCS = TransformWorldToHClip(o.positionWS);
    o.normalWS = TransformObjectToWorldNormal(i.normalOS);
    o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
    o.color = i.color;
    return o;
}

half4 BaseColor(Varyings i)
{
    half4 c = _BaseColor * SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * i.color;
    c.a *= _Fade;
    return c;
}

half3 Lit(Varyings i, half4 c, float facing)
{
    InputData d = (InputData)0;
    d.positionWS = i.positionWS;
    d.normalWS = normalize(i.normalWS) * facing;
    d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
    d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
    d.bakedGI = SampleSH(d.normalWS);
    d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
    d.shadowMask = half4(1, 1, 1, 1);
    SurfaceData s = (SurfaceData)0;
    s.albedo = c.rgb; s.metallic = _Metallic; s.smoothness = _Smoothness; s.occlusion = 1; s.alpha = 1;
    return UniversalFragmentPBR(d, s).rgb;
}

#endif
