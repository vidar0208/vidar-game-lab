// Tinted glass for the OIT post, ported from candycat1992/OIT_Lab (MIT) to URP.
// Pass 0 "ODT": the source's Helpers/ODT.shader idea (alpha blend, ZWrite Off) drawn by URP's own transparent queue.
// Pass 1 "Accum": WB_Accumulate.shader (Blend One One): float4(C * a, a) * w(z, a), with the source's Weight1
//   function; _OitWeighted = 0 gives the source's unweighted "Blended" mode (w = 1).
// Pass 2 "Reveal": WB_Revealage.shader (Blend Zero OneMinusSrcAlpha): writes a, so the target ends at prod(1 - a).
// Passes 1 and 2 are drawn by OITDirector with CommandBuffer.DrawRenderer. URP has no depth buffer to share with them,
// so they test against a linear eye depth texture of the opaque scene (_OitSceneDepth) and discard hidden fragments.
// Shading (same in all passes): ambient + key + fill diffuse as in the source, plus a Blinn-Phong highlight and a
// Fresnel edge that are new here. Lights and the camera position come from globals set by the director, so ODT and
// OIT shade identically (URP's own per-camera constants are not reliable inside a command buffer run after Render).
Shader "GameTech/OITGlass"
{
    Properties { _Color("Color", Color) = (1, 0.5, 0.1, 0.45) }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
        float4 _Color;
        CBUFFER_END
        float4 _GKeyDir, _GKeyCol, _GFillDir, _GFillCol, _GAmb, _GCamPos;
        float4x4 _OitV, _OitVP;
        float _OitWeighted;
        TEXTURE2D_FLOAT(_OitSceneDepth);
        struct A { float4 p : POSITION; float3 n : NORMAL; };
        struct O { float4 p : SV_POSITION; float3 w : TEXCOORD0; float3 n : TEXCOORD1; float z : TEXCOORD2; };

        float4 Shade(O i, bool front)
        {
            float3 N = normalize(i.n) * (front ? 1 : -1);
            float3 V = normalize(_GCamPos.xyz - i.w);
            if (dot(N, V) < 0) N = -N;
            float3 L = normalize(_GKeyDir.xyz), F = normalize(_GFillDir.xyz), H = normalize(L + V);
            float fres = pow(1 - saturate(dot(N, V)), 4);
            float3 alb = _Color.rgb;
            float3 c = alb * (_GAmb.rgb + _GKeyCol.rgb * (0.25 + 0.55 * saturate(dot(N, L))) + _GFillCol.rgb * saturate(dot(N, F)));
            c += _GKeyCol.rgb * pow(saturate(dot(N, H)), 90) * 2.0 + fres * 0.3 * (_GAmb.rgb + alb);
            return float4(c, saturate(_Color.a + fres * 0.35));
        }
        // the source's Weight1 (McGuire and Bavoil 2013, eq. 7 style), z = camera-space depth
        float W(float z, float a)
        {
            return _OitWeighted > 0.5 ? a * max(1e-2, min(3 * 1e3, 10.0 / (1e-5 + pow(z / 5, 2) + pow(z / 200, 6)))) : 1.0;
        }
        O VURP(A v) { O o; o.w = TransformObjectToWorld(v.p.xyz); o.p = TransformWorldToHClip(o.w); o.n = TransformObjectToWorldNormal(v.n); o.z = 0; return o; }
        O VOIT(A v)
        {
            O o; o.w = TransformObjectToWorld(v.p.xyz); o.p = mul(_OitVP, float4(o.w, 1)); o.n = TransformObjectToWorldNormal(v.n);
            o.z = -mul(_OitV, float4(o.w, 1)).z; return o;
        }
        void DepthTest(O i) { if (i.z > LOAD_TEXTURE2D(_OitSceneDepth, uint2(i.p.xy)).r + 0.003) discard; }
        ENDHLSL

        Pass
        {
            Name "ODT"
            ZWrite Off Cull Off Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex VURP
            #pragma fragment F
            float4 F(O i, bool front : SV_IsFrontFace) : SV_Target { return Shade(i, front); }
            ENDHLSL
        }
        Pass
        {
            Name "Accum" Tags { "LightMode"="OITAccum" }
            ZWrite Off ZTest Always Cull Off Blend One One
            HLSLPROGRAM
            #pragma vertex VOIT
            #pragma fragment F
            float4 F(O i, bool front : SV_IsFrontFace) : SV_Target
            {
                DepthTest(i); float4 s = Shade(i, front);
                return float4(s.rgb * s.a, s.a) * W(i.z, s.a);
            }
            ENDHLSL
        }
        Pass
        {
            Name "Reveal" Tags { "LightMode"="OITReveal" }
            ZWrite Off ZTest Always Cull Off Blend Zero OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex VOIT
            #pragma fragment F
            float4 F(O i, bool front : SV_IsFrontFace) : SV_Target { DepthTest(i); return Shade(i, front).aaaa; }
            ENDHLSL
        }
    }
}
