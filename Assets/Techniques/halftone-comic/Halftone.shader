// Based on Halftone.shader from GarrettGunnell/Post-Processing (MIT, Copyright (c) 2022 Garrett Gunnell).
// Pass 0 converts RGB to CMYK and thresholds each plate against a rotated dot pattern; pass 1 prints the plates.
// Changes are marked GT:
//  - the grid is rotated in pixel space (the source rotates UVs, which skews the grid on a non-square screen),
//    and the four screen angles are a property instead of constants (defaults are the source's 15, 75, 0, 45 degrees)
//  - the threshold is remapped from [0, 1] to [-1, 1], the range of the dot pattern; the source compares the pattern
//    with the ink value directly, which puts 50% ink on paper white. _SourceMapping = 1 restores the source compare
//  - optional anti-aliasing with the fwidth term the source computes but does not use
Shader "Hidden/GT/Halftone" {
    Properties {
        _MainTex ("Texture", 2D) = "white" {}
    }

    SubShader {

        CGINCLUDE

        #include "UnityCG.cginc"

            struct VertexData {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vp(VertexData v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 _MainTex_TexelSize;

        ENDCG

        // RGB To CMYK
        Pass {
            CGPROGRAM
            #pragma vertex vp
            #pragma fragment fp

            sampler2D _MainTex;
            float _CyanDotSize, _MagentaDotSize, _YellowDotSize, _BlackDotSize, _CyanBias, _MagentaBias, _YellowBias, _BlackBias;
            float4 _Angles;        // GT: screen angles in degrees for C, M, Y, K
            float4 _Curves;        // GT: the source's curve exponents (1, 1, 1, 0.15) as a property
            float _SourceMapping;  // GT
            float _Smooth;         // GT

            // dotSize is a frequency: the dot pitch is 2 pi / dotSize pixels
            float halftone(float2 px, float angleDeg, float v, float bias, float dotSize, float curve) {
                float a = radians(angleDeg);
                float2x2 R = float2x2(cos(a), -sin(a), sin(a), cos(a));
                float2 p = mul(px, R) * dotSize;                                   // GT: pixel space
                float halftone = (sin(p.x) + sin(p.y)) / 2.0f;
                float halftoneChange = fwidth(halftone) * 0.5f;

                float t = pow(saturate(v + bias), curve);
                float threshold = _SourceMapping > 0.5f ? pow(v + bias, curve) : t * 2.0f - 1.0f;   // GT
                float hard = halftone < threshold;
                float soft = 1.0f - smoothstep(threshold - halftoneChange, threshold + halftoneChange, halftone); // GT
                return _Smooth > 0.5f ? soft : hard;
            }

            float4 fp(v2f i) : SV_Target {
                float3 col = saturate(tex2D(_MainTex, i.uv).rgb);
                float r = col.r;
                float g = col.g;
                float b = col.b;
                float k = min(1.0f - r, min(1.0f - g, 1.0f - b));
                float3 cmy = 0.0f;
                float invK = 1.0f - k;

                if (invK != 0.0f) {
                    cmy.r = (1.0f - r - k) / invK;
                    cmy.g = (1.0f - g - k) / invK;
                    cmy.b = (1.0f - b - k) / invK;
                }

                float2 px = i.uv * _MainTex_TexelSize.zw;
                cmy.r = halftone(px, _Angles.x, cmy.r, _CyanBias, _CyanDotSize, _Curves.x);
                cmy.g = halftone(px, _Angles.y, cmy.g, _MagentaBias, _MagentaDotSize, _Curves.y);
                cmy.b = halftone(px, _Angles.z, cmy.b, _YellowBias, _YellowDotSize, _Curves.z);
                k = halftone(px, _Angles.w, k, _BlackBias, _BlackDotSize, _Curves.w);

                return float4(saturate(cmy), k);
            }
            ENDCG
        }

        // Print: subtract each plate from white, each read with its own pixel offset (misregistration)
        Pass {
            CGPROGRAM
            #pragma vertex vp
            #pragma fragment fp

            Texture2D _MainTex;
            SamplerState linear_mirror_sampler;

            int _PrintCyan, _PrintMagenta, _PrintYellow, _PrintBlack;
            float2 _CyanOffset, _MagentaOffset, _YellowOffset, _BlackOffset;

            float4 fp(v2f i) : SV_Target {

                float c = _MainTex.Sample(linear_mirror_sampler, i.uv + _CyanOffset * _MainTex_TexelSize.xy).r;
                float m = _MainTex.Sample(linear_mirror_sampler, i.uv + _MagentaOffset * _MainTex_TexelSize.xy).g;
                float y = _MainTex.Sample(linear_mirror_sampler, i.uv + _YellowOffset * _MainTex_TexelSize.xy).b;
                float k = _MainTex.Sample(linear_mirror_sampler, i.uv + _BlackOffset * _MainTex_TexelSize.xy).a;

                float3 output = 1.0f;
                output.r -= c * _PrintCyan;
                output.g -= m * _PrintMagenta;
                output.b -= y * _PrintYellow;
                return float4(saturate(output - k * _PrintBlack), 1.0f);
            }
            ENDCG
        }
    }
}
