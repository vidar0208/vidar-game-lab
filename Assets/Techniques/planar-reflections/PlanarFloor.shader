// Polished floor that catches the planar reflection: the reflection texture is read at the pixel's own screen position.
// _PlanarMode 0: one reflection texture. 1: left of _PlanarS1 reads only the skybox reflection (environment probe) instead.
// 2: three bands split at _PlanarS1 and _PlanarS2, each reading its own reflection texture.
// Main light with shadows plus ambient for the diffuse part, Schlick Fresnel for the reflection weight.
Shader "GT/PlanarFloor"
{
    Properties
    {
        _BaseColor ("Base colour", Color) = (0.05, 0.05, 0.06, 1)
        _ReflStrength ("Reflection strength", Range(0, 1)) = 0.85
        _F0 ("Reflectance at normal incidence", Range(0, 1)) = 0.3
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor; half _ReflStrength, _F0;
            CBUFFER_END
            TEXTURE2D(_PlanarTex0); TEXTURE2D(_PlanarTex1); TEXTURE2D(_PlanarTex2);
            SamplerState gt_linear_clamp_sampler;
            float _PlanarMode, _PlanarS1, _PlanarS2;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float4 screenPos : TEXCOORD2; };

            Varyings vert(Attributes a)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(a.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(a.normalOS);
                o.screenPos = ComputeScreenPos(p.positionCS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 suv = i.screenPos.xy / i.screenPos.w;
                float3 N = normalize(i.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                Light ml = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 diffuse = _BaseColor.rgb * (ml.color * saturate(dot(N, ml.direction)) * ml.shadowAttenuation + SampleSH(N));

                half3 planar;
                if (_PlanarMode > 1.5)
                    planar = suv.x < _PlanarS1 ? SAMPLE_TEXTURE2D(_PlanarTex0, gt_linear_clamp_sampler, suv).rgb
                           : suv.x < _PlanarS2 ? SAMPLE_TEXTURE2D(_PlanarTex1, gt_linear_clamp_sampler, suv).rgb
                           : SAMPLE_TEXTURE2D(_PlanarTex2, gt_linear_clamp_sampler, suv).rgb;
                else
                    planar = SAMPLE_TEXTURE2D(_PlanarTex0, gt_linear_clamp_sampler, suv).rgb;
                half3 cube = GlossyEnvironmentReflection(reflect(-V, N), 0, 1);
                half3 refl = (_PlanarMode > 0.5 && _PlanarMode < 1.5 && suv.x < _PlanarS1) ? cube : planar;

                half fres = _F0 + (1 - _F0) * pow(1 - saturate(dot(N, V)), 5);
                half w = fres * _ReflStrength;
                return half4(diffuse * (1 - w) + refl * w, 1);
            }
            ENDHLSL
        }
    }
}
