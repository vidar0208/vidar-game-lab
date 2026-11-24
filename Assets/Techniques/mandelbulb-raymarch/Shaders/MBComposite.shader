// Full-screen display for the Mandelbulb post: image A left of _Split, image B right of it. The images are rendered
// at 2x the output size; one bilinear tap at each output pixel centre averages 2 x 2 rendered pixels.
Shader "GameTech/MBComposite"
{
    Properties { _Split("Split", Float) = 0.5 _Line("Line", Float) = 1 }
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
            TEXTURE2D(_TexA); TEXTURE2D(_TexB); SAMPLER(sampler_linear_clamp);
            float _Split, _Line;
            struct A { float4 p : POSITION; float2 uv : TEXCOORD0; };
            struct O { float4 p : SV_POSITION; float2 uv : TEXCOORD0; };
            O V(A i) { O o; o.p = TransformObjectToHClip(i.p.xyz); o.uv = i.uv; return o; }
            float4 F(O i) : SV_Target
            {
                float3 col = i.uv.x < _Split ? SAMPLE_TEXTURE2D(_TexA, sampler_linear_clamp, i.uv).rgb : SAMPLE_TEXTURE2D(_TexB, sampler_linear_clamp, i.uv).rgb;
                float d = abs(i.uv.x - _Split) * 1920.0;
                col = lerp(col, float3(1, 1, 1), _Line * saturate(1.5 - d) * 0.85);
                return float4(col, 1);
            }
            ENDHLSL
        }
    }
}
