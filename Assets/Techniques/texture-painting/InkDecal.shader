// The "usual" comparison: one flat, alpha-blended quad per hit, lit by the main light, faded out by _Fade.
Shader "GameTech/InkDecalQuad"
{
    Properties { _BaseMap ("Brush", 2D) = "white" {} _BaseColor ("Colour", Color) = (1,1,1,1) _Fade ("Fade", Float) = 1 }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial) float4 _BaseMap_ST; float4 _BaseColor; float _Fade; CBUFFER_END
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 n : TEXCOORD1; float3 w : TEXCOORD2; };
            V vert(A i) { V o; o.w = TransformObjectToWorld(i.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.w); o.n = TransformObjectToWorldNormal(i.normalOS); o.uv = i.uv; return o; }
            half4 frag(V i, bool front : SV_IsFrontFace) : SV_Target
            {
                float a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _Fade;
                float3 n = normalize(front ? i.n : -i.n);
                Light l = GetMainLight();
                float3 v = normalize(GetWorldSpaceViewDir(i.w)), hv = normalize(l.direction + v);
                float3 c = _BaseColor.rgb * (saturate(dot(n, l.direction)) * l.color + SampleSH(n)) + pow(saturate(dot(n, hv)), 60) * 0.5 * l.color;
                return half4(c, a);
            }
            ENDHLSL
        }
    }
}
