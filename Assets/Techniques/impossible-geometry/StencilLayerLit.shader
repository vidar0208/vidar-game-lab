// Material for the layered room sets. It is drawn only where the stencil buffer equals _StencilID.
// The source project gets the same result with URP Render Objects renderer features (one per layer, stencil compare
// Equal, drawn after the opaques); here the test sits in the material so the shared renderer asset stays untouched.
// Lighting is a small custom model (one room lamp + hemisphere ambient): shadow maps know nothing about the stencil,
// so the layered sets neither cast nor receive shadow-map shadows.
Shader "GameTech/StencilLayerLit"
{
    Properties
    {
        _BaseColor("Color", Color) = (1,1,1,1)
        [HDR] _Emission("Emission", Color) = (0,0,0,0)
        _LightColor("Room light colour", Color) = (1,1,1,1)
        _Gloss("Gloss", Range(0,1)) = 0.15
        _Pattern("Pattern (0 none, 1 planks, 2 tiles)", Float) = 0
        [IntRange] _StencilID("Stencil ID", Range(0, 255)) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp("Stencil compare", Float) = 3
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+2" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            Stencil { Ref [_StencilID] Comp [_StencilComp] }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor; half4 _Emission; half4 _LightColor; half _Gloss; float _Pattern; float _StencilID; float _StencilComp;
            CBUFFER_END
            float3 _Room_LightPos;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                return o;
            }

            float Hash(float x) { return frac(sin(x * 127.1) * 43758.5453); }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 wp = i.positionWS;
                half3 albedo = _BaseColor.rgb;
                if (_Pattern > 0.5 && _Pattern < 1.5)
                {
                    float u = wp.x / 0.24;                                        // floor planks running along z
                    float seam = smoothstep(0.0, 0.06, abs(frac(u) - 0.5) * 2.0 - 0.9) ;
                    float v = (wp.z + Hash(floor(u)) * 3.0) / 1.6;
                    float butt = smoothstep(0.0, 0.02, abs(frac(v) - 0.5) * 2.0 - 0.975);
                    albedo *= (0.82 + 0.36 * Hash(floor(u) + floor(v) * 17.0)) * (1.0 - 0.6 * max(seam, butt));
                }
                else if (_Pattern > 1.5)
                {
                    float2 g = wp.xz / 0.56;                                      // square stone tiles
                    float2 f = abs(frac(g) - 0.5) * 2.0;
                    float seam = smoothstep(0.0, 0.04, max(f.x, f.y) - 0.93);
                    albedo *= (0.85 + 0.3 * Hash(floor(g.x) * 7.0 + floor(g.y) * 31.0)) * (1.0 - 0.55 * seam);
                }
                float3 toL = _Room_LightPos - wp;
                float dist = length(toL);
                float3 l = toL / max(dist, 0.001);
                float atten = 1.0 / (1.0 + 0.10 * dist * dist);
                float ndl = saturate(dot(n, l));
                float3 v3 = normalize(GetWorldSpaceViewDir(wp));
                float spec = pow(saturate(dot(n, normalize(l + v3))), 48.0) * _Gloss * ndl;
                half3 hemi = lerp(half3(0.16, 0.15, 0.15), half3(0.42, 0.44, 0.48), n.y * 0.5 + 0.5);
                float lowAO = lerp(0.62, 1.0, saturate(wp.y / 0.45));              // cheap darkening near the floor
                half3 col = albedo * (_LightColor.rgb * (ndl * atten * 3.2) + hemi * _LightColor.rgb * 0.75 * lowAO)
                          + _LightColor.rgb * spec * atten * 2.0 + _Emission.rgb;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
