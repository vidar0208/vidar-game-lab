// HLSL rewrite of the 5_ZeldaToon Shader Graph and its MainLightModel_Normals / GetMainLight / DirectSpecular
// sub-graphs (ciro-unity/BotW-ToonShader, Shaders/ folder, Unlicense). Node by node:
//   atten      = distanceAtten * (shadowAtten + BlackOffset 0.001)
//   brightness = min(atten, N.L), remapped from -1..1 to 0..1, sampled through the "Hard Shading" gradient (3 bands)
//   light colour is converted to HSV, V is replaced by max(band, backlight rim), converted back, times the texture
//   + light-side rim (Fresnel power 8) + stepped Blinn-Phong highlight masked by a dab texture
// _Toon_Stage lets the video add the layers one by one; stage 0 is URP's own PBR lighting for the comparison.
Shader "GameTech/ZeldaToon"
{
    Properties
    {
        _BaseColor("Diffuse Colour", Color) = (1,1,1,1)
        _BaseMap("Diffuse Texture", 2D) = "white" {}
        _SpecColor("Specular Color", Color) = (0.2075,0.2075,0.2075,1)
        _Smoothness("Smoothness", Range(0,1)) = 0.4
        _SpecStrength("Specular strength (Specular Map A)", Range(0,1)) = 1
        _DabsTex("Dabs", 2D) = "white" {}
        _DabsTiling("Dabs Tiling", Float) = 10
        _DabsRotation("Dabs Rotation (rad)", Float) = 15
        _Rim("Rim lights on (1) or off (0)", Range(0,1)) = 1
        _FlipBack("Flip the normal on back faces (1) or keep it (0)", Range(0,1)) = 1
        _Cutoff("Alpha cutoff", Range(0,1)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_DabsTex); SAMPLER(sampler_DabsTex);
        CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor; float4 _BaseMap_ST; half4 _SpecColor; half _Smoothness; half _SpecStrength;
        float _DabsTiling; float _DabsRotation; half _Cutoff; float _Cull; half _Rim; half _FlipBack;
        CBUFFER_END
        float _Toon_Stage;      // 0 PBR, 1 Lambert, 2 two bands, 3 three bands, 4 + rims, 5 + highlight, 6 + received shadows
        float _Toon_LitEdge;    // gradient position where the lit band starts (source: 0.653 -> N.L 0.31)
        float _Toon_Backlight;  // "Backlight Amount" (source: 2.3)

        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
        struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; };

        Varyings Vert(Attributes i)
        {
            Varyings o;
            o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(i.normalOS);
            o.uv = i.uv;
            return o;
        }

        half4 Albedo(float2 uv)
        {
            half4 t = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv * _BaseMap_ST.xy + _BaseMap_ST.zw);
            clip(t.a * _BaseColor.a - _Cutoff);
            return half4(t.rgb * _BaseColor.rgb, 1);
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION

            // Shader Graph "Colorspace Conversion" node
            float3 RGB2HSV(float3 c)
            {
                float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 P = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 Q = lerp(float4(P.xyw, c.r), float4(c.r, P.yzx), step(P.x, c.r));
                float D = Q.x - min(Q.w, Q.y);
                float E = 1e-10;
                return float3(abs(Q.z + (Q.w - Q.y) / (6.0 * D + E)), D / (Q.x + E), Q.x);
            }
            float3 HSV2RGB(float3 c)
            {
                float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                float3 P = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, saturate(P - K.xxx), c.y);
            }
            // "Hard Shading" gradient of 5_ZeldaToon: 0.453 up to 0.079, 0.660 from 0.094, 1.0 from 0.653 (blend widths kept)
            float Bands(float t, float stage)
            {
                float mid = stage < 2.5 ? 0.6604 : lerp(0.4528, 0.6604, saturate((t - 0.0794) / 0.0147));
                return lerp(mid, 1.0, saturate((t - (_Toon_LitEdge - 0.0118)) / 0.0118));
            }

            half4 Frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                half3 albedo = Albedo(i.uv).rgb;
                float3 N = normalize(i.normalWS) * IS_FRONT_VFACE(face, 1.0, _FlipBack > 0.5 ? -1.0 : 1.0);
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);

                if (_Toon_Stage < 0.5)
                {
                    // comparison side: URP's PBR lighting (the function URP/Lit calls)
                    InputData d = (InputData)0;
                    d.positionWS = i.positionWS; d.normalWS = N; d.viewDirectionWS = V; d.shadowCoord = shadowCoord;
                    d.bakedGI = SampleSH(N); d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS); d.shadowMask = half4(1, 1, 1, 1);
                    SurfaceData s = (SurfaceData)0;
                    s.albedo = albedo; s.metallic = 0; s.smoothness = 0.35; s.occlusion = 1; s.alpha = 1;
                    return UniversalFragmentPBR(d, s);
                }

                Light light = GetMainLight(shadowCoord);
                float3 L = light.direction;
                float NdotL = dot(N, L);
                if (_Toon_Stage < 1.5) return half4(albedo * light.color * saturate(NdotL), 1);   // plain Lambert

                float shadowAtten = _Toon_Stage > 5.5 ? light.shadowAttenuation : 1.0;
                float atten = light.distanceAttenuation * (shadowAtten + 0.001);
                float3 diffuseLight = light.color * atten;
                float brightness = min(atten, NdotL);
                float band = Bands(brightness * 0.5 + 0.5, _Toon_Stage);

                float fres = 1.0 - saturate(dot(N, V));
                float rimBack = 0, rimLit = 0;
                if (_Toon_Stage > 3.5 && _Rim > 0.5)
                {
                    float3 camFwd = -UNITY_MATRIX_V[2].xyz;
                    float back = saturate(dot(L, camFwd) - 0.6) * _Toon_Backlight;
                    rimBack = step(1.0 - back + 0.01, pow(fres, 3.0));
                    float edge = clamp(-((NdotL - 0.3) * 4.0 + (dot(-V, L) - 0.5)), 0.2, 1.1);
                    rimLit = step(edge, pow(fres, 8.0));
                }
                float3 hsv = RGB2HSV(diffuseLight);
                float3 col = albedo * HSV2RGB(float3(hsv.x, hsv.y, max(rimBack, band))) + rimLit;

                if (_Toon_Stage > 4.5)
                {
                    float3 H = SafeNormalize(L + V);
                    float3 spec = diffuseLight * _SpecColor.rgb * pow(saturate(dot(N, H)), exp2(10.0 * _Smoothness + 1.0));
                    float specV = max(spec.r, max(spec.g, spec.b));
                    float2 uv = i.uv - 0.5;
                    float sn = sin(_DabsRotation), cs = cos(_DabsRotation);
                    uv = float2(uv.x * cs + uv.y * sn, -uv.x * sn + uv.y * cs) + 0.5;
                    float dabs = SAMPLE_TEXTURE2D(_DabsTex, sampler_DabsTex, uv * _DabsTiling).r;
                    col += _SpecColor.rgb * step(0.01, specV * _SpecStrength * dabs);
                }
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex SVert
            #pragma fragment SFrag
            float3 _LightDirection;
            Varyings SVert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = i.uv;
                float4 cs = TransformWorldToHClip(ApplyShadowBias(o.positionWS, o.normalWS, _LightDirection));
            #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
            #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                o.positionCS = cs;
                return o;
            }
            half4 SFrag(Varyings i) : SV_Target { Albedo(i.uv); return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DFrag
            half4 DFrag(Varyings i) : SV_Target { Albedo(i.uv); return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment NFrag
            half4 NFrag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                Albedo(i.uv);
                return half4(normalize(i.normalWS) * IS_FRONT_VFACE(face, 1.0, _FlipBack > 0.5 ? -1.0 : 1.0), 0);
            }
            ENDHLSL
        }
    }
}
