// Comparison shader: the same surface and lighting as GameTech/DitheredLit, drawn the usual way for transparency:
// transparent queue, alpha blending, no depth write. Unity sorts transparent objects back to front by object
// distance; triangles inside one mesh are drawn in index order. It casts no shadow of its own (the scene's shadows
// come from a shared shadow-only copy so both halves of a split frame get the same shadows).
Shader "GameTech/BlendLit"
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
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }

        HLSLINCLUDE
        #include "DitherCommon.hlsl"
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Cull Off ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            half4 Frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                half4 c = BaseColor(i);
                return half4(Lit(i, c, IS_FRONT_VFACE(face, 1.0, -1.0)), c.a);
            }
            ENDHLSL
        }
    }
}
