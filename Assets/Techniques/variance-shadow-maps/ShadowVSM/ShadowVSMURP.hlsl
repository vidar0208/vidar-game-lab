// URP port of the source's receiver function VSM_ShadowIntensity (ShadowVSM.cginc). Same steps: project into the
// light's space, offset by the normal, pick the cascade from the distance to the box centre, read the blurred first
// and second depth moments, and bound the lit probability with Chebyshev's inequality (min variance 0.00008, then the
// source's smoothstep(0.75, 1) against light bleeding). Changes: cascades come from the slices of a texture array
// instead of rows of one atlas, and the cascade index is clamped to the last slice. _GT_VSMMode 0 (GameTech) is the
// comparison: one point-sampled read of the unblurred depth and a plain depth compare.
#ifndef GT_SHADOW_VSM_URP
#define GT_SHADOW_VSM_URP

TEXTURE2D_ARRAY(VSM_ShadowTex);
SAMPLER(sampler_gt_linear_clamp);
SAMPLER(sampler_gt_point_clamp);
float4x4 VSM_LightMatrix;
float4x4 VSM_LightMatrixNormal;
float VSM_InvNumCascades;
float _GT_VSMMode;
float _GT_VSMHardBias;

float VSM_ShadowIntensity(float3 wPos, float3 wNormal)
{
    float3 lightSpacePos = mul(VSM_LightMatrix, float4(wPos, 1)).xyz;
    float2 lightSpaceNorm = mul((float3x3)VSM_LightMatrixNormal, wNormal).xy;
    float depth = lightSpacePos.z;

    float2 uv = lightSpacePos.xy;       /* should be in range [-0.5, 0.5] here */
    uv += lightSpaceNorm.xy;

    const float MAX = 0.485;
    float2 uv_abs = abs(uv);
    float magnitude = max(uv_abs.x, uv_abs.y) * (2 / MAX);
    float cascade = floor(log2(max(magnitude, 1)));
    cascade = min(cascade, round(1 / VSM_InvNumCascades) - 1);   // GameTech
    float inv_cascade_scale = exp2(-cascade);
    uv *= inv_cascade_scale;
    uv += 0.5;

    if (_GT_VSMMode < 0.5)   // GameTech: plain depth compare on the unblurred map
    {
        float d = SAMPLE_TEXTURE2D_ARRAY_LOD(VSM_ShadowTex, sampler_gt_point_clamp, uv, cascade, 0).r;
        return depth <= d + _GT_VSMHardBias ? 1 : 0;
    }

    float2 s = SAMPLE_TEXTURE2D_ARRAY_LOD(VSM_ShadowTex, sampler_gt_linear_clamp, uv, cascade, 0).rg;
    float x = s.r;
    float x2 = s.g;
    float var = x2 - x * x;
    var = max(var, 0.00008);
    float p_inv = depth < x;
    float delta = depth - x;
    float p_max = var / (var + delta * delta);
    float p_max_inv = smoothstep(0.75, 1, p_max);
    return max(p_max_inv, p_inv);
}
#endif
