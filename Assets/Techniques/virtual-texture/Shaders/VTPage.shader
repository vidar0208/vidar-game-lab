// Draws one page of the procedural virtual texture into its tile (padding included). Also used once at start to
// draw the whole map into the regular 2048 x 2048 texture used for the comparison.
Shader "Hidden/GameTech/VTPage"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "VTPageContent.hlsl"
            float4 _PageRect;   // xy: virtual uv at the tile corner, zw: virtual uv size of the tile
            float4 _PageInfo;   // x: virtual uv per tile pixel, y: mip
            float4 frag(Varyings i) : SV_Target
            {
                float2 p = _PageRect.xy + i.texcoord * _PageRect.zw;
                return float4(GTPageContent(p, _PageInfo.x, 64.0), 1);
            }
            ENDHLSL
        }
    }
}
