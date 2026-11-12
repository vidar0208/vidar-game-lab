// URP / HLSL port of the paint and fluid shaders of EsProgram/InkPainter (MIT, Copyright (c) 2017 Es_Program):
// PaintMain, PaintNormal, PaintHeight, InkPainterFoundation, HeightDrip, HeightToColor, HeightToNormal, InvertAlpha.
// One pass per source shader, used with Graphics.Blit. Changes from the source:
//  - the brush footprint is measured in metres (hit UV offset x metres per UV), so a stamp is not stretched
//    on surfaces whose UVs are not square; the source measures it in UV units.
//  - normals are stored as plain RGB (x, y, z, 1) instead of the DXT5nm swizzle, and the brush normal is rotated
//    together with the brush.
Shader "Hidden/GameTech/InkPaint"
{
    Properties { _MainTex ("", 2D) = "black" {} }
    SubShader
    {
        ZTest Always Cull Off ZWrite Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_MainTex); float4 _MainTex_TexelSize;
        TEXTURE2D(_Brush); TEXTURE2D(_BrushNormal); TEXTURE2D(_BrushHeight);
        TEXTURE2D(_NormalMap); TEXTURE2D(_ColorMap); TEXTURE2D(_BaseColorTex); TEXTURE2D(_BumpMap);
        SAMPLER(sampler_linear_clamp);
        float4 _PaintUV; float4 _UVToMetres; float _BrushSize; float _BrushRotate; float4 _ControlColor;
        float _NormalBlend; float _HeightBlend; float4 _Color;
        float _ScaleFactor, _Viscosity, _HorizontalSpread, _Alpha, _Border, _NormalScaleFactor;
        float4 _FlowDirection; float4 _FixedColor;

        struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
        struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
        V vert(A i) { V o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.uv = i.uv; return o; }

        float4 Tex(TEXTURE2D_PARAM(t, s), float2 uv) { return SAMPLE_TEXTURE2D_LOD(t, s, uv, 0); }
        #define SAMPLE(t, uv) Tex(TEXTURE2D_ARGS(t, sampler_linear_clamp), uv)

        // ---- InkPainterFoundation
        bool ExistPointInTriangle(float3 p, float3 t1, float3 t2, float3 t3)
        {
            const float TOLERANCE = 1 - 0.1;
            float3 a = normalize(cross(t1 - t3, p - t1));
            float3 b = normalize(cross(t2 - t1, p - t2));
            float3 c = normalize(cross(t3 - t2, p - t3));
            return TOLERANCE < dot(a, b) && TOLERANCE < dot(b, c);
        }
        float2 Rotate(float2 p, float degree)
        {
            float rad = radians(degree);
            return float2(p.x * cos(rad) - p.y * sin(rad), p.x * sin(rad) + p.y * cos(rad));
        }
        // offset from the hit point in metres on the surface
        float2 Offset(float2 mainUV) { return (mainUV - _PaintUV.xy) * _UVToMetres.xy; }
        bool IsPaintRange(float2 mainUV)
        {
            float s = _BrushSize, deg = _BrushRotate;
            float3 p = float3(Offset(mainUV), 0);
            float3 v1 = float3(Rotate(float2(-s, s), deg), 0);
            float3 v2 = float3(Rotate(float2(-s, -s), deg), 0);
            float3 v3 = float3(Rotate(float2(s, -s), deg), 0);
            float3 v4 = float3(Rotate(float2(s, s), deg), 0);
            return ExistPointInTriangle(p, v1, v2, v3) || ExistPointInTriangle(p, v1, v3, v4);
        }
        float2 CalcBrushUV(float2 mainUV) { return Rotate(Offset(mainUV) / _BrushSize, -_BrushRotate) * 0.5 + 0.5; }
        // ColorBlend, USE_CONTROL / ALPHA_ONLY
        float4 ColorBlend(float4 targetColor, float4 mainColor, float blend) { return mainColor * (1 - blend * targetColor.a) + targetColor * targetColor.a * blend; }
        float3 Unpack(float4 n) { return n.xyz * 2 - 1; }
        float4 Pack(float3 n) { return float4(n * 0.5 + 0.5, 1); }
        float rand(float3 seed) { return frac(sin(dot(seed.xyz, float3(12.9898, 78.233, 56.787))) * 43758.5453); }
        ENDHLSL

        // 0: PaintMain (USE_CONTROL; _Alpha > 0.5 selects ALPHA_ONLY, used while the fluid is on)
        Pass { HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(V i) : SV_Target
            {
                float4 base = SAMPLE(_MainTex, i.uv);
                if (IsPaintRange(i.uv))
                {
                    float4 brushColor = SAMPLE(_Brush, CalcBrushUV(i.uv));
                    float4 target = _ControlColor;
                    if (_Alpha > 0.5) { target = base; target.a = _ControlColor.a; }
                    return ColorBlend(target, base, brushColor.a);
                }
                return base;
            }
        ENDHLSL }

        // 1: PaintNormal (USE_BRUSH)
        Pass { HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(V i) : SV_Target
            {
                float4 base = SAMPLE(_MainTex, i.uv);
                if (IsPaintRange(i.uv))
                {
                    float2 buv = CalcBrushUV(i.uv);
                    float4 brushColor = SAMPLE(_Brush, buv);
                    if (brushColor.a > 0)
                    {
                        float3 n = Unpack(SAMPLE(_BrushNormal, buv));
                        n.xy = Rotate(n.xy, _BrushRotate);
                        return lerp(base, Pack(normalize(n)), _NormalBlend * brushColor.a);
                    }
                }
                return base;
            }
        ENDHLSL }

        // 2: PaintHeight (COLOR_RGB_HEIGHT_A)
        Pass { HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(V i) : SV_Target
            {
                float4 base = SAMPLE(_MainTex, i.uv);
                if (IsPaintRange(i.uv))
                {
                    float2 buv = CalcBrushUV(i.uv);
                    float4 brushColor = SAMPLE(_Brush, buv);
                    if (brushColor.a > 0)
                    {
                        float4 height = SAMPLE(_BrushHeight, buv);
                        height.a = 0.299 * height.r + 0.587 * height.g + 0.114 * height.b;
                        height.rgb = _Color.rgb;
                        brushColor.a = _Color.a;
                        return float4(lerp(brushColor.rgb, height.rgb, brushColor.a), lerp(base.a, height.a, _HeightBlend));
                    }
                }
                return base;
            }
        ENDHLSL }

        // 3: HeightDrip (COLOR_SYNTHESIS_OVERWRITE)
        Pass { HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(V i) : SV_Target
            {
                float4 col = SAMPLE(_MainTex, i.uv);
                float3 normal = normalize(Unpack(SAMPLE(_NormalMap, i.uv)));
                float VITIATE_Z = pow(normal.b, 2) - normal.y * 0.2;
                float VITIATE_X = _HorizontalSpread * rand(float3(i.uv.xy, i.uv.x + i.uv.y)) * (1 + normal.b * 30);
                float2 texel = _MainTex_TexelSize.xy;
                float2 shiftZ = float2(_FlowDirection.x * texel.x, _FlowDirection.y * texel.y) * _ScaleFactor * _Viscosity * VITIATE_Z;
                float2 shiftX = float2(texel.x * _FlowDirection.y, texel.y * _FlowDirection.x) * _ScaleFactor * _Viscosity * VITIATE_X;
                float4 texZ = SAMPLE(_MainTex, clamp(i.uv + shiftZ, 0, 1));
                float4 texx = SAMPLE(_MainTex, clamp(i.uv - shiftX + shiftZ, 0, 1));
                float4 texX = SAMPLE(_MainTex, clamp(i.uv + shiftX + shiftZ, 0, 1));
                float amountUp = (texZ.a + texx.a + texX.a) * 0.3333;
                if (amountUp > (1 - _Viscosity))
                    return float4(_FixedColor.rgb, (col.a + amountUp) * 0.5);
                return col;
            }
        ENDHLSL }

        // 4: HeightToColor
        Pass { HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(V i) : SV_Target
            {
                float4 h = SAMPLE(_MainTex, i.uv);
                if (h.a > _Border) return lerp(SAMPLE(_BaseColorTex, i.uv), float4(h.rgb, 1), _Alpha);
                return SAMPLE(_ColorMap, i.uv);
            }
        ENDHLSL }

        // 5: HeightToNormal
        Pass { HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(V i) : SV_Target
            {
                float2 sx = float2(_MainTex_TexelSize.x, 0), sz = float2(0, _MainTex_TexelSize.y);
                float aX = 2 * SAMPLE(_MainTex, i.uv + sx).a - 1, ax = 2 * SAMPLE(_MainTex, i.uv - sx).a - 1;
                float aZ = 2 * SAMPLE(_MainTex, i.uv + sz).a - 1, az = 2 * SAMPLE(_MainTex, i.uv - sz).a - 1;
                float3 du = float3(1, 0, _NormalScaleFactor * (aX - ax));
                float3 dv = float3(0, 1, _NormalScaleFactor * (aZ - az));
                float3 n = normalize(cross(du, dv));
                if (SAMPLE(_MainTex, i.uv).a <= _Border) return SAMPLE(_BumpMap, i.uv);
                return Pack(n);
            }
        ENDHLSL }

        // 6: InvertAlpha (ARGB32 clamps the negative alpha to 0, which ends the drip)
        Pass { HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(V i) : SV_Target { float4 c = SAMPLE(_MainTex, i.uv); c.a = -c.a; return c; }
        ENDHLSL }

        // 7: SingleColorFill
        Pass { HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(V i) : SV_Target { return _Color; }
        ENDHLSL }
    }
}
