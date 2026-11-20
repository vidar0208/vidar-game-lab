// Per-light settings, as in VolumetricLight.cs of SlightlyMad/VolumetricLights (BSD-3-Clause,
// Copyright (c) 2016, Michal Skalsky). Same fields and defaults; height fog and the cookie are not ported.
using System.Collections.Generic;
using UnityEngine;

namespace GameTech.VolumetricLightsURP
{
    [RequireComponent(typeof(Light))]
    public class VolumetricLight : MonoBehaviour
    {
        public static readonly List<VolumetricLight> All = new List<VolumetricLight>();

        [Range(1, 64)] public int SampleCount = 8;
        [Range(0.0f, 1.0f)] public float ScatteringCoef = 0.5f;
        [Range(0.0f, 0.1f)] public float ExtinctionCoef = 0.01f;
        [Range(0.0f, 0.999f)] public float MieG = 0.1f;
        public bool Noise = false;
        public float NoiseScale = 0.015f;
        public float NoiseIntensity = 1.0f;
        public float NoiseIntensityOffset = 0.3f;
        public Vector2 NoiseVelocity = new Vector2(3.0f, 3.0f);
        public float MaxRayLength = 400.0f;

        Light _light;
        public Light Light => _light != null ? _light : (_light = GetComponent<Light>());

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        // the source's _MieG vector
        public Vector4 MieVector => new Vector4(1 - (MieG * MieG), 1 + (MieG * MieG), 2 * MieG, 1.0f / (4.0f * Mathf.PI));
    }
}
