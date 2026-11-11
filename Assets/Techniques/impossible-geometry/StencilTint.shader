// Debug view: tints every pixel whose stencil value equals _StencilID. One full-viewport quad per ID, drawn last.
Shader "GameTech/StencilTint"
{
    Properties
    {
        _Color("Color", Color) = (1,0.1,0.7,0.55)
        [IntRange] _StencilID("Stencil ID", Range(0, 255)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+50" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off ZTest Always ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            Stencil { Ref [_StencilID] Comp Equal }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Color; float _StencilID;
            CBUFFER_END
            float4 Vert(float4 positionOS : POSITION) : SV_POSITION { return float4(positionOS.xy * 2, UNITY_NEAR_CLIP_VALUE, 1); }
            half4 Frag() : SV_Target { return _Color; }
            ENDHLSL
        }
    }
}
