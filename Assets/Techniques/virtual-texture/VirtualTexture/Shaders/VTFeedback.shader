// Port of the source's Feedback and FeedbackDownScale shaders.
Shader "Hidden/GameTech/VTFeedback"
{
    SubShader
    {
        Pass
        {
            ZWrite On ZTest LEqual Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "VTCommon.hlsl"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata v) { v2f o; o.pos = TransformObjectToHClip(v.vertex.xyz); o.uv = v.uv; return o; }
            float4 frag(v2f i) : SV_Target { return VTFragFeedback(i.uv); }
            ENDHLSL
        }
    }
}
