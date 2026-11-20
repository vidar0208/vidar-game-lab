// Builds the volumetric lights scene from code: a closed dark hall with three window slots in the back wall
// (a low sun shines through them), two hanging spot lamps (one above a slatted shelf), an amber point-light
// lantern, crates, a pillar and the stand-in mannequin walking under the cool spot. A copy of the URP asset uses
// one renderer with the VolumetricLightFeature; additional-light shadows are switched on for the volume shadows.
using System.Collections.Generic;
using GameTech.VolumetricLightsURP;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GameTech.Posts
{
    public static class VolumeScene
    {
        const string Dir = "Assets/Techniques/volumetric-lights/";
        const string Lit = "Universal Render Pipeline/Lit";

        static Material M(string name, Color c, float metal, float smooth)
        {
            var m = new Material(Shader.Find(Lit));
            m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth);
            AssetDatabase.CreateAsset(m, Dir + name + ".mat"); return m;
        }

        static Material Emit(string name, Color c, float k)
        {
            var m = M(name, Color.black, 0f, 0.2f);
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * k);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(m); return m;
        }

        static GameObject Box(string name, Material m, Vector3 pos, Vector3 size, Transform parent = null, float yaw = 0f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = size; go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            var r = go.GetComponent<Renderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.On;
            return go;
        }

        static GameObject Cyl(string name, Material m, Vector3 pos, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder); go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.position = pos; go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = m; return go;
        }

        static Light AddLight(string name, LightType type, Color c, float intensity, Vector3 pos, Vector3 euler, float range, float angle = 0f)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.type = type; l.color = c; l.intensity = intensity; l.range = range;
            if (type == LightType.Spot) { l.spotAngle = angle; l.innerSpotAngle = angle * 0.6f; }
            l.shadows = LightShadows.Hard; l.shadowBias = 0.03f; l.shadowNormalBias = 0.3f;
            l.transform.position = pos; l.transform.rotation = Quaternion.Euler(euler);
            return l;
        }

        static VolumetricLight Vol(Light l, int samples, float scattering, float mieG, bool noise)
        {
            var v = l.gameObject.AddComponent<VolumetricLight>();
            v.SampleCount = samples; v.ScatteringCoef = scattering; v.ExtinctionCoef = 0.01f; v.MieG = mieG;
            v.Noise = noise; v.NoiseScale = 0.11f; v.NoiseIntensity = 1.6f; v.NoiseIntensityOffset = 0.22f; v.NoiseVelocity = new Vector2(0.35f, 0.12f) / 0.11f;
            v.MaxRayLength = 40f;
            return v;
        }

        // the source's 8x8 dither pattern (VolumetricLightRenderer.GenerateDitherTexture), values n / 65
        static readonly int[] Dither8 = { 1,49,13,61,4,52,16,64, 33,17,45,29,36,20,48,32, 9,57,5,53,12,60,8,56, 41,25,37,21,44,28,40,24,
                                          3,51,15,63,2,50,14,62, 35,19,47,31,34,18,46,30, 11,59,7,55,10,58,6,54, 43,27,39,23,42,26,38,22 };

        static Texture2D DitherTex()
        {
            var t = new Texture2D(8, 8, TextureFormat.R8, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, name = "Dither8" };
            var px = new Color32[64];
            for (int i = 0; i < 64; i++) { byte b = (byte)(Dither8[i] / 65.0f * 255); px[i] = new Color32(b, b, b, b); }
            t.SetPixels32(px); t.Apply(false);
            AssetDatabase.CreateAsset(t, Dir + "Dither8.asset"); return t;
        }

        // tiling 3D value-noise fbm, 64^3 (stands in for the source's NoiseVolume file)
        static Texture3D NoiseTex()
        {
            int n = 64; var rnd = new System.Random(100);
            float[] lat(int g) { var a = new float[g * g * g]; for (int i = 0; i < a.Length; i++) a[i] = (float)rnd.NextDouble(); return a; }
            var oct = new[] { (8, lat(8), 0.5f), (16, lat(16), 0.3f), (32, lat(32), 0.2f) };
            var px = new byte[n * n * n];
            for (int z = 0; z < n; z++)
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float v = 0;
                        foreach (var (g, a, w) in oct)
                        {
                            float fx = x * g / (float)n, fy = y * g / (float)n, fz = z * g / (float)n;
                            int x0 = (int)fx, y0 = (int)fy, z0 = (int)fz; float tx = fx - x0, ty = fy - y0, tz = fz - z0;
                            tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty); tz = tz * tz * (3 - 2 * tz);
                            float L(int i, int j, int k) => a[((k % g) * g + (j % g)) * g + (i % g)];
                            float c00 = Mathf.Lerp(L(x0, y0, z0), L(x0 + 1, y0, z0), tx), c10 = Mathf.Lerp(L(x0, y0 + 1, z0), L(x0 + 1, y0 + 1, z0), tx);
                            float c01 = Mathf.Lerp(L(x0, y0, z0 + 1), L(x0 + 1, y0, z0 + 1), tx), c11 = Mathf.Lerp(L(x0, y0 + 1, z0 + 1), L(x0 + 1, y0 + 1, z0 + 1), tx);
                            v += w * Mathf.Lerp(Mathf.Lerp(c00, c10, ty), Mathf.Lerp(c01, c11, ty), tz);
                        }
                        px[(z * n + y) * n + x] = (byte)Mathf.Clamp(v * 255f, 0, 255);
                    }
            var t = new Texture3D(n, n, n, TextureFormat.R8, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "Noise3D" };
            t.SetPixelData(px, 0); t.Apply(false);
            AssetDatabase.CreateAsset(t, Dir + "Noise3D.asset"); return t;
        }

        public static void Build()
        {
            RenderSettings.skybox = null; RenderSettings.fog = false;
            SceneKit.Ambient(new Color(0.03f, 0.035f, 0.045f), new Color(0.02f, 0.02f, 0.025f), new Color(0.015f, 0.013f, 0.012f));
            var dir = new GameObject("Director").AddComponent<VolumeDirector>();
            dir.dither = DitherTex(); dir.noise = NoiseTex();

            // pipeline copy: one renderer (copy of the project's PC renderer) with the volumetric light feature
            var srcRp = (QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
            var srcRd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
            var rd = Object.Instantiate(srcRd); rd.name = "R_Volume";
            AssetDatabase.CreateAsset(rd, Dir + "R_Volume.asset");
            var feat = ScriptableObject.CreateInstance<VolumetricLightFeature>(); feat.name = "VolumetricLights";
            feat.shader = AssetDatabase.LoadAssetAtPath<Shader>(Dir + "VolumetricLights/VolumetricLightURP.shader");
            AssetDatabase.AddObjectToAsset(feat, rd);
            rd.rendererFeatures.Add(feat);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feat, out string _, out long lid);
            var so = new SerializedObject(rd);
            var map = so.FindProperty("m_RendererFeatureMap"); map.InsertArrayElementAtIndex(map.arraySize); map.GetArrayElementAtIndex(map.arraySize - 1).longValue = lid;
            so.ApplyModifiedPropertiesWithoutUndo();
            var rp = Object.Instantiate(srcRp); rp.name = "VolumePipeline";
            AssetDatabase.CreateAsset(rp, Dir + "VolumePipeline.asset");
            var rso = new SerializedObject(rp);
            void SetB(string p, bool v) { var pr = rso.FindProperty(p); if (pr != null) pr.boolValue = v; else Debug.LogWarning("[gt] no property " + p); }
            void SetI(string p, int v) { var pr = rso.FindProperty(p); if (pr != null) pr.intValue = v; else Debug.LogWarning("[gt] no property " + p); }
            void SetF(string p, float v) { var pr = rso.FindProperty(p); if (pr != null) pr.floatValue = v; else Debug.LogWarning("[gt] no property " + p); }
            var list = rso.FindProperty("m_RendererDataList"); list.ClearArray(); list.InsertArrayElementAtIndex(0); list.GetArrayElementAtIndex(0).objectReferenceValue = rd;
            SetI("m_DefaultRendererIndex", 0); SetB("m_SupportsHDR", true); SetI("m_MSAA", 1); SetF("m_RenderScale", 1f);
            SetF("m_ShadowDistance", 30f); SetI("m_ShadowCascadeCount", 1); SetI("m_MainLightShadowmapResolution", 4096);
            SetI("m_AdditionalLightsRenderingMode", 1); SetB("m_AdditionalLightShadowsSupported", true);
            SetI("m_AdditionalLightsShadowmapResolution", 4096);
            SetI("m_AdditionalLightsShadowResolutionTierLow", 1024); SetI("m_AdditionalLightsShadowResolutionTierMedium", 1024); SetI("m_AdditionalLightsShadowResolutionTierHigh", 2048);
            SetB("m_SoftShadowsSupported", true);
            rso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rd); EditorUtility.SetDirty(rp); AssetDatabase.SaveAssets();
            dir.pipeline = rp;
            Debug.Log($"[gt] pipeline copy: features {rd.rendererFeatures.Count} ({feat.shader?.name}), HDR {rp.supportsHDR}, add shadows {rp.supportsAdditionalLightShadows}, cascades {rp.shadowCascadeCount}");

            // hall: x -7..7, z -2..9, height 5, open at the front (camera side)
            var floorM = M("Floor", new Color(0.16f, 0.15f, 0.14f), 0f, 0.35f);
            var wallM = M("Wall", new Color(0.22f, 0.21f, 0.2f), 0f, 0.12f);
            var woodM = M("Wood", new Color(0.32f, 0.2f, 0.12f), 0f, 0.3f);
            var metalM = M("Metal", new Color(0.25f, 0.25f, 0.27f), 0.9f, 0.5f);
            var crateM = M("Crate", new Color(0.42f, 0.33f, 0.22f), 0f, 0.25f);
            var room = new GameObject("Hall").transform;
            Box("Floor", floorM, new Vector3(0, -0.1f, 3.5f), new Vector3(14f, 0.2f, 12f), room);
            Box("Ceiling", wallM, new Vector3(0, 5.1f, 3.5f), new Vector3(14f, 0.2f, 12f), room);
            Box("Wall L", wallM, new Vector3(-7.1f, 2.5f, 3.5f), new Vector3(0.2f, 5f, 12f), room);
            Box("Wall R", wallM, new Vector3(7.1f, 2.5f, 3.5f), new Vector3(0.2f, 5f, 12f), room);
            // back wall with three window slots (x centres -4, -1.4, 1.2; 0.8 m wide; y 1.0 .. 4.2)
            float[] slots = { -4f, -1.4f, 1.2f }; float sw = 0.8f; float y0 = 1.0f, y1 = 4.2f, wz = 9.1f;
            Box("Back low", wallM, new Vector3(0, y0 / 2, wz), new Vector3(14.2f, y0, 0.3f), room);
            Box("Back high", wallM, new Vector3(0, (y1 + 5.2f) / 2, wz), new Vector3(14.2f, 5.2f - y1, 0.3f), room);
            float x = -7.1f;
            for (int i = 0; i <= slots.Length; i++)
            {
                float xe = i < slots.Length ? slots[i] - sw / 2 : 7.1f;
                Box("Back pier " + i, wallM, new Vector3((x + xe) / 2, (y0 + y1) / 2, wz), new Vector3(xe - x, y1 - y0, 0.3f), room);
                if (i < slots.Length) { x = slots[i] + sw / 2; Box("Mullion " + i, metalM, new Vector3(slots[i], (y0 + y1) / 2, wz), new Vector3(0.05f, y1 - y0, 0.06f), room); }
                for (int k = 0; i < slots.Length && k < 3; k++) Box("Transom " + i + k, metalM, new Vector3(slots[i], y0 + 0.8f + k * 0.8f, wz), new Vector3(sw, 0.05f, 0.06f), room);
            }
            var skyM = Emit("Sky", new Color(1f, 0.85f, 0.65f), 0.9f);
            var sky = Box("Sky glow", skyM, new Vector3(0, 2.6f, 10.6f), new Vector3(16f, 5.2f, 0.1f), room);
            sky.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            // pillar, crates, slatted shelf under the warm spot, lamp housings
            Box("Pillar", wallM, new Vector3(-0.2f, 2.5f, 5.6f), new Vector3(0.6f, 5f, 0.6f), room);
            Box("Crate 1", crateM, new Vector3(-5.0f, 0.45f, 5.5f), new Vector3(0.9f, 0.9f, 0.9f), room, 12f);
            Box("Crate 2", crateM, new Vector3(-5.6f, 0.35f, 4.3f), new Vector3(0.7f, 0.7f, 0.7f), room, -20f);
            Box("Crate 3", crateM, new Vector3(-5.1f, 1.2f, 5.4f), new Vector3(0.6f, 0.6f, 0.6f), room, 35f);
            var shelf = new GameObject("Shelf").transform; shelf.SetParent(room); shelf.position = new Vector3(-2.6f, 0f, 3.2f);
            for (int s = 0; s < 4; s++) Box("Leg " + s, woodM, new Vector3(s < 2 ? -0.9f : 0.9f, 1.1f, s % 2 == 0 ? -0.35f : 0.35f), new Vector3(0.06f, 2.2f, 0.06f), shelf);
            for (int s = 0; s < 9; s++) Box("Slat " + s, woodM, new Vector3(-0.8f + s * 0.2f, 2.2f, 0f), new Vector3(0.09f, 0.04f, 0.8f), shelf);
            Box("Shelf board", woodM, new Vector3(0, 0.9f, 0f), new Vector3(1.9f, 0.04f, 0.8f), shelf);
            Box("Box on shelf", crateM, new Vector3(0.3f, 1.12f, 0f), new Vector3(0.45f, 0.4f, 0.45f), shelf, 20f);
            foreach (var p in new[] { new Vector3(-2.6f, 4.75f, 3.2f), new Vector3(3.0f, 4.75f, 4.4f) })
            {
                Cyl("Lamp shade", metalM, p, new Vector3(0.36f, 0.08f, 0.36f));
                Cyl("Lamp cord", metalM, p + new Vector3(0, 0.25f, 0), new Vector3(0.02f, 0.2f, 0.02f));
            }
            var bulbM = Emit("Bulb", new Color(1f, 0.85f, 0.6f), 6f);
            var lantern = Cyl("Lantern", bulbM, new Vector3(5.0f, 1.0f, 2.2f), new Vector3(0.12f, 0.1f, 0.12f));
            lantern.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            Box("Lantern stand", woodM, new Vector3(5.0f, 0.45f, 2.2f), new Vector3(0.5f, 0.9f, 0.5f), room);
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)) r.receiveShadows = true;

            // the stand-in mannequin (walks under the cool spot)
            var man = Mannequin.Build(Lit, Dir, (m, part) =>
            {
                m.SetColor("_BaseColor", part == "skin" ? new Color(0.78f, 0.62f, 0.52f) : new Color(0.55f, 0.15f, 0.12f));
                m.SetFloat("_Smoothness", part == "skin" ? 0.4f : 0.3f);
            });
            man.transform.position = new Vector3(3.0f, 0f, 4.4f);
            foreach (var smr in man.GetComponentsInChildren<SkinnedMeshRenderer>()) { smr.shadowCastingMode = ShadowCastingMode.On; smr.updateWhenOffscreen = true; }
            dir.character = man.transform;

            // lights: sun through the windows, warm spot over the shelf, cool spot over the walkway, amber lantern
            var sun = AddLight("Sun", LightType.Directional, new Color(1f, 0.88f, 0.7f), 2.2f, Vector3.zero, new Vector3(24f, 202f, 0f), 0f);
            var warm = AddLight("Warm spot", LightType.Spot, new Color(1f, 0.72f, 0.42f), 30f, new Vector3(-2.6f, 4.68f, 3.2f), new Vector3(90f, 0f, 0f), 8f, 34f);
            var cool = AddLight("Cool spot", LightType.Spot, new Color(0.62f, 0.8f, 1f), 30f, new Vector3(3.0f, 4.68f, 4.4f), new Vector3(90f, 0f, 0f), 8f, 42f);
            var lamp = AddLight("Lantern", LightType.Point, new Color(1f, 0.6f, 0.25f), 4f, new Vector3(5.0f, 1.25f, 2.2f), Vector3.zero, 4.5f);
            lamp.shadowNearPlane = 0.15f;
            dir.vols = new[] { Vol(sun, 8, 0.3f, 0.5f, true), Vol(warm, 8, 0.45f, 0.25f, true), Vol(cool, 8, 0.16f, 0.25f, true), Vol(lamp, 8, 0.6f, 0.1f, true) };

            SceneKit.Grade(Dir, 0.45f, 0.3f, 12f, 6f, 0.3f, 0.1f);
            AssetDatabase.SaveAssets();

            var bg = new Color(0.01f, 0.012f, 0.016f);
            dir.camA = SceneKit.Cam("Cam A", new Rect(0, 0, 1, 1), 50f, bg, 0, true);
            dir.camB = SceneKit.Cam("Cam B", new Rect(0, 0, 1, 1), 50f, bg, 1, true);
            dir.camC = SceneKit.Cam("Cam C", new Rect(0, 0, 1, 1), 50f, bg, 2, true);
            foreach (var c in new[] { dir.camA, dir.camB, dir.camC }) { c.nearClipPlane = 0.05f; c.farClipPlane = 60f; c.gameObject.AddComponent<VolumetricCamera>(); }
            var clear = SceneKit.Cam("Clear", new Rect(0, 0, 1, 1), 60f, Color.black, -10, false); clear.cullingMask = 0;

            dir.title = LabelSpec.Create("", 0.012f, 0.93f, 40);
            dir.l1 = LabelSpec.Create("", 0.012f, 0.058f, 34); dir.l2 = LabelSpec.Create("", 0.012f, 0.018f, 30);
            dir.m1 = LabelSpec.Create("", 0.512f, 0.058f, 34); dir.m2 = LabelSpec.Create("", 0.512f, 0.018f, 30);
            dir.r1 = LabelSpec.Create("", 0.68f, 0.058f, 34); dir.r2 = LabelSpec.Create("", 0.68f, 0.018f, 30);
            Debug.Log("[gt] built: hall, 3 windows, 2 spots, 1 point, sun, shelf, crates, mannequin");
        }
    }
}
