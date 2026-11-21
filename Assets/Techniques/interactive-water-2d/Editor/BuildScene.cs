// Builds the 2D interactive water scene: a stone pool with a WaterRW surface (two instances, solver on / off, on
// layers 20 / 21), bodies that drop in on a schedule, a skimming hull, a dusk backdrop, two cameras and labels.
// Values for the water component and material are the ones in the source's sample scene.
using System.Collections.Generic;
using Ruccho;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace GameTech.Posts
{
    public static class WaterScene
    {
        const string Dir = "Assets/Techniques/interactive-water-2d/";
        const string Lit = "Universal Render Pipeline/Lit";
        const string Unlit = "Universal Render Pipeline/Unlit";
        const int StaticLayer = 2;   // Ignore Raycast: collides with bodies but is not picked up by the water's linecast

        static void Set(SerializedObject so, string name, System.Action<SerializedProperty> f)
        {
            var p = so.FindProperty(name);
            if (p == null) Debug.LogError("[gt] missing property " + name); else f(p);
        }

        static Texture2D NormalTex(string name, int seed)
        {
            const int N = 128;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var rnd = new System.Random(seed);
            var waves = new List<Vector4>();
            for (int i = 0; i < 7; i++) waves.Add(new Vector4(rnd.Next(1, 6) * (rnd.Next(2) * 2 - 1), rnd.Next(1, 6) * (rnd.Next(2) * 2 - 1), (float)rnd.NextDouble() * 6.28f, 1f / (1 + i * 0.5f)));
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (float)x / N, v = (float)y / N, dx = 0, dy = 0;
                    foreach (var w in waves)
                    {
                        float ph = 6.2831853f * (w.x * u + w.y * v) + w.z, d = Mathf.Cos(ph) * w.w;
                        dx += d * w.x; dy += d * w.y;
                    }
                    dx *= 0.08f; dy *= 0.08f;
                    px[y * N + x] = new Color(Mathf.Clamp01(0.5f + 0.5f * dx), Mathf.Clamp01(0.5f + 0.5f * dy), 1, 1);
                }
            t.SetPixels(px); t.Apply(true);
            AssetDatabase.CreateAsset(t, Dir + name + ".asset");
            return t;
        }

        static Texture2D Gradient(string name, Color[] stops)
        {
            const int H = 256;
            var t = new Texture2D(4, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[4 * H];
            for (int y = 0; y < H; y++)
            {
                float f = (float)y / (H - 1) * (stops.Length - 1);
                int i = Mathf.Min(stops.Length - 2, Mathf.FloorToInt(f));
                var c = Color.Lerp(stops[i], stops[i + 1], f - i);
                for (int x = 0; x < 4; x++) px[y * 4 + x] = c;
            }
            t.SetPixels(px); t.Apply();
            AssetDatabase.CreateAsset(t, Dir + name + ".asset");
            return t;
        }

        // filled silhouette between y = bottom and a sum-of-sines ridge
        static Mesh Ridge(string name, float x0, float x1, float bottom, float baseH, float amp, int seed)
        {
            var rnd = new System.Random(seed);
            float p1 = (float)rnd.NextDouble() * 6, p2 = (float)rnd.NextDouble() * 6, p3 = (float)rnd.NextDouble() * 6;
            var v = new List<Vector3>(); var tri = new List<int>(); const int n = 96;
            for (int i = 0; i <= n; i++)
            {
                float x = Mathf.Lerp(x0, x1, (float)i / n);
                float h = baseH + amp * (0.55f * Mathf.Sin(x * 0.21f + p1) + 0.3f * Mathf.Sin(x * 0.53f + p2) + 0.15f * Mathf.Sin(x * 1.3f + p3));
                v.Add(new Vector3(x, bottom, 0)); v.Add(new Vector3(x, h, 0));
                if (i > 0) { int b = (i - 1) * 2; tri.AddRange(new[] { b, b + 1, b + 3, b, b + 3, b + 2 }); }
            }
            var m = new Mesh { name = name }; m.SetVertices(v); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, Dir + name + ".asset");
            return m;
        }

        static GameObject Prim(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material m, Quaternion? rot = null, int layer = 0)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name; go.layer = layer;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.position = pos; go.transform.localScale = scale; go.transform.rotation = rot ?? Quaternion.identity;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            return go;
        }

        static void StaticBox(string name, Vector2 center, Vector2 size, float z0, float z1, Material m)
        {
            var go = Prim(PrimitiveType.Cube, name, new Vector3(center.x, center.y, (z0 + z1) / 2), new Vector3(size.x, size.y, z1 - z0), m, null, StaticLayer);
            var bc = go.AddComponent<BoxCollider2D>(); bc.size = Vector2.one;
        }

        static WaterRWCompute Water(string name, int layer, Material mat, ComputeShader cs)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.position = new Vector3(0, -1.6f, 0); go.transform.localScale = new Vector3(14f, 3.2f, 1f);  // surface at y = 0
            var mf = go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            var w = go.AddComponent<WaterRWCompute>();
            var so = new SerializedObject(w);
            Set(so, "meshFilter", p => p.objectReferenceValue = mf);
            Set(so, "computeShader", p => p.objectReferenceValue = cs);
            Set(so, "meshSegmentsPerUnit", p => p.floatValue = 4f);
            Set(so, "c", p => p.floatValue = 0.8f);
            Set(so, "decay", p => p.floatValue = 0.7f);
            Set(so, "spatialScale", p => p.floatValue = 0.125f);
            Set(so, "waveBufferPixelsPerUnit", p => p.floatValue = 4f);
            Set(so, "maxSurfaceWidth", p => p.intValue = 20);
            Set(so, "maxInteractionItems", p => p.intValue = 32);
            Set(so, "layersToInteractWith", p => p.intValue = 1);
            Set(so, "scrollToMainCamera", p => p.boolValue = false);
            so.ApplyModifiedPropertiesWithoutUndo();
            return w;
        }

        static Rigidbody2D Body(string name, bool round, Vector2 size, Material m, float mass)
        {
            var go = round
                ? Prim(PrimitiveType.Sphere, name, new Vector3(0, 6, 0.7f), new Vector3(size.x, size.x, size.x), m)
                : Prim(PrimitiveType.Cube, name, new Vector3(0, 6, 0.7f), new Vector3(size.x, size.y, Mathf.Min(0.6f, size.x)), m);
            if (round) go.AddComponent<CircleCollider2D>().radius = 0.5f; else go.AddComponent<BoxCollider2D>().size = Vector2.one;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.useAutoMass = false; rb.mass = mass; rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            return rb;
        }

        public static void Build()
        {
            SceneKit.Shadows(40f);
            SceneKit.Ambient(new Color(0.42f, 0.45f, 0.62f), new Color(0.55f, 0.42f, 0.40f), new Color(0.18f, 0.16f, 0.20f));
            SceneKit.Sun("Sun", new Vector3(28f, -35f, 0f), new Color(1.0f, 0.78f, 0.58f), 1.6f, true);

            // backdrop: dusk sky, sun disc, two hill layers
            var skyTex = Gradient("SkyGradient", new[] { new Color(0.95f, 0.56f, 0.36f), new Color(0.86f, 0.47f, 0.45f), new Color(0.43f, 0.34f, 0.55f), new Color(0.14f, 0.16f, 0.33f) });
            var skyM = SceneKit.Mat(Dir, "Sky", Unlit, Color.white); skyM.SetTexture("_BaseMap", skyTex);
            Prim(PrimitiveType.Quad, "Sky", new Vector3(0, 6.5f, 30f), new Vector3(90f, 26f, 1f), skyM);
            Prim(PrimitiveType.Sphere, "Sun disc", new Vector3(-6.5f, 3.6f, 28f), new Vector3(2.6f, 2.6f, 0.1f), SceneKit.Mat(Dir, "SunDisc", Unlit, new Color(3.2f, 2.1f, 1.2f)));
            var far = SceneKit.Mat(Dir, "HillFar", Unlit, new Color(0.36f, 0.28f, 0.42f));
            var near = SceneKit.Mat(Dir, "HillNear", Unlit, new Color(0.20f, 0.17f, 0.28f));
            SceneKit.Part("Hills far", Ridge("HillFarMesh", -40, 40, -6, 2.2f, 1.6f, 3), far, new Vector3(0, 0, 22f), Quaternion.identity);
            SceneKit.Part("Hills near", Ridge("HillNearMesh", -30, 30, -6, 1.1f, 1.2f, 8), near, new Vector3(0, 0, 12f), Quaternion.identity);

            // pool: stone blocks either side, sand floor, rocks and weed under the water
            var stone = SceneKit.Mat(Dir, "Stone", Lit, new Color(0.46f, 0.42f, 0.40f), 0, 0.25f);
            var stoneTop = SceneKit.Mat(Dir, "StoneTop", Lit, new Color(0.56f, 0.52f, 0.47f), 0, 0.3f);
            var sand = SceneKit.Mat(Dir, "Sand", Lit, new Color(0.72f, 0.62f, 0.45f), 0, 0.15f);
            StaticBox("Ledge L", new Vector2(-8.4f, -1.4f), new Vector2(2.8f, 4.0f), -1.2f, 2.6f, stone);
            StaticBox("Ledge R", new Vector2(8.4f, -1.2f), new Vector2(2.8f, 4.4f), -1.2f, 2.6f, stone);
            Prim(PrimitiveType.Cube, "Ledge L top", new Vector3(-8.4f, 0.65f, 0.7f), new Vector3(3.0f, 0.12f, 3.9f), stoneTop);
            Prim(PrimitiveType.Cube, "Ledge R top", new Vector3(8.4f, 1.05f, 0.7f), new Vector3(3.0f, 0.12f, 3.9f), stoneTop);
            StaticBox("Floor", new Vector2(0, -3.45f), new Vector2(20f, 0.5f), -1.2f, 3.5f, sand);
            var rock = SceneKit.Mat(Dir, "Rock", Lit, new Color(0.33f, 0.36f, 0.38f), 0, 0.35f);
            var rnd = new System.Random(11);
            for (int i = 0; i < 9; i++)
            {
                float x = -6.3f + i * 1.55f + (float)rnd.NextDouble() * 0.6f, s = 0.35f + (float)rnd.NextDouble() * 0.45f;
                Prim(PrimitiveType.Sphere, "Rock " + i, new Vector3(x, -3.2f + s * 0.25f, 1.6f + (float)rnd.NextDouble() * 1.2f), new Vector3(s * 1.4f, s * 0.8f, s), rock, Quaternion.Euler(0, 0, (float)rnd.NextDouble() * 40 - 20));
            }
            var weed = SceneKit.Mat(Dir, "Weed", Lit, new Color(0.22f, 0.48f, 0.30f), 0, 0.4f);
            for (int i = 0; i < 12; i++)
            {
                float x = -6.6f + i * 1.15f + (float)rnd.NextDouble() * 0.5f, h = 0.8f + (float)rnd.NextDouble() * 1.0f;
                Prim(PrimitiveType.Capsule, "Weed " + i, new Vector3(x, -3.2f + h * 0.5f, 1.4f + (float)rnd.NextDouble() * 1.4f), new Vector3(0.09f, h * 0.5f, 0.09f), weed, Quaternion.Euler(0, 0, (float)rnd.NextDouble() * 24 - 12));
            }

            // water: same material values as the source's sample scene; normal maps generated here
            var cs = AssetDatabase.LoadAssetAtPath<ComputeShader>(Dir + "WaterRW/WaterRWCompute.compute");
            if (cs == null) Debug.LogError("[gt] compute shader not found");
            var wm = new Material(Shader.Find("GameTech/WaterRW"));
            wm.SetTexture("_NormalA", NormalTex("NormalA", 5)); wm.SetTextureScale("_NormalA", new Vector2(2, 8));
            wm.SetTexture("_NormalB", NormalTex("NormalB", 9)); wm.SetTextureScale("_NormalB", new Vector2(2, 8));
            wm.SetFloat("_WaveBufferPixelsPerUnit", 4f); wm.SetFloat("_WaveHeight", 0.25f);   // source: 0.1
            AssetDatabase.CreateAsset(wm, Dir + "Water.mat");
            var on = Water("Water (solver on)", 20, wm, cs);
            var off = Water("Water (solver off)", 21, wm, cs);

            // bodies
            var colours = new[] { new Color(0.86f, 0.32f, 0.22f), new Color(0.95f, 0.75f, 0.25f), new Color(0.30f, 0.55f, 0.85f), new Color(0.92f, 0.90f, 0.84f), new Color(0.55f, 0.38f, 0.24f) };
            var mats = new Material[colours.Length];
            for (int i = 0; i < colours.Length; i++) mats[i] = SceneKit.Mat(Dir, "Body" + i, Lit, colours[i], 0, 0.45f);
            var times = new[] { 0.3f, 1.6f, 3.2f, 4.6f, 6.2f, 7.8f, 9.0f, 10.5f, 12.0f, 13.6f, 15.0f, 19.8f, 21.5f, 23.0f };
            var xs = new[] { -1.8f, 1.4f, -0.2f, 2.3f, -2.4f, 0.6f, -1.0f, 2.0f, -2.0f, 0.9f, -0.5f, 1.8f, -1.6f, 0.4f };
            var bodies = new List<Rigidbody2D>();
            for (int i = 0; i < times.Length; i++)
            {
                int kind = i % 4;
                Rigidbody2D b;
                if (kind == 0) b = Body("Ball " + i, true, new Vector2(0.7f, 0.7f), mats[i % 3], 1f);
                else if (kind == 1) b = Body("Crate " + i, false, new Vector2(0.75f, 0.75f), mats[4], 1.2f);
                else if (kind == 2) b = Body("Ball " + i, true, new Vector2(0.5f, 0.5f), mats[(i + 1) % 4], 0.6f);
                else b = Body("Plank " + i, false, new Vector2(1.6f, 0.22f), mats[4], 1f);
                bodies.Add(b);
            }
            var hull = SceneKit.Mat(Dir, "Hull", Lit, new Color(0.88f, 0.86f, 0.80f), 0, 0.55f);
            var sk = Prim(PrimitiveType.Cube, "Skimmer", new Vector3(-5.2f, -0.06f, 0.7f), new Vector3(1.0f, 0.3f, 0.5f), hull);
            sk.AddComponent<BoxCollider2D>().size = Vector2.one;
            var cabin = Prim(PrimitiveType.Cube, "Cabin", Vector3.zero, Vector3.one, mats[0]);
            cabin.transform.SetParent(sk.transform, false); cabin.transform.localPosition = new Vector3(-0.1f, 0.85f, 0); cabin.transform.localScale = new Vector3(0.4f, 1.2f, 0.7f);
            var skb = sk.AddComponent<Rigidbody2D>(); skb.bodyType = RigidbodyType2D.Kinematic; skb.interpolation = RigidbodyInterpolation2D.Interpolate;

            // cameras
            var bg = new Color(0.14f, 0.16f, 0.33f);
            var clear = SceneKit.Cam("Clear", new Rect(0, 0, 1, 1), 30, Color.black, -10, false); clear.cullingMask = 0;
            var camA = SceneKit.Cam("Cam A", new Rect(0, 0, 1, 1), 22, bg, 0);
            var camB = SceneKit.Cam("Cam B", new Rect(0.5f, 0, 0.5f, 1), 24, bg, 1);
            foreach (var c in new[] { camA, camB })
            {
                c.farClipPlane = 80f;
                c.GetComponent<UniversalAdditionalCameraData>().requiresColorTexture = true;
                c.transform.position = new Vector3(0, 1.4f, -27f);
            }
            camA.tag = "MainCamera";
            SceneKit.Grade(Dir, 0.45f, 0f, 12f, 6f, 0.25f, 0.15f);

            // labels
            var title = LabelSpec.Create("", 0.012f, 0.935f, 46);
            var l1 = LabelSpec.Create("", 0.012f, 0.03f, 30);
            var l2 = LabelSpec.Create("", 0.012f, 0.018f, 26);
            var m1 = LabelSpec.Create("", 0.512f, 0.058f, 30);
            var m2 = LabelSpec.Create("", 0.512f, 0.018f, 26);

            var d = new GameObject("Director").AddComponent<WaterDirector>();
            d.camA = camA; d.camB = camB; d.waterOn = on; d.waterOff = off;
            d.bodies = bodies.ToArray(); d.dropTimes = times; d.dropX = xs; d.skimmer = skb;
            d.title = title; d.l1 = l1; d.l2 = l2; d.m1 = m1; d.m2 = m2;
            Debug.Log("[gt] water scene built: " + bodies.Count + " bodies");
        }
    }
}
