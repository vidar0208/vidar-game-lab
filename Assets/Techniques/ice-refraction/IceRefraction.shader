// HLSL rewrite of the Ice shader graph in daniel-ilett/shaders-ice (MIT, Copyright (c) 2020 Daniel Ilett).
// Node for node:
//   uv = mesh UV * _IceTiling                                   (Tiling And Offset)
//   n  = Normal From Texture(_IceNormals, uv, offset 0.5, strength 8), a height map turned into a tangent normal
//   scene = Scene Color(screen position + n * _RefractStrength)  (the URP opaque texture, read with a shifted UV)
//   albedo = Fresnel Effect(power = 1 / _FresnelStrength) + scene * _IceColor
//   PBR output: albedo, normal = n, metallic 0.25, smoothness 0.8, alpha 1, transparent surface
// Property names and defaults are the source's. Added: _FresnelOn (layer breakdown) and depth write on.
Shader "GameTech/IceRefraction"
{
    Properties
    {
        _FresnelStrength("Fresnel Strength", Float) = 0.2
        [HDR] _IceColor("Ice Color", Color) = (0.115, 1.315, 2.0, 1)
        _IceNormals("Ice Normals (height map)", 2D) = "gray" {}
        _IceTiling("Ice Tiling", Vector) = (1, 1, 0, 0)
        _RefractStrength("Refract Strength", Float) = -0.02
        _FresnelOn("Fresnel on (added for the breakdown)", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

        TEXTURE2D(_IceNormals); SAMPLER(sampler_IceNormals);
        CBUFFER_START(UnityPerMaterial)
        float _FresnelStrength; half4 _IceColor; float4 _IceTiling; float _RefractStrength; float _FresnelOn;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float2 uv : TEXCOORD0; };
        struct Varyings
        {
            float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1;
            float4 tangentWS : TEXCOORD2; float2 uv : TEXCOORD3; float4 screenPos : TEXCOORD4;
        };

        Varyings Vert(Attributes i)
        {
            Varyings o;
            o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(i.normalOS);
            o.tangentWS = float4(TransformObjectToWorldDir(i.tangentOS.xyz), i.tangentOS.w * GetOddNegativeScale());
            o.uv = i.uv;
            o.screenPos = ComputeScreenPos(o.positionCS);
            return o;
        }

        // Shader Graph "Normal From Texture" node
        float3 NormalFromTexture(float2 uv, float offset, float strength)
        {
            offset = pow(offset, 3) * 0.1;
            float2 offsetU = float2(uv.x + offset, uv.y);
            float2 offsetV = float2(uv.x, uv.y + offset);
            float normalSample = SAMPLE_TEXTURE2D(_IceNormals, sampler_IceNormals, uv).r;
            float uSample = SAMPLE_TEXTURE2D(_IceNormals, sampler_IceNormals, offsetU).r;
            float vSample = SAMPLE_TEXTURE2D(_IceNormals, sampler_IceNormals, offsetV).r;
            float3 va = float3(1, 0, (uSample - normalSample) * strength);
            float3 vb = float3(0, 1, (vSample - normalSample) * strength);
            return normalize(cross(va, vb));
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv * _IceTiling.xy;
                float3 n = NormalFromTexture(uv, 0.5, 8.0);

                float2 screenUV = i.screenPos.xy / i.screenPos.w;
                float3 scene = SampleSceneColor(screenUV + n.xy * _RefractStrength);

                float3 normalWS = normalize(i.normalWS);
                float3 viewWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float fresnel = pow(1.0 - saturate(dot(normalWS, viewWS)), 1.0 / _FresnelStrength) * _FresnelOn;
                float3 albedo = fresnel + scene * _IceColor.rgb;

                float3 bitangent = cross(normalWS, i.tangentWS.xyz) * i.tangentWS.w;
                float3 shadingNormal = normalize(n.x * i.tangentWS.xyz + n.y * bitangent + n.z * normalWS);

                InputData inp = (InputData)0;
                inp.positionWS = i.positionWS;
                inp.normalWS = shadingNormal;
                inp.viewDirectionWS = viewWS;
                inp.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inp.bakedGI = SampleSH(shadingNormal);
                inp.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inp.shadowMask = half4(1, 1, 1, 1);
                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo; s.metallic = 0.25; s.smoothness = 0.8; s.occlusion = 1; s.alpha = 1;
                return UniversalFragmentPBR(inp, s);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
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
    }
}
