#ifndef CAPSULE_OCCLUSION_INCLUDED
#define CAPSULE_OCCLUSION_INCLUDED

#include "CapsuleOcclusionShared.hlsl"

// GameTech change: clusters in a 1D buffer (count, pointer) instead of 3D integer textures (Metal-friendly)
StructuredBuffer<uint2> _CapsuleClusters;
StructuredBuffer<uint2> _CapsuleClusterData;

bool GetCluster(float2 uv, float linearDepth, out uint2 cluster)
{
	int3 p = GetClusterPos(uv, linearDepth);
	cluster = uint2(0, 0);
	if (any(p < 0) || any(p >= (int3)_ClusterSize)) return false;
	cluster = _CapsuleClusters[ClusterPosToIndex((uint3)p)];
	return true;
}

float _CapsuleOcclusionIntensity;

#define CalcOcclusionCapsule capOcclusionFast

float _smooth(float x) { return x * x * (3.0 - 2.0 * x); }

float capOcclusionFast(float3 p, float3 n, float3 a, float3 b, float r, float maxD)
{
    float r2 = r*r;
    // Original function but in ^2 space
    float3 ba = b - a, pa = p - a;
    float h = saturate(dot(pa,ba)/dot(ba,ba));
    float3 d = pa - h*ba;
    float l = dot(d,d);
    float o = -dot(d,n)*r2*r/(l*l);
    // Max dist
    o *= _smooth(saturate(1.0 - (l-r2)/(maxD*maxD)));
	return saturate(o);
}

float capOcclusion(float3 p, float3 n, float3 a, float3 b, float r, float maxD)
{
    // closest sphere
    float3  ba = b - a, pa = p - a;
    float h = saturate(dot(pa,ba)/dot(ba,ba));
    float3  d = pa - h*ba;
    float l = length(d);
    float o = dot(-d,n)*r*r/(l*l*l); // occlusion of closest sphere
    o *= 1.0 + r*(l-r)/(l*l);  // multiplier
	// o *= 1.0 - saturate((l - r) / maxD);
    return saturate(1.0 - o);
}

float GetCapsuleOcclusion(float3 positionWS, float3 normalWS, float2 uv, float linearDepth)
{
	float occlusion = 0.0;

	uint2 cluster;
	GetCluster(uv, linearDepth, cluster);
	uint count = cluster.x;
	uint pointer = cluster.y;

	for (uint j = 0; j < count; j++)
	{
		uint2 clusterData = _CapsuleClusterData[pointer];

		float4 param1 = _CapsuleParams1[clusterData.x];
		float4 param2 = _CapsuleParams2[clusterData.x];
		occlusion += CalcOcclusionCapsule(positionWS, normalWS, param1.xyz, param2.xyz, param1.w, param2.w);

		pointer = clusterData.y;
	}

	return 1.0 / (max(0, occlusion * _CapsuleOcclusionIntensity) + 1.0);
}

float3 GetHeatColorBand(float3 col, float x, float y)
{
    return col * _smooth(saturate(1.0 - abs(x - y) * 4.0));
}
float3 GetHeatColor(float x)
{
    return GetHeatColorBand(float3(1,0,0), x, 1.0) +
        GetHeatColorBand(float3(1,1,0), x, 0.75) +
        GetHeatColorBand(float3(0,1,0), x, 0.5) +
        GetHeatColorBand(float3(0,1,1), x, 0.25) +
        GetHeatColorBand(float3(0,0,1), x, 0.0);
}

float3 GetCapsuleOcclusionDebugColor(float3 positionWS, float3 normalWS, float2 uv, float linearDepth)
{
	float occlusion = GetCapsuleOcclusion(positionWS, normalWS, uv, linearDepth);
	uint2 cluster;
	GetCluster(uv, linearDepth, cluster);
	float3 heat = GetHeatColor(1.0 - exp(-(float)cluster.x / 10));
	return (normalWS.y * 0.25 + 0.75) * heat * occlusion;
}

#endif // CAPSULE_OCCLUSION_INCLUDED