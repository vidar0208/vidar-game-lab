// FinalBlit.shader from AKGWSB/FFTConvolutionBloom (MIT), copied. Changes marked GT: the shader name, the sampler line and
// _ScreenParams replaced by _GTSrcSize (the scene texture size). _GTSrcSize.w = 1 flips the read back (paired with
// the source flip). Additive (Blend One One) onto a copy of the scene.
Shader "Hidden/GT/FFTBloomFinal"
{
    Properties
    {
        _MainTex ("_MainTex", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            Blend One One
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };
            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };
            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = TransformObjectToHClip(v.vertex.xyz);
                o.uv = v.uv;
                return o;
            }
            // GT: sampler_LinearClamp is already declared by URP Core (GlobalSamplers)
            Texture2D _MainTex;
            Texture2D _BloomTex;
            float FFTBloomIntensity;
            float4 _GTSrcSize; // GT
            float4 frag (v2f i) : SV_Target
            {
                float aspect = _GTSrcSize.x / _GTSrcSize.y; // GT
                float2 uv = i.uv;
                if (_GTSrcSize.w > 0.5) uv.y = 1.0 - uv.y; // GT: toggle
                uv.x *= 0.5;
                uv.y *= 1 / aspect;
                return float4(_MainTex.Sample(sampler_LinearClamp, uv).rgb * FFTBloomIntensity, 1.0);
            }
            ENDHLSL
        }
    }
}
