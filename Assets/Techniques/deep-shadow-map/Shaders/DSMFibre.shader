// Fibres drawn as thin camera-facing ribbons from the point buffer (one instance per fibre). Shading is Kajiya-Kay
// style like the source's DeepShadowShade (diffuse from the tangent, a primary and a shifted secondary highlight) plus
// light scattered through the fibre when it is back-lit. Every term from the sun is multiplied by the shadow lookup.
Shader "GameTech/DSMFibre"
{
    Properties
    {
        _Root("Root colour", Color) = (0.32, 0.16, 0.06, 1)
        _Tip("Tip colour", Color) = (0.95, 0.74, 0.42, 1)
        _Width("Width (m)", Float) = 0.0011
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex V
            #pragma fragment F
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "DSMCommon.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _Root, _Tip; float _Width;
            CBUFFER_END
            StructuredBuffer<float4> _Points;
            uint _Segs;
            struct A { float4 p : POSITION; float2 uv : TEXCOORD0; uint inst : SV_InstanceID; };
            struct O { float4 p : SV_POSITION; float3 w : TEXCOORD0; float3 t : TEXCOORD1; float2 uv : TEXCOORD2; float fog : TEXCOORD3; };
            float H1(uint n) { n = (n << 13u) ^ n; n = n * (n * n * 15731u + 789221u) + 1376312589u; return float(n & 0x7fffffffu) / float(0x7fffffff); }
            O V(A v)
            {
                O o;
                uint k = (uint)(v.uv.y + 0.5), base = v.inst * _Segs;
                float3 p = _Points[base + k].xyz;
                float3 pn = _Points[base + min(k + 1, _Segs - 1)].xyz, pp = _Points[base + (k > 0 ? k - 1 : 0)].xyz;
                float3 t = normalize(pn - pp);
                float3 vdir = normalize(_WorldSpaceCameraPos - p);
                float3 side = normalize(cross(t, vdir));
                float u = k / (float)(_Segs - 1);
                p += side * v.uv.x * _Width * 0.5 * (1.0 - 0.5 * u);
                o.w = p; o.t = t; o.uv = float2(u, H1(v.inst));
                o.p = TransformWorldToHClip(p); o.fog = ComputeFogFactor(o.p.z);
                return o;
            }
            float4 F(O i) : SV_Target
            {
                float3 T = normalize(i.t), Vd = normalize(_WorldSpaceCameraPos - i.w), L = -_LF.xyz;
                float sh = DsmTransmittance(i.w);
                if (_GDebug > 0.5) return float4(lerp(float3(0.02, 0.02, 0.025), float3(1, 1, 1), sh) * 0.8, 1);
                float3 albedo = lerp(_Root.rgb, _Tip.rgb, smoothstep(0.0, 0.5, i.uv.x)) * (0.8 + 0.4 * i.uv.y);
                float tl = dot(T, L); float diff = sqrt(saturate(1.0 - tl * tl));
                float3 h = normalize(L + Vd);
                float th1 = dot(normalize(T + 0.08 * float3(0, 1, 0)), h), th2 = dot(normalize(T - 0.12 * float3(0, 1, 0)), h);
                float s1 = smoothstep(-1.0, 0.0, th1) * pow(sqrt(saturate(1.0 - th1 * th1)), 140.0);
                float s2 = smoothstep(-1.0, 0.0, th2) * pow(sqrt(saturate(1.0 - th2 * th2)), 30.0);
                float trans = pow(saturate(dot(Vd, -L)), 5.0);
                float3 col = albedo * (_GAmb.rgb + _GLightCol.rgb * sh * (0.55 * diff + 1.6 * trans));
                col += _GLightCol.rgb * sh * (s1 * 0.35 + s2 * 0.25 * albedo);
                col = MixFog(col, i.fog);
                return float4(col, 1);
            }
            ENDHLSL
        }
    }
}
