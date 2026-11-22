// Lit shader with kShading's subsurface wrap lighting and thickness-based transmission (Kink3d/kShading, MIT,
// commit cc62b3b, ShaderLibrary/Lighting.hlsl: Radiance + LightingExtended), ported to Unity 6.3 URP.
// The subsurface and transmission terms follow that file; the surrounding GGX/Lambert BRDF, GI and the light
// loop use the current URP shader library instead of kShading's 2019-era copies. Toggles: _SUBSURFACE, _TRANSMISSION.
Shader "GameTech/SSSLit"
{
    Properties
    {
        _BaseMap("Albedo", 2D) = "white" {}
        _BaseColor("Color", Color) = (1,1,1,1)
        _Smoothness("Smoothness", Range(0,1)) = 0.5
        _SubsurfaceColor("Subsurface Color", Color) = (1,0.4,0.25,1)
        _Thickness("Thickness", Range(0,1)) = 0.5
        _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local _SUBSURFACE
            #pragma shader_feature_local _TRANSMISSION
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST; half4 _BaseColor; half _Smoothness; half4 _SubsurfaceColor; half _Thickness; half _Cull;
            CBUFFER_END

            struct A { float4 pos : POSITION; float3 nrm : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 cs : SV_POSITION; float3 ws : TEXCOORD0; float3 nws : TEXCOORD1; float2 uv : TEXCOORD2; };

            V Vert(A i)
            {
                V o; o.ws = TransformObjectToWorld(i.pos.xyz); o.cs = TransformWorldToHClip(o.ws);
                o.nws = TransformObjectToWorldNormal(i.nrm); o.uv = TRANSFORM_TEX(i.uv, _BaseMap); return o;
            }

            // kShading Radiance(): wrapped N.L, tinted by the subsurface colour where the light wraps past the terminator.
            // (The source stores the wrap in a scalar half, which keeps only the red channel; here it stays per channel.)
            half3 RadianceK(half3 n, half3 l, half3 lightColor, half atten, half3 ssc)
            {
                half NdotL = saturate(dot(n, l));
            #ifdef _SUBSURFACE
                half3 NdotLWrap = saturate((dot(n, l) + ssc) / ((1 + ssc) * (1 + ssc)));
                return lightColor * (atten * lerp(NdotLWrap * ssc, NdotLWrap, NdotL));
            #else
                return lightColor * (atten * NdotL);
            #endif
            }

            // kShading LightingExtended(): direct BRDF * radiance, plus view-dependent transmission scaled by (1 - thickness)
            half3 LightK(BRDFData b, Light light, half3 n, half3 v)
            {
            #ifdef _SUBSURFACE
                half3 internalColor = _SubsurfaceColor.rgb;
            #else
                half3 internalColor = b.diffuse;
            #endif
                half atten = light.distanceAttenuation * light.shadowAttenuation;
                half3 radiance = RadianceK(n, light.direction, light.color, atten, internalColor);
                half3 color = (b.diffuse + b.specular * DirectBRDFSpecular(b, n, light.direction, v)) * radiance;
            #ifdef _TRANSMISSION
                half3 subsurfaceHalfDir = SafeNormalize(light.direction + n * (0.5 - internalColor * 0.5));
                half subsurfaceVoH = saturate(dot(v, -subsurfaceHalfDir));
                half3 transmission = subsurfaceVoH * (1 - _Thickness) / ((2 - internalColor) * (2 - internalColor));
                color += transmission * light.color * light.distanceAttenuation;
            #endif
                return color;
            }

            half4 Frag(V i, bool front : SV_IsFrontFace) : SV_Target
            {
                half3 n = normalize(i.nws); n = front ? n : -n;
                half3 v = GetWorldSpaceNormalizeViewDir(i.ws);
                half4 alb = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                BRDFData b; half alpha = 1;
                InitializeBRDFData(alb.rgb, 0, half3(0,0,0), _Smoothness, alpha, b);

                InputData inputData = (InputData)0;
                inputData.positionWS = i.ws; inputData.normalWS = n; inputData.viewDirectionWS = v;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.cs);
                inputData.shadowCoord = TransformWorldToShadowCoord(i.ws);

                half3 color = GlobalIllumination(b, SampleSH(n), 1, i.ws, n, v);
                Light mainLight = GetMainLight(inputData.shadowCoord);
                color += LightK(b, mainLight, n, v);
            #if defined(_ADDITIONAL_LIGHTS)
                uint pixelLightCount = GetAdditionalLightsCount();
                #if USE_CLUSTER_LIGHT_LOOP
                [loop] for (uint li = 0; li < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); li++)
                {
                    Light dl = GetAdditionalLight(li, i.ws, half4(1,1,1,1));
                    color += LightK(b, dl, n, v);
                }
                #endif
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light al = GetAdditionalLight(lightIndex, i.ws, half4(1,1,1,1));
                    color += LightK(b, al, n, v);
                LIGHT_LOOP_END
            #endif
                return half4(color, 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
