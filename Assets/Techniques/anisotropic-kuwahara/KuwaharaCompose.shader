// Full-viewport quad for the display camera: left of _Split shows _A, right shows _B, with a thin white line between them.
Shader "Hidden/GT/KuwaharaCompose"
{
    Properties { _A ("Left", 2D) = "black" {} _B ("Right", 2D) = "black" {} _Split ("Split", Float) = 1 }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Overlay" }
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_A); TEXTURE2D(_B);
            SamplerState gt_linear_clamp_sampler;
            float _Split;
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes a) { Varyings o; o.positionCS = float4(a.positionOS.xy * 2, UNITY_NEAR_CLIP_VALUE, 1); return o; }
            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(i.positionCS);
                half3 c = uv.x < _Split ? SAMPLE_TEXTURE2D(_A, gt_linear_clamp_sampler, uv).rgb : SAMPLE_TEXTURE2D(_B, gt_linear_clamp_sampler, uv).rgb;
                float px = abs(uv.x - _Split) * _ScaledScreenParams.x;
                float onScreen = step(0.001, _Split) * step(_Split, 0.999);
                c = lerp(c, half3(1, 1, 1), saturate(1.5 - px) * onScreen);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
