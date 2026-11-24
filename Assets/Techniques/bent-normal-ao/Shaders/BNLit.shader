// Image-based lighting from the studio cubemap with the baked atlas (rgb = bent normal in world space, a = AO).
// Per-camera global _GMode:
//   0  no occlusion: diffuse along the normal, reflection untouched
//   1  AO as a multiplier on both diffuse and reflection
//   2  bent normals, as in the source's Shader Implementation Example: diffuse sampled along the bent normal (x AO),
//      reflection x lerp(max(dot(R, bentNormal), 0), 1, AO)
//   3  the baked AO map, 4  the baked bent normal map (as colour)
// Specular uses the reflection vector at a roughness mip; diffuse uses the blurriest mip.
Shader "GameTech/BNLit"
{
    Properties
    {
        _BaseColor("Base colour", Color) = (1, 1, 1, 1)
        _Metallic("Metallic", Range(0, 1)) = 1
        _Smoothness("Smoothness", Range(0, 1)) = 0.95
        _Cube("Cubemap", CUBE) = "black" {}
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
            TEXTURE2D(_GBNAO); SAMPLER(sampler_linear_clamp);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor; float _Metallic, _Smoothness;
            CBUFFER_END
            float _GMode;
            struct A { float4 p : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; };
            struct O { float4 p : SV_POSITION; float3 w : TEXCOORD0; float3 n : TEXCOORD1; float2 uv : TEXCOORD2; };
            O V(A v) { O o; o.w = TransformObjectToWorld(v.p.xyz); o.n = TransformObjectToWorldNormal(v.n); o.p = TransformWorldToHClip(o.w); o.uv = v.uv; return o; }

            float4 F(O i) : SV_Target
            {
                float4 bnao = SAMPLE_TEXTURE2D_LOD(_GBNAO, sampler_linear_clamp, i.uv, 0);
                float3 bn = normalize(bnao.xyz); float ao = bnao.w;
                // map views: shown with their stored values (undo the display's linear-to-sRGB step)
                if (_GMode > 3.5) return float4(pow(bn * 0.5 + 0.5, 2.2), 1);
                if (_GMode > 2.5) return float4(pow(ao, 2.2).xxx, 1);

                float3 n = normalize(i.n), v = normalize(GetWorldSpaceViewDir(i.w)), r = reflect(-v, n);
                BRDFData brdf; half alpha = 1;
                InitializeBRDFData(_BaseColor.rgb, _Metallic, half3(0, 0, 0), _Smoothness, alpha, brdf);
                float mip = PerceptualRoughnessToMipmapLevel(1.0 - _Smoothness);
                float3 spec = SAMPLE_TEXTURECUBE_LOD(_Cube, sampler_Cube, r, mip).rgb;
                float3 diff;
                if (_GMode > 1.5)
                {
                    diff = SAMPLE_TEXTURECUBE_LOD(_Cube, sampler_Cube, bn, 7).rgb * ao;
                    float occ = lerp(max(dot(r, bn), 0), 1, ao);
                    spec *= occ;
                }
                else
                {
                    diff = SAMPLE_TEXTURECUBE_LOD(_Cube, sampler_Cube, n, 7).rgb;
                    if (_GMode > 0.5) { diff *= ao; spec *= ao; }
                }
                float fres = Pow4(1.0 - saturate(dot(n, v)));
                return float4(EnvironmentBRDF(brdf, diff, spec, fres), 1);
            }
            ENDHLSL
        }
    }
}
