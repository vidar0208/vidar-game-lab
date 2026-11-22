// Strand material for the Demo Team Hair renderer, written for this post. Vertex stage is the package's own
// (HairMaterialCommonUnlit.hlsl -> GetHairVertex reads the simulated particles). Fragment stage is mine:
// Kajiya-Kay style lighting with two shifted specular lobes for a key and a rim light set by the scene director,
// per-strand tint variation and darker roots.
Shader "GameTech/HairStrandKK"
{
	Properties
	{
		_BaseColor ("Base colour", Color) = (0.1, 0.065, 0.04, 1)
		_Spec1 ("Primary highlight", Color) = (0.32, 0.29, 0.26, 1)
		_Spec2 ("Secondary highlight", Color) = (0.35, 0.22, 0.12, 1)
		_Exp1 ("Primary exponent", Float) = 90
		_Exp2 ("Secondary exponent", Float) = 18
		_Shift1 ("Primary shift", Float) = 0.08
		_Shift2 ("Secondary shift", Float) = -0.08
		_RootDark ("Root darkening", Float) = 0.55
	}

	HLSLINCLUDE

	#pragma target 5.0

	#pragma multi_compile_local_vertex HAIR_VERTEX_STATIC HAIR_VERTEX_LIVE
	#pragma multi_compile_vertex PROCEDURAL_INSTANCING_ON

	#pragma instancing_options assumeuniformscaling nomatrices nolodfade nolightprobe nolightmap procedural:HairVertexInstancingSetup

	#include "Packages/com.unity.demoteam.hair/Runtime/HairMaterialCommonUnlit.hlsl"

	float4 _BaseColor, _Spec1, _Spec2;
	float _Exp1, _Exp2, _Shift1, _Shift2, _RootDark;
	float4 _GTKeyDir, _GTKeyCol, _GTRimDir, _GTRimCol, _GTAmbient;

	struct KKVaryings
	{
		float4 positionCS : SV_POSITION;
		float3 positionWS : TEXCOORD1;
		float3 tangentWS : TEXCOORD2;
		float3 normalWS : TEXCOORD3;
		float3 strandColor : COLOR;
		float2 strandUV : TEXCOORD0;
	};

	KKVaryings KKVert(UnlitAttribs IN)
	{
		UNITY_SETUP_INSTANCE_ID(IN);
		HairVertexData v = GetHairVertex(IN.packedID, IN.staticPositionOS, IN.staticNormalOS, IN.staticTangentOS);
		KKVaryings OUT;
		OUT.positionCS = TransformObjectToHClip(v.surfacePosition);
		OUT.positionWS = TransformObjectToWorld(v.surfacePosition);
		float3 sT = TransformObjectToWorldDir(v.surfaceTangent);
		float3 sN = TransformObjectToWorldDir(v.surfaceNormal);
		OUT.tangentWS = normalize(cross(sT, sN));     // along the strand
		OUT.normalWS = sN;
		OUT.strandColor = v.strandIndexColor;
		OUT.strandUV = v.surfaceUV;
		return OUT;
	}

	float KK(float3 T, float3 H, float e)
	{
		float th = dot(T, H);
		return pow(sqrt(saturate(1.0 - th * th)), e);
	}

	float3 Light(float3 T, float3 N, float3 V, float3 L, float3 C, float3 albedo)
	{
		float3 H = normalize(L + V);
		float wrap = saturate((dot(N, L) + 0.5) / 1.5);
		float tl = dot(T, L);
		float diff = lerp(0.25, 1.0, sqrt(saturate(1.0 - tl * tl)));
		float3 T1 = normalize(T + N * _Shift1), T2 = normalize(T + N * _Shift2);
		float atten = smoothstep(-0.2, 0.3, dot(N, L));
		float3 spec = _Spec1.rgb * KK(T1, H, _Exp1) + _Spec2.rgb * albedo * 4.0 * KK(T2, H, _Exp2);
		return C * (albedo * diff * wrap + spec * atten);
	}

	float4 KKFrag(KKVaryings IN, bool front : SV_IsFrontFace) : SV_Target
	{
		float3 V = normalize(_WorldSpaceCameraPos - IN.positionWS);
		float3 T = normalize(IN.tangentWS);
		float3 N = normalize(IN.normalWS); if (!front) N = -N;
		float h = frac(dot(IN.strandColor, float3(12.9898, 78.233, 37.719)) * 43.7585);
		float3 albedo = _BaseColor.rgb * lerp(0.8, 1.25, h);
		float root = lerp(1.0 - _RootDark, 1.0, smoothstep(0.0, 0.35, IN.strandUV.y));
		float3 c = Light(T, N, V, normalize(_GTKeyDir.xyz), _GTKeyCol.rgb, albedo)
		         + Light(T, N, V, normalize(_GTRimDir.xyz), _GTRimCol.rgb, albedo)
		         + _GTAmbient.rgb * albedo;
		return float4(c * root, 1.0);
	}

	ENDHLSL

	SubShader
	{
		Tags { "RenderType" = "Opaque" "DisableBatching" = "True" "Queue" = "Geometry" }
		ZTest LEqual
		ZWrite On
		Cull Off

		Pass
		{
			Name "Forward"
			Tags { "LightMode" = "UniversalForward" }
			HLSLPROGRAM
			#pragma vertex KKVert
			#pragma fragment KKFrag
			ENDHLSL
		}
	}

	Fallback Off
}
