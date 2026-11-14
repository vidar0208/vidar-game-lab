// Debug view of the per-object shadow atlas. The atlas is a shadow-map texture, read here only through depth
// comparisons: 16 comparisons at evenly spaced depths give the stored depth in 16 steps. Slice outlines come from
// the same offset/extent the resolve uses.
Shader "Hidden/GT/POSAtlasView"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D_SHADOW(_PerObjectShadowAtlas); SAMPLER_CMP(sampler_PerObjectShadowAtlas);
            float4 _Slices[4];
            float4 _SliceColours[4];
            float _SliceCount, _Invert;
            float4 _AtlasSize;
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes a) { Varyings o; o.positionCS = TransformObjectToHClip(a.positionOS.xyz); o.uv = a.uv; return o; }
            half4 frag(Varyings i) : SV_Target
            {
                float v = 0;
                [unroll] for (int k = 0; k < 16; k++)
                    v += SAMPLE_TEXTURE2D_SHADOW(_PerObjectShadowAtlas, sampler_PerObjectShadowAtlas, float3(i.uv, (k + 0.5) / 16.0));
                v /= 16.0;
                if (_Invert > 0.5) v = 1 - v;
                half3 c = half3(0.05, 0.05, 0.06);
                float2 px = i.uv * _AtlasSize.xy;
                for (int s = 0; s < 4; s++)
                {
                    if (s >= (int)_SliceCount) break;
                    float2 lo = _Slices[s].xy * _AtlasSize.xy, hi = (_Slices[s].xy + _Slices[s].zw) * _AtlasSize.xy;
                    if (px.x >= lo.x && px.x < hi.x && px.y >= lo.y && px.y < hi.y)
                    {
                        c = lerp(half3(0.16, 0.17, 0.2), half3(0.95, 0.95, 0.95), v);
                        float e = min(min(px.x - lo.x, hi.x - px.x), min(px.y - lo.y, hi.y - px.y));
                        if (e < 4) c = _SliceColours[s].rgb;
                    }
                }
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
