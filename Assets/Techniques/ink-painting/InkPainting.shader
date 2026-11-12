// URP / HLSL port of sacshadow/3D_ChineseInkPaintingStyleShader (MIT, Copyright (c) 2023 sacshadow),
// shader CIPR_2_Ran_And_Other_RimLight: the texture pass (Gou inline + CunCa strokes + Ran wash) and the two
// back-face outline passes "Outline 0" and "Outline 1", with the same property names and meanings.
// Changes: one shader with three passes (UniversalForward, SRPDefaultUnlit, UniversalForwardOnly) instead of the
// Built-in passes; triplanar blend weights use abs(normal) (the source lerps with the signed normal); _InkStage
// switches the three steps on one by one for the video; _PaperTex multiplies a paper grain in screen space;
// _BaseMap / _AlbedoMix give the character a faint tint of its own colours; _FeiBai and _Debug are video switches.
// The source ran in a gamma-space project, so the colour maths here is done on gamma values and converted at the end.
Shader "GameTech/InkPainting"
{
    Properties
    {
        _ColorRemap ("Color Remap", 2D) = "black" {}
        _DistanceCtrl ("Distance {MaxDistance, outlineDrop, rimClose, rimFar}", Vector) = (64, 0.49, 5.28, 1.6)
        _RimColor ("Rim Color", Color) = (0.236, 0.236, 0.236, 1)
        _LambertRim ("Lambert <=> RimLight", Range(0, 1)) = 0.75
        _InlineControl ("Inline Ctrl{range, step, noiseSize, cutoff}", Vector) = (0.6, 0.8, 4.87, 0.33)
        _OutlineColor ("Outline Color", Color) = (0.896, 0.896, 0.896, 1)
        _OutlineNoiseFB ("Outline FeiBai", 2D) = "white" {}
        _OutlineNoiseColor ("Outline Noise Color", 2D) = "white" {}
        _OutlineCtrl ("Outline Ctrl{width 0, width 1, period, amount}", Vector) = (12, 2.5, 4, 0.25)
        _OutlineNoiseCtrl ("ON Ctrl{size0, cutoff0, size1, cutoff1}", Vector) = (1.67, 3.5, 9, 5)
        _OutlineColorRand ("OC Rand{size, range,,}", Vector) = (200, 2, 1, 1)
        _Stroke ("Stroke", 2D) = "white" {}
        _Noise ("Noise", 2D) = "white" {}
        _Size ("Size {cun, ca, ran, noise}", Vector) = (7.5, 10, 50, 10)
        _AreaBegin ("Area Begin{cun, ca, ran, noise_cun}", Vector) = (0.15, 0.1, 0.05, 5.15)
        _AreaEnd ("Area End{cun, ca, ran, noise_ca}", Vector) = (0.5, 0.7, 1.1, 1.68)
        _Dark ("Dark {cun, ca, ran, noise}", Vector) = (2.34, 2, 1, 2.5)
        _PaperTex ("Paper", 2D) = "white" {}
        _BaseMap ("Albedo (character tint)", 2D) = "white" {}
        _AlbedoMix ("Albedo mix", Range(0, 1)) = 0
        _FeiBai ("FeiBai on", Float) = 1
        _Debug ("Debug: 1 = distance remap", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_ColorRemap); TEXTURE2D(_OutlineNoiseFB); TEXTURE2D(_OutlineNoiseColor);
        TEXTURE2D(_Stroke); TEXTURE2D(_Noise); TEXTURE2D(_PaperTex); TEXTURE2D(_BaseMap);
        SAMPLER(sampler_linear_repeat); SAMPLER(sampler_linear_clamp);
        CBUFFER_START(UnityPerMaterial)
            float4 _DistanceCtrl, _RimColor, _InlineControl, _OutlineColor, _OutlineCtrl, _OutlineNoiseCtrl, _OutlineColorRand;
            float4 _Size, _AreaBegin, _AreaEnd, _Dark, _BaseMap_ST;
            float _LambertRim, _AlbedoMix, _FeiBai, _Debug;
        CBUFFER_END
        float _InkStage;   // global: 0 = Gou only, 1 = Gou + CunCa, 2 = Gou + CunCa + Ran

        #define TEX(t, uv) SAMPLE_TEXTURE2D(t, sampler_linear_repeat, uv)
        float4 RemapColor(float dist01, float2 screenPx)
        {   // ColorRemap by distance times OutlineColor, plus the stepped colour noise
            float4 crm = 1;
            crm.rgb = SAMPLE_TEXTURE2D(_ColorRemap, sampler_linear_clamp, float2(dist01, 0.5)).r * LinearToSRGB(_OutlineColor.rgb);
            float colNoise = TEX(_OutlineNoiseColor, screenPx / _OutlineColorRand.x / 10).r;
            float range = _OutlineColorRand.y / 10;
            colNoise = (round(colNoise * 3) / 3) * range - range / 4;
            crm.rgb += colNoise;
            return crm;   // gamma
        }
        float3 Paper(float4 positionCS) { return LinearToSRGB(TEX(_PaperTex, positionCS.xy / 1024.0).rgb); }   // gamma
        ENDHLSL

        // ---- texture pass: Gou inline, CunCa, Ran
        Pass
        {
            Name "Ink"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct V
            {
                float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float rimLight : TEXCOORD1; float dist : TEXCOORD2;
                float diff : TEXCOORD3; float4 rduv : TEXCOORD4; float3 worldPos : TEXCOORD5; float3 normal : TEXCOORD6; float fog : TEXCOORD7;
            };
            V vert(A v)
            {
                float3 worldPos = TransformObjectToWorld(v.positionOS.xyz);
                float3 worldNormal = TransformObjectToWorldNormal(v.normalOS);
                float3 worldViewDir = normalize(GetCameraPositionWS() - worldPos);
                float dist = length(worldPos - GetCameraPositionWS());
                float rimLight = saturate(1 - saturate(dot(worldViewDir, worldNormal)));
                float rimRate = lerp(_DistanceCtrl.z, _DistanceCtrl.w, pow(saturate(dist / _DistanceCtrl.x), 0.05));
                rimLight = saturate(pow(rimLight, rimRate));
                float nl = max(0, dot(worldNormal, GetMainLight().direction));
                V o;
                o.positionCS = TransformWorldToHClip(worldPos);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.rimLight = rimLight;
                o.dist = dist / _DistanceCtrl.x;
                o.diff = pow(1 - nl, 2);
                o.rduv.xy = (worldPos.xy + worldPos.yz + worldPos.xz) * _InlineControl.z;
                o.rduv.zw = -(worldPos.xy + worldPos.yz + worldPos.xz) * _InlineControl.z;
                o.worldPos = worldPos; o.normal = worldNormal;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float4 col = 1;
                // Ran: wash towards _RimColor where the Lambert / rim area is dark
                float area = lerp(i.diff, i.rimLight, _LambertRim);
                float cArea = smoothstep(_AreaBegin.z, _AreaEnd.z, area);
                if (_InkStage > 1.5) col.rgb = lerp(col.rgb, LinearToSRGB(_RimColor.rgb), pow(cArea, _Dark.z));
                col.rgb *= lerp(1, LinearToSRGB(SAMPLE_TEXTURE2D(_BaseMap, sampler_linear_repeat, i.uv).rgb), _AlbedoMix);

                // Gou: inline from the rim term, broken by the FeiBai noise
                float outlineNoise = TEX(_OutlineNoiseFB, i.rduv.xy).r * _FeiBai;
                float a = _InlineControl.x, b = saturate(1 - a);
                float4 crm = RemapColor(i.dist, i.positionCS.xy);

                // CunCa: triplanar stroke texture, one channel per direction, masked by the area term
                float3 n = normalize(i.normal);
                float strokeTop = TEX(_Stroke, i.worldPos.xz / _Size.x).r;
                float strokeForward = TEX(_Stroke, i.worldPos.xy / _Size.x).g;
                float strokeSide = TEX(_Stroke, i.worldPos.zy / _Size.x).b;
                float strokeBottom = TEX(_Stroke, i.worldPos.xz / _Size.x).a;
                float cun = strokeTop;
                cun = lerp(cun, strokeForward, abs(n.z));
                cun = lerp(cun, strokeSide, abs(n.x));
                cun = lerp(cun, strokeBottom, max(-n.y, 0));
                cun = pow(saturate(cun), _Dark.x);
                float noise = pow(TEX(_Noise, i.rduv.xy / _Size.w).b, _Dark.w);
                cun = lerp(1, saturate(cun + noise * _AreaBegin.w), smoothstep(_AreaBegin.x, _AreaEnd.x, area));
                if (_InkStage < 0.5) cun = 1;

                float outline = step((i.rimLight - b) / a + outlineNoise * _InlineControl.w, _InlineControl.y);
                float stroke = outline * cun;
                col.rgb = lerp(crm.rgb, col.rgb, stroke);
                col.rgb = SRGBToLinear(saturate(col.rgb * Paper(i.positionCS)));
                col.rgb = MixFog(col.rgb, i.fog);
                if (_Debug > 0.5) col.rgb = SRGBToLinear(SAMPLE_TEXTURE2D(_ColorRemap, sampler_linear_clamp, float2(i.dist, 0.5)).rrr);
                return half4(col.rgb, 1);
            }
            ENDHLSL
        }

        // ---- Outline 0 and Outline 1: back faces pushed out along the view-space normal in clip space
        HLSLINCLUDE
        struct OA { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
        struct OV { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float dist : TEXCOORD1; float fog : TEXCOORD2; };
        OV OutlineVert(OA v, float width, float noiseSize)
        {
            float3 worldPos = TransformObjectToWorld(v.positionOS.xyz);
            float dist = length(worldPos - GetCameraPositionWS());
            float3 normal = mul((float3x3)UNITY_MATRIX_IT_MV, v.normalOS);
            float2 extend = normalize(mul((float2x2)UNITY_MATRIX_P, normal.xy));
            float rand = 1 + sin(length(worldPos.xyz) * 180 * _OutlineCtrl.z) * _OutlineCtrl.w;
            OV o;
            o.positionCS = TransformWorldToHClip(worldPos);
            o.positionCS.xy += extend * width * 0.001 * pow(o.positionCS.w, _DistanceCtrl.y) * rand;
            o.uv = (worldPos.xy + worldPos.yz + worldPos.xz) * noiseSize;
            o.dist = dist / _DistanceCtrl.x;
            o.fog = ComputeFogFactor(o.positionCS.z);
            return o;
        }
        half4 OutlineFrag(OV i, float cutoff, bool fog)
        {
            float noise = TEX(_OutlineNoiseFB, i.uv).r;
            clip(noise - cutoff / 10 * _FeiBai);
            float4 col = RemapColor(i.dist, i.positionCS.xy);
            col.rgb = SRGBToLinear(saturate(col.rgb * Paper(i.positionCS)));
            if (fog) col.rgb = MixFog(col.rgb, i.fog);
            return half4(col.rgb, 1);
        }
        ENDHLSL
        Pass
        {
            Name "Outline0"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            OV vert(OA v) { return OutlineVert(v, _OutlineCtrl.x, _OutlineNoiseCtrl.x); }
            half4 frag(OV i) : SV_Target { if (_Debug > 0.5) clip(-1); return OutlineFrag(i, _OutlineNoiseCtrl.y, true); }
            ENDHLSL
        }
        Pass
        {
            Name "Outline1"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Cull Front
            Offset 20, 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            OV vert(OA v) { return OutlineVert(v, _OutlineCtrl.x + _OutlineCtrl.y, _OutlineNoiseCtrl.z); }
            half4 frag(OV i) : SV_Target { if (_Debug > 0.5) clip(-1); return OutlineFrag(i, _OutlineNoiseCtrl.w, false); }
            ENDHLSL
        }
    }
}
