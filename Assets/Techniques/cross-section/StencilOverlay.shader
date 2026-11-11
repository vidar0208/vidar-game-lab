// Debug view: tints every pixel whose stencil value is 255. Drawn as a full-viewport quad after the scene.
Shader "GameTech/StencilOverlay"
{
    Properties { _Color("Color", Color) = (1,0.1,0.7,0.6) }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+20" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off ZTest Always ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            Stencil { Ref 255 Comp Equal }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            CBUFFER_END
            float4 Vert(float4 positionOS : POSITION) : SV_POSITION { return float4(positionOS.xy * 2, UNITY_NEAR_CLIP_VALUE, 1); }
            half4 Frag() : SV_Target { return _Color; }
            ENDHLSL
        }
    }
}
