// The ordinary surface under the rain. Plays the part of IRCSS/MatrixVFX "Unlit/StanUnlitDissolve": it discards
// its pixels with the same screen-space test, split_from_midle(screen x, transition, 1), so the surface opens from
// the middle of the screen while the transition value runs from 1 to 2. Changed: lit with URP PBR lighting and
// shadows instead of an unlit texture.
Shader "GameTech/MatrixBaseLit"
{
    Properties
    {
        _BaseMap("Albedo", 2D) = "white" {}
        _BaseColor("Color", Color) = (1,1,1,1)
        _Metallic("Metallic", Range(0,1)) = 0
        _Smoothness("Smoothness", Range(0,1)) = 0.3
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "MatrixCommon.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST; half4 _BaseColor; half _Metallic; half _Smoothness;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
        struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; float4 screenPos : TEXCOORD3; };

        Varyings Vert(Attributes i)
        {
            Varyings o;
            o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(i.normalOS);
            o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
            o.screenPos = ComputeScreenPos(o.positionCS);
            return o;
        }

        half4 Surface(Varyings i)
        {
            float2 screenPos = i.screenPos.xy / i.screenPos.w;
            if (split_from_midle(screenPos.x, _Global_Transition_value, 1.0) == 1.0) discard;
            half4 a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
            clip(a.a - 0.3);
            return a;
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _FORWARD_PLUS
            half4 Frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                half3 albedo = Surface(i).rgb;
                float front = IS_FRONT_VFACE(face, 1.0, -1.0);
                InputData inp = (InputData)0;
                inp.positionWS = i.positionWS;
                inp.normalWS = normalize(i.normalWS) * front;
                inp.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inp.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inp.bakedGI = SampleSH(inp.normalWS);
                inp.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inp.shadowMask = half4(1, 1, 1, 1);
                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo; s.metallic = _Metallic; s.smoothness = _Smoothness; s.occlusion = 1; s.alpha = 1;
                return UniversalFragmentPBR(inp, s);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Off
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
            // the screen-space test has no meaning in the light's view: only the texture alpha clips here
            half4 SFrag(Varyings i) : SV_Target { clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a - 0.3); return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DFrag
            half4 DFrag(Varyings i) : SV_Target { Surface(i); return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment NFrag
            half4 NFrag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                Surface(i);
                return half4(normalize(i.normalWS) * IS_FRONT_VFACE(face, 1.0, -1.0), 0);
            }
            ENDHLSL
        }
    }
}
