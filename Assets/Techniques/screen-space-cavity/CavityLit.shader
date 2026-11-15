// Lit surface for the cavity scene: URP PBR (metallic 0, flat colour), multiplied by the cavity buffer the same way
// the source's ToonyLit shader uses it (bakedGI *= cavity * 4; lightColor *= cavity * 4). With no emission and no
// separate specular term in this scene, multiplying the lit colour is the same as multiplying direct and ambient light.
// Extra pass GTViewNormalDepth writes the buffer the cavity pass reads: view-space normal (z flipped) and linear depth.
Shader "GameTech/CavityLit"
{
    Properties
    {
        _BaseColor("Colour", Color) = (1,1,1,1)
        _Smoothness("Smoothness", Range(0,1)) = 0.2
        [HideInInspector] _ScreenSpaceCavityTexture("Cavity buffer", 2D) = "grey" {}
        [HideInInspector] _SSCScreen("Cavity buffer size, on, flip", Vector) = (1,1,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor; half _Smoothness;
        float4 _SSCScreen; // x, y = buffer size; z = cavity on; w = flip y
        CBUFFER_END

        TEXTURE2D(_ScreenSpaceCavityTexture);

        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
        struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float fogFactor : TEXCOORD2; };

        Varyings Vert(Attributes i)
        {
            Varyings o;
            o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(i.normalOS);
            o.fogFactor = ComputeFogFactor(o.positionCS.z);
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            half4 Frag(Varyings i) : SV_Target
            {
                InputData d = (InputData)0;
                d.positionWS = i.positionWS;
                d.normalWS = normalize(i.normalWS);
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                d.bakedGI = SampleSH(d.normalWS);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);
                SurfaceData s = (SurfaceData)0;
                s.albedo = _BaseColor.rgb; s.metallic = 0; s.smoothness = _Smoothness; s.occlusion = 1; s.alpha = 1;
                half4 c = UniversalFragmentPBR(d, s);
                int2 px = int2(i.positionCS.xy);
                if (_SSCScreen.w > 0.5) px.y = int(_SSCScreen.y) - 1 - px.y;
                half cavity = LOAD_TEXTURE2D(_ScreenSpaceCavityTexture, px).r;
                c.rgb *= lerp(1.0, cavity * 4.0, _SSCScreen.z);   // z = 0: cavity off
                c.rgb = MixFog(c.rgb, i.fogFactor);
                return c;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma vertex SVert
            #pragma fragment SFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;
            Varyings SVert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.fogFactor = 0;
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 ld = normalize(_LightPosition - o.positionWS);
            #else
                float3 ld = _LightDirection;
            #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(o.positionWS, o.normalWS, ld));
            #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
            #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                o.positionCS = cs;
                return o;
            }
            half4 SFrag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DFrag
            half4 DFrag(Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment NFrag
            half4 NFrag(Varyings i) : SV_Target { return half4(normalize(i.normalWS), 0); }
            ENDHLSL
        }

        Pass
        {
            Name "GTViewNormalDepth"
            Tags { "LightMode"="GTViewNormalDepth" }
            ZWrite On ZTest LEqual Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment VFrag
            float4 VFrag(Varyings i) : SV_Target
            {
                float3 n = TransformWorldToViewDir(normalize(i.normalWS), true);
                float depth = -TransformWorldToView(i.positionWS).z;
                return float4(n.x, n.y, -n.z, depth);   // URP 10 DepthNormals convention: view space, z flipped
            }
            ENDHLSL
        }
    }
}
