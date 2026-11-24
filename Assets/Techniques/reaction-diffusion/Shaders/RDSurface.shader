// Display for the reaction-diffusion grid on a flat slab. Like the source's Surface shader graph: the v channel goes
// through smoothstep(threshold, threshold + edge smoothing) to blend two materials (matte and metallic), and the
// normal comes from the v differences to the left/right/top/bottom texel. The lighting is my own: two directional
// lights with GGX specular plus a simple sky gradient for reflections (no shadows).
Shader "GameTech/RDSurface"
{
    Properties
    {
        _RD("RD", 2D) = "black" {}
        _Threshold("Threshold", Float) = 0.13
        _Edge("Edge Smoothing", Float) = 0.17
        _Bump("Bump", Float) = 8
        _ColA("Colour A", Color) = (0.04, 0.06, 0.2, 1)
        _ColB("Colour B", Color) = (1.0, 0.76, 0.34, 1)
        _MetalB("Metallic B", Float) = 0.9
        _SmoothA("Smoothness A", Float) = 0.35
        _SmoothB("Smoothness B", Float) = 0.8
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex V
            #pragma fragment F
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_RD); SAMPLER(sampler_linear_repeat);
            float4 _RD_TexelSize;
            float _Threshold, _Edge, _Bump, _MetalB, _SmoothA, _SmoothB;
            float4 _ColA, _ColB;
            float4 _L1Dir, _L1Col, _L2Dir, _L2Col, _SkyTop, _SkyBottom;
            struct A { float4 p : POSITION; float2 uv : TEXCOORD0; };
            struct O { float4 p : SV_POSITION; float2 uv : TEXCOORD0; float3 ws : TEXCOORD1; };
            O V(A i) { O o; o.ws = TransformObjectToWorld(i.p.xyz); o.p = TransformWorldToHClip(o.ws); o.uv = i.uv; return o; }

            float Vv(float2 uv) { return SAMPLE_TEXTURE2D(_RD, sampler_linear_repeat, uv).y; }
            float3 Sky(float3 d) { return lerp(_SkyBottom.rgb, _SkyTop.rgb, saturate(d.y * 0.6 + 0.4)); }

            float3 Light(float3 n, float3 v, float3 l, float3 lc, float3 albedo, float metal, float rough)
            {
                float3 h = normalize(l + v);
                float nl = saturate(dot(n, l)), nh = saturate(dot(n, h)), nv = saturate(dot(n, v)) + 1e-4, vh = saturate(dot(v, h));
                float a = rough * rough, a2 = a * a;
                float dd = nh * nh * (a2 - 1) + 1; float D = a2 / (PI * dd * dd);
                float k = (rough + 1) * (rough + 1) / 8; float G = (nl / (nl * (1 - k) + k)) * (nv / (nv * (1 - k) + k));
                float3 F0 = lerp(0.04, albedo, metal); float3 Fr = F0 + (1 - F0) * pow(1 - vh, 5);
                float3 spec = D * G * Fr / (4 * nl * nv + 1e-4);
                float3 diff = albedo * (1 - metal) / PI;
                return (diff * (1 - Fr) + spec) * lc * nl * PI;
            }

            float4 F(O i) : SV_Target
            {
                float2 t = _RD_TexelSize.xy;
                float c = Vv(i.uv), l = Vv(i.uv - float2(t.x, 0)), r = Vv(i.uv + float2(t.x, 0));
                float tp = Vv(i.uv + float2(0, t.y)), b = Vv(i.uv - float2(0, t.y));
                float m = smoothstep(_Threshold, _Threshold + _Edge, c);
                float3 n = normalize(float3((l - r) * _Bump, 1, (b - tp) * _Bump));   // slab lies in XZ: uv.x -> +X, uv.y -> +Z
                float3 albedo = lerp(_ColA.rgb, _ColB.rgb, m);
                float metal = lerp(0, _MetalB, m), smooth = lerp(_SmoothA, _SmoothB, m), rough = max(0.05, 1 - smooth);
                float3 v = normalize(GetCameraPositionWS() - i.ws);
                float3 col = Light(n, v, normalize(_L1Dir.xyz), _L1Col.rgb, albedo, metal, rough)
                           + Light(n, v, normalize(_L2Dir.xyz), _L2Col.rgb, albedo, metal, rough);
                float3 F0 = lerp(0.04, albedo, metal);
                float nv = saturate(dot(n, v));
                float3 Fe = F0 + (max(smooth, F0) - F0) * pow(1 - nv, 5);
                col += Sky(reflect(-v, n)) * Fe * smooth + albedo * (1 - metal) * Sky(n) * 0.35;
                return float4(col, 1);
            }
            ENDHLSL
        }
    }
}
