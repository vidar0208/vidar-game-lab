// 74 ice refraction: ice slab, crystals, shard and ball on a snow field at dusk, with lanterns, striped poles,
// trees and a cabin behind them so the shifted scene colour is easy to read. Everything is built from code.
// Each ice object exists twice (layers 8 and 9) with two materials, so a split screen can show two settings.
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace GameTech.Posts
{
    public static class IceScene
    {
        const string Dir = "Assets/Techniques/ice-refraction/";
        const string Lit = "Universal Render Pipeline/Lit";
        static Material matL, matR;
        static int iceCount;

        static Material Glow(string name, Color c)
        {
            var m = new Material(Shader.Find("GameTech/Glow")); m.SetColor("_BaseColor", c);
            AssetDatabase.CreateAsset(m, Dir + name + ".mat");
            return m;
        }

        // box-projected UVs (2 m per tile at tiling 1) and tangents for a MeshKit mesh
        static Mesh WithUV(Mesh m, float uvScale = 0.5f)
        {
            var v = m.vertices; var n = m.normals; var uv = new Vector2[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                float ax = Mathf.Abs(n[i].x), ay = Mathf.Abs(n[i].y), az = Mathf.Abs(n[i].z);
                uv[i] = (ax >= ay && ax >= az ? new Vector2(v[i].z, v[i].y) : ay >= az ? new Vector2(v[i].x, v[i].z) : new Vector2(v[i].x, v[i].y)) * uvScale;
            }
            m.uv = uv; m.RecalculateTangents();
            return m;
        }

        static void Ice(string name, Mesh mesh, Vector3 pos, Quaternion rot, float scale = 1f)
        {
            for (int k = 0; k < 2; k++)
            {
                var go = SceneKit.Part(name + (k == 0 ? " L" : " R"), mesh, k == 0 ? matL : matR, pos, rot);
                go.transform.localScale = Vector3.one * scale; go.layer = k == 0 ? 8 : 9;
                if (k == 0) go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            iceCount++;
        }

        static void Lantern(Vector3 p, float h, Material dark, Material glow)
        {
            var k = new MeshKit();
            k.Box(new Vector3(0, h / 2, 0), new Vector3(0.08f, h, 0.08f));
            k.Box(new Vector3(0, h + 0.39f, 0), new Vector3(0.36f, 0.06f, 0.36f));
            k.Box(new Vector3(0, h + 0.01f, 0), new Vector3(0.30f, 0.04f, 0.30f));
            SceneKit.Part("Lantern post", k.Build("LanternPost"), dark, p, Quaternion.identity);
            SceneKit.Part("Lantern lamp", new MeshKit().Box(new Vector3(0, h + 0.20f, 0), new Vector3(0.24f, 0.34f, 0.24f)).Build("LanternLamp"), glow, p, Quaternion.identity)
                .GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var l = new GameObject("Lantern light").AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(1f, 0.62f, 0.28f); l.intensity = 7f; l.range = 7f; l.shadows = LightShadows.None;
            l.transform.position = p + new Vector3(0, h + 0.2f, 0.25f);
        }

        static void Pole(Vector3 p, float h, Material red, Material white)
        {
            var a = new MeshKit(); var b = new MeshKit();
            int bands = Mathf.RoundToInt(h / 0.3f);
            for (int i = 0; i < bands; i++)
                (i % 2 == 0 ? a : b).Lathe(new[] { new Vector2(i * 0.3f, 0), new Vector2(i * 0.3f, 0.07f), new Vector2(i * 0.3f + 0.3f, 0.07f), new Vector2(i * 0.3f + 0.3f, 0) }, 20);
            SceneKit.Part("Pole red", a.Build("PoleRed"), red, p, Quaternion.identity);
            SceneKit.Part("Pole white", b.Build("PoleWhite"), white, p, Quaternion.identity);
        }

        public static void Build()
        {
            iceCount = 0;
            // sharp scene colour for the refraction: opaque texture at full resolution (project default was 2x down)
            var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var so = new SerializedObject(rp);
            so.FindProperty("m_OpaqueDownsampling").intValue = 0; so.FindProperty("m_RequireOpaqueTexture").boolValue = true;
            so.ApplyModifiedProperties(); EditorUtility.SetDirty(rp);
            Debug.Log($"[gt] ice: opaque texture {rp.supportsCameraOpaqueTexture}, downsampling {rp.opaqueDownsampling}");

            SceneKit.Ambient(new Color(0.22f, 0.29f, 0.42f), new Color(0.15f, 0.19f, 0.27f), new Color(0.09f, 0.10f, 0.13f));
            SceneKit.Shadows(30f);
            var sun = SceneKit.Sun("Key light (cold)", new Vector3(34, 212, 0), new Color(0.66f, 0.78f, 1f), 1.7f, true);
            SceneKit.Sun("Rim light (warm)", new Vector3(10, 24, 0), new Color(1f, 0.72f, 0.5f), 0.45f, false);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.20f, 0.25f, 0.36f); RenderSettings.fogDensity = 0.016f;
            var sky = new Material(Shader.Find("Skybox/Procedural"));
            sky.SetFloat("_SunSize", 0.02f); sky.SetFloat("_AtmosphereThickness", 0.72f); sky.SetFloat("_Exposure", 0.5f);
            sky.SetColor("_SkyTint", new Color(0.28f, 0.38f, 0.62f)); sky.SetColor("_GroundColor", new Color(0.20f, 0.24f, 0.32f));
            AssetDatabase.CreateAsset(sky, Dir + "Sky.mat");
            RenderSettings.skybox = sky; RenderSettings.sun = sun;

            // ---- snow field
            var snow = SceneKit.Mat(Dir, "Snow", Lit, new Color(0.80f, 0.85f, 0.93f), 0f, 0.3f);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); Object.DestroyImmediate(ground.GetComponent<Collider>());
            ground.name = "Snow field"; ground.transform.localScale = Vector3.one * 12; ground.GetComponent<Renderer>().sharedMaterial = snow;
            var mounds = new MeshKit();
            var rng = new System.Random(74);
            for (int i = 0; i < 16; i++)
            {
                float x = (float)rng.NextDouble() * 30f - 15f, z = -4f - (float)rng.NextDouble() * 16f, r = 1.2f + (float)rng.NextDouble() * 2.2f, hh = 0.25f + (float)rng.NextDouble() * 0.5f;
                var prof = new[] { new Vector2(-0.1f, 0), new Vector2(-0.1f, r), new Vector2(0, r), new Vector2(hh * 0.6f, r * 0.72f), new Vector2(hh * 0.92f, r * 0.34f), new Vector2(hh, 0) };
                var one = new MeshKit().Lathe(prof, 28);
                foreach (var p in one.v) mounds.v.Add(p + new Vector3(x, 0, z));
                foreach (var t in one.t) mounds.t.Add(t + mounds.v.Count - one.v.Count);
            }
            SceneKit.Part("Snow mounds", mounds.Build("Mounds", 60f), snow, Vector3.zero, Quaternion.identity);

            // ---- things behind the ice: lanterns, striped poles, trees, cabin
            var dark = SceneKit.Mat(Dir, "DarkMetal", Lit, new Color(0.06f, 0.06f, 0.07f), 0.6f, 0.4f);
            var lamp = Glow("LampGlow", new Color(2.4f, 0.85f, 0.18f));
            var red = SceneKit.Mat(Dir, "PoleRed", Lit, new Color(0.78f, 0.07f, 0.05f), 0f, 0.4f);
            var white = SceneKit.Mat(Dir, "PoleWhite", Lit, new Color(0.9f, 0.9f, 0.9f), 0f, 0.4f);
            Lantern(new Vector3(-0.6f, 0, -1.8f), 1.05f, dark, lamp);
            Lantern(new Vector3(2.3f, 0, -2.3f), 1.35f, dark, lamp);
            Lantern(new Vector3(-3.4f, 0, -3.0f), 1.2f, dark, lamp);
            Pole(new Vector3(0.25f, 0, -1.4f), 2.4f, red, white);
            Pole(new Vector3(0.85f, 0, -1.7f), 2.1f, red, white);
            Pole(new Vector3(3.2f, 0, -1.6f), 2.4f, red, white);
            Pole(new Vector3(-2.45f, 0, -1.5f), 2.4f, red, white);

            var pine = SceneKit.Mat(Dir, "Pine", Lit, new Color(0.12f, 0.27f, 0.19f), 0f, 0.2f);
            var trunk = SceneKit.Mat(Dir, "Trunk", Lit, new Color(0.12f, 0.08f, 0.06f), 0f, 0.2f);
            var trees = new MeshKit(); var trunks = new MeshKit();
            for (int i = 0; i < 14; i++)
            {
                float x = -13f + i * 2.0f + (float)rng.NextDouble() * 1.2f, z = -6f - (float)rng.NextDouble() * 9f, s = 0.8f + (float)rng.NextDouble() * 0.7f;
                if (Mathf.Abs(x + 4.5f) < 2.4f && z > -10f) z = -11f;      // keep the cabin clear
                void Add(MeshKit dst, Vector2[] prof)
                {
                    var one = new MeshKit().Lathe(prof, 12);
                    foreach (var p in one.v) dst.v.Add(p * s + new Vector3(x, 0, z));
                    foreach (var t in one.t) dst.t.Add(t + dst.v.Count - one.v.Count);
                }
                Add(trunks, new[] { new Vector2(0, 0), new Vector2(0, 0.12f), new Vector2(1.0f, 0.10f), new Vector2(1.0f, 0) });
                for (int c = 0; c < 3; c++)
                    Add(trees, new[] { new Vector2(0.8f + c * 0.9f, 0), new Vector2(0.8f + c * 0.9f, 1.1f - c * 0.28f), new Vector2(2.3f + c * 0.9f, 0) });
            }
            SceneKit.Part("Trees", trees.Build("Trees", 40f), pine, Vector3.zero, Quaternion.identity);
            SceneKit.Part("Trunks", trunks.Build("Trunks", 40f), trunk, Vector3.zero, Quaternion.identity);

            var wood = SceneKit.Mat(Dir, "Wood", Lit, new Color(0.30f, 0.20f, 0.14f), 0f, 0.25f);
            var cabin = new MeshKit().Box(new Vector3(0, 1.0f, 0), new Vector3(3.2f, 2.0f, 2.6f));
            SceneKit.Part("Cabin", cabin.Build("Cabin"), wood, new Vector3(-4.5f, 0, -8f), Quaternion.Euler(0, 14, 0));
            var roof = new MeshKit().Box(new Vector3(0, 0, 0), new Vector3(2.0f, 0.14f, 3.0f));
            SceneKit.Part("Roof a", roof.Build("Roof"), snow, new Vector3(-4.5f, 2.45f, -8f) + Quaternion.Euler(0, 14, 0) * new Vector3(-0.82f, 0, 0), Quaternion.Euler(0, 14, 0) * Quaternion.Euler(0, 0, 32));
            SceneKit.Part("Roof b", roof.Build("Roof"), snow, new Vector3(-4.5f, 2.45f, -8f) + Quaternion.Euler(0, 14, 0) * new Vector3(0.82f, 0, 0), Quaternion.Euler(0, 14, 0) * Quaternion.Euler(0, 0, -32));
            SceneKit.Part("Window", new MeshKit().Box(Vector3.zero, new Vector3(0.7f, 0.6f, 0.05f)).Build("Window"), Glow("WindowGlow", new Color(2.0f, 0.8f, 0.2f)),
                new Vector3(-4.5f, 1.1f, -8f) + Quaternion.Euler(0, 14, 0) * new Vector3(0.4f, 0, 1.31f), Quaternion.Euler(0, 14, 0));

            // ---- ice
            matL = new Material(Shader.Find("GameTech/IceRefraction")); AssetDatabase.CreateAsset(matL, Dir + "IceLeft.mat");
            matR = new Material(Shader.Find("GameTech/IceRefraction")); AssetDatabase.CreateAsset(matR, Dir + "IceRight.mat");
            Ice("Slab", WithUV(new MeshKit().Box(Vector3.zero, new Vector3(2.4f, 1.76f, 0.42f)).Build("Slab", 1f)), new Vector3(0, 0.86f, 0), Quaternion.Euler(0, 6, 0));
            Mesh Crystal(string name, float h, float r) =>
                WithUV(new MeshKit().Lathe(new[] { new Vector2(-0.2f, 0), new Vector2(-0.2f, r), new Vector2(h * 0.78f, r * 0.92f), new Vector2(h, 0) }, 6).Build(name, 1f));
            Ice("Crystal 1", Crystal("Crystal1", 2.1f, 0.36f), new Vector3(2.5f, 0, 0.2f), Quaternion.Euler(4, 0, -5));
            Ice("Crystal 2", Crystal("Crystal2", 1.5f, 0.30f), new Vector3(3.15f, 0, 0.45f), Quaternion.Euler(-8, 25, 10));
            Ice("Crystal 3", Crystal("Crystal3", 1.1f, 0.26f), new Vector3(1.95f, 0, 0.55f), Quaternion.Euler(6, 40, -14));
            Ice("Crystal 4", Crystal("Crystal4", 0.8f, 0.22f), new Vector3(2.7f, 0, 0.9f), Quaternion.Euler(14, 10, 6));
            Ice("Shard", WithUV(new MeshKit().Box(Vector3.zero, new Vector3(0.95f, 2.3f, 0.3f)).Build("Shard", 1f)), new Vector3(-2.5f, 1.1f, 0.15f), Quaternion.Euler(0, -22, 5));
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Ice("Ball", ball.GetComponent<MeshFilter>().sharedMesh, new Vector3(1.2f, 0.36f, 1.15f), Quaternion.identity, 0.74f);
            Object.DestroyImmediate(ball);

            var dr = new GameObject("Director").AddComponent<IceDirector>();
            dr.matL = matL; dr.matR = matR;
            var bg = new Color(0.10f, 0.13f, 0.2f);
            dr.camL = SceneKit.Cam("Camera left", new Rect(0, 0, 0.5f, 1), 32, bg, 0); dr.camL.cullingMask = ~(1 << 9);
            dr.camR = SceneKit.Cam("Camera right", new Rect(0.5f, 0, 0.5f, 1), 32, bg, 1); dr.camR.cullingMask = ~(1 << 8);
            dr.camFull = SceneKit.Cam("Camera full", new Rect(0, 0, 1, 1), 32, bg, 2); dr.camFull.cullingMask = ~(1 << 8); dr.camFull.tag = "MainCamera";
            foreach (var cam in new[] { dr.camL, dr.camR, dr.camFull })
            {
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.GetComponent<UniversalAdditionalCameraData>().requiresColorOption = CameraOverrideOption.On;
            }

            SceneKit.Grade(Dir, 0.5f, 0.35f, 10f, 8f, 0.3f, 0f);

            LabelSpec.Create("OFF: scene colour, no offset", 0.03f, 0.05f).During(2.0f, 8f);
            LabelSpec.Create("ON: offset + ice colour + Fresnel", 0.53f, 0.05f).During(2.0f, 8f);
            LabelSpec.Create("1. Scene colour, read at the pixel's own position", 0.03f, 0.05f).During(8.2f, 9.5f);
            LabelSpec.Create("2. + UV offset from the ice normal (refract strength -0.02)", 0.03f, 0.05f).During(9.5f, 11f);
            LabelSpec.Create("3. + ice colour (0.12, 1.32, 2.0)", 0.03f, 0.05f).During(11f, 12.5f);
            LabelSpec.Create("4. + Fresnel (strength 0.2 = power 5)", 0.03f, 0.05f).During(12.5f, 14f);
            LabelSpec.Create("Ice tiling 1", 0.03f, 0.05f).During(14.2f, 16.5f);
            LabelSpec.Create("Ice tiling 3", 0.53f, 0.05f).During(14.2f, 16.5f);
            LabelSpec.Create("Fresnel strength 0.2", 0.03f, 0.05f).During(16.5f, 19f);
            LabelSpec.Create("Fresnel strength 1.0", 0.53f, 0.05f).During(16.5f, 19f);
            LabelSpec.Create("Camera moves behind the slab: the lantern is read through the ice", 0.03f, 0.05f).During(19.3f, 24.1f);
            Debug.Log($"[gt] ice scene: {iceCount} ice objects");
        }
    }
}
