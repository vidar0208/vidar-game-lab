// Mine: small URP forward shader that receives capsule occlusion, the way the source package is meant to be used
// (include CapsuleOcclusion.hlsl, call GetCapsuleOcclusion with world position, normal, screen uv and linear depth).
// Direct light: main light with shadow map. Ambient: spherical harmonics, multiplied by the capsule occlusion.
// _GT_COView (per camera): 0 shaded, 1 ambient only, 2 the source's debug colour. _Character marks the figure's
// materials so a camera can switch occlusion on the figure itself (_GT_COChar).
Shader "GameTech/CapsuleLit"
{
    Properties
    {
        _BaseColor("Colour", Color) = (0.6, 0.6, 0.6, 1)
        _Smoothness("Smoothness", Range(0, 1)) = 0.3
        _Character("Is character", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "../CapsuleOcclusion/CapsuleOcclusion.hlsl"

        CBUFFER_START(UnityPerMaterial)
        float4 _BaseColor; float _Smoothness; float _Character;
        CBUFFER_END
        float _GT_COView, _GT_COChar, _GT_COFar;
        float4x4 _GT_COViewProj;

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

        void ClusterCoords(float3 ws, out float2 uv, out float lin01)
        {
            float4 c = mul(_GT_COViewProj, float4(ws, 1));
            uv = c.xy / c.w * 0.5 + 0.5;
            float eye = dot(ws - _WorldSpaceCameraPos, -UNITY_MATRIX_V[2].xyz);
            lin01 = eye / max(_GT_COFar, 1e-3);
        }

        float3 Shade(Varyings i)
        {
            float3 n = normalize(i.normalWS);
            float3 ws = i.positionWS;
            float2 uv; float lin01; ClusterCoords(ws, uv, lin01);
            if (_GT_COView > 1.5) return GetCapsuleOcclusionDebugColor(ws, n, uv, lin01);

            float ao = (_Character > 0.5 && _GT_COChar < 0.5) ? 1.0 : GetCapsuleOcclusion(ws, n, uv, lin01);
            float3 albedo = _BaseColor.rgb;
            float3 ambient = albedo * SampleSH(n) * ao;
            if (_GT_COView > 0.5) return ambient;

            Light L = GetMainLight(TransformWorldToShadowCoord(ws));
            float ndl = saturate(dot(n, L.direction));
            float3 v = normalize(_WorldSpaceCameraPos - ws);
            float3 h = normalize(L.direction + v);
            float spec = pow(saturate(dot(n, h)), exp2(10 * _Smoothness + 1)) * _Smoothness * 0.3;
            float3 direct = (albedo + spec) * L.color * ndl * L.shadowAttenuation;
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
            half4 NFrag(Varyings i) : SV_Target { return half4(normalize(i.normalWS), 0); }
            ENDHLSL
        }
    }
}
