// URP rewrite of the Parallax mapping shaders in Habrador/Unity-Advanced-Shaders-Tutorial (MIT, Copyright (c) 2022 Erik Nordeus):
// BricksParallax, HoleInGround and MountainInPlane. Same algorithm as the source:
//   view direction in tangent space as (tangent, normal, bitangent), ray starts at (u, 0, v) and walks down
//   rayPos += stepDistance * rayDir for a fixed number of steps until rayPos.y < height(rayPos.xz);
//   getWeightedTexPos interpolates between the last two positions by their distance to the surface.
//   Height functions are the source's: bricks (1 - h) * -_Depth, hole -(exp2(g * _HoleDepth) - 1) with the
//   noise and mask lerped twice, mountain (1 - noise(uv * 0.2)) * -_Height with grass / stone / water by height.
// Changed for this build: URP passes; the mesh UVs are in metres so the march is in metres; the view direction is
// computed per pixel; colour is sampled with the gradients of the original UV (mip-mapped) instead of mip 0;
// the step count, step size and interpolation are material properties; the hole animation and mountain scroll come
// from the timeline (_HoleAnim, _Scroll) instead of _Time; lighting is Lambert like the source's surface shaders, with
// a normal from the normal map (bricks, ground) or from the height function (mountain) read at the hit position;
// _March 0 = plain normal mapping at the original UV (comparison), _Debug 1 = height map, 2 = flat quad + its edges.
Shader "GameTech/ParallaxMarch"
{
    Properties
    {
        _ColorTex("Color", 2D) = "white" {}
        _HeightTex("Height / noise", 2D) = "white" {}
        _NormalTex("Normal", 2D) = "bump" {}
        _MaskTex("Hole mask", 2D) = "black" {}
        _Variant("Variant 0 bricks 1 hole 2 mountain", Float) = 0
        _ColorTiling("Tiles per metre", Float) = 1
        _NoiseTiling("Hole noise tiles per metre", Float) = 1
        _Depth("Depth (bricks)", Float) = 0.04
        _HoleDepth("Hole depth", Float) = 0.4
        _HoleSize("Hole size", Float) = 4
        _HoleAnim("Hole open 0..1", Float) = 1
        _MaskRect("Mask origin xy, size zw (metres)", Vector) = (0, 0, 1, 1)
        _QuadSize("Quad size in metres (debug edges)", Vector) = (1, 1, 0, 0)
        _Height("Mountain height", Float) = 0.97
        _Scroll("Mountain scroll", Float) = 0
        _WaterColor("Water", Color) = (0.25, 0.45, 0.6, 1)
        _Tint("Tint", Color) = (1, 1, 1, 1)
        _Steps("Steps", Float) = 300
        _StepDistance("Step distance", Float) = 0.001
        _Interp("Interpolate last two steps", Float) = 1
        _March("Ray march on", Float) = 1
        _Debug("Debug view", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        TEXTURE2D(_ColorTex); SAMPLER(sampler_ColorTex);
        TEXTURE2D(_HeightTex); SAMPLER(sampler_HeightTex);
        TEXTURE2D(_NormalTex); SAMPLER(sampler_NormalTex);
        TEXTURE2D(_MaskTex); SAMPLER(sampler_MaskTex);
        CBUFFER_START(UnityPerMaterial)
        float4 _ColorTex_ST; float _Variant, _ColorTiling, _NoiseTiling, _Depth, _HoleDepth, _HoleSize, _HoleAnim;
        float4 _MaskRect; float4 _QuadSize; float _Height, _Scroll; half4 _WaterColor, _Tint; float _Steps, _StepDistance, _Interp, _March, _Debug;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float2 uv : TEXCOORD0; };
        struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float4 tangentWS : TEXCOORD2; float2 uv : TEXCOORD3; float fog : TEXCOORD4; };

        Varyings Vert(Attributes i)
        {
            Varyings o;
            o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(i.normalOS);
            o.tangentWS = float4(TransformObjectToWorldDir(i.tangentOS.xyz), i.tangentOS.w * GetOddNegativeScale());
            o.uv = i.uv;
            o.fog = ComputeFogFactor(o.positionCS.z);
            return o;
        }

        // ---- the source's height functions (negative = below the surface), all in metres
        float GrayScaleDepth(float2 p)   // HoleInGround getGrayScaleDepth
        {
            float4 finalColor = SAMPLE_TEXTURE2D_LOD(_HeightTex, sampler_HeightTex, p * _NoiseTiling, 2);
            float holeSize = lerp(0.0, _HoleSize, _HoleAnim);
            float2 m = (p - _MaskRect.xy) / _MaskRect.zw;
            float holeAlpha = SAMPLE_TEXTURE2D_LOD(_MaskTex, sampler_MaskTex, m, 2).r * holeSize;
            finalColor = lerp(finalColor, float4(1, 1, 1, 1), 1 - holeAlpha);
            finalColor = lerp(finalColor, float4(1, 1, 1, 1), 1 - holeAlpha);
            return 1 - finalColor.r;
        }
        float MountainNoise(float2 p) { return SAMPLE_TEXTURE2D_LOD(_HeightTex, sampler_HeightTex, (p + float2(_Scroll, 0)) * 0.2, 0).r; }
        float GetHeight(float2 p)
        {
            if (_Variant < 0.5) return (1 - SAMPLE_TEXTURE2D_LOD(_HeightTex, sampler_HeightTex, p * _ColorTiling, 0).r) * -1 * _Depth;
            if (_Variant < 1.5) return -(exp2(GrayScaleDepth(p) * _HoleDepth) - 1);
            return (1 - MountainNoise(p)) * -1 * _Height;
        }
        float MaxDepth() { return _Variant < 0.5 ? _Depth : (_Variant < 1.5 ? exp2(_HoleDepth) - 1 : _Height); }

        float2 WeightedTexPos(float3 rayPos, float3 rayDir, float stepDistance)   // getWeightedTexPos
        {
            float3 oldPos = rayPos - stepDistance * rayDir;
            float oldDistToTerrain = abs(GetHeight(oldPos.xz) - oldPos.y);
            float currentDistToTerrain = rayPos.y - GetHeight(rayPos.xz);
            float weight = currentDistToTerrain / (currentDistToTerrain - oldDistToTerrain);
            return oldPos.xz * weight + rayPos.xz * (1 - weight);
        }

        float2 March(float2 uv, float3 tangentViewDir)
        {
            float3 rayPos = float3(uv.x, 0, uv.y);
            float3 rayDir = normalize(tangentViewDir);
            int steps = (int)_Steps;
            [loop] for (int i = 0; i < 1024; i++)
            {
                if (i >= steps) break;
                if (rayPos.y < GetHeight(rayPos.xz))
                    return _Interp > 0.5 ? WeightedTexPos(rayPos, rayDir, _StepDistance) : rayPos.xz;
                rayPos += _StepDistance * rayDir;
            }
            return rayPos.xz;   // no hit within the steps: last position
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            half4 Frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                float3 T = normalize(i.tangentWS.xyz);
                float3 B = cross(N, T) * i.tangentWS.w;
                float3 viewDir = i.positionWS - GetCameraPositionWS();   // camera to surface, as in the source
                float3 tangentViewDir = float3(dot(viewDir, T), dot(viewDir, N), dot(viewDir, B));

                float2 uv = i.uv;
                if (_Debug > 1.5)
                {   // the real geometry: one flat quad, two triangles
                    float2 q = uv / _QuadSize.xy;   // 0..1 over the quad
                    float w = 1.6;
                    float e = min(min(q.x / fwidth(q.x), (1 - q.x) / fwidth(q.x)), min(q.y / fwidth(q.y), (1 - q.y) / fwidth(q.y)));
                    float d = abs(q.x - q.y) / fwidth(q.x - q.y);
                    float edge = 1 - saturate(min(e, d) - w);
                    Light l0 = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                    half3 flat = 0.55 * (l0.color * saturate(dot(N, l0.direction)) * l0.shadowAttenuation + SampleSH(N));
                    return half4(lerp(flat, half3(1.0, 0.75, 0.2), edge), 1);
                }
                if (_Debug > 0.5)
                {
                    float h = 1 + GetHeight(uv) / MaxDepth();
                    return half4(h.xxx, 1);
                }

                float2 hit = _March > 0.5 ? March(uv, tangentViewDir) : uv;
                float2 dx = ddx(uv), dy = ddy(uv);
                half3 albedo; float3 nT;   // nT in (tangent, normal, bitangent) order
                if (_Variant < 1.5)
                {
                    float2 cuv = hit * _ColorTiling; float2 cdx = dx * _ColorTiling, cdy = dy * _ColorTiling;
                    albedo = SAMPLE_TEXTURE2D_GRAD(_ColorTex, sampler_ColorTex, cuv, cdx, cdy).rgb;
                    float3 n = UnpackNormal(SAMPLE_TEXTURE2D_GRAD(_NormalTex, sampler_NormalTex, cuv, cdx, cdy));
                    nT = float3(n.x, n.z, n.y);
                    if (_Variant > 0.5)
                    {   // the source tints the hole by its depth: lerp(black, colour, 1 - depth)
                        float g = _March > 0.5 ? GrayScaleDepth(hit) : 0;
                        albedo *= saturate(1 - g);
                    }
                }
                else
                {   // getBlendTexture: grass low, stone high, water in the lowest 20 %
                    float height = GetHeight(hit);
                    float detail = SAMPLE_TEXTURE2D_LOD(_HeightTex, sampler_HeightTex, (hit + float2(_Scroll, 0)) * 2.0, 0).r;
                    half3 grass = half3(0.20, 0.36, 0.11) * (0.7 + 0.6 * detail);
                    half3 stone = half3(0.47, 0.45, 0.42) * (0.75 + 0.5 * detail);
                    float colorGrayscale = 1 - (abs(height) / _Height);
                    albedo = lerp(grass, stone, colorGrayscale);
                    if (colorGrayscale < 0.2) albedo = MountainNoise(hit) * _WaterColor.rgb;
                    float e = 0.01;
                    float hx = GetHeight(hit + float2(e, 0)) - GetHeight(hit - float2(e, 0));
                    float hz = GetHeight(hit + float2(0, e)) - GetHeight(hit - float2(0, e));
                    nT = normalize(float3(-hx / (2 * e), 1, -hz / (2 * e)));
                    if (colorGrayscale < 0.2) nT = float3(0, 1, 0);
                    if (_March < 0.5) nT = float3(0, 1, 0);
                }
                albedo *= _Tint.rgb;
                float3 nW = normalize(T * nT.x + N * nT.y + B * nT.z);
                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 lit = albedo * (light.color * saturate(dot(nW, light.direction)) * light.shadowAttenuation + SampleSH(nW));
                return half4(MixFog(lit, i.fog), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma vertex VertShadow
            #pragma fragment FragNull
            float3 _LightDirection;
            float4 VertShadow(Attributes i) : SV_POSITION
            {
                float3 p = TransformObjectToWorld(i.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(i.normalOS);
                float4 c = TransformWorldToHClip(ApplyShadowBias(p, n, _LightDirection));
                #if UNITY_REVERSED_Z
                c.z = min(c.z, UNITY_NEAR_CLIP_VALUE);
                #else
                c.z = max(c.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return c;
            }
            half4 FragNull() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex VertDepth
            #pragma fragment FragNull
            float4 VertDepth(Attributes i) : SV_POSITION { return TransformObjectToHClip(i.positionOS.xyz); }
            half4 FragNull() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
