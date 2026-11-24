// Terrain for the erosion post: a flat N x N grid displaced in the vertex shader by the height buffer the erosion
// compute writes (global _GMap, set per camera), so both halves of the split can show different maps without copying
// meshes. Normals come from neighbouring heights. My own shading: colour by slope (grass on flat ground, rock on
// steep), sediment colour where the map rose above the original noise (_GMap0), low warm sun with URP shadows,
// sky light from SH, distance fog to the horizon colour. _GMode 1 = height-change view (orange removed, teal added).
Shader "GameTech/ErosionTerrain"
{
    Properties
    {
        _Grass("Grass", Color) = (0.25, 0.33, 0.14, 1)
        _Rock("Rock", Color) = (0.50, 0.42, 0.35, 1)
        _Sediment("Sediment", Color) = (0.58, 0.50, 0.38, 1)
        _FogColor("Fog", Color) = (0.80, 0.70, 0.60, 1)
        _FogDensity("Fog density", Float) = 0.004
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
        float4 _Grass, _Rock, _Sediment, _FogColor; float _FogDensity;
        CBUFFER_END
        StructuredBuffer<float> _GMap, _GMap0;
        int _GN, _GBorder; float _GScale, _GElev, _GMode;

        float H(int x, int y) { x = clamp(x, -_GBorder, _GN - 1 + _GBorder); y = clamp(y, -_GBorder, _GN - 1 + _GBorder); return _GMap[(y + _GBorder) * (_GN + 2 * _GBorder) + x + _GBorder]; }
        float H0(int x, int y) { return _GMap0[(y + _GBorder) * (_GN + 2 * _GBorder) + x + _GBorder]; }

        struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
        struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float delta : TEXCOORD2; float2 grid : TEXCOORD3; };
        float HB(float2 g) { float2 f = frac(g); int2 i = (int2)floor(g); return lerp(lerp(H(i.x, i.y), H(i.x + 1, i.y), f.x), lerp(H(i.x, i.y + 1), H(i.x + 1, i.y + 1), f.x), f.y); }

        Varyings Vert(Attributes i)
        {
            Varyings o;
            int x = (int)(i.uv.x + 0.5), y = (int)(i.uv.y + 0.5);
            float cell = 2.0 * _GScale / (_GN - 1);
            float h = H(x, y);
            float3 ws = float3((x / (_GN - 1.0) * 2 - 1) * _GScale, h * _GElev, (y / (_GN - 1.0) * 2 - 1) * _GScale);
            float hx = (H(x + 1, y) - H(x - 1, y)) * _GElev, hz = (H(x, y + 1) - H(x, y - 1)) * _GElev;
            o.normalWS = normalize(float3(-hx, 2 * cell, -hz));
            o.positionWS = ws; o.positionCS = TransformWorldToHClip(ws);
            o.delta = (h - H0(x, y)) * _GElev; o.grid = float2(x, y);
            return o;
        }

        float3 Shade(Varyings i)
        {
            // per-pixel normal from the height buffer (bilinear), sharper than the vertex normal
            float cell = 2.0 * _GScale / (_GN - 1);
            float hx = (HB(i.grid + float2(1, 0)) - HB(i.grid - float2(1, 0))) * _GElev, hz = (HB(i.grid + float2(0, 1)) - HB(i.grid - float2(0, 1))) * _GElev;
            float3 n = normalize(float3(-hx, 2 * cell, -hz)), ws = i.positionWS;
            float flat = smoothstep(0.84, 0.9, n.y);
            float3 alb = lerp(_Rock.rgb, _Grass.rgb, flat);
            alb = lerp(alb, _Sediment.rgb, smoothstep(0.0, 0.8, i.delta) * 0.5);
            alb = lerp(alb, _Rock.rgb * 0.8, saturate(-i.delta / 0.25) * 0.6);
            if (_GMode > 0.5)
            {
                float g = 0.55;
                alb = lerp(float3(g, g, g), float3(1.0, 0.42, 0.12), saturate(-i.delta / 0.15));
                alb = lerp(alb, float3(0.1, 0.75, 0.8), saturate(i.delta / 0.15));
            }
            Light L = GetMainLight(TransformWorldToShadowCoord(ws));
            float ndl = saturate(dot(n, L.direction));
            float3 col = alb * L.color * ndl * L.shadowAttenuation + alb * SampleSH(n);
            float dist = distance(ws, _WorldSpaceCameraPos);
            float f = 1 - exp(-dist * _FogDensity);
            return lerp(col, _FogColor.rgb, f * 0.85);
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            half4 Frag(Varyings i) : SV_Target { return half4(Shade(i), 1); }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma target 4.5
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
            half4 SFrag(Varyings i) : SV_Target { return 0; }
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
