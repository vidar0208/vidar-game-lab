// URP version of IRCSS/MatrixVFX "Unlit/ScreenSpaceMatrixEffect" (MIT, Copyright 2020 Shahriar Shahrabi).
// The source blits this over the camera target after everything. Here it is one full-viewport quad drawn last
// with the depth test off. Same formula: text(uv * screen * 0.6) * rain(uv * screen * 0.6), drop length = screen height.
Shader "GameTech/MatrixScreen"
{
    SubShader
    {
        Tags { "Queue"="Transparent+200" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off ZTest Always ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "MatrixCommon.hlsl"

            float _MX_ScreenW, _MX_ScreenH;     // camera size in pixels (source: _screen_width, _screen_height)
            #define SS_SCALE 0.6

            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(float4 positionOS : POSITION)
            {
                v2f o;
                o.vertex = float4(positionOS.xy * 2, UNITY_NEAR_CLIP_VALUE, 1);
                o.uv = positionOS.xy + 0.5;
                if (_ProjectionParams.x < 0) o.uv.y = 1 - o.uv.y;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float2 c = i.uv * float2(_MX_ScreenW, _MX_ScreenH) * SS_SCALE;
                float3 rain_col = global_colored > 0.5 ? rain_colored_len(c, _MX_ScreenH) : rain_len(c, _MX_ScreenH);
                float3 col = text(c) * rain_col;
                return half4(min(1.5, col), 1);
            }
            ENDHLSL
        }
    }
}
