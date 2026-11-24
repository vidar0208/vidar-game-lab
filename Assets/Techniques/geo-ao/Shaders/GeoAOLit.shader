// Mine: small URP forward shader for the GeoAO post. Direct light: main light with its shadow map. Ambient: spherical
// harmonics times an occlusion term chosen by the per-camera global _GMode:
// 0 no AO, 1 GeoAO (the per-vertex value, read from a buffer by vertex id; the source reads a per-vertex texture through uv2), 2 URP SSAO (screen-space texture),
// 3 SSAO term only, 4 GeoAO term only. Has DepthOnly and DepthNormals passes so URP's SSAO sees these surfaces.
Shader "GameTech/GeoAOLit"
{
    Properties
    {
        _BaseColor("Colour", Color) = (0.6, 0.6, 0.6, 1)
        _Noise("Concrete noise", Float) = 0.25
        _Smoothness("Smoothness", Range(0, 1)) = 0.2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        CBUFFER_START(UnityPerMaterial)
        float4 _BaseColor; float _Noise; float _Smoothness;
        CBUFFER_END
        float _GMode, _GSSAOPow, _AOBase;
        StructuredBuffer<float> _GAOBuf;

        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; uint vid : SV_VertexID; };
        struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float ao : TEXCOORD2; };

        Varyings Vert(Attributes i)
        {
            Varyings o;
            o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
            o.normalWS = TransformObjectToWorldNormal(i.normalOS);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.ao = _GAOBuf[(uint)_AOBase + i.vid];
            return o;
        }

        float H3(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
        float N3(float3 x)
        {
            float3 i = floor(x), f = frac(x); f = f * f * (3 - 2 * f);
            return lerp(lerp(lerp(H3(i), H3(i + float3(1, 0, 0)), f.x), lerp(H3(i + float3(0, 1, 0)), H3(i + float3(1, 1, 0)), f.x), f.y),
                        lerp(lerp(H3(i + float3(0, 0, 1)), H3(i + float3(1, 0, 1)), f.x), lerp(H3(i + float3(0, 1, 1)), H3(i + float3(1, 1, 1)), f.x), f.y), f.z);
        }

        float3 Shade(Varyings i)
        {
            float3 n = normalize(i.normalWS);
            float3 ws = i.positionWS;
            float aoI = 1, aoD = 1;
            if (_GMode > 0.5 && _GMode < 1.5) aoI = i.ao;
            if (_GMode > 3.5) return i.ao.xxx * 0.92;
        #if defined(_SCREEN_SPACE_OCCLUSION)
            if (_GMode > 1.5 && _GMode < 3.5)
            {
                AmbientOcclusionFactor f = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.positionCS));
                aoI = pow(saturate(f.indirectAmbientOcclusion), _GSSAOPow); aoD = f.directAmbientOcclusion;
            }
        #endif
            if (_GMode > 2.5) return aoI.xxx * 0.92;

            float tex = N3(ws * 3.1) * 0.6 + N3(ws * 11.7) * 0.3 + N3(ws * 37.0) * 0.1;
            float3 albedo = _BaseColor.rgb * lerp(1 - _Noise, 1 + _Noise * 0.6, tex);
            float3 ambient = albedo * SampleSH(n) * aoI;

            Light L = GetMainLight(TransformWorldToShadowCoord(ws));
            float ndl = saturate(dot(n, L.direction));
            float3 v = normalize(_WorldSpaceCameraPos - ws);
            float3 h = normalize(L.direction + v);
            float spec = pow(saturate(dot(n, h)), exp2(10 * _Smoothness + 1)) * _Smoothness * 0.25;
            float3 direct = (albedo + spec) * L.color * ndl * L.shadowAttenuation * aoD;
            return direct + ambient;
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
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
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
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;
            Varyings SVert(Attributes i)
            {
                Varyings o = Vert(i);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 ld = normalize(_LightPosition - o.positionWS);
            #else
                float3 ld = _LightDirection;
            #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(o.positionWS, o.normalWS, ld));
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

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment NFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 NFrag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = saturate(PackNormalOctQuadEncode(n) * 0.5 + 0.5);
                return half4(PackFloat2To888(oct), 0);
            #else
                return half4(n, 0);
            #endif
            }
            ENDHLSL
        }
    }
}
