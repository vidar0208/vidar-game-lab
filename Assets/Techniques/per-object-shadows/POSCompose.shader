// Full-viewport quad for the display camera: up to two bands (_B0 | _B1, split at _S1; with _Crop each half shows the
// centre half of its frame), band 0 optionally shown as
// grey from its red channel (the R8 shadow mask), an optional atlas panel (_Atlas in _AtlasRect), thin light lines
// at the split, dark strips behind the label rows.
Shader "Hidden/GT/POSCompose"
{
    Properties { _S1 ("Split", Float) = 1 }
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
            TEXTURE2D(_B0); TEXTURE2D(_B1); TEXTURE2D(_Atlas);
            SamplerState gt_point_clamp_sampler, gt_linear_clamp_sampler;
            float _S1, _Gray0, _AtlasOn, _Crop;
            float4 _AtlasRect;
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes a) { Varyings o; o.positionCS = float4(a.positionOS.xy * 2, UNITY_NEAR_CLIP_VALUE, 1); return o; }
            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(i.positionCS);
                half3 c;
                if (uv.x < _S1)
                {
                    half4 b = SAMPLE_TEXTURE2D(_B0, gt_point_clamp_sampler, uv + float2(_Crop * 0.25, 0));
                    c = _Gray0 > 0.5 ? b.rrr : b.rgb;
                }
                else c = SAMPLE_TEXTURE2D(_B1, gt_point_clamp_sampler, uv - float2(_Crop * 0.25, 0)).rgb;
                float sx = _ScaledScreenParams.x;
                float onScreen = step(0.001, _S1) * step(_S1, 0.999);
                c = lerp(c, half3(0.85, 0.85, 0.85), saturate(2.0 - abs(uv.x - _S1) * sx) * onScreen);
                if (_AtlasOn > 0.5)
                {
                    float2 a = (uv - _AtlasRect.xy) / _AtlasRect.zw;
                    if (all(a >= 0) && all(a <= 1)) c = SAMPLE_TEXTURE2D(_Atlas, gt_linear_clamp_sampler, a).rgb;
                    else if (all(a >= -0.004) && all(a <= 1.004)) c = half3(0.85, 0.85, 0.85);
                }
                if (uv.y < 0.11 || uv.y > 0.905) c *= 0.25;
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
