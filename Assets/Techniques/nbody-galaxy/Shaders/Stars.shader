// Draws every star as a small camera-facing soft quad, added on top of each other (dense regions glow, bloom does
// the rest). Positions come straight from the simulation buffer. Bodies heavier than _MaxMass (the galaxy centres)
// are not drawn. First half of the buffer = galaxy A colour, second half = galaxy B colour.
Shader "GameTech/Stars"
{
    Properties
    {
        _Size ("Star size", Float) = 0.045
        _Gain ("Gain", Float) = 0.55
        _MaxMass ("Max drawn mass", Float) = 0.5
        _Half ("First index of galaxy B", Float) = 16384
        _ColA ("Galaxy A colour", Color) = (0.45, 0.65, 1, 1)
        _ColB ("Galaxy B colour", Color) = (1, 0.6, 0.28, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend One One ZWrite Off ZTest LEqual Cull Off
            HLSLPROGRAM
            #pragma vertex V
            #pragma fragment F
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            StructuredBuffer<float4> _Pos;
            StructuredBuffer<float4> _Vel;
            float _Size, _Gain, _MaxMass, _Half;
            float4 _ColA, _ColB;
            struct O { float4 p : SV_POSITION; float2 uv : TEXCOORD0; float3 c : COLOR; };
            O V(uint id : SV_VertexID)
            {
                O o;
                uint iid = id / 6, vid = id % 6;
                float4 P = _Pos[iid];
                float2 q = float2((vid == 1 || vid == 2 || vid == 4) ? 1 : -1, (vid == 2 || vid == 4 || vid == 5) ? 1 : -1);
                float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz;
                float s = P.w > _MaxMass ? 0 : _Size;
                float3 w = P.xyz + (right * q.x + up * q.y) * s;
                o.p = TransformWorldToHClip(w);
                o.uv = q;
                float speed = length(_Vel[iid].xyz);
                float3 base = (float)iid < _Half ? _ColA.rgb : _ColB.rgb;
                o.c = base * _Gain * (0.7 + 0.6 * saturate(speed * 0.8));
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
