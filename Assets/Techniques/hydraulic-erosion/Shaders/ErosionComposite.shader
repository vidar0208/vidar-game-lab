// Full-screen display for the erosion post: camera A's image left of _Split, camera B's image right of it.
Shader "GameTech/ErosionComposite"
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
            TEXTURE2D(_TexA); TEXTURE2D(_TexB); SAMPLER(sampler_point_clamp);
            float _Split, _Line;
            struct A { float4 p : POSITION; float2 uv : TEXCOORD0; };
            struct O { float4 p : SV_POSITION; float2 uv : TEXCOORD0; };
            O V(A i) { O o; o.p = TransformObjectToHClip(i.p.xyz); o.uv = i.uv; return o; }
            float4 F(O i) : SV_Target
            {
                float3 col = i.uv.x < _Split ? SAMPLE_TEXTURE2D(_TexA, sampler_point_clamp, i.uv).rgb : SAMPLE_TEXTURE2D(_TexB, sampler_point_clamp, i.uv).rgb;
                float d = abs(i.uv.x - _Split) * 1920.0;
                col = lerp(col, float3(1, 1, 1), _Line * saturate(1.5 - d) * 0.85);
                return float4(col, 1);
            }
            ENDHLSL
        }
    }
}
