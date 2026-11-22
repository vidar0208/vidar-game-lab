// Writes linear camera-space depth of the opaque scene into an RFloat target (with its own depth buffer), so the
// OIT accumulate and revealage passes can skip glass hidden behind opaque objects. Stands in for the shared depth
// buffer the source gets from SetTargetBuffers(accum, opaqueTex.depthBuffer) in the built-in pipeline.
Shader "GameTech/OITEyeDepth"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="OITDepth" }
            ZWrite On ZTest LEqual Cull Back
            HLSLPROGRAM
            #pragma vertex V
            #pragma fragment F
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4x4 _OitV, _OitVP;
            struct A { float4 p : POSITION; };
            struct O { float4 p : SV_POSITION; float z : TEXCOORD0; };
            O V(A v) { O o; float3 w = TransformObjectToWorld(v.p.xyz); o.p = mul(_OitVP, float4(w, 1)); o.z = -mul(_OitV, float4(w, 1)).z; return o; }
            float4 F(O i) : SV_Target { return i.z.xxxx; }
            ENDHLSL
        }
    }
}
