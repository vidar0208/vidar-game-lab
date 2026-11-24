// Draws the particles of MeshParticles.compute as additive camera-facing quads. Colour by age: white-hot when born,
// then orange, then dark red, fading out at the end of life. Dead particles collapse to a point.
Shader "GameTech/MeshParticleDraw"
{
    Properties
    {
        _Size ("Quad half size (m)", Float) = 0.006
        _Gain ("Brightness", Float) = 1
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
            StructuredBuffer<float4> _Pos, _Vel;
            float _Size, _Gain;
            float4 _Hot, _Mid, _Cold;
            struct O { float4 p : SV_POSITION; float2 uv : TEXCOORD0; float3 c : COLOR; };
            O V(uint id : SV_VertexID)
            {
                O o;
                uint iid = id / 6, vid = id % 6;
                float4 P = _Pos[iid], Vl = _Vel[iid];
                float2 q = float2((vid == 1 || vid == 2 || vid == 4) ? 1 : -1, (vid == 2 || vid == 4 || vid == 5) ? 1 : -1);
                float k = saturate(P.w / max(Vl.w, 1e-4));
                bool alive = P.w < Vl.w;
                float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz;
                float s = alive ? _Size * (1.0 - 0.5 * k) : 0;
                o.p = TransformWorldToHClip(P.xyz + (right * q.x + up * q.y) * s);
                o.uv = q;
                float3 c = k < 0.25 ? lerp(_Hot.rgb, _Mid.rgb, k / 0.25) : lerp(_Mid.rgb, _Cold.rgb, (k - 0.25) / 0.75);
                o.c = c * (1 - k) * _Gain;
                return o;
            }
            float4 F(O i) : SV_Target
            {
                float r2 = dot(i.uv, i.uv);
                clip(1 - r2);
                return float4(i.c * (1 - r2), 1);
            }
            ENDHLSL
        }
    }
}
