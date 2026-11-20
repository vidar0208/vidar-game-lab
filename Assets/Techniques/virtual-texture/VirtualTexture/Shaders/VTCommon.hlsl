// Port of the source's VirtualTexture.cginc, VTFeedback.cginc and VTDebug.cginc to HLSL for URP.
// Include after URP's Core.hlsl (uses its sampler_PointClamp / sampler_LinearClamp).
#ifndef GT_VIRTUAL_TEXTURE_INCLUDED
#define GT_VIRTUAL_TEXTURE_INCLUDED

// x: page size (pages per side), y: virtual texture size in pixels x feedback scale, z: max mip, w: mip bias
float4 _VTFeedbackParam;
// x: page count per side, y: 1 / page count, z: max mip level
float4 _VTPageParam;
// x: padding offset, y: page area of a tile, zw: 1 / tile count
float4 _VTTileParam;

TEXTURE2D(_VTLookupTex);
TEXTURE2D(_VTTiledTex0);

// virtual uv -> uv in the tile cache, through the page table
float2 VTTransferUV(float2 uv)
{
    float2 uvInt = uv - frac(uv * _VTPageParam.x) * _VTPageParam.y;
    // GameTech change: sample the lookup at the texel centre (the source samples its corner)
    float4 page = SAMPLE_TEXTURE2D_LOD(_VTLookupTex, sampler_PointClamp, uvInt + 0.5 * _VTPageParam.y, 0) * 255;
    float2 inPageOffset = frac(uv * exp2(_VTPageParam.z - page.b));
    float2 inTileOffset = inPageOffset * _VTTileParam.y + _VTTileParam.x;
    return (page.rg + inTileOffset) * _VTTileParam.zw;
}

float4 VTTex2D(float2 cacheUV) { return SAMPLE_TEXTURE2D_LOD(_VTTiledTex0, sampler_LinearClamp, cacheUV, 0); }

float VTGetMipLevel(float2 uv) { return SAMPLE_TEXTURE2D_LOD(_VTLookupTex, sampler_PointClamp, uv, 0).b * 255; }

// feedback: which page and which mip this pixel needs
float4 VTFragFeedback(float2 uv0)
{
    float2 page = floor(uv0 * _VTFeedbackParam.x);
    float2 uv = uv0 * _VTFeedbackParam.y;
    float2 dx = ddx(uv);
    float2 dy = ddy(uv);
    int mip = clamp(int(0.5 * log2(max(dot(dx, dx), dot(dy, dy))) + 0.5 + _VTFeedbackParam.w), 0, _VTFeedbackParam.z);
    return float4(page / 255.0, mip / 255.0, 1);
}

float4 VTGetMipColor(float mip)
{
    const float4 colors[12] = {
        float4(1, 0, 0, 1), float4(0, 0, 1, 1), float4(1, 0.5, 0, 1), float4(1, 0, 0.5, 1),
        float4(0, 0.5, 0.5, 1), float4(0, 0.25, 0.5, 1), float4(0.25, 0.5, 0, 1), float4(0.5, 0, 1, 1),
        float4(1, 0.25, 0.5, 1), float4(0.5, 0.5, 0.5, 1), float4(0.25, 0.25, 0.25, 1), float4(0.125, 0.125, 0.125, 1)
    };
    return colors[clamp((int)mip, 0, 11)];
}

#endif
