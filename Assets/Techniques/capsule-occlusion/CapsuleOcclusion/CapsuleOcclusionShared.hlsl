#ifndef CAPSULE_OCCLUSION_SHARED_INCLUDED
#define CAPSULE_OCCLUSION_SHARED_INCLUDED

#define MAX_CAPSULE_COUNT 512

float4 _CapsuleParams1[MAX_CAPSULE_COUNT];
float4 _CapsuleParams2[MAX_CAPSULE_COUNT];
int _CapsuleCount;
float3 _ClusterSize; // GameTech change: float (was uint3), set from C# as a vector
float _DepthToRange;

// Note that these are not actually used, but would be if clusters were stored in a 1D structure instead of 3D.
uint ClusterPosToIndex(uint3 pos)
{
	uint3 cs = (uint3)_ClusterSize; // GameTech change: used, clusters are stored in a 1D buffer
	return (pos.z * cs.x * cs.y) + (pos.y * cs.x) + pos.x;
}
uint3 ClusterIndexToPos(uint id)
{
	uint3 cs = (uint3)_ClusterSize;
	uint z = id / (cs.x * cs.y);
	id -= (z * cs.x * cs.y);
	uint y = id / cs.x;
	uint x = id % cs.x;
	return uint3(x, y, z);
}

float3 ClusterPosToUVW(float3 pos)
{
	float3 uvw = (pos + 0.5) / _ClusterSize;
	// uvw.z = sq(uvw.z);
	uvw.z *= uvw.z;
	return uvw;
}

int3 GetClusterPos(float2 uv, float linearDepth) // GameTech change: int3, callers check the range
{
	float3 uvw = float3(uv, sqrt(linearDepth * _DepthToRange));
	return (int3)floor(uvw * _ClusterSize - float3(0, 0, 0.5));
}

#endif // CAPSULE_OCCLUSION_SHARED_INCLUDED