using UnityEngine;

namespace GameTech.VolumetricLightsURP
{
    // Per-camera switches for the comparisons (not in the source): volumetrics on/off, volume shadows on/off,
    // a sample count that overrides every light (0 = use each light's own).
    public class VolumetricCamera : MonoBehaviour
    {
        public bool volumetric = true;
        public bool shadows = true;
        public int sampleOverride = 0;
        public bool skipDirectional = false;   // leave out the directional light (used to isolate the spots)
    }
}
