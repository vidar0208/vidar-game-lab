// Bake step 1 (the source's BakeCamera + BNAO_Depth_2Sided): one orthographic depth view of every prop from one
// sample direction, both faces drawn. Meshes are in world space, so the vertex shader only applies this view's
// matrix. The colour target (RFloat) gets the distance along the view direction, which the compute pass compares.
Shader "GameTech/BNDepth"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma vertex V
            #pragma fragment F
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4x4 _GDepthVP; float4 _GDepthOrigin, _GDepthFwd;
            struct A { float4 p : POSITION; };
            struct O { float4 p : SV_POSITION; float d : TEXCOORD0; };
            O V(A v) { O o; o.p = mul(_GDepthVP, float4(v.p.xyz, 1)); o.d = dot(v.p.xyz - _GDepthOrigin.xyz, _GDepthFwd.xyz); return o; }
            float4 F(O i) : SV_Target { return float4(i.d, 0, 0, 0); }
            ENDHLSL
        }
    }
}
