// URP port of the source's "Water-RW/With Compute" shader (GameTech). Same vertex step (surface displaced by the
// 1D wave buffer) and the same fragment steps (mirror the screen about the surface line for the reflection, offset
// for the refraction, sine ripple near the surface, two scrolling normal maps, fade, transparency, background
// blend, colour multiply/add, surface line). Changes: reads URP's opaque texture instead of the 2D Renderer's
// camera sorting layer texture, so it works with the normal 3D renderer; normal maps are decoded as plain RG;
// the surface line uses the absolute screen height; the wave height scale (0.1 in the source) is a property;
// a global _GT_WaterRR (per camera) switches reflection/refraction off for the comparison.
Shader "GameTech/WaterRW"
{
    Properties
    {
        [PerRendererData] _MainTex ("Wave Texture", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _NormalA("Normal A", 2D) = "gray" {}
        _NormalAAmount("Normal A Intensity", Range(0,1)) = 0.026
        _NormalASpeed("Normal A Speed", Vector) = (0.2, 0.2, 0, 0)
        _NormalB("Normal B", 2D) = "gray" {}
        _NormalBAmount("Normal B Intensity", Range(0,1)) = 0.038
        _NormalBSpeed("Normal B Speed", Vector) = (-0.3, 0.3, 0, 0)
        _BGBlend("Background Blend", Range(0,1)) = 0.21
        _Transparency("Transparency", Range(0,1)) = 0.781
        _Multiplier("Multiplier", Color) = (0.693, 0.938, 1.0, 1.0)
        _Addend("Addend", Color) = (0.089, 0.250, 0.330, 0.0)
        _WaveSize("Wave Size in Viewport Space", Range(0, 1)) = 0.029
        _WaveDistance("Wave Distance in Viewport Space", Range(0, 1)) = 0.166
        _WavePosFreq("Wave Frequency by Position", Float) = 127.9
        _WaveTimeFreq("Wave Frequency by Time", Float) = 18.5
        _SurfaceColor("Surface Color", Color) = (0.438, 0.489, 0.623, 0.812)
        _SurfaceWidth("Surface Width in Pixel", Float) = 4
        _FadeDistance("Fade Distance in Viewport Space", Float) = 128
        _SmoothBufferEdge("Smooth Buffer Edge in World Space", Float) = 0.5
        _WavePositionLocal("Scrolled Position", Float) = 0
        _WaveBufferPixelsPerUnit("Buffer pixels per unit", Float) = 4
        _Reflection("Reflection Intensity", Range(0, 1)) = 1.0
        _WaveHeight("Wave height per buffer unit", Float) = 0.1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Blend One OneMinusSrcAlpha
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_NormalA); SAMPLER(sampler_NormalA);
            TEXTURE2D(_NormalB); SAMPLER(sampler_NormalB);
            CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_TexelSize, _NormalA_ST, _NormalB_ST;
            float4 _Color, _NormalASpeed, _NormalBSpeed, _Multiplier, _Addend, _SurfaceColor;
            float _NormalAAmount, _NormalBAmount, _BGBlend, _Transparency, _WaveSize, _WaveDistance, _WavePosFreq, _WaveTimeFreq;
            float _SurfaceWidth, _FadeDistance, _SmoothBufferEdge, _WavePositionLocal, _Reflection, _WaveHeight, _WaveBufferPixelsPerUnit;
            CBUFFER_END
            float _GT_WaterRR;

            struct Attributes { float4 vertex : POSITION; float2 texcoord : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 texcoord : TEXCOORD0; float4 screen : TEXCOORD1; float3 world : TEXCOORD2; };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                float4 v = float4(IN.vertex.xy, 0, 1);
                float bufferWidth = _MainTex_TexelSize.z;
                float3 posWorldScale = mul((float3x3)UNITY_MATRIX_M, v.xyz);
                float posWorld = posWorldScale.x - _WavePositionLocal;
                float posInPixel = posWorld * _WaveBufferPixelsPerUnit + bufferWidth * 0.5;
                float posInUv = posInPixel / bufferWidth;
                float wave = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, float2(posInUv, 0.5), 0).r;
                float smooth = _SmoothBufferEdge * _WaveBufferPixelsPerUnit / bufferWidth;
                float edge = smoothstep(0, smooth, posInUv) * (1 - smoothstep(1 - smooth, 1, posInUv));
                float w = wave * edge;
                float3 world = TransformObjectToWorld(v.xyz);
                world.y += w * _WaveHeight * (v.y + 0.5);
                OUT.positionCS = TransformWorldToHClip(world);
                OUT.screen = ComputeScreenPos(OUT.positionCS);
                OUT.world = world;
                OUT.texcoord = IN.texcoord;
                return OUT;
            }

            float2 DecodeRG(float4 t) { return t.rg * 2 - 1; }

            half4 Frag(Varyings IN) : SV_Target
            {
                float4 unitPosClip = TransformObjectToHClip(float3(0, 0.5, 0));
                float4 unitPosScreen = ComputeScreenPos(unitPosClip); unitPosScreen.xy /= unitPosScreen.w;
                float4 negUnitPosClip = TransformObjectToHClip(float3(0, -0.5, 0));
                float4 negUnitPosScreen = ComputeScreenPos(negUnitPosClip); negUnitPosScreen.xy /= negUnitPosScreen.w;

                float surfacePosViewport = unitPosScreen.y;
                float2 fragPosViewport = IN.screen.xy / IN.screen.w;
                float reflectedPosViewport = 2 * surfacePosViewport - fragPosViewport.y;
                float2 reflectedPos = float2(fragPosViewport.x, reflectedPosViewport);
                float2 refractedPos = fragPosViewport;

                float distFade = saturate(min(1, (1 - reflectedPos.y) / min(_FadeDistance, 1 - surfacePosViewport)));

                float wave = max(0, 1 - (surfacePosViewport - fragPosViewport.y) / _WaveDistance);
                float deltaWave = sin(_Time.y * _WaveTimeFreq + (surfacePosViewport - fragPosViewport.y) * _WavePosFreq) * (_WaveSize * 0.5) * pow(wave, 2);
                reflectedPos.x += deltaWave;
                refractedPos.x += -deltaWave;

                float2 posNormal = IN.world.xy / 10;
                float2 deltaNormal = 0;
                float2 posNormalA = posNormal + _NormalASpeed.xy * _Time.y;
                deltaNormal += DecodeRG(SAMPLE_TEXTURE2D(_NormalA, sampler_NormalA, frac(posNormalA * _NormalA_ST.xy))) * _NormalAAmount;
                float2 posNormalB = posNormal + _NormalBSpeed.xy * _Time.y;
                deltaNormal += DecodeRG(SAMPLE_TEXTURE2D(_NormalB, sampler_NormalB, frac(posNormalB * _NormalB_ST.xy))) * _NormalBAmount;
                reflectedPos += deltaNormal;
                refractedPos += -deltaNormal;

                float surfaceWidthScreen = _SurfaceWidth / _ScreenParams.y;
                float surface = step((1 - IN.texcoord.y) * abs(unitPosScreen.y - negUnitPosScreen.y), surfaceWidthScreen);

                float4 col;
                if (_GT_WaterRR > 0.5)
                {
                    float4 bg = float4(SampleSceneColor(refractedPos), 1);
                    col = float4(SampleSceneColor(reflectedPos), 1) * _Reflection;
                    col.rgb *= step(reflectedPos.y, 1) * step(0, reflectedPos.y);
                    col *= distFade;
                    col.a = (1 - _Transparency) + (col.r + col.g + col.b) / 3 * _Transparency;
                    col = lerp(col, bg, _BGBlend);
                    col.rgb *= _Multiplier.rgb;
                    col.rgb += _Addend.rgb;
                }
                else
                {
                    // comparison: the same water without the screen reads, an opaque colour with a little depth shading
                    col = float4(_Addend.rgb * (0.75 + 0.5 * IN.texcoord.y), 1);
                }
                col = lerp(col, _SurfaceColor, surface);
                col *= _Color;
                col.rgb *= col.a;
                return col;
            }
            ENDHLSL
        }
    }
}
