// URP port of the source's depth pass (DepthShader + vsm_depth_frag in ShadowVSMCustomDepth). Writes the same three
// channels: r = depth in the light's space scaled to [-64, 64], g = depth squared plus the source's slope term,
// b = 1 (coverage, so the blur can skip texels where nothing was drawn). Change: the source let an orthographic
// camera produce the depth; here the vertex shader projects with VSM_LightMatrix itself (the same matrix the
// receivers use) and _GT_VSMCascadeScale picks the cascade box, so caster and receiver depths are identical.
Shader "Hidden/GameTech/VSMDepth"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            ZWrite On ZTest LEqual Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4x4 VSM_LightMatrix;
            float _GT_VSMCascadeScale;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float depth : TEXCOORD0; };

            Varyings Vert(Attributes i)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                float3 ls = mul(VSM_LightMatrix, float4(ws, 1)).xyz;   // xy in [-0.5, 0.5] for the first cascade, z in [-64, 64]
                float2 xy = ls.xy * 2 * _GT_VSMCascadeScale;
            #if UNITY_UV_STARTS_AT_TOP
                xy.y = -xy.y;
            #endif
                float z01 = ls.z / 128 + 0.5;   // 0 = nearest the light
            #if UNITY_REVERSED_Z
                float zc = 1 - z01;
            #else
                float zc = z01;
            #endif
                o.positionCS = float4(xy, zc, 1);
                o.depth = ls.z;
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float depth = i.depth;
                float dx = ddx(depth);
                float dy = ddy(depth);
                float bias = 0.25 * (dx * dx + dy * dy);
                return float4(depth, depth * depth + bias, 1, 0);
            }
            ENDHLSL
        }
    }
}
