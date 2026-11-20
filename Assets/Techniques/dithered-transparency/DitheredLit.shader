// URP rewrite of "Dithered Transparent/Dithered" + "Hidden/Dithered Transparent/Shadow"
// (gkjohnson/unity-dithered-transparency-shader, MIT). Same idea as the source: the material stays in the opaque
// queue and writes depth; a pixel is kept or discarded by comparing alpha with a screen-space threshold
// (4x4 matrix or a tiled texture). Lighting is URP PBR instead of the source's vertex Lambert.
// The shadow caster runs the same clip on the shadow map's pixel position. (In the source's shadow pass
// the screen position is never written in the vertex shader; here SV_Position is used directly.)
Shader "GameTech/DitheredLit"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Color", Color) = (1,1,1,1)
        _Metallic("Metallic", Range(0,1)) = 0
        _Smoothness("Smoothness", Range(0,1)) = 0.4
        _Fade("Fade", Range(0,1)) = 1
        _Wind("Wind", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "DitherCommon.hlsl"
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
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            half4 Frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                half4 c = BaseColor(i);
                DitherClip(i.positionCS.xy, c.a);
                return half4(Lit(i, c, IS_FRONT_VFACE(face, 1.0, -1.0)), 1);
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
            half4 SFrag(Varyings i) : SV_Target
            {
                // 0 = an ordinary opaque shadow caster: alpha is ignored, the whole surface casts
                if (_GT_ShadowDither > 0.5) DitherClip(i.positionCS.xy, BaseColor(i).a);
                return 0;
            }
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
            half4 DFrag(Varyings i) : SV_Target { DitherClip(i.positionCS.xy, BaseColor(i).a); return i.positionCS.z; }
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
                DitherClip(i.positionCS.xy, BaseColor(i).a);
                return half4(normalize(i.normalWS) * IS_FRONT_VFACE(face, 1.0, -1.0), 0);
            }
            ENDHLSL
        }
    }
}
