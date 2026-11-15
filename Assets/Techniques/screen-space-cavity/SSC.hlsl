// Port of SSC.hlsl from malyawka/URP-ScreenSpaceCavity (BSD-3-Clause, Copyright (c) 2021, MalyaWla (Pavel Pustovalov)).
// Curvature() and Cavity() below are the source's code, unchanged apart from the background test. What changed is
// where they read their inputs: the source reads URP 10's depth texture and DepthNormals texture (view-space normals,
// z flipped). URP 17 keeps world-space normals there, so this port draws its own buffer with the same contents
// (view-space normal with z flipped in xyz, linear eye depth in w) and the sample helpers read that. The camera
// projection comes in as _SSCProj because this pass runs outside the camera's own render (the source reads
// unity_CameraProjection). Dropped: the unused SSAO random table, the commented-out samplers and the orthographic path.
#ifndef GT_SSC_INCLUDED
#define GT_SSC_INCLUDED

float4 _CurvatureParams;
float4 _CavityParams;
float4 _SourceSize;
float4x4 _SSCProj;
TEXTURE2D(_SSCNormalDepth);

#define CURVATURE_SCALE         _CurvatureParams.x
#define CURVATURE_RIDGE         _CurvatureParams.z
#define CURVATURE_VALLEY        _CurvatureParams.w

#define CAVITY_DISTANCE         _CavityParams.x
#define CAVITY_ATTENUATION      _CavityParams.y
#define CAVITY_RIDGE            _CavityParams.z
#define CAVITY_VALLEY           _CavityParams.w

#define CAVITY_SAMPLES          _CurvatureParams.y

#define EPSILON                 1.0e-4

static const float kContrast = 0.6;
static const float kGeometryCoeff = 0.8;
static const float kBeta = 0.002;

float2 GetScreenSpacePosition(float2 uv)
{
    return uv * _SourceSize.xy;
}

float2 CosSin(float theta)
{
    float sn, cs;
    sincos(theta, sn, cs);
    return float2(cs, sn);
}

float3 PickSamplePoint(float2 uv, float randAddon, int index)
{
    float2 positionSS = GetScreenSpacePosition(uv);
    float gn = InterleavedGradientNoise(positionSS, index);

    float u = frac(gn) * 2.0 - 1.0;
    float theta = gn * TWO_PI;
    return float3(CosSin(theta) * sqrt(1.0 - u * u), u);
}

// --- inputs: this port's own buffer instead of URP's depth and normals textures ---
float SampleAndGetLinearDepth(float2 uv)
{
    return SAMPLE_TEXTURE2D_LOD(_SSCNormalDepth, sampler_PointClamp, uv, 0).w;
}

float3 SampleNormal(float2 uv)
{
    return SAMPLE_TEXTURE2D_LOD(_SSCNormalDepth, sampler_PointClamp, uv, 0).xyz;
}
// ---

float3 ReconstructViewPos(float2 uv, float depth, float2 p11_22, float2 p13_31)
{
    float3 viewPos = float3(depth * ((uv.xy * 2.0 - 1.0 - p13_31) * p11_22), depth);
    return viewPos;
}

void SampleDepthNormalView(float2 uv, float2 p11_22, float2 p13_31, out float depth, out float3 normal, out float3 vpos)
{
    depth  = SampleAndGetLinearDepth(uv);
    vpos = ReconstructViewPos(uv, depth, p11_22, p13_31);
    normal = SampleNormal(uv);
}

float3x3 GetCoordinateConversionParameters(out float2 p11_22, out float2 p13_31)
{
    float3x3 camProj = (float3x3)_SSCProj;

    p11_22 = rcp(float2(camProj._11, camProj._22));
    p13_31 = float2(camProj._13, camProj._23);

    return camProj;
}

float CurvatureSoftClamp(float curvature, float control)
{
    if (curvature < 0.5 / control)
    {
        return curvature * (1.0 - curvature * control);
    }
    return 0.25 / control;
}

