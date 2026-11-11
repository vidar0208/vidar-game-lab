// URP version of IRCSS/MatrixVFX "Unlit/TriPlanarMatrix" (MIT, Copyright 2020 Shahriar Shahrabi).
// Same render state (additive, no depth write, no culling) and the same fragment logic: three world-space
// projections of the glyph rain, blended by the normal raised to a power, cut by the two transition tests.
// Added: _MX_Mode (1 = front projection only, for the comparison shot), optional alpha clip from a base map
// (IA's hair cards), _MX_Scale and _MX_Sharpness as values instead of #defines.
Shader "GameTech/MatrixRain"
{
    Properties
    {
        _BaseMap("Alpha source", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend One One
        Cull Off
        ZWrite Off
        Offset -1, -1
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "MatrixCommon.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            CBUFFER_END
            float _MX_Mode;         // 0 = three projections, 1 = front (XY) projection only
            float _MX_Sharpness;    // source: #define sharpness 10

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 normal : NORMAL;
                float4 screenPos : TEXCOORD2;
                float2 uv : TEXCOORD3;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.worldPos = TransformObjectToWorld(v.vertex.xyz);
                o.vertex = TransformWorldToHClip(o.worldPos);
                o.normal = TransformObjectToWorldNormal(v.normal);
                o.screenPos = ComputeScreenPos(o.vertex);
                o.uv = v.uv;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a - 0.3);
                float4 col = float4(0, 0, 0, 1);
                float3 colFront = MatrixEffect(i.worldPos.xy + sin(i.worldPos.zz));
                float3 colSide = MatrixEffect(i.worldPos.zy + sin(i.worldPos.xx));
                float3 colTop = MatrixEffect(i.worldPos.xz + sin(i.worldPos.yy));

                float3 blendWeight = pow(normalize(abs(i.normal)), _MX_Sharpness);
                blendWeight /= (blendWeight.x + blendWeight.y + blendWeight.z);
                if (_MX_Mode > 0.5) blendWeight = float3(0, 0, 1);
                col.xyz = colFront * blendWeight.z + colSide * blendWeight.x + colTop * blendWeight.y;

                float distance_to_center = distance(i.worldPos.xyz, _Global_Effect_center.xyz);
                float control_value = saturate(_Global_Transition_value);
                if (control_value * 60.0 < distance_to_center) col = col * 0.0;

                float2 screenPos = i.screenPos.xy / i.screenPos.w;
                col *= split_from_midle(screenPos.x, _Global_Transition_value, 0.0);
                col = min(1.5, col);
                return col;
            }
            ENDHLSL
        }
    }
}
