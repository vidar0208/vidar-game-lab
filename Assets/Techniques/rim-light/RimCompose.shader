// Full-viewport quad for the display camera: up to three bands (_B0 | _B1 | _B2, split at _S1 / _S2). Each band reads
// its frame shifted by _Off (x per band), so a band can show the centre of its frame. Thin light lines at the splits,
// dark strips behind the label rows.
Shader "Hidden/GT/RimCompose"
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
            TEXTURE2D(_B0); TEXTURE2D(_B1); TEXTURE2D(_B2);
            SamplerState gt_point_clamp_sampler;
            float _S1, _S2;
            float4 _Off;
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
                half3 c = uv.x < _S1 ? SAMPLE_TEXTURE2D(_B0, gt_point_clamp_sampler, uv + float2(_Off.x, 0)).rgb
                        : uv.x < _S2 ? SAMPLE_TEXTURE2D(_B1, gt_point_clamp_sampler, uv + float2(_Off.y, 0)).rgb
                        : SAMPLE_TEXTURE2D(_B2, gt_point_clamp_sampler, uv + float2(_Off.z, 0)).rgb;
                float sx = _ScaledScreenParams.x;
                c = lerp(c, half3(0.85, 0.85, 0.85), max(Line(uv.x, _S1, sx), Line(uv.x, _S2, sx)));
                if (uv.y < 0.11 || uv.y > 0.905) c *= 0.25;
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
