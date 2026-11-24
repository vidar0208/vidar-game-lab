// Two-level distance field lookups, after ZephyrL/DFAO-unity (GDFTexturing.hlsl, MIT, Copyright (c) 2019 OUTBOUNDS).
// Level 1: one local SDF per prop, all stacked along z in one 3D atlas, each stored as distance / its box diagonal.
// Level 2: one coarse global field over the whole scene, rebuilt from the local SDFs (DFBuild.compute).
// Change from the source: VolumeData fields padded to float4 (structured buffer layout on Metal).
#ifndef DF_COMMON
#define DF_COMMON

SamplerState gdf_sampler_linear_clamp;

struct VolumeData
{
    float4x4 WorldToLocal;
    float4 ExtentsIndex;   // xyz half extents of the local box, w atlas slot
    float4 Center;         // xyz centre of the local box in local space
};

inline bool GDFInsideAABB(float3 pos, float3 mn, float3 mx)
{
    return pos.x > mn.x && pos.y > mn.y && pos.z > mn.z && pos.x < mx.x && pos.y < mx.y && pos.z < mx.z;
}

inline bool GDFInsideVolume(float3 pos, VolumeData d)
{
    float3 lp = mul(d.WorldToLocal, float4(pos, 1)).xyz - d.Center.xyz;
    return GDFInsideAABB(lp, -d.ExtentsIndex.xyz, d.ExtentsIndex.xyz);
}

inline float2 GDFLineAABBIntsc(float3 ro, float3 re, float3 mn, float3 mx)
{
    float3 invRd = 1.0 / (re - ro);
    float3 mini = invRd * (mn - ro), maxi = invRd * (mx - ro);
    float3 closest = min(mini, maxi), furthest = max(mini, maxi);
    float2 r;
    r.x = max(closest.x, max(closest.y, closest.z));
    r.y = min(furthest.x, min(furthest.y, furthest.z));
    return saturate(r);
}

inline float GDFAtlasTex3D(float3 pos, VolumeData d, int numTexture, Texture3D atlasTex)
{
    float3 ext = d.ExtentsIndex.xyz;
    float3 lp = mul(d.WorldToLocal, float4(pos, 1)).xyz - d.Center.xyz;
    float3 uv = lp / (ext * 2) + 0.5;
    uv.z = (d.ExtentsIndex.w + uv.z) / numTexture;
    float s = atlasTex.SampleLevel(gdf_sampler_linear_clamp, uv, 0).r * length(ext * 2);
    return s / length(d.WorldToLocal._m00_m01_m02);
}

// outside a local box: distance to the box along the line to its centre, plus the stored distance at the entry point
inline void GDFGlobalDistanceToAABBPlus(float3 pos, VolumeData d, int numTexture, Texture3D tex, out float aoDist, out float rmDist)
{
    float3 ext = d.ExtentsIndex.xyz;
    float3 lp = mul(d.WorldToLocal, float4(pos, 1)).xyz - d.Center.xyz;
    float3 le = float3(0, 0, 0);
    float2 hit = GDFLineAABBIntsc(lp, le, -ext, ext);
    float3 lerpPos = lerp(lp, le, hit.xxx);
    float3 uv = lerpPos / (ext * 2) + 0.5;
    uv.z = (d.ExtentsIndex.w + uv.z) / numTexture;
    float inside = max(0, tex.SampleLevel(gdf_sampler_linear_clamp, uv, 0).r) * length(ext * 2);
    float outside = hit.x * distance(lp, le);
    float scale = 1.0 / length(d.WorldToLocal._m00_m01_m02);
    aoDist = (inside + outside) * scale;
    rmDist = max(inside, outside) * scale;
}
#endif
