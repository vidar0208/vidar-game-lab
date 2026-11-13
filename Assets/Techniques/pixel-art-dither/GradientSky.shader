// Skybox: a plain vertical gradient, zenith to horizon to ground. Colours are set on the material.
Shader "GT/GradientSky"
{
    Properties
    {
        _Top ("Zenith", Color) = (0.16, 0.32, 0.70, 1)
        _Horizon ("Horizon", Color) = (0.80, 0.87, 0.95, 1)
        _Bottom ("Below horizon", Color) = (0.50, 0.55, 0.60, 1)
        _Exp ("Falloff", Float) = 0.55
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            half4 _Top, _Horizon, _Bottom;
            float _Exp;
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };
            Varyings vert(Attributes a) { Varyings o; o.positionCS = TransformObjectToHClip(a.positionOS.xyz); o.dir = a.positionOS.xyz; return o; }
            half4 frag(Varyings i) : SV_Target
            {
                float y = normalize(i.dir).y;
                half3 c = y > 0 ? lerp(_Horizon.rgb, _Top.rgb, pow(saturate(y), _Exp)) : lerp(_Horizon.rgb, _Bottom.rgb, sqrt(saturate(-y * 4)));
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
