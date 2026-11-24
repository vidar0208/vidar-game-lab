// Draws every particle as a small soft dot, added on top of each other (dense clusters glow, bloom does the rest).
// Positions (pixels) come straight from the simulation buffer and are placed on the XY plane at _Origin.
Shader "GameTech/LifeParticles"
{
    Properties
    {
        _Size ("Dot radius (sim pixels)", Float) = 5
        _Scale ("World units per sim pixel", Float) = 0.01
        _Gain ("Gain", Float) = 0.7
        _Origin ("Origin", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend One One ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex V
            #pragma fragment F
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            StructuredBuffer<float4> _Pos;
            StructuredBuffer<uint> _Colour;
            float _Size, _Scale, _Gain;
            float4 _Origin;
            float4 _Palette[8];
            struct O { float4 p : SV_POSITION; float2 uv : TEXCOORD0; float3 c : COLOR; };
            O V(uint id : SV_VertexID)
            {
                O o;
                uint iid = id / 6, vid = id % 6;
                float4 P = _Pos[iid];
                float2 q = float2((vid == 1 || vid == 2 || vid == 4) ? 1 : -1, (vid == 2 || vid == 4 || vid == 5) ? 1 : -1);
                float3 w = _Origin.xyz + float3(P.xy + q * _Size, 0) * _Scale;
                o.p = TransformWorldToHClip(w);
                o.uv = q;
                float speed = length(P.zw);
                o.c = _Palette[_Colour[iid]].rgb * _Gain * (0.75 + 0.5 * saturate(speed * 0.5));
                return o;
            }
            float4 F(O i) : SV_Target
            {
                float d = dot(i.uv, i.uv);
                float a = saturate(1 - d); a *= a;
                return float4(i.c * a, 0);
            }
            ENDHLSL
        }
    }
}
