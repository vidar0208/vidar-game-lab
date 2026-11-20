// Port of the source's FeedbackDownScale: each output pixel keeps the input pixel with the finest mip
// (smallest blue) of its 2x2, 4x4 or 8x8 block.
Shader "Hidden/GameTech/VTFeedbackDownScale"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    TEXTURE2D(_MainTex);
    float4 _SrcTexel;
    float4 GetMaxFeedback(float2 uv, int count)
    {
        float4 col = float4(1, 1, 1, 1);
        float2 start = uv - 0.5 * (count - 1) * _SrcTexel.xy;
        for (int y = 0; y < count; y++)
            for (int x = 0; x < count; x++)
            {
                float4 col1 = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_PointClamp, start + float2(_SrcTexel.x * x, _SrcTexel.y * y), 0);
                col = lerp(col, col1, step(col1.b, col.b));
            }
        return col;
    }
    ENDHLSL
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass { HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            float4 frag(Varyings i) : SV_Target { return GetMaxFeedback(i.texcoord, 2); }
            ENDHLSL }
        Pass { HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            float4 frag(Varyings i) : SV_Target { return GetMaxFeedback(i.texcoord, 4); }
            ENDHLSL }
        Pass { HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            float4 frag(Varyings i) : SV_Target { return GetMaxFeedback(i.texcoord, 8); }
            ENDHLSL }
    }
}
