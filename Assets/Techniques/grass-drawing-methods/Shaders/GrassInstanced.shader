// Instanced grass for the drawing-methods post. One shader, two ways of drawing the same field:
//  _Card = 1: billboard cards, after daniel-ilett/shaders-6grass Billboard Grass (MIT): a quad with an alpha-tested
//    grass texture, turned to line up with the camera plane around Y only (the source's third BillboardGrass.cs mode,
//    LookRotation(camera forward with y = 0.01)), done here in the vertex shader per instance instead of per object.
//  _Card = 0: blade meshes, after the source's Mesh Grass: a real blade mesh per instance, base-to-tip colour,
//    casts and receives main light shadows.
// Instances come from a StructuredBuffer (_Inst: xyz position + scale, then yaw, bend, colour variation, phase) and are
// drawn with Graphics.RenderMeshPrimitives. Both methods get the same lighting (normals bent halfway to up), fog and
// the same mild sway, so the
// only difference on screen is the geometry. _DebugInst = 1 paints each instance a random colour.
Shader "GameTech/GrassInstanced"
{
    Properties
    {
        _CardTex("Card texture", 2D) = "white" {}
        _BaseColor("Base", Color) = (0.08, 0.2, 0.04, 1)
        _TipColor("Tip", Color) = (0.55, 0.75, 0.25, 1)
        _Card("Card mode", Float) = 0
        _CardWidth("Card width", Float) = 0.55
        _DebugInst("Debug instances", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_CardTex); SAMPLER(sampler_CardTex);
        CBUFFER_START(UnityPerMaterial)
        float4 _CardTex_ST; float4 _BaseColor, _TipColor; float _Card, _CardWidth, _DebugInst;
        CBUFFER_END
        StructuredBuffer<float4> _Inst;
        float4 _GCamFwd; float _GTime; float4 _GWind;
        float3 _LightDirection;
        struct A { float4 p : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; uint id : SV_InstanceID; };
        struct O { float4 p : SV_POSITION; float3 w : TEXCOORD0; float3 n : TEXCOORD1; float2 uv : TEXCOORD2; float4 c : TEXCOORD3; float fog : TEXCOORD4; };

        float3 Hue(float h) { return saturate(abs(frac(h + float3(0, 2, 1) / 3.0) * 6 - 3) - 1); }

        void Place(A v, out float3 w, out float3 n, out float h01, out float4 extra)
        {
            float4 a = _Inst[v.id * 2], b = _Inst[v.id * 2 + 1];
            float s = a.w; float3 p = v.p.xyz * s; float3 nn = v.n;
            float yaw = b.x;
            if (_Card > 0.5)
            {
                // the source's camera-plane alignment around Y: forward = camera forward with y ~ 0
                float3 f = normalize(float3(_GCamFwd.x, 0.01, _GCamFwd.z)); float3 r = normalize(cross(float3(0, 1, 0), f));
                p = r * p.x * _CardWidth + float3(0, 1, 0) * p.y; nn = -f;
            }
            else
            {
                float cs = cos(yaw), sn = sin(yaw);
                // bend the blade forward along its own facing, more at the top
                float t = saturate(v.p.y); p.z += b.y * t * t * s; p.y -= 0.35 * b.y * t * t * t * s;
                p = float3(cs * p.x + sn * p.z, p.y, -sn * p.x + cs * p.z);
                nn = float3(cs * nn.x + sn * nn.z, nn.y, -sn * nn.x + cs * nn.z);
            }
            h01 = saturate(v.p.y);
            // mild sway, same for both methods
            float sway = sin(_GTime * 1.6 + a.x * 0.55 + a.z * 0.35 + b.w) * 0.5 + sin(_GTime * 2.7 + a.x * 1.3 + b.w) * 0.2;
            p.xz += _GWind.xz * sway * h01 * h01 * 0.09 * s * 4;
            w = a.xyz + p; n = normalize(nn); extra = float4(b.z, b.w, frac(v.id * 0.6180339), 0);
        }
        ENDHLSL

        Pass
        {
            Name "Grass" Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex V
            #pragma fragment F
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            O V(A v)
            {
                O o; float h; float4 e; Place(v, o.w, o.n, h, e);
                o.p = TransformWorldToHClip(o.w); o.uv = v.uv; o.c = float4(h, e.x, e.z, 0); o.fog = ComputeFogFactor(o.p.z);
                return o;
            }
            float4 F(O i, bool front : SV_IsFrontFace) : SV_Target
            {
                float mask = 1;
                if (_Card > 0.5) { mask = SAMPLE_TEXTURE2D(_CardTex, sampler_CardTex, i.uv).a; clip(mask - 0.5); }
                float h = _Card > 0.5 ? i.uv.y : i.c.x;
                float3 alb = lerp(_BaseColor.rgb, _TipColor.rgb, h) * (0.85 + 0.3 * i.c.y);
                if (_DebugInst > 0.5) alb = Hue(i.c.z) * 0.8 + 0.1;
                float3 n = normalize(i.n) * (front ? 1 : -1), v = GetWorldSpaceNormalizeViewDir(i.w);
                if (dot(n, v) < 0) n = -n;
                n = normalize(lerp(n, float3(0, 1, 0), 0.5));          // common grass trick, applied to both methods
                Light l = GetMainLight(TransformWorldToShadowCoord(i.w), i.w, 1);
                float ndl = saturate(dot(n, l.direction) * 0.6 + 0.4);
                float trans = pow(saturate(dot(v, -l.direction)), 4) * 0.9 * h;      // light through the blade, strongest at the tips
                float ao = lerp(0.35, 1.0, h);                                       // darker near the ground, inside the field
                float3 col = alb * SampleSH(float3(0, 1, 0)) * ao + alb * l.color * l.shadowAttenuation * (ndl + trans) * ao;
                col = MixFog(col, i.fog);
                return float4(col, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster" Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Off
            HLSLPROGRAM
            #pragma vertex V
            #pragma fragment F
            O V(A v)
            {
                O o; float h; float4 e; Place(v, o.w, o.n, h, e);
                o.p = TransformWorldToHClip(ApplyShadowBias(o.w, o.n, _LightDirection));
                #if UNITY_REVERSED_Z
                o.p.z = min(o.p.z, UNITY_NEAR_CLIP_VALUE);
                #else
                o.p.z = max(o.p.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.uv = v.uv; o.c = 0; o.fog = 0; return o;
            }
            float4 F(O i) : SV_Target { if (_Card > 0.5) clip(SAMPLE_TEXTURE2D(_CardTex, sampler_CardTex, i.uv).a - 0.5); return 0; }
            ENDHLSL
        }
    }
}
