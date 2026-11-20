// Triplanar normal mapping for URP. The projection, the UV flip for back-facing sides and the five normal blends
// are an HLSL port of bgolus/Normal-Mapping-for-a-Triplanar-Shader (Unlicense):
// TriplanarNaive, TriplanarUDN, TriplanarWhiteout, TriplanarRNM and TriplanarGPUGems3 shaders.
// Lighting is URP PBR instead of the source's single-light Lambert + SH.
#ifndef GT_TRIPLANAR_COMMON
#define GT_TRIPLANAR_COMMON

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

CBUFFER_START(UnityPerMaterial)
half4 _BaseColor;     // colour in the cobbles' gaps
half4 _BaseColor2;    // colour on the cobble tops
half _Smoothness;
half _Metallic;
float _TexScale;      // texture repeats per metre (the source's _MainTex_ST.xy)
CBUFFER_END

// rgb = tangent-space normal (0..1 packed, +y = +v), a = height. Linear, not sRGB.
TEXTURE2D(_TriTex); SAMPLER(sampler_TriTex);

// Set per camera before it renders: 0 = vertex normal only, 1 = naive, 2 = UDN, 3 = Whiteout, 4 = RNM, 5 = GPU Gems 3
float _GT_TriMode;

// Reoriented Normal Mapping, as altered in the source to take -1..1 normals
half3 blend_rnm(half3 n1, half3 n2)
{
    n1.z += 1;
    n2.xy = -n2.xy;
    return n1 * dot(n1, n2) / n1.z - n2;
}

half3 UnpackTri(half4 t) { return t.rgb * 2 - 1; }

// Returns the world normal for the chosen method and the triplanar-blended height (for the albedo).
half3 TriplanarNormal(float3 pos, half3 N, half4 tangentWS, int mode, out half height)
{
    // triplanar blend. The source writes saturate(pow(worldNormal, 4)) in most files and pow(abs(n), 4) in
    // Whiteout; abs() is used for all here so negative axes never depend on how pow() of a negative number compiles.
    half3 triblend = pow(abs(N), 4);
    triblend /= max(dot(triblend, half3(1, 1, 1)), 0.0001);

    float2 uvX = pos.zy * _TexScale;
    float2 uvY = pos.xz * _TexScale;
    float2 uvZ = pos.xy * _TexScale;

    // minor optimization of sign(), prevents return value of 0
    half3 axisSign = N < 0 ? -1 : 1;
    // flip UVs horizontally to correct for back side projection (TRIPLANAR_CORRECT_PROJECTED_U; not in the naive file)
    if (mode != 1)
    {
        uvX.x *= axisSign.x;
        uvY.x *= axisSign.y;
        uvZ.x *= -axisSign.z;
    }

    half4 sX = SAMPLE_TEXTURE2D(_TriTex, sampler_TriTex, uvX);
    half4 sY = SAMPLE_TEXTURE2D(_TriTex, sampler_TriTex, uvY);
    half4 sZ = SAMPLE_TEXTURE2D(_TriTex, sampler_TriTex, uvZ);
    height = sX.a * triblend.x + sY.a * triblend.y + sZ.a * triblend.z;
    if (mode == 0) return N;

    half3 tnormalX = UnpackTri(sX);
    half3 tnormalY = UnpackTri(sY);
    half3 tnormalZ = UnpackTri(sZ);

    if (mode == 1)
    {
        // naive: blend the three tangent-space samples, then apply them with the mesh's own tangent frame
        half3 tnormal = tnormalX * triblend.x + tnormalY * triblend.y + tnormalZ * triblend.z;
        half3 T = normalize(tangentWS.xyz);
        half3 B = cross(N, T) * (tangentWS.w * GetOddNegativeScale());
        return normalize(T * tnormal.x + B * tnormal.y + N * tnormal.z);
    }

    // flip normal maps' x axis to account for flipped UVs
    tnormalX.x *= axisSign.x;
    tnormalY.x *= axisSign.y;
    tnormalZ.x *= -axisSign.z;

    if (mode == 2)
    {
        // swizzle world normals to match tangent space and apply ala UDN normal blending
        tnormalX = half3(tnormalX.xy + N.zy, N.x);
        tnormalY = half3(tnormalY.xy + N.xz, N.y);
        tnormalZ = half3(tnormalZ.xy + N.xy, N.z);
    }
    else if (mode == 3)
    {
        // swizzle world normals to match tangent space and apply Whiteout normal blend
        tnormalX = half3(tnormalX.xy + N.zy, tnormalX.z * N.x);
        tnormalY = half3(tnormalY.xy + N.xz, tnormalY.z * N.y);
        tnormalZ = half3(tnormalZ.xy + N.xy, tnormalZ.z * N.z);
    }
    else if (mode == 4)
    {
        half3 absVertNormal = abs(N);
        // swizzle world normals to match tangent space and apply reoriented normal mapping blend
        tnormalX = blend_rnm(half3(N.zy, absVertNormal.x), tnormalX);
        tnormalY = blend_rnm(half3(N.xz, absVertNormal.y), tnormalY);
        tnormalZ = blend_rnm(half3(N.xy, absVertNormal.z), tnormalZ);
        // apply world space sign to tangent space Z
        tnormalX.z *= axisSign.x;
        tnormalY.z *= axisSign.y;
        tnormalZ.z *= axisSign.z;
    }
    else
    {
        // GPU Gems 3: swizzle tangent normal map to match world normals, blend and add to the world normal
        half3 normalX = half3(0.0, tnormalX.yx);
        half3 normalY = half3(tnormalY.x, 0.0, tnormalY.y);
        half3 normalZ = half3(tnormalZ.xy, 0.0);
        return normalize(normalX * triblend.x + normalY * triblend.y + normalZ * triblend.z + N);
    }

    // swizzle tangent normals to match world normal and blend together
    return normalize(tnormalX.zyx * triblend.x + tnormalY.xzy * triblend.y + tnormalZ.xyz * triblend.z);
}

struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; };
struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float4 tangentWS : TEXCOORD2;
};

Varyings Vert(Attributes i)
{
    Varyings o;
    o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
    o.positionCS = TransformWorldToHClip(o.positionWS);
    o.normalWS = TransformObjectToWorldNormal(i.normalOS);
    o.tangentWS = float4(TransformObjectToWorldDir(i.tangentOS.xyz), i.tangentOS.w);
    return o;
}

half3 Shade(Varyings i)
{
    half3 N = normalize(i.normalWS);
    half h;
    half3 n = TriplanarNormal(i.positionWS, N, i.tangentWS, (int)round(_GT_TriMode), h);
    InputData d = (InputData)0;
    d.positionWS = i.positionWS;
    d.normalWS = n;
    d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
    d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
    d.bakedGI = SampleSH(n);
    d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
    d.shadowMask = half4(1, 1, 1, 1);
    SurfaceData s = (SurfaceData)0;
    s.albedo = lerp(_BaseColor.rgb, _BaseColor2.rgb, smoothstep(0.15, 0.75, h));
    s.metallic = _Metallic; s.smoothness = _Smoothness * lerp(0.75, 1.0, h); s.occlusion = lerp(0.55, 1.0, saturate(h * 1.6)); s.alpha = 1;
    return UniversalFragmentPBR(d, s).rgb;
}

#endif
