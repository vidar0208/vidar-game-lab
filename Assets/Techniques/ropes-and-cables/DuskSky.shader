// Dusk sky for the rope post: a gradient by view height plus a sun disc and halo, drawn as a full-viewport quad
// at the far plane. Only a backdrop, not part of the technique.
Shader "GameTech/DuskSky"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Background" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off ZTest LEqual Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct V { float4 positionCS : SV_POSITION; };
            V Vert(float4 positionOS : POSITION)
            {
                V o; o.positionCS = float4(positionOS.xy * 2, UNITY_RAW_FAR_CLIP_VALUE, 1); return o;
            }

            half4 Frag(V i) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(i.positionCS);
                float3 wp = ComputeWorldSpacePosition(uv, UNITY_RAW_FAR_CLIP_VALUE, UNITY_MATRIX_I_VP);
                float3 dir = normalize(wp - _WorldSpaceCameraPos);
                float h = dir.y;
                half3 hor = half3(1.0, 0.55, 0.27), mid = half3(0.80, 0.42, 0.44), top = half3(0.11, 0.17, 0.36);
                half3 col = lerp(hor, mid, smoothstep(0.0, 0.16, h));
                col = lerp(col, top, smoothstep(0.08, 0.55, h));
                float sd = saturate(dot(dir, _MainLightPosition.xyz));
                col += half3(1.0, 0.72, 0.42) * (pow(sd, 2500.0) * 5.0 + pow(sd, 60.0) * 0.38 + pow(sd, 6.0) * 0.12);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
