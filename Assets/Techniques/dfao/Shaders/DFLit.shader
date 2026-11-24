// Mine: URP forward shader for the DFAO post. Per pixel it evaluates the source's two distance-field terms from the
// global field: ambient occlusion (steps along the normal, GDFCameraScreenAO's ambientOcclusion) and a sphere-traced
// soft shadow toward the sun (its softShadow). The source runs them in a full-screen pass over the depth buffer; here
// they run in the surface shader with the surface position and normal. Per-camera global _GMode:
// 0 shadow map, no AO | 1 DF shadow + DF AO | 2 DF shadow, no AO | 3 DF AO term only | 4 DF shadow term only.
Shader "GameTech/DFLit"
{
    Properties
    {
        _BaseColor("Colour", Color) = (0.6, 0.6, 0.6, 1)
        _Noise("Stone noise", Float) = 0.25
        _Smoothness("Smoothness", Range(0, 1)) = 0.2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "DFCommon.hlsl"

        CBUFFER_START(UnityPerMaterial)
        float4 _BaseColor; float _Noise; float _Smoothness;
        CBUFFER_END
        float _GMode;
        Texture3D _GDFTex; float4 _GDFMin, _GDFMax;
        float _AOStep, _AOSteps, _AOIntensity, _ShK, _ShSteps, _ShMax, _ShAcc;

        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
        struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };

        Varyings Vert(Attributes i)
        {
            Varyings o;
            o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
            o.normalWS = TransformObjectToWorldNormal(i.normalOS);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            return o;
        }

        float2 GDF(float3 p)
        {
            float3 uv = (p - _GDFMin.xyz) / (_GDFMax.xyz - _GDFMin.xyz);
            if (any(uv < 0) || any(uv > 1)) return 1e3;
            return _GDFTex.SampleLevel(gdf_sampler_linear_clamp, uv, 0).rg;
        }

        // the source's ambientOcclusion: compare the field with the distance walked along the normal
        float DFAO(float3 p, float3 n)
        {
            float ao = 0;
            for (int i = 1; i <= (int)_AOSteps; i++)
            {
                float dist = _AOStep * i;
                ao += max((dist - GDF(p + n * dist).r) / dist, 0.0);
            }
            return saturate(1.0 - ao * _AOIntensity);
        }

        // the source's softShadow: sphere trace toward the light, keep the smallest k * d / t. Changes: the step is
        // clamped to 2-25 cm and the closest approach between two samples is estimated (iq's 2018 variant), which
        // removes the banding that big steps give in the penumbra
        float DFShadow(float3 p, float3 L)
        {
            float res = 1.0, t = 0.08, ph = 1e10;
            for (int i = 0; i < (int)_ShSteps; i++)
            {
                float h = GDF(p + L * t).g;
                if (h < _ShAcc) return 0;
                // the estimate only holds while y < h (steps are clamped, so ph can be smaller than h)
                float y = h * h / (2.0 * ph);
                float e = (y < h && t - y > 0.01) ? _ShK * sqrt(h * h - y * y) / (t - y) : _ShK * h / t;
                res = min(res, e);
                ph = h;
                t += clamp(h, 0.02, 0.25);
                if (t > _ShMax) break;
            }
            res = saturate(res);
            return res * res * (3 - 2 * res);
        }

        float H3(float3 p) { p = frac(p * float3(0.1031, 0.1030, 0.0973)); p += dot(p, p.yxz + 33.33); return frac((p.x + p.y) * p.z); }
        float N3(float3 x)
        {
            float3 i = floor(x), f = frac(x); f = f * f * (3 - 2 * f);
            return lerp(lerp(lerp(H3(i), H3(i + float3(1, 0, 0)), f.x), lerp(H3(i + float3(0, 1, 0)), H3(i + float3(1, 1, 0)), f.x), f.y),
                        lerp(lerp(H3(i + float3(0, 0, 1)), H3(i + float3(1, 0, 1)), f.x), lerp(H3(i + float3(0, 1, 1)), H3(i + float3(1, 1, 1)), f.x), f.y), f.z);
        }

        float3 Shade(Varyings i)
        {
            float3 n = normalize(i.normalWS), ws = i.positionWS;
            Light L = GetMainLight(TransformWorldToShadowCoord(ws));
            float3 p = ws + n * 0.06;
            float m = _GMode;
            float ao = (m > 0.5 && m < 1.5) || (m > 2.5 && m < 3.5) ? DFAO(p, n) : 1;
            if (m > 2.5 && m < 3.5) return ao.xxx * 0.92;
            float ndl = saturate(dot(n, L.direction));
            float sh = m < 0.5 ? L.shadowAttenuation : (ndl > 0 ? DFShadow(p, L.direction) : 0);
            if (m > 3.5) return (sh * ndl * 0.85 + 0.07).xxx;

            float tex = N3(ws * 2.3) * 0.6 + N3(ws * 9.1) * 0.3 + N3(ws * 31.0) * 0.1;
            float3 albedo = _BaseColor.rgb * lerp(1 - _Noise, 1 + _Noise * 0.6, tex);
            float3 v = normalize(_WorldSpaceCameraPos - ws), h = normalize(L.direction + v);
            float spec = pow(saturate(dot(n, h)), exp2(10 * _Smoothness + 1)) * _Smoothness * 0.25;
            return (albedo + spec) * L.color * ndl * sh + albedo * SampleSH(n) * ao;
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            half4 Frag(Varyings i) : SV_Target { return half4(Shade(i), 1); }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex SVert
            #pragma fragment SFrag
            float3 _LightDirection;
            Varyings SVert(Attributes i)
            {
                Varyings o = Vert(i);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(o.positionWS, o.normalWS, _LightDirection));
            #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
            #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                o.positionCS = cs;
                return o;
            }
            half4 SFrag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment DFrag
            half4 DFrag(Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }
    }
}
