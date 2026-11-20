// URP version of the source's trail shader (GpuTrail/StartEndColor): reads the vertex buffer the compute shaders
// wrote (via the source's GpuTrailShaderInclude), additive. Colour comes from the trail's node colour and fades
// with node age (uv.x: 1 = newest), and softly across the ribbon width (uv.y). _UseFlat paints one flat colour,
// used for the LOD debug view.
Shader "GameTech/GpuTrailURP"
{
    Properties
    {
        _Intensity("Intensity", Float) = 3
        _FadePower("Age fade power", Float) = 1.5
        _FlatColor("Flat colour", Color) = (1,1,1,1)
        _UseFlat("Use flat colour", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Trail"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Off ZWrite Off ZTest LEqual
            Blend One One

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "../GpuTrail/Shaders/GpuTrailShaderInclude.hlsl"

            float _Intensity, _FadePower, _UseFlat;
            float4 _FlatColor;

            struct v2f { float4 pos : SV_POSITION; half4 col : COLOR; float2 uv : TEXCOORD0; };

            v2f vert(uint vId : SV_VertexID, uint iId : SV_InstanceID)
            {
                v2f o;
                Vertex v = GetVertex(vId, iId);
                o.pos = TransformWorldToHClip(v.pos);
                o.uv = v.uv;
                o.col = _UseFlat > 0.5 ? half4(_FlatColor) : v.color;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                if (i.uv.x < 0) discard;                    // unused vertices keep uv -1
                float age = pow(saturate(i.uv.x), _FadePower);
                float across = smoothstep(0.0, 0.7, 1.0 - abs(i.uv.y * 2.0 - 1.0));
                return half4(i.col.rgb * (_Intensity * age * across), 1);
            }
            ENDHLSL
        }
    }
}
