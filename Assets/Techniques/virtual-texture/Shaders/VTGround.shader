// The ground: _Mode 0 samples the virtual texture (page table -> tile cache), 1 samples the regular texture
// (trilinear, anisotropic), 2 is the virtual texture tinted by the mip level the page table holds for that page.
// Exponential distance haze towards _HazeColor.
Shader "GameTech/VTGround"
{
    Properties
    {
        _RegularTex ("Regular texture", 2D) = "grey" {}
        _Mode ("Mode", Float) = 0
        _HazeColor ("Haze", Color) = (0.3, 0.22, 0.25, 1)
        _HazeDensity ("Haze density", Float) = 0.025
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "../VirtualTexture/Shaders/VTCommon.hlsl"
            TEXTURE2D(_RegularTex); SAMPLER(sampler_RegularTex);
            float _Mode; float4 _HazeColor; float _HazeDensity;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 ws : TEXCOORD1; };
            v2f vert(appdata v)
            {
                v2f o;
                o.ws = TransformObjectToWorld(v.vertex.xyz);
                o.pos = TransformWorldToHClip(o.ws);
                o.uv = v.uv;
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float3 col;
                if (_Mode > 0.5 && _Mode < 1.5)
                    col = SAMPLE_TEXTURE2D(_RegularTex, sampler_RegularTex, i.uv).rgb;
                else
                {
                    col = VTTex2D(VTTransferUV(i.uv)).rgb;
                    if (_Mode > 1.5)
                        col = lerp(col, VTGetMipColor(VTGetMipLevel(i.uv)).rgb, 0.3);
                }
                float d = distance(i.ws, _WorldSpaceCameraPos);
                col = lerp(col, _HazeColor.rgb, 1.0 - exp(-d * _HazeDensity));
                return float4(col, 1);
            }
            ENDHLSL
        }
    }
}
