// Full-viewport quad for the display camera. Up to three side-by-side views (_A | _B | _C, split at _Split and _Split2),
// or with _Vertical = 1 two stacked views (_A on top, _B below). Thin dark lines mark the splits.
// Point sampling, so a small texture is shown as an exact pixel zoom.
Shader "Hidden/GT/HalftoneCompose"
{
    Properties { _A ("A", 2D) = "black" {} _B ("B", 2D) = "black" {} _C ("C", 2D) = "black" {} _Split ("Split", Float) = 1 _Split2 ("Split 2", Float) = 1 _Vertical ("Vertical", Float) = 0 }
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
            TEXTURE2D(_A); TEXTURE2D(_B); TEXTURE2D(_C);
            SamplerState gt_point_clamp_sampler;
            float _Split, _Split2, _Vertical;
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes a) { Varyings o; o.positionCS = float4(a.positionOS.xy * 2, UNITY_NEAR_CLIP_VALUE, 1); return o; }
            float Line(float coord, float split, float size)
            {
                float onScreen = step(0.001, split) * step(split, 0.999);
                return saturate(2.5 - abs(coord - split) * size) * onScreen;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(i.positionCS);
                half3 c;
                float l;
                if (_Vertical > 0.5)
                {
                    c = uv.y > 0.5 ? SAMPLE_TEXTURE2D(_A, gt_point_clamp_sampler, uv).rgb : SAMPLE_TEXTURE2D(_B, gt_point_clamp_sampler, uv).rgb;
                    l = Line(uv.y, 0.5, _ScaledScreenParams.y);
                }
                else
                {
                    c = uv.x < _Split ? SAMPLE_TEXTURE2D(_A, gt_point_clamp_sampler, uv).rgb
                      : uv.x < _Split2 ? SAMPLE_TEXTURE2D(_B, gt_point_clamp_sampler, uv).rgb
                      : SAMPLE_TEXTURE2D(_C, gt_point_clamp_sampler, uv).rgb;
                    l = max(Line(uv.x, _Split, _ScaledScreenParams.x), Line(uv.x, _Split2, _ScaledScreenParams.x));
                }
                c = lerp(c, half3(0.08, 0.08, 0.08), l);   // dark line: the halftone paper is white
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
