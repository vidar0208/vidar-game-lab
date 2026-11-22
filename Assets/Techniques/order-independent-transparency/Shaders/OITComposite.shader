// Full-screen display for the OIT post. Each half of the screen (split at _Split) shows one mode:
// 0 = URP's default transparent queue (ODT), 1 = weighted blended OIT, 2 = unweighted blended OIT,
// 3 = accumulation buffer view (sum(C a w) / sum(a w)), 4 = revealage buffer view (prod(1 - a)).
// Modes 1 and 2 use the source's WB_Blend.shader formula unchanged:
//   col = float4(accum.rgb / clamp(accum.a, 1e-4, 5e4), revealage); out = (1 - col.a) * col + col.a * background
Shader "GameTech/OITComposite"
{
    Properties { _Split("Split", Float) = 0.5 _LeftMode("Left", Float) = 0 _RightMode("Right", Float) = 1 _Line("Line", Float) = 1 }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex V
            #pragma fragment F
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_OdtTex); TEXTURE2D(_OpaqueTex); TEXTURE2D(_AccumW); TEXTURE2D(_AccumU); TEXTURE2D(_Reveal);
            SAMPLER(sampler_point_clamp);
            float _Split, _LeftMode, _RightMode, _Line;
            struct A { float4 p : POSITION; float2 uv : TEXCOORD0; };
            struct O { float4 p : SV_POSITION; float2 uv : TEXCOORD0; };
            O V(A i) { O o; o.p = TransformObjectToHClip(i.p.xyz); o.uv = i.uv; return o; }
            float3 Pick(float m, float2 uv)
            {
                if (m < 0.5) return SAMPLE_TEXTURE2D(_OdtTex, sampler_point_clamp, uv).rgb;
                float3 bg = SAMPLE_TEXTURE2D(_OpaqueTex, sampler_point_clamp, uv).rgb;
                float4 accum = m < 1.5 || m > 2.5 ? SAMPLE_TEXTURE2D(_AccumW, sampler_point_clamp, uv) : SAMPLE_TEXTURE2D(_AccumU, sampler_point_clamp, uv);
                float rev = SAMPLE_TEXTURE2D(_Reveal, sampler_point_clamp, uv).r;
                float4 col = float4(accum.rgb / clamp(accum.a, 1e-4, 5e4), rev);
                if (m > 3.5) return rev.xxx * 0.9 + 0.02;
                if (m > 2.5) return col.rgb * step(1e-4, accum.a) * 0.85;
                return (1.0 - col.a) * col.rgb + col.a * bg;
            }
            float4 F(O i) : SV_Target
            {
                float3 col = i.uv.x < _Split ? Pick(_LeftMode, i.uv) : Pick(_RightMode, i.uv);
                float d = abs(i.uv.x - _Split) * 1920.0;
                col = lerp(col, float3(1, 1, 1), _Line * saturate(1.5 - d) * 0.85);
                return float4(col, 1);
            }
            ENDHLSL
        }
    }
}
