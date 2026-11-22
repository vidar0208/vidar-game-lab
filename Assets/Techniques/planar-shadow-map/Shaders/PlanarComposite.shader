// Full-screen display for the planar shadow post: left of _Split shows camera A's image (or, with _LeftMap = 1,
// the high-res planar shadow map itself over a darkened copy of the scene, to show it is aligned with the screen),
// right shows camera B's image.
Shader "GameTech/PlanarComposite"
{
    Properties { _Split("Split", Float) = 0.5 _LeftMap("Left map", Float) = 0 _Line("Line", Float) = 1 }
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
            TEXTURE2D(_TexA); TEXTURE2D(_TexB); TEXTURE2D_FLOAT(_PsmMapHi);
            SAMPLER(sampler_point_clamp);
            float _Split, _LeftMap, _Line;
            struct A { float4 p : POSITION; float2 uv : TEXCOORD0; };
            struct O { float4 p : SV_POSITION; float2 uv : TEXCOORD0; };
            O V(A i) { O o; o.p = TransformObjectToHClip(i.p.xyz); o.uv = i.uv; return o; }
            float4 F(O i) : SV_Target
            {
                float3 col;
                if (i.uv.x < _Split)
                {
                    if (_LeftMap > 0.5)
                    {
                        float h = SAMPLE_TEXTURE2D(_PsmMapHi, sampler_point_clamp, i.uv).r;
                        float3 scene = SAMPLE_TEXTURE2D(_TexB, sampler_point_clamp, i.uv).rgb * 0.22;
                        col = h < 999 ? lerp(float3(1.0, 0.92, 0.8), float3(0.95, 0.35, 0.1), saturate(h / 1.8)) : scene;
                    }
                    else col = SAMPLE_TEXTURE2D(_TexA, sampler_point_clamp, i.uv).rgb;
                }
                else col = SAMPLE_TEXTURE2D(_TexB, sampler_point_clamp, i.uv).rgb;
                float d = abs(i.uv.x - _Split) * 1920.0;
                col = lerp(col, float3(1, 1, 1), _Line * saturate(1.5 - d) * 0.85);
                return float4(col, 1);
            }
            ENDHLSL
        }
    }
}
