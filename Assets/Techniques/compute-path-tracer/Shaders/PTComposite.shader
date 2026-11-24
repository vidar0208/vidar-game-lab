// Full-screen display for the path tracing post: image A left of _Split, image B right of it. Accumulated images
// are sums of samples; _ScaleA/_ScaleB divide them by the sample count (the source's Display.shader divides by Frame).
Shader "GameTech/PTComposite"
{
    Properties { _Split("Split", Float) = 0.5 _Line("Line", Float) = 1 _ScaleA("Scale A", Float) = 1 _ScaleB("Scale B", Float) = 1 }
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
            TEXTURE2D(_TexA); TEXTURE2D(_TexB); SAMPLER(sampler_point_clamp);
            float _Split, _Line, _ScaleA, _ScaleB;
            struct A { float4 p : POSITION; float2 uv : TEXCOORD0; };
            struct O { float4 p : SV_POSITION; float2 uv : TEXCOORD0; };
            O V(A i) { O o; o.p = TransformObjectToHClip(i.p.xyz); o.uv = i.uv; return o; }
            float4 F(O i) : SV_Target
            {
                float3 col = i.uv.x < _Split ? SAMPLE_TEXTURE2D(_TexA, sampler_point_clamp, i.uv).rgb * _ScaleA : SAMPLE_TEXTURE2D(_TexB, sampler_point_clamp, i.uv).rgb * _ScaleB;
                float d = abs(i.uv.x - _Split) * 1920.0;
                col = lerp(col, float3(1, 1, 1), _Line * saturate(1.5 - d) * 0.85);
                return float4(col, 1);
            }
            ENDHLSL
        }
    }
}
