// URP port of SlightlyMad/VolumetricLights (BSD-3-Clause, Copyright (c) 2016, Michal Skalsky):
// VolumetricLight.shader (ray march, Mie scattering, ray-cone / ray-sphere setup, light attenuation with shadows),
// BilateralBlur.shader (min/max depth downsample, depth-aware Gaussian blur, bilateral upsample) and BlitAdd.shader.
// Changes: drawn as full-screen passes per light instead of light-volume meshes; shadows come from URP's shadow
// maps; spot cookie replaced by a smooth cone edge; the source's falloff texture replaced by its usual curve.
Shader "Hidden/GameTech/VolumetricLightURP"
{
    HLSLINCLUDE
    #define _MAIN_LIGHT_SHADOWS
    #define _ADDITIONAL_LIGHT_SHADOWS
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

    // per light (set before each draw)
    float4 _VL_Pos;        // xyz position, w range
    float4 _VL_Axis;       // xyz spot axis or directional light forward
    float4 _VL_Spot;       // x cos(outer half angle), y cos(inner half angle), z additional-light shadow index, w shadows on
    float4 _VL_Color;      // light colour * intensity
    float4 _VolumetricLight; // x scattering, y extinction
    float4 _MieG;          // (1 - g^2, 1 + g^2, 2g, 1 / 4pi)
    float4 _NoiseData;     // x scale, y intensity, z offset, w on
    float4 _NoiseVelocity;
    float _SampleCount;
    float _MaxRayLength;
    TEXTURE3D(_GT_NoiseTex); SAMPLER(sampler_GT_NoiseTex);
    TEXTURE2D(_GT_DitherTex);
    // passes 3..6
    TEXTURE2D(_VL_Src); TEXTURE2D(_VL_HalfDepth);
    float4 _VL_HalfTexel;  // 1/w, 1/h, w, h of the half-res buffers
    // sampler_PointClamp / sampler_LinearClamp come from URP's global samplers

    float GetDensity(float3 wpos)
    {
        float density = 1;
        if (_NoiseData.w > 0.5)
        {
            float noise = SAMPLE_TEXTURE3D_LOD(_GT_NoiseTex, sampler_GT_NoiseTex, frac(wpos * _NoiseData.x + float3(_Time.y * _NoiseVelocity.x, 0, _Time.y * _NoiseVelocity.y)), 0).r;
            noise = saturate(noise - _NoiseData.z) * _NoiseData.y;
            density = saturate(noise);
        }
        return density;
    }

    float MieScattering(float cosAngle, float4 g)
    {
        return g.w * (g.x / (pow(g.y - g.z * cosAngle, 1.5)));
    }

    // light attenuation at a point on the ray, with the light's shadow map (the source's GetLightAttenuation)
    float GetLightAttenuation(float3 wpos, int type)
    {
        if (type == 2)
        {
            if (_VL_Spot.w < 0.5) return 1;
            return MainLightRealtimeShadow(TransformWorldToShadowCoord(wpos));
        }
        float3 tolight = _VL_Pos.xyz - wpos;
        float x = dot(tolight, tolight) / (_VL_Pos.w * _VL_Pos.w);
        float atten = saturate(1.0 / (1.0 + 25.0 * x)) * saturate((1.0 - x) * 5.0);   // built-in pipeline falloff curve
        float3 lightDir = normalize(tolight);
        if (type == 0) atten *= smoothstep(_VL_Spot.x, _VL_Spot.y, dot(-lightDir, _VL_Axis.xyz));
        if (_VL_Spot.w > 0.5) atten *= AdditionalLightRealtimeShadow((int)_VL_Spot.z, wpos, lightDir);
        return atten;
    }

    float4 RayMarch(float2 screenPos, float3 rayStart, float3 rayDir, float rayLength, int type)
    {
        float2 interleavedPos = fmod(floor(screenPos.xy), 8.0);
        float offset = LOAD_TEXTURE2D(_GT_DitherTex, int2(interleavedPos)).r;

        int stepCount = (int)_SampleCount;
        float stepSize = rayLength / stepCount;
        float3 step = rayDir * stepSize;
        float3 currentPosition = rayStart + step * offset;
        float4 vlight = 0;
        float cosAngle;
        float extinction;
        if (type == 2) { extinction = 0; cosAngle = dot(_VL_Axis.xyz, -rayDir); }
        else extinction = length(_WorldSpaceCameraPos - currentPosition) * _VolumetricLight.y * 0.5;

        [loop]
        for (int i = 0; i < stepCount; ++i)
        {
            float atten = GetLightAttenuation(currentPosition, type);
            float density = GetDensity(currentPosition);
            float scattering = _VolumetricLight.x * stepSize * density;
            extinction += _VolumetricLight.y * stepSize * density;
            float4 light = atten * scattering * exp(-extinction);
            if (type != 2)
            {
                float3 tolight = normalize(currentPosition - _VL_Pos.xyz);
                cosAngle = dot(tolight, -rayDir);
                light *= MieScattering(cosAngle, _MieG);
            }
            vlight += light;
            currentPosition += step;
        }
        if (type == 2) vlight *= MieScattering(cosAngle, _MieG);
        vlight *= _VL_Color;
        vlight = max(0, vlight);
        vlight.w = type == 2 ? exp(-extinction) : 0;
        return vlight;
    }

    bool InSpot(float3 p)
    {
        float3 v = p - _VL_Pos.xyz;
        float c = dot(v, _VL_Axis.xyz);
        return c > 0 && c < _VL_Pos.w && c * c >= _VL_Spot.x * _VL_Spot.x * dot(v, v);
    }

    // [start, end] of the ray inside the light's volume, clipped to [0, tMax]
    float2 LightInterval(float3 ro, float3 rd, float tMax, int type)
    {
        if (type == 2) return float2(0, min(tMax, _MaxRayLength));
        float3 oc = ro - _VL_Pos.xyz;
        float b = dot(rd, oc), c = dot(oc, oc) - _VL_Pos.w * _VL_Pos.w, h = b * b - c;
        if (h <= 0) return float2(1, 0);
        h = sqrt(h);
        float s0 = max(-b - h, 0), s1 = min(-b + h, tMax);
        if (s1 <= s0 || type == 1) return float2(s0, s1);
        // spot: the source's RayConeIntersect quadratic, then keep the part of [s0, s1] inside the cone (convex, one segment)
        float3 axis = _VL_Axis.xyz; float cos2 = _VL_Spot.x * _VL_Spot.x;
        float a = dot(rd, axis), cc = dot(oc, axis), d = dot(oc, rd), e = dot(oc, oc);
        float A = a * a - cos2, B = 2 * (cc * a - d * cos2), C = cc * cc - e * cos2;
        float D = B * B - 4 * A * C;
        float r1 = s0, r2 = s0;
        if (D > 0 && abs(A) > 1e-6) { D = sqrt(D); r1 = (-B - D) / (2 * A); r2 = (-B + D) / (2 * A); if (r1 > r2) { float t = r1; r1 = r2; r2 = t; } }
        float tp = abs(a) > 1e-6 ? (_VL_Pos.w - cc) / a : s0;   // cap plane at the range
        float t[5] = { s0, clamp(r1, s0, s1), clamp(r2, s0, s1), clamp(tp, s0, s1), s1 };
        // tiny sort network for t[1..3]
        if (t[1] > t[2]) { float q = t[1]; t[1] = t[2]; t[2] = q; }
        if (t[2] > t[3]) { float q = t[2]; t[2] = t[3]; t[3] = q; }
        if (t[1] > t[2]) { float q = t[1]; t[1] = t[2]; t[2] = q; }
        float lo = 1e9, hi = -1e9;
        [unroll] for (int k = 0; k < 4; k++)
        {
            if (t[k + 1] > t[k] && InSpot(ro + rd * (0.5 * (t[k] + t[k + 1])))) { lo = min(lo, t[k]); hi = max(hi, t[k + 1]); }
        }
        return float2(lo, hi);
    }

    float4 FragLight(Varyings input, int type)
    {
        float2 uv = input.texcoord;
        float raw = SampleSceneDepth(uv);
        float3 wpos = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
        float3 ro = _WorldSpaceCameraPos;
        float3 rd = wpos - ro; float tMax = length(rd); rd /= tMax;
#if UNITY_REVERSED_Z
        if (raw <= 0.0) tMax = 1e5;
#else
        if (raw >= 1.0) tMax = 1e5;
#endif
        float2 seg = LightInterval(ro, rd, tMax, type);
        if (seg.y <= seg.x) return float4(0, 0, 0, type == 2 ? 1 : 0);
        return RayMarch(input.positionCS.xy, ro + rd * seg.x, rd, seg.y - seg.x, type);
    }

    // ---- BilateralBlur.shader ----
    #define UPSAMPLE_DEPTH_THRESHOLD 1.5f
    #define BLUR_DEPTH_FACTOR 0.5
    #define GAUSS_BLUR_DEVIATION 1.5
    #define HALF_RES_BLUR_KERNEL_SIZE 5

    float FragDownsampleDepth(Varyings input) : SV_Target
    {
        // min/max depth in a chessboard pattern (DOWNSAMPLE_DEPTH_MODE 2)
        float2 full = _VL_HalfTexel.xy * 0.5;
        float2 uv00 = input.texcoord - 0.5 * full;
        float4 depth;
        depth.x = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_PointClamp, uv00).r;
        depth.y = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_PointClamp, uv00 + float2(0, full.y)).r;
        depth.z = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_PointClamp, uv00 + float2(full.x, 0)).r;
        depth.w = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_PointClamp, uv00 + full).r;
        float minDepth = min(min(depth.x, depth.y), min(depth.z, depth.w));
        float maxDepth = max(max(depth.x, depth.y), max(depth.z, depth.w));
        int2 position = int2(input.positionCS.xy) % 2;
        int index = position.x + position.y;
        return index == 1 ? minDepth : maxDepth;
    }

    float GaussianWeight(float offset, float deviation)
    {
        float weight = 1.0f / sqrt(2.0f * PI * deviation * deviation);
        weight *= exp(-(offset * offset) / (2.0f * deviation * deviation));
        return weight;
    }

    float4 BilateralBlur(Varyings input, float2 direction)
    {
        const int kernelRadius = HALF_RES_BLUR_KERNEL_SIZE;
        const float deviation = kernelRadius / GAUSS_BLUR_DEVIATION;
        float2 uv = input.texcoord;
        float4 centerColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv);
        float3 color = centerColor.xyz;
        float centerDepth = LinearEyeDepth(SAMPLE_TEXTURE2D(_VL_HalfDepth, sampler_PointClamp, uv).r, _ZBufferParams);
        float weight = GaussianWeight(0, deviation);
        color *= weight;
        float weightSum = weight;
        [unroll] for (int i = -kernelRadius; i <= kernelRadius; i += 1)
        {
            if (i == 0) continue;
            float2 o = direction * i * _VL_HalfTexel.xy;
            float3 sampleColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, uv + o).rgb;
            float sampleDepth = LinearEyeDepth(SAMPLE_TEXTURE2D(_VL_HalfDepth, sampler_PointClamp, uv + o).r, _ZBufferParams);
            float dFactor = abs(centerDepth - sampleDepth) * BLUR_DEPTH_FACTOR;
            float w = exp(-(dFactor * dFactor));
            weight = GaussianWeight(i, deviation) * w;
            color += weight * sampleColor;
            weightSum += weight;
        }
        color /= weightSum;
        return float4(color, centerColor.w);
    }

    float4 BilateralUpsample(float2 uv)
    {
        float2 uv00 = uv - 0.5 * _VL_HalfTexel.xy;
        float2 uv10 = uv00 + float2(_VL_HalfTexel.x, 0), uv01 = uv00 + float2(0, _VL_HalfTexel.y), uv11 = uv00 + _VL_HalfTexel.xy;
        float4 highResDepth = LinearEyeDepth(SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_PointClamp, uv).r, _ZBufferParams).xxxx;
        float4 lowResDepth;
        lowResDepth[0] = LinearEyeDepth(SAMPLE_TEXTURE2D(_VL_HalfDepth, sampler_PointClamp, uv00).r, _ZBufferParams);
        lowResDepth[1] = LinearEyeDepth(SAMPLE_TEXTURE2D(_VL_HalfDepth, sampler_PointClamp, uv10).r, _ZBufferParams);
        lowResDepth[2] = LinearEyeDepth(SAMPLE_TEXTURE2D(_VL_HalfDepth, sampler_PointClamp, uv01).r, _ZBufferParams);
        lowResDepth[3] = LinearEyeDepth(SAMPLE_TEXTURE2D(_VL_HalfDepth, sampler_PointClamp, uv11).r, _ZBufferParams);
        float4 depthDiff = abs(lowResDepth - highResDepth);
        float accumDiff = dot(depthDiff, float4(1, 1, 1, 1));
        [branch] if (accumDiff < UPSAMPLE_DEPTH_THRESHOLD) return SAMPLE_TEXTURE2D(_VL_Src, sampler_LinearClamp, uv);
        float minDepthDiff = depthDiff[0]; float2 nearestUv = uv00;
        if (depthDiff[1] < minDepthDiff) { nearestUv = uv10; minDepthDiff = depthDiff[1]; }
        if (depthDiff[2] < minDepthDiff) { nearestUv = uv01; minDepthDiff = depthDiff[2]; }
        if (depthDiff[3] < minDepthDiff) { nearestUv = uv11; minDepthDiff = depthDiff[3]; }
        return SAMPLE_TEXTURE2D(_VL_Src, sampler_PointClamp, nearestUv);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass { Name "SpotPoint" Blend One One
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment F
            float4 F(Varyings i) : SV_Target { return FragLight(i, (int)round(_VL_Axis.w)); }   // 0 spot, 1 point
            ENDHLSL }

        Pass { Name "Directional" Blend One One, One Zero
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment F
            float4 F(Varyings i) : SV_Target { return FragLight(i, 2); }
            ENDHLSL }

        Pass { Name "DownsampleDepth" Blend One Zero
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDownsampleDepth
            ENDHLSL }

        Pass { Name "BlurH" Blend One Zero
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment F
            float4 F(Varyings i) : SV_Target { return BilateralBlur(i, float2(1, 0)); }
            ENDHLSL }

        Pass { Name "BlurV" Blend One Zero
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment F
            float4 F(Varyings i) : SV_Target { return BilateralBlur(i, float2(0, 1)); }
            ENDHLSL }

        Pass { Name "UpsampleAdd" Blend One Zero
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment F
            // BlitAdd: scene * volume.a + volume.rgb, with the volume bilaterally upsampled from half resolution
            float4 F(Varyings i) : SV_Target
            {
                float4 vl = BilateralUpsample(i.texcoord);
                float4 scene = SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, i.texcoord);
                return float4(scene.rgb * vl.w + vl.rgb, scene.a);
            }
            ENDHLSL }
    }
}