void Curvature(float2 uv, out float curvature)
{
    curvature = 0.0;
    float3 offset = float3(_SourceSize.zw, 0.0) * (CURVATURE_SCALE);

    float normal_up = SampleNormal(uv + offset.zy).g;
    float normal_down = SampleNormal(uv - offset.zy).g;
    float normal_right = SampleNormal(uv + offset.xz).r;
    float normal_left = SampleNormal(uv - offset.xz).r;

    float normal_diff = (normal_up - normal_down) + (normal_right - normal_left);

    if (normal_diff >= 0.0)
    {
        curvature = 2.0 * CurvatureSoftClamp(normal_diff, CURVATURE_RIDGE);
    }
    else
    {
        curvature = -2.0 * CurvatureSoftClamp(-normal_diff, CURVATURE_VALLEY);
    }
}

void Cavity(float2 uv, out float cavity, out float edges)
{
    cavity = edges = 0.0;
    float2 p11_22, p13_31;
    float3x3 camProj = GetCoordinateConversionParameters(p11_22, p13_31);

    float depth;
    float3 normal, vpos;
    SampleDepthNormalView(uv, p11_22, p13_31, depth, normal, vpos);

    // source: if (depth == 1.0 || depth == 0.0) return;  (raw depth); here the cleared background holds 10000
    if (depth >= 5000.0)
    {
        return;
    }

    float randAddon = uv.x * 1e-10;
    float rcpSampleCount = rcp(CAVITY_SAMPLES);

    UNITY_LOOP
    for (int i = 0; i < int(CAVITY_SAMPLES); i++)
    {
        float3 v_s1 = PickSamplePoint(uv, randAddon, i);
        v_s1 *= sqrt((i + 1.0) * rcpSampleCount ) * CAVITY_DISTANCE * 0.5;
        float3 vpos_s1 = vpos + v_s1;

        float3 spos_s1 = mul(camProj, vpos_s1);
        float2 uv_s1_01 = clamp((spos_s1.xy * rcp(vpos_s1.z) + 1.0) * 0.5, 0.0, 1.0);

        float depth_s1 = SampleAndGetLinearDepth(uv_s1_01);

        float3 vpos_s2 = ReconstructViewPos(uv_s1_01, depth_s1, p11_22, p13_31);
        float3 dir = vpos_s2 - vpos;
        float len = length(dir);
        float f_dot = dot(dir, normal);
        float f_cavities = f_dot - kBeta * depth;
        float f_edge = -f_dot - kBeta * depth;
        float f_bias = 0.05 * len + 0.0001;

        float attenuation = 1.0 / (len * (1.0 + len * len * CAVITY_ATTENUATION));

        if (f_cavities > -f_bias)
        {
            cavity += f_cavities * attenuation;
        }

        if (f_edge > f_bias)
        {
            edges += f_edge * attenuation;
        }
    }

    cavity *= 1.0 / CAVITY_SAMPLES; //CAVITY_DISTANCE;
    edges *= 1.0 / CAVITY_SAMPLES; //CAVITY_DISTANCE;

    cavity = PositivePow(cavity * rcpSampleCount, kContrast);
    edges = PositivePow(edges * rcpSampleCount, kContrast);

    cavity = clamp(cavity * CAVITY_VALLEY, 0.0, 1.0);
    edges = edges * CAVITY_RIDGE;
}

// Same output as the source: a light multiplier stored at a quarter (0.25 = unchanged), read back as value x 4.
float SSC(float2 uv)
{
    float cavity = 0.0, edges = 0.0, curvature = 0.0;

    #ifdef _TYPE_CURVATURE
    Curvature(uv, curvature);
    #endif

    #ifdef _TYPE_CAVITY
    Cavity(uv, cavity, edges);
    #endif

    return clamp((1.0 - cavity) * (1.0 + edges) * (1.0 + curvature), 0.0, 4.0) * 0.25;
}

#endif
