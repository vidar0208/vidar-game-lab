// URP shader for the CoR-skinned figure: positions and normals come from the skinning kernel's output buffers
// (indexed by vertex id), colour from the vertex colour, with stripes around the limbs (vertex alpha = stripe amount)
// so twisting is visible. Main light with shadows, simple sky/ground ambient, a soft rim. Shadow caster included.
Shader "GameTech/CorFigure"
{
    Properties
    {
        _StripeColor ("Stripe colour", Color) = (0.08, 0.09, 0.12, 1)
        _Stripes ("Stripes around", Float) = 8
        _Smooth ("Specular power", Float) = 40
    }
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    StructuredBuffer<float3> verticesOutBuffer;
    StructuredBuffer<float3> normalsOutBuffer;
    float4 _StripeColor; float _Stripes; float _Smooth;
    struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; uint id : SV_VertexID; };
    ENDHLSL
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct v2f { float4 pos : SV_POSITION; float3 ws : TEXCOORD0; float3 nws : TEXCOORD1; float2 uv : TEXCOORD2; float4 color : COLOR; };
            v2f vert(appdata v)
            {
                v2f o;
                o.ws = TransformObjectToWorld(verticesOutBuffer[v.id]);
                o.nws = TransformObjectToWorldNormal(normalsOutBuffer[v.id]);
                o.pos = TransformWorldToHClip(o.ws);
                o.uv = v.uv; o.color = v.color;
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.nws);
                float s = frac(i.uv.x * _Stripes);
                float w = fwidth(i.uv.x * _Stripes);
                float stripe = smoothstep(0.5 - w, 0.5 + w, s) * (1.0 - smoothstep(1.0 - w, 1.0, s));
                float3 albedo = lerp(i.color.rgb, _StripeColor.rgb, stripe * i.color.a);
                Light l = GetMainLight(TransformWorldToShadowCoord(i.ws));
                float ndl = saturate(dot(n, l.direction));
                float3 v = normalize(GetWorldSpaceViewDir(i.ws));
                float3 h = normalize(l.direction + v);
                float spec = pow(saturate(dot(n, h)), _Smooth) * 0.25;
                float sh = l.shadowAttenuation;
                float3 amb = lerp(float3(0.10, 0.09, 0.08), float3(0.20, 0.23, 0.30), n.y * 0.5 + 0.5);
                float rim = pow(1.0 - saturate(dot(n, v)), 3.0) * 0.25;
                float3 col = albedo * (amb + l.color * ndl * sh) + l.color * spec * sh + rim * float3(0.5, 0.6, 0.8);
                return float4(col, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float4 vert(appdata v) : SV_POSITION
            {
                float3 ws = TransformObjectToWorld(verticesOutBuffer[v.id]);
                float3 nws = TransformObjectToWorldNormal(normalsOutBuffer[v.id]);
                float4 p = TransformWorldToHClip(ApplyShadowBias(ws, nws, _LightDirection));
            #if UNITY_REVERSED_Z
                p.z = min(p.z, UNITY_NEAR_CLIP_VALUE);
            #else
                p.z = max(p.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return p;
            }
            float4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
