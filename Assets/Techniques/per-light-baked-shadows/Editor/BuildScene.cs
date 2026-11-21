// Builds the dynamic lighting scene: three identical open-top rooms (floor, three outer walls, a partition wall with a
// doorway, pillars, a bench), 40 m apart, each with the same three fixed point lights.
//   Room A: the lights are on the source's real-time channel (32): no precomputed shadows.
//   Room B: the lights are raytraced once by the source's tracer (per-light shadow masks), then stay steady.
//   Room C: the same raytraced lights; at runtime the director changes colour and the source's flicker effects run.
// Geometry is static (with Contribute GI) so the source's tracer picks it up. Each box gets its own mesh with a generated
// lightmap UV set (the tracer needs uv1). The scene is saved first, because the tracer writes its data next to the scene.
using System.Collections.Generic;
using AlpacaIT.DynamicLighting;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GameTech.Posts
{
    public static class DLScene
    {
        const string Dir = "Assets/Techniques/per-light-baked-shadows/";
        public const string DL = "Dynamic Lighting/Diffuse";
        public static readonly Vector3[] Rooms = { new Vector3(-40f, 0f, 0f), new Vector3(0f, 0f, 0f), new Vector3(40f, 0f, 0f) };
        static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();

        // a box mesh with its size baked in, uv0 in metres / 2 (for tiling), uv1 generated for the lightmap
        static Mesh BoxMesh(Vector3 size)
        {
            string key = $"{size.x:F2}x{size.y:F2}x{size.z:F2}";
            if (meshes.TryGetValue(key, out var cached)) return cached;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            Vector3 h = size * 0.5f;
            void Face(Vector3 normal, Vector3 u, Vector3 w, float su, float sw)
            {
                int b = v.Count; Vector3 c = Vector3.Scale(normal, h);
                Vector3 hu = u * su * 0.5f, hw = w * sw * 0.5f;
                v.Add(c - hu - hw); v.Add(c + hu - hw); v.Add(c + hu + hw); v.Add(c - hu + hw);
                for (int i = 0; i < 4; i++) n.Add(normal);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(su / 2f, 0)); uv.Add(new Vector2(su / 2f, sw / 2f)); uv.Add(new Vector2(0, sw / 2f));
                // Unity front faces are clockwise as seen from outside; with u = viewer right and w = viewer up, u x w = -normal
                if (Vector3.Dot(Vector3.Cross(u, w), normal) < 0) t.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
                else t.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            Face(Vector3.up, Vector3.right, Vector3.forward, size.x, size.z);
            Face(Vector3.down, Vector3.right, Vector3.back, size.x, size.z);
            Face(Vector3.forward, Vector3.left, Vector3.up, size.x, size.y);
            Face(Vector3.back, Vector3.right, Vector3.up, size.x, size.y);
            Face(Vector3.right, Vector3.forward, Vector3.up, size.z, size.y);
            Face(Vector3.left, Vector3.back, Vector3.up, size.z, size.y);
            var m = new Mesh { name = "Box " + key };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            Unwrapping.GenerateSecondaryUVSet(m);
            AssetDatabase.CreateAsset(m, Dir + "Mesh_" + key + ".asset");
            meshes[key] = m;
            return m;
        }

        static void Box(Transform room, string name, Material mat, Vector3 centre, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(room, false);
            go.transform.localPosition = centre;
            go.AddComponent<MeshFilter>().sharedMesh = BoxMesh(size);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.isStatic = true;   // all static flags, incl. Contribute GI, which the tracer requires
            GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(go) & ~StaticEditorFlags.BatchingStatic);
        }

        static Texture2D Tex(string name, int size, System.Func<int, int, Color> f)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) px[y * size + x] = f(x, y);
            tex.SetPixels(px); tex.Apply(true);
            AssetDatabase.CreateAsset(tex, Dir + name + ".asset");
            return tex;
        }

        static float Noise(int x, int y, int seed) { uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1013904223); h = (h ^ (h >> 13)) * 1274126177u; return (h & 0xffff) / 65535f; }

        static Material Mat(string name, Color c, Texture2D tex)
        {
            var m = new Material(Shader.Find(DL)) { name = name };
            m.SetColor("_Color", c); if (tex) m.SetTexture("_MainTex", tex);
            AssetDatabase.CreateAsset(m, Dir + name + ".mat");
            return m;
        }

        // light setups (same in every room): position, colour, intensity, radius
        public static readonly (string name, Vector3 pos, Color col, float intensity, float radius)[] Lights =
        {
            ("Warm lamp", new Vector3(-2.4f, 2.3f, -0.6f), new Color(1.0f, 0.70f, 0.42f), 2.0f, 9f),
            ("Cool lamp", new Vector3(3.6f, 2.5f, 2.6f), new Color(0.55f, 0.72f, 1.0f), 1.6f, 6.5f),
            ("Floor ember", new Vector3(-5.0f, 0.5f, -3.4f), new Color(1.0f, 0.45f, 0.2f), 1.4f, 5f),
        };

        public static void Build()
        {
            RenderSettings.skybox = null; RenderSettings.fog = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = Color.black;

            var tile = Tex("FloorTiles", 512, (x, y) =>
            {
                bool grout = (x % 256) < 5 || (y % 256) < 5;
                float nz = 0.9f + 0.1f * Noise(x / 4, y / 4, 1) + 0.05f * Noise(x, y, 2);
                float tileTint = 0.95f + 0.08f * Noise(x / 256, y / 256, 3);
                float g = grout ? 0.45f : nz * tileTint;
                return new Color(g, g, g, 1);
            });
            var plaster = Tex("Plaster", 256, (x, y) => { float g = 0.92f + 0.06f * Noise(x / 3, y / 3, 4) + 0.03f * Noise(x, y, 5); return new Color(g, g, g, 1); });
            var floorM = Mat("Floor", new Color(0.62f, 0.60f, 0.57f), tile);
            var wallM = Mat("Wall", new Color(0.78f, 0.76f, 0.72f), plaster);
            var partM = Mat("Partition", new Color(0.70f, 0.66f, 0.62f), plaster);
            var pillarM = Mat("Pillar", new Color(0.55f, 0.52f, 0.50f), plaster);
            var woodM = Mat("Bench", new Color(0.45f, 0.30f, 0.20f), plaster);

            var manager = DynamicLightManager.Instance;
            manager.ambientColor = new Color(0.55f, 0.65f, 0.85f, 0.05f);

            var bulbM = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(bulbM, Dir + "Bulb.mat");
            var bulbMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            var lights = new List<DynamicLight>(); var bulbs = new List<Renderer>();
            string[] names = { "Room A (real-time channel, no precomputed shadows)", "Room B (raytraced, steady)", "Room C (raytraced, runtime colour and flicker)" };
            for (int r = 0; r < 3; r++)
            {
                var room = new GameObject(names[r]).transform; room.position = Rooms[r];
                // shell: floor, back wall, left and right walls (open front and open top, like a set)
                Box(room, "Floor", floorM, new Vector3(0, -0.1f, 0), new Vector3(12.4f, 0.2f, 9.4f));
                Box(room, "Back wall", wallM, new Vector3(0, 1.5f, 4.6f), new Vector3(12.4f, 3f, 0.2f));
                Box(room, "Left wall", wallM, new Vector3(-6.1f, 1.5f, 0), new Vector3(0.2f, 3f, 9.0f));
                Box(room, "Right wall", wallM, new Vector3(6.1f, 1.5f, 0), new Vector3(0.2f, 3f, 9.0f));
                // partition at x = 0 with a doorway at z 0.4..1.9, 2.3 m high
                Box(room, "Partition front", partM, new Vector3(0, 1.5f, -2.05f), new Vector3(0.25f, 3f, 4.9f));
                Box(room, "Partition back", partM, new Vector3(0, 1.5f, 3.2f), new Vector3(0.25f, 3f, 2.6f));
                Box(room, "Partition lintel", partM, new Vector3(0, 2.65f, 1.15f), new Vector3(0.25f, 0.7f, 1.5f));
                // pillars and a bench
                foreach (var p in new[] { new Vector2(-3.6f, -2.2f), new Vector2(-1.0f, -2.6f), new Vector2(-3.8f, 2.4f), new Vector2(2.6f, -1.6f), new Vector2(4.4f, 0.6f) })
                    Box(room, "Pillar", pillarM, new Vector3(p.x, 1.5f, p.y), new Vector3(0.4f, 3f, 0.4f));
                Box(room, "Bench", woodM, new Vector3(2.0f, 0.25f, -3.4f), new Vector3(2.0f, 0.5f, 0.6f));

                foreach (var L in Lights)
                {
                    var lgo = new GameObject(L.name); lgo.transform.SetParent(room, false); lgo.transform.localPosition = L.pos;
                    var dl = lgo.AddComponent<DynamicLight>();
                    dl.lightColor = L.col; dl.lightIntensity = L.intensity; dl.lightRadius = L.radius;
                    dl.lightShadows = DynamicLightShadowMode.RaytracedShadows;
                    dl.lightIllumination = DynamicLightIlluminationMode.DirectIllumination;
                    if (r == 0) dl.lightChannel = DynamicLightManager.realtimeLightChannel;   // source: channel 32 = real-time light, no raytraced shadows
                    lights.Add(dl);
                    // bulb: not static, so it does not block its own light in the bake
                    var b = new GameObject("Bulb"); b.transform.SetParent(lgo.transform, false); b.transform.localScale = Vector3.one * 0.14f;
                    b.AddComponent<MeshFilter>().sharedMesh = bulbMesh;
                    var br = b.AddComponent<MeshRenderer>(); br.sharedMaterial = bulbM; br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    bulbs.Add(br);
                }
            }
            AssetDatabase.SaveAssets();

            var bg = new Color(0.02f, 0.022f, 0.028f);
            var clear = SceneKit.Cam("Clear", new Rect(0, 0, 1, 1), 30, Color.black, -10, false); clear.cullingMask = 0;
            var camA = SceneKit.Cam("Cam A", new Rect(0, 0, 1, 1), 40, bg, 0);
            var camB = SceneKit.Cam("Cam B", new Rect(0.5f, 0, 0.5f, 1), 40, bg, 1);
            camA.tag = "MainCamera";
            foreach (var c in new[] { camA, camB }) { c.nearClipPlane = 0.05f; c.farClipPlane = 30f; c.transform.position = new Vector3(0, 8, -9); c.transform.LookAt(Vector3.zero); }
            SceneKit.Grade(Dir, 0.45f, 0.2f, 12f, 4f, 0.32f, 0.10f);

            var title = LabelSpec.Create("", 0.012f, 0.935f, 46);
            var l1 = LabelSpec.Create("", 0.012f, 0.03f, 30);
            var l2 = LabelSpec.Create("", 0.012f, 0.018f, 26);
            var m1 = LabelSpec.Create("", 0.512f, 0.058f, 30);
            var m2 = LabelSpec.Create("", 0.512f, 0.018f, 26);

            var d = new GameObject("Director").AddComponent<DLDirector>();
            d.camA = camA; d.camB = camB; d.lights = lights.ToArray(); d.bulbs = bulbs.ToArray();
            d.title = title; d.l1 = l1; d.l2 = l2; d.m1 = m1; d.m2 = m2;

            // the tracer writes next to the scene, so save it first, then raytrace (2048 = the source's recommended menu entry)
            var scenePath = Dir + "Main.unity";
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), scenePath);
            Physics.SyncTransforms();
            var floor = GameObject.Find("Floor");
            Debug.Log($"[gt] static check: floor isStatic {floor.isStatic}, flags {GameObjectUtility.GetStaticEditorFlags(floor)}, pixel density {manager.pixelDensityPerSquareMeter}");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            manager.Raytrace(2048);
            sw.Stop();
            Debug.Log($"[gt] raytrace done in {sw.Elapsed.TotalSeconds:F1} s, raycasted lights {CountRaycasted(manager)}");
        }

        static int CountRaycasted(DynamicLightManager m)
        {
            var f = typeof(DynamicLightManager).GetField("raycastedDynamicLights", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            return f?.GetValue(m) is System.Collections.ICollection c ? c.Count : -1;
        }
    }
}
