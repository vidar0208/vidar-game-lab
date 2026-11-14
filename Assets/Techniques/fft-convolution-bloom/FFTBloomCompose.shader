// Full-viewport quad for the display camera: up to three bands (_B0 | _B1 | _B2, split at _S1 / _S2).
// HDR bands go through ACES (the same AcesTonemap(unity_to_ACES()) URP uses); a band flagged in _LDR is already
// tonemapped by URP's own post-processing. A kernel preview sits in the top right corner of a band when _KOn is set.
// Dark strips behind the label rows.
Shader "Hidden/GT/FFTBloomCompose"
{
    Properties { _S1 ("Split 1", Float) = 1 _S2 ("Split 2", Float) = 1 }
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
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ACES.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            TEXTURE2D(_B0); TEXTURE2D(_B1); TEXTURE2D(_B2);
            TEXTURE2D(_K0); TEXTURE2D(_K1); TEXTURE2D(_K2);
            SamplerState gt_point_clamp_sampler, gt_linear_clamp_sampler;
            float _S1, _S2; float4 _LDR, _KOn;
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes a) { Varyings o; o.positionCS = float4(a.positionOS.xy * 2, UNITY_NEAR_CLIP_VALUE, 1); return o; }
            float Line(float coord, float split, float size)
            {
                float onScreen = step(0.001, split) * step(split, 0.999);
                return saturate(2.0 - abs(coord - split) * size) * onScreen;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(i.positionCS);
                int b = uv.x < _S1 ? 0 : (uv.x < _S2 ? 1 : 2);
                half3 c = b == 0 ? SAMPLE_TEXTURE2D(_B0, gt_point_clamp_sampler, uv).rgb
                        : b == 1 ? SAMPLE_TEXTURE2D(_B1, gt_point_clamp_sampler, uv).rgb
                        : SAMPLE_TEXTURE2D(_B2, gt_point_clamp_sampler, uv).rgb;
                if (_LDR[b] < 0.5) c = AcesTonemap(unity_to_ACES(max(c, 0)));

                // kernel preview: 0.075 of the width, square in pixels, top right of the band under the title strip
                float bandEnd = b == 0 ? min(_S1, 1) : (b == 1 ? min(_S2, 1) : 1);
                float w = 0.075, h = w * _ScaledScreenParams.x / _ScaledScreenParams.y;
                float2 p0 = float2(bandEnd - 0.012 - w, 0.885 - h);
                float2 q = (uv - p0) / float2(w, h);
                if (_KOn[b] > 0.5 && all(q > -0.02) && all(q < 1.02))
                {
                    half3 k = b == 0 ? SAMPLE_TEXTURE2D(_K0, gt_linear_clamp_sampler, q).rgb
                            : b == 1 ? SAMPLE_TEXTURE2D(_K1, gt_linear_clamp_sampler, q).rgb
                            : SAMPLE_TEXTURE2D(_K2, gt_linear_clamp_sampler, q).rgb;
                    c = (all(q > 0) && all(q < 1)) ? k : half3(0.6, 0.6, 0.6);
                }

                float sx = _ScaledScreenParams.x;
                c = lerp(c, half3(0.85, 0.85, 0.85), max(Line(uv.x, _S1, sx), Line(uv.x, _S2, sx)));
                if (uv.y < 0.11 || uv.y > 0.905) c *= 0.25;
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
