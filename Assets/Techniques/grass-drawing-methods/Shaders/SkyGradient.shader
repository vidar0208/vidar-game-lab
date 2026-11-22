// Background sky for the grass post: horizon-to-zenith gradient plus a glow around the main light. No fog, so the
// hazy field fades into the horizon colour while the sky above keeps its gradient.
Shader "GameTech/SkyGradient"
{
    Properties { _Horizon("Horizon", Color) = (0.86, 0.66, 0.46, 1) _Zenith("Zenith", Color) = (0.32, 0.42, 0.62, 1) _Sun("Sun glow", Color) = (1, 0.75, 0.45, 1) }
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _Horizon, _Zenith, _Sun;
            CBUFFER_END
            struct A { float4 p : POSITION; };
            struct O { float4 p : SV_POSITION; float3 w : TEXCOORD0; };
            O V(A v) { O o; o.w = TransformObjectToWorld(v.p.xyz); o.p = TransformWorldToHClip(o.w); return o; }
            float4 F(O i) : SV_Target
            {
                float3 d = normalize(i.w - _WorldSpaceCameraPos);
                float3 col = lerp(_Horizon.rgb, _Zenith.rgb, pow(saturate(d.y * 1.6), 0.6));
                float s = saturate(dot(d, _MainLightPosition.xyz));
                col += _Sun.rgb * (pow(s, 600) * 6 + pow(s, 24) * 0.55 + pow(s, 4) * 0.15);
                return float4(col, 1);
            }
            ENDHLSL
        }
    }
}
