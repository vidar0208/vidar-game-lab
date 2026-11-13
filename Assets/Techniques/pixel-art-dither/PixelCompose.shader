// Full-viewport quad for the display camera.
// _Mode 0: up to four side-by-side bands of the same frame (_A | _B | _C | _D, split at _S1, _S2, _S3), thin light lines between them.
// _Mode 1: two stacked zoomed views of one region (_A on top, _B below), _Zoom times the full-frame size, centred at _ZoomC.
// Dark strips behind the label rows keep text readable over dithered pictures. With _Chips = 1 the three palettes are drawn as
// swatches at the right end of each of three bands. Point sampling everywhere, so a 480 x 270 texture is shown as exact 4 x 4 blocks.
Shader "Hidden/GT/PixelCompose"
{
    Properties
    {
        _A ("A", 2D) = "black" {} _B ("B", 2D) = "black" {} _C ("C", 2D) = "black" {} _D ("D", 2D) = "black" {}
        _P0 ("Palette 0", 2D) = "black" {} _P1 ("Palette 1", 2D) = "black" {} _P2 ("Palette 2", 2D) = "black" {}
        _S1 ("Split 1", Float) = 1 _S2 ("Split 2", Float) = 1 _S3 ("Split 3", Float) = 1
        _Mode ("Mode", Float) = 0 _Zoom ("Zoom", Float) = 2 _ZoomC ("Zoom centre", Vector) = (0.5, 0.5, 0, 0)
        _Chips ("Chips", Float) = 0 _PW ("Palette width", Float) = 6
    }
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
            TEXTURE2D(_A); TEXTURE2D(_B); TEXTURE2D(_C); TEXTURE2D(_D);
            TEXTURE2D(_P0); TEXTURE2D(_P1); TEXTURE2D(_P2);
            SamplerState gt_point_clamp_sampler;
            float _S1, _S2, _S3, _Mode, _Zoom, _Chips, _PW;
            float4 _ZoomC;
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes a) { Varyings o; o.positionCS = float4(a.positionOS.xy * 2, UNITY_NEAR_CLIP_VALUE, 1); return o; }

            float Line(float coord, float split, float size)
            {
                float onScreen = step(0.001, split) * step(split, 0.999);
                return saturate(2.0 - abs(coord - split) * size) * onScreen;
            }

            half3 Chip(TEXTURE2D_PARAM(p, s), float2 uv, float bandEnd, half3 c)
            {
                float cw = 30.0 / 1920.0;
                float x1 = min(bandEnd, 1.0) - 0.012, x0 = x1 - _PW * cw;
                if (uv.x < x0 || uv.x >= x1 || uv.y < 0.028 || uv.y > 0.085) return c;
                float k = (uv.x - x0) / cw;
                float edge = step(frac(k), 2.0 / 30.0);
                half3 col = SAMPLE_TEXTURE2D(p, s, float2((floor(k) + 0.5) / _PW, 0.5)).rgb;
                return lerp(col, half3(0.02, 0.02, 0.02), edge);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(i.positionCS);
                half3 c;
                if (_Mode < 0.5)
                {
                    c = uv.x < _S1 ? SAMPLE_TEXTURE2D(_A, gt_point_clamp_sampler, uv).rgb
                      : uv.x < _S2 ? SAMPLE_TEXTURE2D(_B, gt_point_clamp_sampler, uv).rgb
                      : uv.x < _S3 ? SAMPLE_TEXTURE2D(_C, gt_point_clamp_sampler, uv).rgb
                      : SAMPLE_TEXTURE2D(_D, gt_point_clamp_sampler, uv).rgb;
                    float sx = _ScaledScreenParams.x;
                    float l = max(max(Line(uv.x, _S1, sx), Line(uv.x, _S2, sx)), Line(uv.x, _S3, sx));
                    c = lerp(c, half3(0.85, 0.85, 0.85), l);
                    if (uv.y < 0.11 || uv.y > 0.905) c *= 0.18;
                    if (_Chips > 0.5)
                    {
                        c = Chip(TEXTURE2D_ARGS(_P0, gt_point_clamp_sampler), uv, _S1, c);
                        c = Chip(TEXTURE2D_ARGS(_P1, gt_point_clamp_sampler), uv, _S2, c);
                        c = Chip(TEXTURE2D_ARGS(_P2, gt_point_clamp_sampler), uv, 1.0, c);
                    }
                }
                else
                {
                    bool top = uv.y > 0.5;
                    float v = top ? (uv.y - 0.5) * 2 : uv.y * 2;
                    float2 s = _ZoomC.xy + float2((uv.x - 0.5) / _Zoom, (v - 0.5) * 0.5 / _Zoom);
                    c = top ? SAMPLE_TEXTURE2D(_A, gt_point_clamp_sampler, s).rgb : SAMPLE_TEXTURE2D(_B, gt_point_clamp_sampler, s).rgb;
                    c = lerp(c, half3(0.85, 0.85, 0.85), Line(uv.y, 0.5, _ScaledScreenParams.y));
                    if (uv.y < 0.075 || (uv.y > 0.503 && uv.y < 0.575) || uv.y > 0.905) c *= 0.18;
                }
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
