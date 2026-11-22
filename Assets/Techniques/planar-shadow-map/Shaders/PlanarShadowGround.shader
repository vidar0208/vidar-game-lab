// Ground receiver for the planar shadow post. Lit with URP's BRDF (main light + ambient), and the main light's
// shadow term comes from one of: _Mode 0 none, 1 URP's own cascaded shadow map, 2 planar map (high res),
// 3 planar map (low res).
// Planar lookup after dreamfairy/Unity_URP_Planear_Shadowmap (MIT), ShadowReceiveShader.shader: slide the receiver
// point along the light to the ground plane, project it with the main camera to get the map UV, compare heights.
// PCF as the source's non-GL path: 4 taps at +-0.5 texel, each a 2x2 bilinear compare (the source gets that from a
// hardware comparison sampler; here it is done by hand because the map is a colour texture).
Shader "GameTech/PlanarShadowGround"
{
    Properties
    {
        _BaseMap("Base", 2D) = "white" {}
        _BaseColor("Color", Color) = (1, 1, 1, 1)
        _Smoothness("Smoothness", Range(0, 1)) = 0.3
        _Mode("Shadow mode", Float) = 2
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST; float4 _BaseColor; float _Smoothness; float _Mode;
            CBUFFER_END
            TEXTURE2D_FLOAT(_PsmMapHi); TEXTURE2D_FLOAT(_PsmMapLo);
            float4 _PsmSizeHi, _PsmSizeLo;      // xy = size, zw = 1 / size
            float4x4 _PsmUV; float4 _PsmLightDir; float _PsmPlaneY, _PsmBias;
            struct A { float4 p : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; };
            struct O { float4 p : SV_POSITION; float3 w : TEXCOORD0; float3 n : TEXCOORD1; float2 uv : TEXCOORD2; };
            O V(A v) { O o; o.w = TransformObjectToWorld(v.p.xyz); o.p = TransformWorldToHClip(o.w); o.n = TransformObjectToWorldNormal(v.n); o.uv = TRANSFORM_TEX(v.uv, _BaseMap); return o; }

            // 1 = caster above this receiver point on that texel's light ray
            float Occ(bool hi, int2 t, float4 size, float h)
            {
                t = clamp(t, int2(0, 0), int2(size.xy) - 1);
                float s = hi ? LOAD_TEXTURE2D(_PsmMapHi, t).r : LOAD_TEXTURE2D(_PsmMapLo, t).r;
                return (s < 999.0 && s > h - _PsmBias) ? 1.0 : 0.0;
            }
            float Bilinear(bool hi, float2 uv, float4 size, float h)
            {
                float2 tp = uv * size.xy - 0.5; int2 b = int2(floor(tp)); float2 f = frac(tp);
                float a = Occ(hi, b, size, h), c = Occ(hi, b + int2(1, 0), size, h);
                float d = Occ(hi, b + int2(0, 1), size, h), e = Occ(hi, b + int2(1, 1), size, h);
                return lerp(lerp(a, c, f.x), lerp(d, e, f.x), f.y);
            }
            float PlanarLit(float3 w, bool hi)
            {
                float3 L = normalize(_PsmLightDir.xyz);
                float h = w.y - _PsmPlaneY;
                float3 g = float3(w.x - L.x * h / L.y, _PsmPlaneY, w.z - L.z * h / L.y);
                float4 c = mul(_PsmUV, float4(g, 1));
                float2 uv = c.xy / c.w * 0.5 + 0.5;
                if (any(uv < 0) || any(uv > 1) || c.w <= 0) return 1;
                float4 size = hi ? _PsmSizeHi : _PsmSizeLo; float occ = 0;
                [unroll] for (int k = 0; k < 4; k++)
                {
                    float2 o = float2(k & 1 ? 0.5 : -0.5, k & 2 ? 0.5 : -0.5) * size.zw;
                    occ += Bilinear(hi, uv + o, size, h);
                }
                return 1 - occ * 0.25;
            }
            float4 F(O i) : SV_Target
            {
                float3 n = normalize(i.n), v = GetWorldSpaceNormalizeViewDir(i.w);
                float3 alb = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                BRDFData brdf; float a = 1; InitializeBRDFData(alb, 0, 0, _Smoothness, a, brdf);
                Light l = GetMainLight(TransformWorldToShadowCoord(i.w), i.w, 1);
                float sh = _Mode < 0.5 ? 1 : _Mode < 1.5 ? l.shadowAttenuation : PlanarLit(i.w, _Mode < 2.5);
                float3 col = GlobalIllumination(brdf, SampleSH(n), 1, i.w, n, v);
                col += LightingPhysicallyBased(brdf, l.color, l.direction, l.distanceAttenuation * sh, n, v);
                return float4(col, 1);
            }
            ENDHLSL
        }
    }
}
