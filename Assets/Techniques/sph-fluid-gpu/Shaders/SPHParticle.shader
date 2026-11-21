// Mine (replaces the source's built-in-pipeline surface shader): draws one lit sphere per particle, reading the
// position from the particle buffer by instance id. Colour by speed (deep blue at rest, pale at high speed); a
// particle the source marked red (its grid cell was over the limit) stays red, as in the source.
Shader "GameTech/SPHParticle"
{
    Properties
    {
        _Deep("Slow colour", Color) = (0.03, 0.18, 0.42, 1)
        _Fast("Fast colour", Color) = (0.75, 0.92, 1.0, 1)
        _SpeedRef("Speed for full fast colour", Float) = 30
        _Smoothness("Smoothness", Range(0, 1)) = 0.75
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Particle { float3 Position; float4 Color; };
            StructuredBuffer<Particle> _Particles;
            StructuredBuffer<float3> _Velocities;
            float3 _Origin; float _Scale, _Size;
            float4 _Deep, _Fast; float _SpeedRef, _Smoothness;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 albedo : TEXCOORD2; };

            Varyings Vert(Attributes v, uint iid : SV_InstanceID)
            {
                Varyings o;
                Particle p = _Particles[iid];
                float3 ws = _Origin + p.Position * _Scale + v.positionOS.xyz * _Size;
                o.positionWS = ws;
                o.normalWS = v.normalOS;
                o.positionCS = TransformWorldToHClip(ws);
                float s = saturate(length(_Velocities[iid]) / _SpeedRef);
                float3 c = lerp(_Deep.rgb, _Fast.rgb, s * s * (3 - 2 * s));
                o.albedo = p.Color.g < 0.5 ? float3(0.9, 0.05, 0.03) : c;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                Light L = GetMainLight();
                float ndl = saturate(dot(n, L.direction));
                float3 v = normalize(_WorldSpaceCameraPos - i.positionWS);
                float3 h = normalize(L.direction + v);
                float spec = pow(saturate(dot(n, h)), exp2(10 * _Smoothness + 1)) * _Smoothness * 0.6;
                float fres = pow(1 - saturate(dot(n, v)), 4) * 0.25;
                float3 col = i.albedo * (L.color * ndl + SampleSH(n)) + (spec * L.color) + fres * SampleSH(reflect(-v, n));
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
