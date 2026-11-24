// Draws every MPM particle as a small shaded ball (a camera-facing quad with a sphere normal), positions straight
// from a buffer filled each frame: x, y (grid cells), speed, colour index.
Shader "GameTech/MpmDots"
{
    Properties
    {
        _Size ("Ball radius (cells)", Float) = 0.4
        _Cell ("World units per cell", Float) = 0.1
        _Origin ("Origin", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZWrite On ZTest LEqual Cull Off
            HLSLPROGRAM
            #pragma vertex V
            #pragma fragment F
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            StructuredBuffer<float4> _P;
            float _Size, _Cell;
            float4 _Origin;
            float4 _Palette[8];
            struct O { float4 p : SV_POSITION; float2 uv : TEXCOORD0; float3 c : COLOR; };
            O V(uint id : SV_VertexID)
            {
                O o;
                uint iid = id / 6, vid = id % 6;
                float4 P = _P[iid];
                float2 q = float2((vid == 1 || vid == 2 || vid == 4) ? 1 : -1, (vid == 2 || vid == 4 || vid == 5) ? 1 : -1);
                float z = frac(iid * 0.61803) * 0.01; // tiny depth spread so overlapping balls sort stably
                float3 w = _Origin.xyz + float3((P.xy + q * _Size) * _Cell, z);
                o.p = TransformWorldToHClip(w);
                o.uv = q;
                o.c = _Palette[(uint)P.w].rgb * (1 + 0.35 * saturate(P.z * 1.5));
                return o;
            }
            float4 F(O i) : SV_Target
            {
                float r2 = dot(i.uv, i.uv);
                clip(1 - r2);
                float3 n = float3(i.uv, sqrt(1 - r2));
                float3 L = normalize(float3(-0.45, 0.65, 0.6));
                float diff = saturate(dot(n, L));
                float spec = pow(saturate(dot(n, normalize(L + float3(0, 0, 1)))), 24);
                return float4(i.c * (0.22 + 0.85 * diff) + spec * 0.45, 1);
            }
            ENDHLSL
        }
    }
}
