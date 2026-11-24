// Studio background: the HDRI cubemap seen from inside a big sphere, slightly blurred and darkened so the props stay
// in front. Not part of the bake.
Shader "GameTech/BNSky"
{
    Properties { _Cube("Cubemap", CUBE) = "white" {} _Mip("Mip", Float) = 1.5 _Exposure("Exposure", Float) = 0.6 }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Background" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZWrite Off Cull Front
            HLSLPROGRAM
            #pragma vertex V
            #pragma fragment F
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURECUBE(_Cube); SAMPLER(sampler_Cube);
            CBUFFER_START(UnityPerMaterial)
            float _Mip, _Exposure;
            CBUFFER_END
            struct A { float4 p : POSITION; };
            struct O { float4 p : SV_POSITION; float3 w : TEXCOORD0; };
            O V(A v) { O o; o.w = TransformObjectToWorld(v.p.xyz); o.p = TransformWorldToHClip(o.w); return o; }
            float4 F(O i) : SV_Target
            {
                float3 d = normalize(i.w - _WorldSpaceCameraPos);
                return float4(SAMPLE_TEXTURECUBE_LOD(_Cube, sampler_Cube, d, _Mip).rgb * _Exposure, 1);
            }
            ENDHLSL
        }
    }
}
