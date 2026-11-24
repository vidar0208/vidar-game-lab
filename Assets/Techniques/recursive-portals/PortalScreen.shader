// URP version of Custom/Portal from SebLague/Portals (MIT): the screen shows the portal camera's texture at the
// fragment's screen position, or a flat colour when displayMask is 0. No shadow pass (ShadowsOnly hides the screen).
Shader "GameTech/PortalScreen"
{
    Properties
    {
        _MainTex ("View", 2D) = "black" {}
        _InactiveColour ("Inactive Colour", Color) = (1, 1, 1, 1)
        displayMask ("Display Mask", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Cull Off
        Pass
        {
            Name "Unlit"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _InactiveColour;
                float displayMask;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 screenPos : TEXCOORD0; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float2 uv = i.screenPos.xy / i.screenPos.w;
                half4 portalCol = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                return portalCol * displayMask + _InactiveColour * (1 - displayMask);
            }
            ENDHLSL
        }
    }
}
