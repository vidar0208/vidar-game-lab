// Adds the probe GI to a band's direct-light frame. Adapted from the source's Composite shader: the same per-pixel
// SampleSHVoxel lookup (8 nearest probes, normal-weighted, trilinear) on world position, albedo and normal. The source
// reads URP's deferred G-buffer; here the three inputs are rendered by the script with the source's G-buffer shaders
// (world position, normal) and URP Unlit (albedo). The frame is HDR without URP post-processing, so exposure, ACES
// tone mapping and a light vignette are applied here.
Shader "Hidden/GT/PRTComposite"
{
    Properties { _MainTex ("Direct light (HDR)", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Shaders/SH.hlsl"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };
            v2f vert (appdata v) { v2f o; o.vertex = TransformObjectToHClip(v.vertex.xyz); o.uv = v.uv; return o; }

            TEXTURE2D(_MainTex); TEXTURE2D(_PosTex); TEXTURE2D(_NrmTex); TEXTURE2D(_AlbTex);
            SamplerState gt_point_clamp_sampler;

            float _coefficientVoxelGridSize;
            float4 _coefficientVoxelCorner;
            float4 _coefficientVoxelSize;
            StructuredBuffer<int> _coefficientVoxel;
            float _GIIntensity;
            float _Mode;        // 0 direct only, 1 direct + GI, 2 GI only
            float _Exposure;    // linear multiplier

            float3 Aces(float3 x)
            {
                const float a = 2.51, b = 0.03, c = 2.43, d = 0.59, e = 0.14;   // Narkowicz fit
                return saturate((x * (a * x + b)) / (x * (c * x + d) + e));
            }

            float4 frag (v2f i) : SV_Target
            {
                float3 color = SAMPLE_TEXTURE2D_LOD(_MainTex, gt_point_clamp_sampler, i.uv, 0).rgb;
                float4 worldPos = float4(SAMPLE_TEXTURE2D_LOD(_PosTex, gt_point_clamp_sampler, i.uv, 0).xyz, 1);
                float4 nm = SAMPLE_TEXTURE2D_LOD(_NrmTex, gt_point_clamp_sampler, i.uv, 0);
                float3 albedo = SAMPLE_TEXTURE2D_LOD(_AlbTex, gt_point_clamp_sampler, i.uv, 0).rgb;
                float3 normal = normalize(nm.xyz + 1e-6);

                float3 gi = 0;
                if (nm.w > 0.5 && _Mode > 0.5)
                    gi = SampleSHVoxel(worldPos, albedo, normal, _coefficientVoxel, _coefficientVoxelGridSize,
                                       _coefficientVoxelCorner, _coefficientVoxelSize) * _GIIntensity;

                float3 hdr = _Mode > 1.5 ? gi : (_Mode > 0.5 ? color + gi : color);
                float3 c = Aces(hdr * _Exposure);
                float2 q = i.uv - 0.5;
                c *= 1.0 - 0.35 * smoothstep(0.25, 0.8, dot(q, q) * 2.2);
                return float4(c, 1);
            }
            ENDHLSL
        }
    }
}
