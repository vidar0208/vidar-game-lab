// Shared unlit HDR colour for lamps and other glowing parts (URP Lit's emission keyword does not survive the
// batch-mode scene build reliably). Writes depth, so it shows up in the depth texture too.
Shader "GameTech/Glow"
{
    Properties { [HDR] _BaseColor("Color", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor;
        CBUFFER_END
        float4 Vert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
        ENDHLSL
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag() : SV_Target { return half4(_BaseColor.rgb, 1); }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
