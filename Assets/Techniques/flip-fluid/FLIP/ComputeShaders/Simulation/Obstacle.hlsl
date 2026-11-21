#ifndef CS_SIMULATION_OBSTACLE_HLSL
#define CS_SIMULATION_OBSTACLE_HLSL

#if !defined(IS_CUBE_AREA_SIMULATION) && !defined(IS_SPHERE_AREA_SIMULATION)
#define IS_CUBE_AREA_SIMULATION
#endif

// GameTech change: an optional static box obstacle for the cube area (the source's cube area has none).
// _GTBoxMin.w > 0 turns it on. Cells whose centre is inside the box are solid; particles that end up inside are
// pushed out through the nearest face. The sphere area code below is the source's.
float4 _GTBoxMin;
float4 _GTBoxMax;

inline bool GTInsideBox(float3 p)
{
    return _GTBoxMin.w > 0.0 && all(p > _GTBoxMin.xyz) && all(p < _GTBoxMax.xyz);
}

inline bool IsSolidCell(float3 c_pos)
{
#if defined(IS_CUBE_AREA_SIMULATION)
    return GTInsideBox(c_pos);
#elif defined(IS_SPHERE_AREA_SIMULATION)
    return length(c_pos) > 12.0;
#endif
}

inline void ClampPositionByObstacles(inout float3 position)
{
#if defined(IS_CUBE_AREA_SIMULATION)
    if (GTInsideBox(position))
    {
        const float3 lo = position - _GTBoxMin.xyz;
        const float3 hi = _GTBoxMax.xyz - position;
        const float3 d = min(lo, hi);
        const float e = 1e-3;
        if (d.x <= d.y && d.x <= d.z) position.x = lo.x < hi.x ? _GTBoxMin.x - e : _GTBoxMax.x + e;
        else if (d.y <= d.z)          position.y = lo.y < hi.y ? _GTBoxMin.y - e : _GTBoxMax.y + e;
        else                          position.z = lo.z < hi.z ? _GTBoxMin.z - e : _GTBoxMax.z + e;
    }
#elif defined(IS_SPHERE_AREA_SIMULATION)
    const float3 dir_from_center = position;
    const float dist_from_center = length(dir_from_center);
    position = normalize(dir_from_center) * min(dist_from_center, 11.9);
#endif
}


#endif /* CS_SIMULATION_OBSTACLE_HLSL */
