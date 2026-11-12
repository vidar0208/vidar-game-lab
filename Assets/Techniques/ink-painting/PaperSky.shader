// Background: the paper texture in screen space on an inside-out sphere around the scene.
Shader "GameTech/InkPaperSky"
{
    Properties { _PaperTex ("Paper", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+10" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Cull Front ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_PaperTex); SAMPLER(sampler_linear_repeat);
            CBUFFER_START(UnityPerMaterial) float4 _PaperTex_ST; CBUFFER_END
            float4 vert(float4 p : POSITION) : SV_POSITION { return TransformObjectToHClip(p.xyz); }
            half4 frag(float4 pos : SV_POSITION) : SV_Target { return half4(SAMPLE_TEXTURE2D(_PaperTex, sampler_linear_repeat, pos.xy / 1024.0).rgb, 1); }
            ENDHLSL
        }
    }
}
