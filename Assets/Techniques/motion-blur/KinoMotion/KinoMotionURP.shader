// URP / HLSL port of Kino Motion's Reconstruction.shader (+ Prefilter.cginc, Reconstruction.cginc) and the raw
// path of FrameBlending.shader. keijiro/KinoMotion, MIT, Copyright (c) 2016, 2017 Keijiro Takahashi.
// Same passes, same maths. Changes: URP's _MotionVectorTexture and scene depth instead of the built-in camera
// textures, Blitter's full-screen triangle instead of vert_img, texel sizes passed as globals per blit.
Shader "Hidden/GameTech/KinoMotionURP"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZTest Always Cull Off ZWrite Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

        TEXTURE2D_X(_MotionVectorTexture);
        TEXTURE2D_X(_VelocityTex);
        TEXTURE2D_X(_NeighborMaxTex);
        TEXTURE2D_X(_History1Tex); TEXTURE2D_X(_History2Tex); TEXTURE2D_X(_History3Tex); TEXTURE2D_X(_History4Tex);

        float4 _KmMainTexel;   // texel size of the blit source: (1/w, 1/h, w, h)
        float4 _KmVelTexel;    // texel size of _VelocityTex
        float4 _KmNbrTexel;    // texel size of _NeighborMaxTex
        float _VelocityScale, _MaxBlurRadius, _RcpMaxBlurRadius, _LoopCount;
        float _TileMaxLoop;
        float2 _TileMaxOffs;
        float _History1Weight, _History2Weight, _History3Weight, _History4Weight;

        float2 Src(float2 uv) { return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0).rg; }

        // ---- Prefilter.cginc ----
        float2 VMax(float2 v1, float2 v2) { return dot(v1, v1) < dot(v2, v2) ? v2 : v1; }

        float4 FragVelocitySetup(Varyings i) : SV_Target
        {
            float2 v = SAMPLE_TEXTURE2D_X_LOD(_MotionVectorTexture, sampler_PointClamp, i.texcoord, 0).rg;
            // Motion vector (UV units) to the half-length of the blur in pixels, clamped to the max radius
            v *= (_VelocityScale * 0.5) * _KmMainTexel.zw;
            v /= max(1, length(v) * _RcpMaxBlurRadius);
            float d = Linear01Depth(SampleSceneDepth(i.texcoord), _ZBufferParams);
            return float4((v * _RcpMaxBlurRadius + 1) / 2, d, 0);
        }

        float4 FragTileMax1(Varyings i) : SV_Target
        {
            float4 d = _KmMainTexel.xyxy * float4(-0.5, -0.5, 0.5, 0.5);
            float2 v1 = (Src(i.texcoord + d.xy) * 2 - 1) * _MaxBlurRadius;
            float2 v2 = (Src(i.texcoord + d.zy) * 2 - 1) * _MaxBlurRadius;
            float2 v3 = (Src(i.texcoord + d.xw) * 2 - 1) * _MaxBlurRadius;
            float2 v4 = (Src(i.texcoord + d.zw) * 2 - 1) * _MaxBlurRadius;
            return float4(VMax(VMax(VMax(v1, v2), v3), v4), 0, 0);
        }

        float4 FragTileMax2(Varyings i) : SV_Target
        {
            float4 d = _KmMainTexel.xyxy * float4(-0.5, -0.5, 0.5, 0.5);
            float2 v1 = Src(i.texcoord + d.xy), v2 = Src(i.texcoord + d.zy), v3 = Src(i.texcoord + d.xw), v4 = Src(i.texcoord + d.zw);
            return float4(VMax(VMax(VMax(v1, v2), v3), v4), 0, 0);
        }

        float4 FragTileMaxV(Varyings i) : SV_Target
        {
            float2 uv0 = i.texcoord + _KmMainTexel.xy * _TileMaxOffs.xy;
            float2 du = float2(_KmMainTexel.x, 0), dv = float2(0, _KmMainTexel.y);
            float2 vo = 0;
            int n = (int)_TileMaxLoop;
            for (int ix = 0; ix < n; ix++)
                for (int iy = 0; iy < n; iy++)
                    vo = VMax(vo, Src(uv0 + du * ix + dv * iy));
            return float4(vo, 0, 0);
        }

        float4 FragNeighborMax(Varyings i) : SV_Target
        {
            const float cw = 1.01f; // center weight tweak
            float4 d = _KmMainTexel.xyxy * float4(1, 1, -1, 0);
            float2 v1 = Src(i.texcoord - d.xy), v2 = Src(i.texcoord - d.wy), v3 = Src(i.texcoord - d.zy);
            float2 v4 = Src(i.texcoord - d.xw), v5 = Src(i.texcoord) * cw, v6 = Src(i.texcoord + d.xw);
            float2 v7 = Src(i.texcoord + d.zy), v8 = Src(i.texcoord + d.wy), v9 = Src(i.texcoord + d.xy);
            float2 va = VMax(v1, VMax(v2, v3)), vb = VMax(v4, VMax(v5, v6)), vc = VMax(v7, VMax(v8, v9));
            return float4(VMax(va, VMax(vb, vc)) / cw, 0, 0);
        }

        // ---- Reconstruction.cginc ----
        bool Interval(float phase, float interval) { return frac(phase / interval) > 0.499; }

        float GradientNoise(float2 uv)
        {
            uv = floor((uv + _Time.y) * _ScreenParams.xy);
            float f = dot(float2(0.06711056f, 0.00583715f), uv);
            return frac(52.9829189f * frac(f));
        }

        float2 JitterTile(float2 uv)
        {
            float rx, ry;
            sincos(GradientNoise(uv + float2(2, 0)) * PI * 2, ry, rx);
            return float2(rx, ry) * _KmNbrTexel.xy / 4;
        }

        float3 SampleVelocity(float2 uv)
        {
            float3 v = SAMPLE_TEXTURE2D_X_LOD(_VelocityTex, sampler_PointClamp, uv, 0).xyz;
            return float3((v.xy * 2 - 1) * _MaxBlurRadius, v.z);
        }

        float4 FragReconstruction(Varyings i) : SV_Target
        {
            float2 uv = i.texcoord;
            const float4 c_p = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);
            const float3 vd_p = SampleVelocity(uv);
            const float l_v_p = max(length(vd_p.xy), 0.5);
            const float rcp_d_p = 1 / vd_p.z;
            const float2 v_max = SAMPLE_TEXTURE2D_X_LOD(_NeighborMaxTex, sampler_PointClamp, uv + JitterTile(uv), 0).xy;
            const float l_v_max = length(v_max);
            if (l_v_max < 2) return c_p;
            const float2 v_alt = (l_v_p * 2 > l_v_max) ? vd_p.xy * (l_v_max / l_v_p) : v_max;
            const float sc = floor(min(_LoopCount, l_v_max / 2));
            const float dt = 1 / sc;
            const float t_offs = (GradientNoise(uv) - 0.5) * dt;
            float t = 1 - dt / 2;
            float count = 0;
            float l_v_bg = max(l_v_p, 1);
            float4 acc = 0;
            [loop] while (t > dt / 4)
            {
                const float2 v_s = Interval(count, 4) ? v_alt : v_max;
                const float t_s = (Interval(count, 2) ? -t : t) + t_offs;
                const float l_t = l_v_max * abs(t_s);
                const float2 uv0 = uv + v_s * t_s * _KmMainTexel.xy;
                const float2 uv1 = uv + v_s * t_s * _KmVelTexel.xy;
                const float3 c = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv0, 0).rgb;
                const float3 vd = SampleVelocity(uv1);
                const float fg = saturate((vd_p.z - vd.z) * 20 * rcp_d_p);
                const float l_v = lerp(l_v_bg, length(vd.xy), fg);
                const float w = saturate(l_v - l_t) / l_v * (1.2 - t);
                acc += float4(c, 1) * w;
                l_v_bg = max(l_v_bg, l_v);
                t = Interval(count, 2) ? t - dt : t;
                count += 1;
            }
            acc += float4(c_p.rgb, 1) * (1.2 / (l_v_bg * sc * 2));
            return float4(acc.rgb / acc.a, c_p.a);
        }

        // ---- FrameBlending.shader, raw (uncompressed) path ----
        float4 FragFrameBlendingRaw(Varyings i) : SV_Target
        {
            float4 src = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, i.texcoord, 0);
            float3 acc = src.rgb;
            acc += SAMPLE_TEXTURE2D_X_LOD(_History1Tex, sampler_PointClamp, i.texcoord, 0).rgb * _History1Weight;
            acc += SAMPLE_TEXTURE2D_X_LOD(_History2Tex, sampler_PointClamp, i.texcoord, 0).rgb * _History2Weight;
            acc += SAMPLE_TEXTURE2D_X_LOD(_History3Tex, sampler_PointClamp, i.texcoord, 0).rgb * _History3Weight;
            acc += SAMPLE_TEXTURE2D_X_LOD(_History4Tex, sampler_PointClamp, i.texcoord, 0).rgb * _History4Weight;
            acc /= 1 + _History1Weight + _History2Weight + _History3Weight + _History4Weight;
            return float4(acc, src.a);
        }
        ENDHLSL

        Pass { Name "VelocitySetup" HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragVelocitySetup
            ENDHLSL }
        Pass { Name "TileMax1" HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragTileMax1
            ENDHLSL }
        Pass { Name "TileMax2" HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragTileMax2
            ENDHLSL }
        Pass { Name "TileMaxV" HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragTileMaxV
            ENDHLSL }
        Pass { Name "NeighborMax" HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragNeighborMax
            ENDHLSL }
        Pass { Name "Reconstruction" HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragReconstruction
            ENDHLSL }
        Pass { Name "FrameBlendingRaw" HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragFrameBlendingRaw
            ENDHLSL }
    }
}
