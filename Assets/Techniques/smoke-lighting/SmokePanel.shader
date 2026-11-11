// Debug panel for the 6-way smoke post: the six baked lightmaps of one flipbook frame, each multiplied by its
// live weight, and their sum times the lamp colour. Drawn as a full-viewport quad for the panel camera only.
Shader "GameTech/SmokePanel"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Smoke_RLT); TEXTURE2D(_Smoke_BBF);
            SamplerState gt_smoke_trilinear_clamp_sampler;
            float4 _Panel_W0;       // weights Right, Left, Top
            float4 _Panel_W1;       // weights Bottom, Back, Front
            float4 _Panel_Light;    // lamp colour * intensity * attenuation at the column centre
            float _Panel_Frame;     // flipbook frame shown
            float _Panel_Aspect;    // viewport width / height

            struct V { float4 positionCS : SV_POSITION; float4 sp : TEXCOORD0; };
            V Vert(float4 positionOS : POSITION)
            {
                V o; o.positionCS = float4(positionOS.xy * 2, UNITY_NEAR_CLIP_VALUE, 1); o.sp = ComputeScreenPos(o.positionCS); return o;
            }

            float2 FrameUV(float2 uv)
            {
                float c = fmod(_Panel_Frame, 4.0), r = floor(_Panel_Frame / 4.0);
                return (float2(c, r) + clamp(uv, 0.006, 0.994)) * 0.25;
            }

            // tile with lower-left corner at o and width w (height follows from the aspect); returns local uv or -1
            float2 Tile(float2 uv, float2 o, float w)
            {
                float2 l = (uv - o) / float2(w, w * _Panel_Aspect);
                return (l.x >= 0 && l.x <= 1 && l.y >= 0 && l.y <= 1) ? l : float2(-1, -1);
            }
            float Frame(float2 l) { float2 d = min(l, 1 - l); return step(min(d.x, d.y), 0.012); }

            half4 Frag(V i) : SV_Target
            {
                float2 uv = i.sp.xy / i.sp.w;
                half3 col = half3(0.012, 0.014, 0.018);
                const float w = 0.26;
                float xs[3] = { 0.06, 0.37, 0.68 };
                for (int k = 0; k < 3; k++)
                {
                    float2 a = Tile(uv, float2(xs[k], 0.66), w);
                    if (a.x >= 0)
                    {
                        half4 m = SAMPLE_TEXTURE2D_LOD(_Smoke_RLT, gt_smoke_trilinear_clamp_sampler, FrameUV(a), 0);
                        col = lerp(m[k] * _Panel_W0[k], 0.22, Frame(a));
                    }
                    float2 b = Tile(uv, float2(xs[k], 0.375), w);
                    if (b.x >= 0)
                    {
                        half4 m = SAMPLE_TEXTURE2D_LOD(_Smoke_BBF, gt_smoke_trilinear_clamp_sampler, FrameUV(b), 0);
                        col = lerp(m[k] * _Panel_W1[k], 0.22, Frame(b));
                    }
                }
                float2 s = Tile(uv, float2(0.06, 0.045), 0.30);
                if (s.x >= 0)
                {
                    half4 m0 = SAMPLE_TEXTURE2D_LOD(_Smoke_RLT, gt_smoke_trilinear_clamp_sampler, FrameUV(s), 0);
                    half4 m1 = SAMPLE_TEXTURE2D_LOD(_Smoke_BBF, gt_smoke_trilinear_clamp_sampler, FrameUV(s), 0);
                    half sum = dot(m0.rgb, _Panel_W0.rgb) + dot(m1.rgb, _Panel_W1.rgb);
                    half3 lit = sum * _Panel_Light.rgb;
                    lit = lit / (1.0 + lit);                       // simple tone curve for the display tile
                    col = lerp(lit, half3(0.5, 0.36, 0.2), Frame(s));
                }
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
