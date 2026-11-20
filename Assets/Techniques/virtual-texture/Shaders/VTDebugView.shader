// Debug panels: _View 0 = feedback buffer (mip colour per requested page, checkered by page; dark = nothing),
// 1 = page table (mip colour of each entry, checkered by tile), 2 = tile cache (as stored).
Shader "Hidden/GameTech/VTDebugView"
{
    Properties { _MainTex ("Texture", 2D) = "black" {} _View ("View", Float) = 0 }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Cull Off ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "../VirtualTexture/Shaders/VTCommon.hlsl"
            TEXTURE2D(_MainTex);
            float _View;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata v) { v2f o; o.pos = TransformObjectToHClip(v.vertex.xyz); o.uv = v.uv; return o; }
            float4 frag(v2f i) : SV_Target
            {
                if (_View > 1.5)
                    return float4(SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_LinearClamp, i.uv, 0).rgb, 1);
                float3 f = round(SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_PointClamp, i.uv, 0).rgb * 255);
                if (_View < 0.5 && f.b > 254) return float4(0.02, 0.02, 0.025, 1);
                float chk = fmod(f.r + f.g, 2.0);
                return VTGetMipColor(f.b) * (0.6 + 0.4 * chk);
            }
            ENDHLSL
        }
    }
}
