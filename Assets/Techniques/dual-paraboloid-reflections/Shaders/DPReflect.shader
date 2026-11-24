// Image-based lighting from one environment, read through one of two representations (global _GMode per camera):
//   0  the cubemap (6 faces, hardware cube filtering, its own mips)
//   1  the dual paraboloid atlas baked from that cubemap (front/back halves, 8 LOD tiles), sampled like the
//      source's texDualParaboloidlod: uv = dir.xy / (1 + |dir.z|), LOD blended between the two nearest tiles
// Specular uses the reflection vector at a roughness-based mip; diffuse uses the blurriest level along the normal.
// _GDebug tints each pixel by the part of the map it reads (cube face, or front/back paraboloid).
Shader "GameTech/DPReflect"
{
    Properties
    {
        _BaseColor("Base colour", Color) = (1, 1, 1, 1)
        _Metallic("Metallic", Range(0, 1)) = 1
        _Smoothness("Smoothness", Range(0, 1)) = 0.95
        _Cube("Cubemap", CUBE) = "black" {}
        _DP("Dual paraboloid atlas", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex V
            #pragma fragment F
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURECUBE(_Cube); SAMPLER(sampler_Cube);
            TEXTURE2D(_DP); SAMPLER(sampler_linear_clamp);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor; float _Metallic, _Smoothness;
            CBUFFER_END
            float _GMode, _GDebug;
            struct A { float4 p : POSITION; float3 n : NORMAL; };
            struct O { float4 p : SV_POSITION; float3 w : TEXCOORD0; float3 n : TEXCOORD1; };
            O V(A v) { O o; o.w = TransformObjectToWorld(v.p.xyz); o.n = TransformObjectToWorldNormal(v.n); o.p = TransformWorldToHClip(o.w); return o; }

            float3 DPTile(float2 xy, float lod)
            {
                float2 off = float2(floor(fmod(lod, 2.0)) * 0.5, floor(lod * 0.5) * 0.25);
                return SAMPLE_TEXTURE2D_LOD(_DP, sampler_linear_clamp, off + xy, 0).rgb;
            }
            float3 DPSample(float3 d, float lod)
            {
                float2 frontUV = d.xy / (d.z + 1.0) * 0.5 + 0.5;
                float2 backUV = d.xy / (1.0 - d.z) * 0.5 + 0.5;
                float flagZ = step(0.0, d.z);
                float2 xy = ((frontUV + float2(1.0, 0.0)) * flagZ + backUV * (1.0 - flagZ)) * 0.25;
                lod = clamp(lod, 0.0, 7.0);
                float l1 = floor(lod), l2 = min(ceil(lod), 7.0);
                return lerp(DPTile(xy, l1), DPTile(xy, l2), lod - l1);
            }
            float3 Env(float3 d, float mip)
            {
                return _GMode < 0.5 ? SAMPLE_TEXTURECUBE_LOD(_Cube, sampler_Cube, d, mip).rgb : DPSample(d, mip);
            }
            float3 Tint(float3 d)
            {
                if (_GMode > 0.5) return d.z >= 0.0 ? float3(1.0, 0.45, 0.2) : float3(0.2, 0.5, 1.0);
                float3 a = abs(d);
                if (a.x >= a.y && a.x >= a.z) return d.x > 0 ? float3(1, 0.3, 0.3) : float3(0.3, 1, 1);
                if (a.y >= a.z) return d.y > 0 ? float3(0.4, 1, 0.4) : float3(1, 0.4, 1);
                return d.z > 0 ? float3(0.4, 0.5, 1) : float3(1, 1, 0.35);
            }
            float4 F(O i) : SV_Target
            {
                float3 n = normalize(i.n), v = normalize(GetWorldSpaceViewDir(i.w)), r = reflect(-v, n);
                BRDFData brdf; half alpha = 1;
                InitializeBRDFData(_BaseColor.rgb, _Metallic, half3(0, 0, 0), _Smoothness, alpha, brdf);
                float mip = PerceptualRoughnessToMipmapLevel(1.0 - _Smoothness);
                float3 spec = Env(r, mip), diff = Env(n, 7.0);
                float fres = Pow4(1.0 - saturate(dot(n, v)));
                float3 col = EnvironmentBRDF(brdf, diff, spec, fres);
                if (_GDebug > 0.5) col = lerp(col, Tint(r) * (0.35 + Luminance(col)), 0.6);
                return float4(col, 1);
            }
            ENDHLSL
        }
    }
}
