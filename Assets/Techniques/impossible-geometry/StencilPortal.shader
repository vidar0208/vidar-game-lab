// The portal mask of daniel-ilett/shaders-impossible-geom (Assets/Shaders/StencilGeom.shader, MIT), same render state:
// no colour (Blend Zero One), no depth write, stencil reference replaced wherever the quad passes the depth test.
// Added: a queue one step after ordinary opaque geometry, an explicit empty HLSL program and a debug tint pass switch.
Shader "GameTech/StencilPortal"
{
    Properties
    {
        [IntRange] _StencilID("Stencil ID", Range(0, 255)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+1" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend Zero One
            ZWrite Off
            Cull Back
            Stencil { Ref [_StencilID] Comp Always Pass Replace Fail Keep }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float _StencilID;
            CBUFFER_END
            float4 Vert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half4 Frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
