// Bakes a cubemap into a dual paraboloid atlas, as the source's DualParaboloidGenerate: a 4 x 4 grid of tiles, each
// row holding two mip levels, each mip as a back (-z) and a front (+z) paraboloid. Tile (xx, yy) samples cube mip
// yy * 2 + floor(xx / 2); a tile point (x, y) in [-1, 1] maps to the direction (x, y, +-(0.5 - 0.5 (x^2 + y^2))).
Shader "GameTech/DPGenerate"
{
    Properties { _Cube("Cubemap", CUBE) = "white" {} }
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex V
            #pragma fragment F
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURECUBE(_Cube); SAMPLER(sampler_Cube);
            struct A { float4 p : POSITION; float2 uv : TEXCOORD0; };
            struct O { float4 p : SV_POSITION; float2 uv : TEXCOORD0; };
            O V(A v) { O o; o.p = TransformObjectToHClip(v.p.xyz); o.uv = v.uv; return o; }
            float4 GetPos(float2 uv)
            {
                float xx = floor(uv.x * 4.0), yy = floor(uv.y * 4.0);
                float2 xy = float2(uv.x * 4.0 - xx, uv.y * 4.0 - yy);
                xy = lerp(-1.0, 1.0, xy);
                float mipLevel = yy * 2.0 + floor(xx * 0.5);
                float z = 0.5 - 0.5 * dot(xy, xy);
                return float4(xy, z * (floor(fmod(xx, 2.0)) * 2.0 - 1.0), mipLevel);
            }
            float4 F(O i) : SV_Target
            {
                float4 pos = GetPos(i.uv);
                return float4(SAMPLE_TEXTURECUBE_LOD(_Cube, sampler_Cube, normalize(pos.xyz), pos.w).rgb, 1);
            }
            ENDHLSL
        }
    }
}
