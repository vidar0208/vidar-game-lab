// URP rewrite of CrossSection/Others/StencilledUnlitTexture: a quad lying on the cut plane, drawn only where the
// stencil buffer holds 255 (a back face was the nearest surface). The source samples a texture; this one draws the
// 45 degree hatching procedurally. Extra: in three-plane mode each quad is trimmed by the other two planes.
Shader "GameTech/CrossSectionCap"
{
    Properties
    {
        _Paper("Paper", Color) = (0.92,0.86,0.72,1)
        _Ink("Ink", Color) = (0.13,0.09,0.07,1)
        _Spacing("Hatch spacing (m)", Float) = 0.045
        _Width("Line width 0..1", Range(0,1)) = 0.26
        _Index("Plane index", Float) = 0
        _Alpha("Alpha", Range(0,1)) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp("Stencil compare", Float) = 3
        _ZWrite("ZWrite", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+10" }
        Pass
        {
            Name "Cap"
            Tags { "LightMode"="UniversalForward" }
            Cull Back ZTest LEqual ZWrite [_ZWrite]
            Blend SrcAlpha OneMinusSrcAlpha
            Stencil { Ref 255 Comp [_StencilComp] }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Paper; half4 _Ink; float _Spacing; float _Width; float _Index; float _Alpha; float _StencilComp; float _ZWrite;
            CBUFFER_END
            float4 _CS_PlanePos[3];
            float4 _CS_PlaneNrm[3];
            float _CS_Count;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float2 planeUV : TEXCOORD1; };

            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                // metres along the quad's own axes, so the hatching keeps its scale whatever the quad size
                float3 ax = TransformObjectToWorldDir(float3(1, 0, 0)), ay = TransformObjectToWorldDir(float3(0, 1, 0));
                o.planeUV = float2(dot(o.positionWS, ax), dot(o.positionWS, ay));
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                if (_CS_Count > 1.5)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        if (abs(j - _Index) < 0.5) continue;
                        if (dot(i.positionWS - _CS_PlanePos[j].xyz, _CS_PlaneNrm[j].xyz) <= 0) discard;
                    }
                }
                float s = (i.planeUV.x + i.planeUV.y) / _Spacing;
                float f = abs(frac(s) - 0.5) * 2;                 // 0 on the line centre
                float aa = fwidth(s) * 2;
                float paper = smoothstep(_Width - aa, _Width + aa, f);
                half3 col = lerp(_Ink.rgb, _Paper.rgb, paper);
                Light l = GetMainLight();
                float3 n = normalize(TransformObjectToWorldDir(float3(0, 0, -1)));
                col *= (0.55 + 0.45 * saturate(dot(n, l.direction) * 0.5 + 0.5)) * (0.4 + 0.6 * saturate(l.color));
                return half4(col, _Alpha);
            }
            ENDHLSL
        }
    }
}
