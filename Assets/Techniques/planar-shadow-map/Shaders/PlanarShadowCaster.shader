// Caster pass of the camera-space planar shadow map, after dreamfairy/Unity_URP_Planear_Shadowmap (MIT),
// ShadowCasterShader.shader: each caster vertex is slid along the light direction down to the ground plane
// (xz - L.xz * (y - planeY) / L.y, the source's triangle relation), that ground point is projected with the MAIN
// camera's view-projection, and the result is written in screen space (xy / w, w = 1) like the source does.
// Change: the source stores a normalised height in a hardware depth shadow map with ZTest LEqual; here the height
// is written to a half-float colour target with BlendOp Min, which keeps the same "lowest caster on this light ray"
// value without depending on each platform's depth direction.
Shader "GameTech/PlanarShadowCaster"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="PlanarShadowCaster" }
            ZTest Always ZWrite Off Cull Off
            BlendOp Min Blend One One
            HLSLPROGRAM
            #pragma vertex V
            #pragma fragment F
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4x4 _PsmVP; float4 _PsmLightDir; float _PsmPlaneY;
            struct A { float4 p : POSITION; };
            struct O { float4 p : SV_POSITION; float h : TEXCOORD0; };
            O V(A v)
            {
                O o; float3 w = TransformObjectToWorld(v.p.xyz);
                float3 L = normalize(_PsmLightDir.xyz);            // towards the light, L.y > 0 (light from above)
                float3 g = float3(w.x - L.x * (w.y - _PsmPlaneY) / L.y, _PsmPlaneY, w.z - L.z * (w.y - _PsmPlaneY) / L.y);
                float4 c = mul(_PsmVP, float4(g, 1));
                o.p = float4(c.xy / c.w, 0.5, 1);
                o.h = w.y - _PsmPlaneY;
                return o;
            }
            float4 F(O i) : SV_Target { return i.h.xxxx; }
            ENDHLSL
        }
    }
}
