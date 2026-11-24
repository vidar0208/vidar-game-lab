// Floor that receives the deep shadow map: Lambert from the sun times the transmittance lookup, plus ambient and fog.
// _GDebug 1 shows how many fibre fragments are stored in the shadow-map texel above each floor point.
Shader "GameTech/DSMGround"
{
    Properties { _Color("Colour", Color) = (0.62, 0.58, 0.53, 1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex V
            #pragma fragment F
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "DSMCommon.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            CBUFFER_END
            struct A { float4 p : POSITION; float3 n : NORMAL; };
            struct O { float4 p : SV_POSITION; float3 w : TEXCOORD0; float3 n : TEXCOORD1; float fog : TEXCOORD2; };
            O V(A v) { O o; o.w = TransformObjectToWorld(v.p.xyz); o.n = TransformObjectToWorldNormal(v.n); o.p = TransformWorldToHClip(o.w); o.fog = ComputeFogFactor(o.p.z); return o; }
            float N2(float2 x) { float2 i = floor(x), f = frac(x); f = f * f * (3 - 2 * f);
                float a = frac(sin(dot(i, float2(127.1, 311.7))) * 43758.5), b = frac(sin(dot(i + float2(1, 0), float2(127.1, 311.7))) * 43758.5);
                float c = frac(sin(dot(i + float2(0, 1), float2(127.1, 311.7))) * 43758.5), d = frac(sin(dot(i + 1, float2(127.1, 311.7))) * 43758.5);
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y); }
            float4 F(O i) : SV_Target
            {
                if (_GDebug > 0.5)
                {
                    float c = DsmCount(i.w);
                    float3 hc = c < 0.5 ? float3(0.03, 0.03, 0.04) : DsmHeat(c / (float)_MaxFrags);
                    return float4(MixFog(hc, i.fog), 1);
                }
                float3 n = normalize(i.n), L = -_LF.xyz;
                float sh = DsmTransmittance(i.w);
                float g = 0.85 + 0.15 * N2(i.w.xz * 3.0) + 0.06 * N2(i.w.xz * 23.0);
                float3 col = _Color.rgb * g * (_GAmb.rgb + _GLightCol.rgb * saturate(dot(n, L)) * sh);
                return float4(MixFog(col, i.fog), 1);
            }
            ENDHLSL
        }
    }
}
