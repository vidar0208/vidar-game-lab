// Full-viewport quad for the display camera: 2 or 3 square simulation panels on a dark background (_N),
// cross-faded with the 3D stage render (_Fade = 1 shows the stage).
Shader "Hidden/GT/FluidCompose"
{
    Properties { _Stage ("Stage", 2D) = "black" {} _P0 ("P0", 2D) = "black" {} _P1 ("P1", 2D) = "black" {} _P2 ("P2", 2D) = "black" {} _N ("Panels", Float) = 2 _Fade ("Fade", Float) = 0 }
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
            TEXTURE2D(_Stage); TEXTURE2D(_P0); TEXTURE2D(_P1); TEXTURE2D(_P2);
            SamplerState gt_linear_clamp_sampler;
            float _N, _Fade;
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes a) { Varyings o; o.positionCS = float4(a.positionOS.xy * 2, UNITY_NEAR_CLIP_VALUE, 1); return o; }

            // panel layout in pixels of a 1920 x 1080 frame, scaled to the real size
            bool Panel(float2 px, float x0, float y0, float size, out float2 uv, out float edge)
            {
                uv = (px - float2(x0, y0)) / size;
                float2 d = min(uv, 1 - uv) * size;
                edge = min(d.x, d.y);
                return all(uv >= 0) && all(uv <= 1);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float2 px = suv * float2(1920, 1080);
                half3 c = lerp(half3(0.004, 0.005, 0.008), half3(0.012, 0.013, 0.02), suv.y);
                float2 uv; float edge;
                if (_N < 2.5)
                {
                    if (Panel(px, 100, 170, 830, uv, edge)) c = SAMPLE_TEXTURE2D(_P0, gt_linear_clamp_sampler, uv).rgb;
                    else if (Panel(px, 990, 170, 830, uv, edge)) c = SAMPLE_TEXTURE2D(_P1, gt_linear_clamp_sampler, uv).rgb;
                    else edge = 99;
                }
                else
                {
                    if (Panel(px, 30, 245, 590, uv, edge)) c = SAMPLE_TEXTURE2D(_P0, gt_linear_clamp_sampler, uv).rgb;
                    else if (Panel(px, 665, 245, 590, uv, edge)) c = SAMPLE_TEXTURE2D(_P1, gt_linear_clamp_sampler, uv).rgb;
                    else if (Panel(px, 1300, 245, 590, uv, edge)) c = SAMPLE_TEXTURE2D(_P2, gt_linear_clamp_sampler, uv).rgb;
                    else edge = 99;
                }
                c = lerp(c, half3(0.25, 0.26, 0.3), saturate(1.5 - edge));
                half3 stage = SAMPLE_TEXTURE2D(_Stage, gt_linear_clamp_sampler, suv).rgb;
                return half4(lerp(c, stage, saturate(_Fade)), 1);
            }
            ENDHLSL
        }
    }
}
