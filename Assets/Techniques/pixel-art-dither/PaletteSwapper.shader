// Copied from GarrettGunnell/Post-Processing (MIT, Copyright (c) 2022 Garrett Gunnell), Assets/Pixel Art.
// Changes are marked GT: the name, and _Snap. The source lerps between the two palette entries around grey * width, so unless
// the grey levels land exactly on entries the output holds blends that are not in the palette. _Snap = 1 picks the nearest entry.
Shader "Hidden/GT/PaletteSwapper" {
    Properties {
        _MainTex ("Texture", 2D) = "white" {}
    }

    SubShader {

        Pass {
            CGPROGRAM
            #pragma vertex vp
            #pragma fragment fp

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

            sampler2D _MainTex;
            Texture2D _ColorPalette;
            float4 _ColorPalette_TexelSize;
            SamplerState point_clamp_sampler;
            int _Invert;
            int _Snap; // GT

            fixed4 fp(v2f i) : SV_Target {
                /* Assumes _MainTex is grayscale and quantized */
                float2 uv = float2(tex2D(_MainTex, i.uv).r, 0.5f);

                uv.x = _Invert == 1 ? 1 - uv.x : uv.x;

                if (_Snap == 1) { // GT: nearest of the W entries, grey 0 -> entry 0, grey 1 -> entry W - 1
                    float idx = round(saturate(uv.x) * (_ColorPalette_TexelSize.z - 1));
                    return _ColorPalette.Sample(point_clamp_sampler, float2((idx + 0.5) * _ColorPalette_TexelSize.x, 0.5f));
                }

                float4 firstColor = _ColorPalette.Sample(point_clamp_sampler, float2(floor(_ColorPalette_TexelSize.z * uv.x) / _ColorPalette_TexelSize.z, 0.5f));
                float4 secondColor = _ColorPalette.Sample(point_clamp_sampler, float2(ceil(_ColorPalette_TexelSize.z * uv.x) / _ColorPalette_TexelSize.z, 0.5f));
            
                return lerp(firstColor, secondColor, frac(_ColorPalette_TexelSize.z * uv.x));
            }
            ENDCG
        }
    }
}