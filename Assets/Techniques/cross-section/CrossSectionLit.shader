// URP rewrite of CrossSection/OnePlaneBSP + GenericThreePlanesBSP (Dandarawy/Unity3DCrossSectionShader, BSD-3-Clause).
// Same logic as the source: discard fragments on the positive side of the plane (or inside the corner made by
// three planes), front faces write stencil 0, back faces write stencil 255 and are drawn in one flat colour.
// Plane data comes from global vectors instead of per-material properties.
Shader "GameTech/CrossSectionLit"
{
    Properties
    {
        _BaseColor("Color", Color) = (1,1,1,1)
        _Metallic("Metallic", Range(0,1)) = 0
        _Smoothness("Smoothness", Range(0,1)) = 0.5
        _CrossColor("Cross Section Color", Color) = (0.75,0.28,0.18,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor; half _Metallic; half _Smoothness; half4 _CrossColor;
        CBUFFER_END
        float4 _CS_PlanePos[3];
        float4 _CS_PlaneNrm[3];
        float _CS_Count;      // 0 = no cut, 1 = one plane, 3 = three planes
        float _CS_BackMode;   // 0 = back faces lit (clip only), 1 = back faces flat cross colour

        // checkVisability() of the source: true means the fragment is cut away
        bool CsCut(float3 wp)
        {
            float d0 = dot(wp - _CS_PlanePos[0].xyz, _CS_PlaneNrm[0].xyz);
            if (_CS_Count < 0.5) return false;
            if (_CS_Count < 1.5) return d0 > 0;
            float d1 = dot(wp - _CS_PlanePos[1].xyz, _CS_PlaneNrm[1].xyz);
            float d2 = dot(wp - _CS_PlanePos[2].xyz, _CS_PlaneNrm[2].xyz);
            return d0 > 0 && d1 > 0 && d2 > 0;
        }

        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
        struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };

        Varyings Vert(Attributes i)
        {
            Varyings o;
            o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(i.normalOS);
            return o;
        }

        half4 LitColor(Varyings i, float facing)
        {
            InputData d = (InputData)0;
            d.positionWS = i.positionWS;
            d.normalWS = normalize(i.normalWS) * facing;
            d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
            d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
            d.bakedGI = SampleSH(d.normalWS);
            d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
            d.shadowMask = half4(1, 1, 1, 1);
            SurfaceData s = (SurfaceData)0;
            s.albedo = _BaseColor.rgb; s.metallic = _Metallic; s.smoothness = _Smoothness; s.occlusion = 1; s.alpha = 1;
            return UniversalFragmentPBR(d, s);
        }
        ENDHLSL

        // Front faces: lit, stencil -> 0 (PassFront Zero in the source)
        Pass
        {
            Name "Front"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            Stencil { Ref 255 Comp Always Pass Zero }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            half4 Frag(Varyings i) : SV_Target
            {
                if (CsCut(i.positionWS)) discard;
                return LitColor(i, 1);
            }
            ENDHLSL
        }

        // Back faces: stencil -> 255 (PassBack Replace), drawn in the flat cross-section colour
        Pass
        {
            Name "Back"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            Stencil { Ref 255 Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            half4 Frag(Varyings i) : SV_Target
            {
                if (CsCut(i.positionWS)) discard;
                if (_CS_BackMode > 0.5) return half4(_CrossColor.rgb, 1);
                return LitColor(i, -1);      // "clip only" comparison: the inside of the shell, lit
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
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;
            Varyings SVert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
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
            half4 SFrag(Varyings i) : SV_Target { if (CsCut(i.positionWS)) discard; return 0; }
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
            half4 DFrag(Varyings i) : SV_Target { if (CsCut(i.positionWS)) discard; return i.positionCS.z; }
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
                if (CsCut(i.positionWS)) discard;
                float3 n = normalize(i.normalWS) * (IS_FRONT_VFACE(face, 1.0, -1.0));
                return half4(n, 0);
            }
            ENDHLSL
        }
    }
}
