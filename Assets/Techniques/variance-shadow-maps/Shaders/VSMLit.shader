// Mine: small URP forward shader that receives the VSM shadow, the way the source's Standard* shaders do (direct light
// multiplied by VSM_ShadowIntensity). Ambient: spherical harmonics. Unity's own shadow map is not used.
Shader "GameTech/VSMLit"
{
    Properties
    {
        _BaseColor("Colour", Color) = (0.6, 0.6, 0.6, 1)
        _Smoothness("Smoothness", Range(0, 1)) = 0.3
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "../ShadowVSM/ShadowVSMURP.hlsl"

        CBUFFER_START(UnityPerMaterial)
        float4 _BaseColor; float _Smoothness;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
        struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };

        Varyings Vert(Attributes i)
        {
            Varyings o;
            o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
            o.normalWS = TransformObjectToWorldNormal(i.normalOS);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 ws = i.positionWS;
                float3 albedo = _BaseColor.rgb;
                Light L = GetMainLight();
                float ndl = saturate(dot(n, L.direction));
                float sh = ndl > 0 ? VSM_ShadowIntensity(ws, n) : 0;
                float3 v = normalize(_WorldSpaceCameraPos - ws);
                float3 h = normalize(L.direction + v);
                float spec = pow(saturate(dot(n, h)), exp2(10 * _Smoothness + 1)) * _Smoothness * 0.3;
                float3 col = (albedo + spec) * L.color * ndl * sh + albedo * SampleSH(n);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment DFrag
            half4 DFrag(Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }
    }
}
