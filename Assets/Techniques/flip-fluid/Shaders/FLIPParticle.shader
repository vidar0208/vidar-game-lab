// Mine (the source draws with HDRP VFX Graph or an HDRP instancing shader): one lit sphere per particle in URP,
// position and speed read by instance id from the source's rendering buffer (xyz position, w speed). Colours and the
// speed range 2 to 8 are the source's particle colours.
Shader "GameTech/FLIPParticle"
{
    Properties
    {
        _SlowColor("Slow colour", Color) = (0, 0.3891521, 0.7735849, 1)
        _FastColor("Fast colour", Color) = (0.5999911, 0.7552593, 0.9150943, 1)
        _VelocityRange("Speed range", Vector) = (2, 8, 0, 0)
        _Smoothness("Smoothness", Range(0, 1)) = 0.8
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

            StructuredBuffer<float4> _ParticleRenderingBuffer;
            float3 _Origin; float _Scale, _Size;
            float4 _SlowColor, _FastColor, _VelocityRange; float _Smoothness;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 albedo : TEXCOORD2; };

            Varyings Vert(Attributes v, uint iid : SV_InstanceID)
            {
                Varyings o;
                float4 p = _ParticleRenderingBuffer[iid];
                float3 ws = _Origin + p.xyz * _Scale + v.positionOS.xyz * _Size;
                o.positionWS = ws;
                o.normalWS = v.normalOS;
                o.positionCS = TransformWorldToHClip(ws);
                float s = saturate((p.w - _VelocityRange.x) / (_VelocityRange.y - _VelocityRange.x));
                o.albedo = lerp(_SlowColor.rgb, _FastColor.rgb, s);
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
                float fres = pow(1 - saturate(dot(n, v)), 4) * 0.3;
                float3 col = i.albedo * (L.color * ndl * 0.8 + SampleSH(n)) + spec * L.color + fres * SampleSH(reflect(-v, n));
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
