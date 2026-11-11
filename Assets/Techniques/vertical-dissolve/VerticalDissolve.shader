// URP rewrite of AdultLink/VerticalDissolve (MIT, Copyright (c) 2018 Guillermo Angel), a built-in pipeline surface shader.
// Kept from the source, line by line: the fill remap (0..1 -> -1..1), the wavy height
//   h = wave1Amp * sin(noise + (layer + P.x) * wave1Freq + wave1Off) + P.y + sin((P.z + layer) * wave2Freq + wave2Off + noise) * wave2Amp,
// the two step() masks (with and without the border width), invert, world / vertex coordinates, the border colour on the
// band between the two masks, and the fill colour on back faces. The simplex noise function is the one embedded in the
// source file. Not ported: rim light, emission textures, secondary emission, border texture, normal / specular / occlusion maps.
// Added: _VD_Simple, a global switch for the comparison half of the split screen (border, waves and inside tint off).
Shader "GameTech/VerticalDissolve"
{
    Properties
    {
        _BaseMap("Albedo", 2D) = "white" {}
        _BaseColor("Color", Color) = (1,1,1,1)
        _Metallic("Metallic", Range(0,1)) = 0
        _Smoothness("Smoothness", Range(0,1)) = 0.3
        _Fill("Fill", Float) = 1
        _Borderwidth("Border width", Float) = 0.35
        [HDR] _Bordercolor("Border color", Color) = (0.1,0.95,1.9,1)
        [HDR] _Fillcolor("Fill color (inside tint)", Color) = (0.05,0.5,0.9,1)
        [Toggle] _Tintinsidecolor("Tint inside color", Float) = 1
        _Noisescale("Noise scale", Range(0,20)) = 4
        _Noisespeed("Noise speed", Vector) = (0.3,0.5,0,0)
        [Toggle] _Layernoise("Layer noise", Float) = 1
        [Toggle] _Worldcoordinates("World coordinates", Float) = 1
        [Toggle] _Invertmask("Invert mask", Float) = 0
        _Wave1amplitude("Wave1 amplitude", Range(0,1)) = 0.04
        _Wave1frequency("Wave1 frequency", Range(0,50)) = 14
        _Wave1offset("Wave1 offset", Float) = 0
        _Wave2amplitude("Wave2 amplitude", Range(0,1)) = 0.04
        _Wave2Frequency("Wave2 frequency", Range(0,50)) = 11
        _Wave2offset("Wave2 offset", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST; half4 _BaseColor; half _Metallic; half _Smoothness;
        float _Fill, _Borderwidth; half4 _Bordercolor; half4 _Fillcolor; float _Tintinsidecolor;
        float _Noisescale; float4 _Noisespeed; float _Layernoise, _Worldcoordinates, _Invertmask;
        float _Wave1amplitude, _Wave1frequency, _Wave1offset, _Wave2amplitude, _Wave2Frequency, _Wave2offset;
        CBUFFER_END
        float _VD_Time;      // seconds on the video timeline (source: _Time.y)
        float _VD_Simple;    // 1 = plain clip at a height (comparison half)

        float3 mod3D289(float3 x) { return x - floor(x / 289.0) * 289.0; }
        float4 mod3D289(float4 x) { return x - floor(x / 289.0) * 289.0; }
        float4 permute(float4 x) { return mod3D289((x * 34.0 + 1.0) * x); }
        float4 taylorInvSqrt(float4 r) { return 1.79284291400159 - r * 0.85373472095314; }
        float snoise(float3 v)
        {
            const float2 C = float2(1.0 / 6.0, 1.0 / 3.0);
            float3 i = floor(v + dot(v, C.yyy));
            float3 x0 = v - i + dot(i, C.xxx);
            float3 g = step(x0.yzx, x0.xyz);
            float3 l = 1.0 - g;
            float3 i1 = min(g.xyz, l.zxy);
            float3 i2 = max(g.xyz, l.zxy);
            float3 x1 = x0 - i1 + C.xxx;
            float3 x2 = x0 - i2 + C.yyy;
            float3 x3 = x0 - 0.5;
            i = mod3D289(i);
            float4 p = permute(permute(permute(i.z + float4(0.0, i1.z, i2.z, 1.0)) + i.y + float4(0.0, i1.y, i2.y, 1.0)) + i.x + float4(0.0, i1.x, i2.x, 1.0));
            float4 j = p - 49.0 * floor(p / 49.0);
            float4 x_ = floor(j / 7.0);
            float4 y_ = floor(j - 7.0 * x_);
            float4 x = (x_ * 2.0 + 0.5) / 7.0 - 1.0;
            float4 y = (y_ * 2.0 + 0.5) / 7.0 - 1.0;
            float4 h = 1.0 - abs(x) - abs(y);
            float4 b0 = float4(x.xy, y.xy);
            float4 b1 = float4(x.zw, y.zw);
            float4 s0 = floor(b0) * 2.0 + 1.0;
            float4 s1 = floor(b1) * 2.0 + 1.0;
            float4 sh = -step(h, 0.0);
            float4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
            float4 a1 = b1.xzyw + s1.xzyw * sh.zzww;
            float3 g0 = float3(a0.xy, h.x);
            float3 g1 = float3(a0.zw, h.y);
            float3 g2 = float3(a1.xy, h.z);
            float3 g3 = float3(a1.zw, h.w);
            float4 norm = taylorInvSqrt(float4(dot(g0, g0), dot(g1, g1), dot(g2, g2), dot(g3, g3)));
            g0 *= norm.x; g1 *= norm.y; g2 *= norm.z; g3 *= norm.w;
            float4 m = max(0.6 - float4(dot(x0, x0), dot(x1, x1), dot(x2, x2), dot(x3, x3)), 0.0);
            m = m * m; m = m * m;
            float4 px = float4(dot(x0, g0), dot(x1, g1), dot(x2, g2), dot(x3, g3));
            return 42.0 * dot(m, px);
        }

        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
        struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; float3 positionOS : TEXCOORD3; };

        Varyings Vert(Attributes i)
        {
            Varyings o;
            o.positionOS = i.positionOS.xyz;
            o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(i.normalOS);
            o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
            return o;
        }

        // x = colour mask (the kept part minus the border), y = border band. Clips what is dissolved.
        float2 Dissolve(Varyings i)
        {
            float simple = step(0.5, _VD_Simple);
            float w1 = _Wave1amplitude * (1.0 - simple), w2 = _Wave2amplitude * (1.0 - simple), bw = _Borderwidth * (1.0 - simple);
            float3 P = lerp(i.positionOS, i.positionWS, _Worldcoordinates);
            float noise = snoise(_Noisescale * (P + _Noisespeed.xyz * _VD_Time));
            float layer = noise * _Layernoise;
            float h = w1 * sin(noise + (layer + P.x) * _Wave1frequency + _Wave1offset) + P.y + sin((P.z + layer) * _Wave2Frequency + _Wave2offset + noise) * w2;
            float fill = -1.0 + _Fill * 2.0;
            float below = step(max(0.0, bw) * 0.1 + h, fill);
            float keep = step(h, fill);
            float colorMask = below * (1.0 - _Invertmask) + _Invertmask * (1.0 - keep);
            float border = keep - below;
            float opacity = keep * (1.0 - _Invertmask) + (1.0 - below) * _Invertmask;
            clip(opacity - 0.5);
            return float2(colorMask, border);
        }

        half4 Albedo(float2 uv)
        {
            half4 a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
            clip(a.a - 0.3);
            return a;
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _FORWARD_PLUS
            half4 Frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float2 d = Dissolve(i);
                half3 albedo = Albedo(i.uv).rgb;
                float front = IS_FRONT_VFACE(face, 1.0, 0.0);
                InputData inp = (InputData)0;
                inp.positionWS = i.positionWS;
                inp.normalWS = normalize(i.normalWS) * (front > 0.5 ? 1.0 : -1.0);
                inp.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inp.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inp.bakedGI = SampleSH(inp.normalWS);
                inp.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inp.shadowMask = half4(1, 1, 1, 1);
                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo; s.metallic = _Metallic; s.smoothness = _Smoothness; s.occlusion = 1; s.alpha = 1;
                float tint = _Tintinsidecolor * (1.0 - step(0.5, _VD_Simple));
                // source: front faces get the border, back faces get the border plus the fill colour
                s.emission = _Bordercolor.rgb * d.y + (1.0 - front) * tint * _Fillcolor.rgb * d.x;
                return UniversalFragmentPBR(inp, s);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Off
            HLSLPROGRAM
            #pragma vertex SVert
            #pragma fragment SFrag
            float3 _LightDirection;
            Varyings SVert(Attributes i)
            {
                Varyings o = Vert(i);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(o.positionWS, o.normalWS, _LightDirection));
            #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
            #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                o.positionCS = cs;
                return o;
            }
            half4 SFrag(Varyings i) : SV_Target { Dissolve(i); Albedo(i.uv); return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DFrag
            half4 DFrag(Varyings i) : SV_Target { Dissolve(i); Albedo(i.uv); return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment NFrag
            half4 NFrag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                Dissolve(i); Albedo(i.uv);
                return half4(normalize(i.normalWS) * IS_FRONT_VFACE(face, 1.0, -1.0), 0);
            }
            ENDHLSL
        }
    }
}
