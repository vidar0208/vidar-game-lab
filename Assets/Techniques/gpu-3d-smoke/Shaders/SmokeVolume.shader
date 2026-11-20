// Mine: URP ray march of the smoke volume (replaces the source's built-in SmokeRayCast, same idea: intersect the
// ray with the domain box, step through, absorb by density). Differences: reads a 3D texture (density, light)
// with hardware filtering, stops at the scene depth so solid objects inside the smoke are composited correctly,
// adds the top-light term and a jittered start against banding. Drawn on the back faces of the domain cube.
Shader "GameTech/SmokeVolume"
{
    Properties
    {
        _LitColor ("Lit colour", Color) = (1, 0.96, 0.9, 1)
        _ShadowColor ("Shadow colour", Color) = (0.12, 0.13, 0.17, 1)
        _Absorption ("Absorption", Float) = 60
        _Steps ("Steps", Float) = 96
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Cull Front ZWrite Off ZTest Always
            Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE3D(_Volume); SAMPLER(sampler_Volume);
            float4 _LitColor, _ShadowColor; float _Absorption, _Steps;
            float4 _BoxMin, _BoxMax;
            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 ws : TEXCOORD0; };
            v2f vert(appdata v) { v2f o; o.ws = TransformObjectToWorld(v.vertex.xyz); o.pos = TransformWorldToHClip(o.ws); return o; }

            float4 frag(v2f i) : SV_Target
            {
                float3 ro = _WorldSpaceCameraPos;
                float3 rd = normalize(i.ws - ro);
                float3 inv = 1.0 / rd;
                float3 t0s = (_BoxMin.xyz - ro) * inv, t1s = (_BoxMax.xyz - ro) * inv;
                float3 tmin = min(t0s, t1s), tmax = max(t0s, t1s);
                float tn = max(max(tmin.x, tmin.y), max(tmin.z, 0.0));
                float tf = min(min(tmax.x, tmax.y), tmax.z);
                // stop at opaque geometry
                float2 suv = GetNormalizedScreenSpaceUV(i.pos);
                float eye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float3 fwd = -UNITY_MATRIX_V[2].xyz;
                tf = min(tf, eye / max(dot(rd, fwd), 1e-4));
                if (tf <= tn) return 0;

                float3 size = _BoxMax.xyz - _BoxMin.xyz;
                float dt = (tf - tn) / _Steps;
                float jitter = frac(52.9829189 * frac(dot(i.pos.xy, float2(0.06711056, 0.00583715))));
                float trans = 1.0; float3 col = 0;
                for (int s = 0; s < (int)_Steps; s++)
                {
                    float t = tn + (s + jitter) * dt;
                    float3 uvw = (ro + rd * t - _BoxMin.xyz) / size;
                    float2 dl = SAMPLE_TEXTURE3D_LOD(_Volume, sampler_Volume, uvw, 0).rg;
                    float a = 1.0 - exp(-dl.x * _Absorption * dt / size.x);
                    float3 c = lerp(_ShadowColor.rgb, _LitColor.rgb, dl.y);
                    col += trans * a * c;
                    trans *= 1.0 - a;
                    if (trans < 0.01) break;
                }
                return float4(col, 1.0 - trans);
            }
            ENDHLSL
        }
    }
}
