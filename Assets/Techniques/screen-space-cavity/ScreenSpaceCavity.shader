// Full-screen pass of the cavity port (source: Cavity.shader in malyawka/URP-ScreenSpaceCavity, BSD-3-Clause).
// Same keywords as the source (_TYPE_CURVATURE, _TYPE_CAVITY); drawn as one full-screen triangle into an R8 target.
Shader "Hidden/GT/ScreenSpaceCavity"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "ScreenSpaceCavity"
            HLSLPROGRAM
            #pragma multi_compile_local _ _TYPE_CURVATURE
            #pragma multi_compile_local _ _TYPE_CAVITY
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SSC.hlsl"

            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(uint id : SV_VertexID)
            {
                Varyings o;
                o.positionCS = GetFullScreenTriangleVertexPosition(id);
                return o;
            }

            float Frag(Varyings i) : SV_Target
            {
                return SSC(i.positionCS.xy * _SourceSize.zw);
            }
            ENDHLSL
        }
    }
}
