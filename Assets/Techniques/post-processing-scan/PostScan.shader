// URP rewrite of "Mirza Beig/Post-Processing Scan" (MirzaBeig/Post-Processing-Scan, Unlicense).
// Same maths as the source fragment shader: world position rebuilt from the depth buffer, distance to _Origin (a
// spherical SDF), rings from frac(sdf * tiling - time * speed) raised to a power, times a radius mask, then
// lerp(screen, colour, scan). Differences: drawn as one full-viewport quad after the opaque geometry (alpha blend does
// the final lerp), parameters are global values set by the timeline, and three debug outputs were added.
Shader "GameTech/PostScan"
{
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+100" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off ZTest Always ZWrite Off
            Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            float4 _Scan_Colour;        // HDR colour, alpha scales the whole effect (source: _Colour)
            float3 _Scan_Origin;        // world position of the scan centre (source: _Origin)
            float _Scan_Tiling, _Scan_Speed, _Scan_Power;
            float _Scan_MaskRadius, _Scan_MaskHardness, _Scan_MaskPower, _Scan_MultiplyBlend;
            float _Scan_Time;           // seconds on the video timeline (source: _Time.y)
            float _Scan_Debug;          // 0 = effect, 1 = world position, 2 = SDF, 3 = ring mask

            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(float4 positionOS : POSITION)
            {
                Varyings o; o.positionCS = float4(positionOS.xy * 2, UNITY_NEAR_CLIP_VALUE, 1); return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(i.positionCS);
                float raw = SampleSceneDepth(uv);
                #if UNITY_REVERSED_Z
                bool sky = raw <= 0.000001;
                float depth = raw;
                #else
                bool sky = raw >= 0.999999;
                float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, raw);
                #endif
                float3 wp = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);

                float sdf = distance(wp, _Scan_Origin);
                float edge = _Scan_MaskRadius + 1.0;
                float inner = lerp(0.0, edge - 0.001, _Scan_MaskHardness);
                float t = saturate((sdf - edge) / (inner - edge));
                float mask = pow(t * t * (3.0 - 2.0 * t), _Scan_MaskPower);            // smoothstep(edge, inner, sdf) ^ power
                float ring = pow(frac(sdf * _Scan_Tiling - _Scan_Time * _Scan_Speed), _Scan_Power);
                float scan = ring * mask * _Scan_Colour.a;
                if (sky) scan = 0;

                if (_Scan_Debug > 0.5)
                {
                    half3 c;
                    if (_Scan_Debug < 1.5) c = sky ? 0 : frac(wp) * 0.85;                  // 1 m cells of world x, y, z
                    else if (_Scan_Debug < 2.5)
                    {
                        float g = saturate(1.0 - sdf / edge);
                        float line1 = 1.0 - smoothstep(0.0, 0.035, abs(frac(sdf + 0.5) - 0.5));   // one line per metre
                        c = sky ? 0 : lerp(g * 0.8, 1.0, line1 * 0.6) * half3(1, 1, 1);
                    }
                    else c = sky ? 0 : (ring * mask) * half3(1, 1, 1);
                    return half4(c, 1);
                }

                half3 screen = SampleSceneColor(uv);
                half3 scanColour = lerp(_Scan_Colour.rgb, _Scan_Colour.rgb * screen, _Scan_MultiplyBlend);
                return half4(scanColour * scan, scan);   // + screen * (1 - scan) through the blend state
            }
            ENDHLSL
        }
    }
}
