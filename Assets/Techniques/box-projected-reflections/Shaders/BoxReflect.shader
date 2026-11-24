// Lit surface whose environment reflection comes from one baked reflection probe cubemap, sampled three ways
// (global _GMode, set by the director before each camera renders):
//   0  plain cubemap: the reflection vector is used as-is, as if the room were infinitely far away
//   1  Unity's default box projection: the ray is intersected with the probe's box first (UnityBoxProjection.cginc)
//   2  improved: box projection plus the source's "approximated" contact hardening: the hit distance lowers the
//      cubemap mip, so reflections are sharper where the reflected wall is close (clamped, as in the source)
// Box-projection and contact-hardening math follow frostbone25/Unity-Improved-Box-Projected-Reflections (MIT).
// Direct light, shadows and diffuse use URP's own BRDF functions; only the probe sample is custom.
Shader "GameTech/BoxReflect"
{
    Properties
    {
        _BaseColor("Base colour", Color) = (1, 1, 1, 1)
        _BaseMap("Base map", 2D) = "white" {}
        _Metallic("Metallic", Range(0, 1)) = 0
        _Smoothness("Smoothness", Range(0, 1)) = 0.85
        _Cube("Probe cubemap", CUBE) = "black" {}
        _ReflBoost("Reflection scale", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
        float4 _BaseColor; float4 _BaseMap_ST; float _Metallic, _Smoothness, _ReflBoost;
        CBUFFER_END
        ENDHLSL
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex V
            #pragma fragment F
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURECUBE(_Cube); SAMPLER(sampler_Cube);
            float4 _GProbePos, _GBoxMin, _GBoxMax; float _GMode, _GDebugMip;

            struct A { float4 p : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; };
            struct O { float4 p : SV_POSITION; float3 w : TEXCOORD0; float3 n : TEXCOORD1; float2 uv : TEXCOORD2; };
            O V(A v) { O o; o.w = TransformObjectToWorld(v.p.xyz); o.n = TransformObjectToWorldNormal(v.n); o.p = TransformWorldToHClip(o.w); o.uv = TRANSFORM_TEX(v.uv, _BaseMap); return o; }

            // UnityBoxProjectedCubemapDirectionDefault from the source, with the hit distance as an output
            float3 BoxProject(float3 refl, float3 pos, out float dist)
            {
                float3 nrdir = normalize(refl);
                float3 rbmax = (_GBoxMax.xyz - pos) / nrdir;
                float3 rbmin = (_GBoxMin.xyz - pos) / nrdir;
                float3 rbminmax = (nrdir > 0.0) ? rbmax : rbmin;
                dist = min(min(rbminmax.x, rbminmax.y), rbminmax.z);
                return (pos - _GProbePos.xyz) + nrdir * dist;
            }

            float3 Heat(float x) { x = saturate(x); return saturate(float3(x * 2.0 - 0.6, 1.2 - abs(x * 2.2 - 1.1), 1.0 - x * 2.4)); }

            float4 F(O i) : SV_Target
            {
                float3 n = normalize(i.n);
                float3 v = normalize(GetWorldSpaceViewDir(i.w));
                float3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                BRDFData brdf; half alpha = 1;
                InitializeBRDFData(albedo, _Metallic, half3(0, 0, 0), _Smoothness, alpha, brdf);

                float3 r = reflect(-v, n);
                float perceptualRoughness = 1.0 - _Smoothness;
                float mipOriginal = PerceptualRoughnessToMipmapLevel(perceptualRoughness);
                float mip = mipOriginal; float3 dir = r; float dist = 0;
                if (_GMode > 0.5)
                {
                    dir = BoxProject(r, i.w, dist);
                    if (_GMode > 1.5)
                    {
                        // contact hardening, "approximated" path of the source, with its clamp on
                        float d = clamp(dist, 0.0, UNITY_SPECCUBE_LOD_STEPS);
                        mip = lerp(0.0, mipOriginal, d / UNITY_SPECCUBE_LOD_STEPS);
                    }
                }
                float3 env = SAMPLE_TEXTURECUBE_LOD(_Cube, sampler_Cube, dir, mip).rgb * _ReflBoost;
                if (_GDebugMip > 0.5) return float4(Heat(mip / UNITY_SPECCUBE_LOD_STEPS) * 0.9, 1);

                float fres = Pow4(1.0 - saturate(dot(n, v)));
                float3 col = EnvironmentBRDF(brdf, SampleSH(n), env, fres);
                Light L = GetMainLight(TransformWorldToShadowCoord(i.w));
                col += LightingPhysicallyBased(brdf, L, n, v);
                return float4(col, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster" Tags { "LightMode"="ShadowCaster" }
            ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma vertex V
            #pragma fragment F
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            struct A { float4 p : POSITION; float3 n : NORMAL; };
            float4 V(A v) : SV_POSITION
            {
                float3 w = TransformObjectToWorld(v.p.xyz), n = TransformObjectToWorldNormal(v.n);
                float4 c = TransformWorldToHClip(ApplyShadowBias(w, n, _LightDirection));
                #if UNITY_REVERSED_Z
                c.z = min(c.z, UNITY_NEAR_CLIP_VALUE);
                #else
                c.z = max(c.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return c;
            }
            float4 F() : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly" Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex V
            #pragma fragment F
            struct A { float4 p : POSITION; };
            float4 V(A v) : SV_POSITION { return TransformObjectToHClip(v.p.xyz); }
            float4 F() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
