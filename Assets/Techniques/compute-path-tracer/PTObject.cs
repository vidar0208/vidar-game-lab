// Path tracer material on a scene object (the same fields as the source's RayTracingMaterial). The object's
// MeshRenderer keeps its URP material for the raster comparison.
using UnityEngine;

namespace GameTech.Posts
{
    public class PTObject : MonoBehaviour
    {
        public Color diffuse = Color.white, emission = Color.black, specular = Color.white, absorption = Color.black;
        public float absorptionStrength, emissionStrength, smoothness, specularProbability, ior = 1.5f;
        public int flag;              // 0 normal, 2 glass (as in the source)
        public bool offscreen;        // the lamp outside the frame, left out of one render
    }
}
