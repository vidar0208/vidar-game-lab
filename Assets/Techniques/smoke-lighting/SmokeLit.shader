// Smoke billboards with three lighting workflows, following peeweek/Unity-URP-SmokeLighting (MIT):
//   mode 0  Unlit: flipbook colour only
//   mode 1  Normal map + attenuation ramp: lerp(1, ramp(N.L * 0.5 + 0.5), directionality) * light colour * attenuation
//   mode 2  6-way lightmaps: light direction in tangent space weights six baked maps (Right, Left, Top, Bottom, Back, Front)
// Lights = ambient + main light + additional lights, as in the source's "All Lights" subgraph.
// The mode is a global set per camera, so one set of billboards can be shown three ways side by side.
Shader "GameTech/SmokeLit"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Smoke"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_Smoke_RLT); TEXTURE2D(_Smoke_BBF); TEXTURE2D(_Smoke_Normal); TEXTURE2D(_Smoke_Ramp);
            SamplerState gt_smoke_trilinear_clamp_sampler;
            float _Smoke_Mode;              // 0 unlit, 1 normal + ramp, 2 six-way
            float _Smoke_Directionality;    // normal workflow: 0 = flat, 1 = full ramp
            float _Smoke_SoftDistance;      // soft particle fade distance in metres
            float3 _Smoke_Albedo;
            float _Smoke_UnlitGain;

            struct A
            {
                float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0; float4 flip : TEXCOORD1; float3 up : TEXCOORD2; float4 color : COLOR;
            };
            struct V
            {
                float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float2 uv : TEXCOORD1; float4 flip : TEXCOORD2;
                float3 t : TEXCOORD3; float3 b : TEXCOORD4; float3 n : TEXCOORD5; float4 color : COLOR;
            };

            V Vert(A i)
            {
                V o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = i.uv; o.flip = i.flip; o.color = i.color;
                o.t = i.tangentOS.xyz; o.b = i.up; o.n = i.normalOS;      // the mesh is built in world space
                return o;
            }

            float2 FrameUV(float2 uv, float f)
            {
                float c = fmod(f, 4.0), r = floor(f / 4.0);
                return (float2(c, r) + clamp(uv, 0.006, 0.994)) * 0.25;
            }
            half4 Flip(TEXTURE2D(tex), V i)
            {
                half4 a = SAMPLE_TEXTURE2D(tex, gt_smoke_trilinear_clamp_sampler, FrameUV(i.uv, i.flip.x));
                half4 b = SAMPLE_TEXTURE2D(tex, gt_smoke_trilinear_clamp_sampler, FrameUV(i.uv, i.flip.y));
                return lerp(a, b, i.flip.z);
            }

            // source: 6 Way Lighting (Single Light)
            half3 SixWay(half3 rlt, half3 bbf, float3 lightDirWS, half3 lightColor, half atten, float3 t, float3 b, float3 n)
            {
                float3 L = normalize(float3(dot(lightDirWS, t), dot(lightDirWS, b), dot(lightDirWS, n)));
                half s = rlt.r * saturate(L.x) + rlt.g * saturate(-L.x) + rlt.b * saturate(L.y)
                       + bbf.r * saturate(-L.y) + bbf.g * saturate(-L.z) + bbf.b * saturate(L.z);
                return s * lightColor * atten;
            }
            // source: Normal Lighting (Single Light)
            half3 RampLit(float3 normalWS, float3 lightDirWS, half3 lightColor, half atten)
            {
                float u = dot(lightDirWS, normalWS) * 0.5 + 0.5;
                half ramp = SAMPLE_TEXTURE2D_LOD(_Smoke_Ramp, gt_smoke_trilinear_clamp_sampler, float2(u, 0.5), 0).r;
                return lerp(1.0, ramp, _Smoke_Directionality) * lightColor * atten;
            }

            half4 Frag(V i) : SV_Target
            {
                half4 rlt = Flip(_Smoke_RLT, i);
                half4 bbf = Flip(_Smoke_BBF, i);
                half alpha = rlt.a;
                float3 t = normalize(i.t), b = normalize(i.b), n = normalize(i.n);

                // soft particle: fade where the billboard meets opaque geometry
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float sceneZ = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float soft = smoothstep(0.0, 1.0, (sceneZ - i.positionCS.w) / max(_Smoke_SoftDistance, 0.001));
                half fade = i.flip.w * soft;

                half3 rgb;
                if (_Smoke_Mode < 0.5)
                {
                    // unlit: an even mix of the six maps stands in for a flipbook with its lighting baked in
                    rgb = (rlt.r + rlt.g + rlt.b + bbf.r + bbf.g + bbf.b) / 6.0 * _Smoke_UnlitGain;
                }
                else
                {
                    InputData inputData = (InputData)0;
                    inputData.positionWS = i.positionWS;
                    inputData.normalizedScreenSpaceUV = suv;
                    Light mainLight = GetMainLight();
                    if (_Smoke_Mode < 1.5)
                    {
                        half4 nt = Flip(_Smoke_Normal, i);
                        float3 nts = normalize(nt.xyz * 2.0 - 1.0);
                        float3 nWS = normalize(t * nts.x + b * nts.y + n * nts.z);
                        half3 lit = SampleSH(nWS);
                        lit += RampLit(nWS, mainLight.direction, mainLight.color, mainLight.distanceAttenuation);
                        #if defined(_ADDITIONAL_LIGHTS)
                        uint count = GetAdditionalLightsCount();
                        LIGHT_LOOP_BEGIN(count)
                            Light light = GetAdditionalLight(lightIndex, inputData.positionWS);
                            lit += RampLit(nWS, light.direction, light.color, light.distanceAttenuation);
                        LIGHT_LOOP_END
                        #endif
                        rgb = lit * alpha;
                    }
                    else
                    {
                        half3 lit = SampleSH(n) * alpha;
                        lit += SixWay(rlt.rgb, bbf.rgb, mainLight.direction, mainLight.color, mainLight.distanceAttenuation, t, b, n);
                        #if defined(_ADDITIONAL_LIGHTS)
                        uint count = GetAdditionalLightsCount();
                        LIGHT_LOOP_BEGIN(count)
                            Light light = GetAdditionalLight(lightIndex, inputData.positionWS);
                            lit += SixWay(rlt.rgb, bbf.rgb, light.direction, light.color, light.distanceAttenuation, t, b, n);
                        LIGHT_LOOP_END
                        #endif
                        rgb = lit;
                    }
                }
                rgb *= _Smoke_Albedo * i.color.rgb;
                return half4(rgb * fade * i.color.a, alpha * fade * i.color.a);
            }
            ENDHLSL
        }
    }
}
