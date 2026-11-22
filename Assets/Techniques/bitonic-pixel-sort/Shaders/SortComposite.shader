// Shows the captured scene image and the pixel-sorted images side by side: left of _Split shows texture _LeftSel,
// right of it texture _RightSel (0 = unsorted, 1 = sort A, 2 = sort B), with a thin divider line.
Shader "GameTech/SortComposite"
{
    Properties { _Orig("Orig", 2D) = "black" {} _SortA("A", 2D) = "black" {} _SortB("B", 2D) = "black" {}
        _Split("Split", Float) = 0.5 _LeftSel("Left", Float) = 0 _RightSel("Right", Float) = 1 _Line("Line", Float) = 1 }
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
            TEXTURE2D(_Orig); TEXTURE2D(_SortA); TEXTURE2D(_SortB); SAMPLER(sampler_point_clamp);
            float _Split, _LeftSel, _RightSel, _Line;
            struct A { float4 p : POSITION; float2 uv : TEXCOORD0; };
            struct O { float4 p : SV_POSITION; float2 uv : TEXCOORD0; };
            O V(A i) { O o; o.p = TransformObjectToHClip(i.p.xyz); o.uv = i.uv; return o; }
            float3 Pick(float s, float2 uv)
            {
                float3 a = SAMPLE_TEXTURE2D(_Orig, sampler_point_clamp, uv).rgb;
                float3 b = SAMPLE_TEXTURE2D(_SortA, sampler_point_clamp, uv).rgb;
                float3 c = SAMPLE_TEXTURE2D(_SortB, sampler_point_clamp, uv).rgb;
                return s < 0.5 ? a : (s < 1.5 ? b : c);
            }
            float4 F(O i) : SV_Target
            {
                float3 col = i.uv.x < _Split ? Pick(_LeftSel, i.uv) : Pick(_RightSel, i.uv);
                float d = abs(i.uv.x - _Split) * 1920.0;
                col = lerp(col, float3(1, 1, 1), _Line * saturate(1.5 - d) * 0.85);
                return float4(col, 1);
            }
            ENDHLSL
        }
    }
}
