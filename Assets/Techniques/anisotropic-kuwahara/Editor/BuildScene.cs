// 81 anisotropic Kuwahara: a sunlit courtyard (cobbles, stone wall, columns, flower pots, grass, rocks, a tree) rendered by a
// scene camera into a texture; KuwaharaDirector filters it and a display camera shows the split screen.
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GameTech.Posts
{
    public static class KuwaharaScene
    {
        const string Dir = "Assets/Techniques/anisotropic-kuwahara/";
        const string Lit = "Universal Render Pipeline/Lit";

        static float N3(Vector3 p) => (Mathf.PerlinNoise(p.x + 7.1f, p.y + 3.3f) + Mathf.PerlinNoise(p.y + 1.7f, p.z + 9.2f) + Mathf.PerlinNoise(p.z + 5.5f, p.x + 2.9f)) / 3f;

        // stones on a tiling jittered grid (Voronoi): each stone its own tint, dark mortar between them, a little noise inside
        static Texture2D Stones(string name, int n, int cells, Color a, Color b, float mortar, int seed)
        {
            var rnd = new System.Random(seed);
            var pts = new Vector2[cells * cells]; var tint = new Color[cells * cells];
            for (int i = 0; i < pts.Length; i++)
            {
                pts[i] = new Vector2(i % cells + 0.2f + 0.6f * (float)rnd.NextDouble(), i / cells + 0.2f + 0.6f * (float)rnd.NextDouble());
                tint[i] = Color.Lerp(a, b, (float)rnd.NextDouble()) * (0.8f + 0.35f * (float)rnd.NextDouble());
            }
            var px = new Color[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                var p = new Vector2(x * cells / (float)n, y * cells / (float)n);
                int cx = Mathf.FloorToInt(p.x), cy = Mathf.FloorToInt(p.y);
                float d1 = 1e9f, d2 = 1e9f; int best = 0;
                for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++)
                {
                    int gx = cx + i, gy = cy + j, wx = (gx % cells + cells) % cells, wy = (gy % cells + cells) % cells;
                    var q = pts[wy * cells + wx] + new Vector2(gx - wx, gy - wy);
                    float d = (q - p).magnitude;
                    if (d < d1) { d2 = d1; d1 = d; best = wy * cells + wx; } else if (d < d2) d2 = d;
                }
                float edge = d2 - d1;
                float grain = Mathf.PerlinNoise(x * 0.09f + seed, y * 0.09f) * 0.5f + Mathf.PerlinNoise(x * 0.31f, y * 0.31f + seed) * 0.3f + (float)rnd.NextDouble() * 0.2f;
                var c = tint[best] * (0.7f + 0.55f * grain) * (0.8f + 0.2f * Mathf.Clamp01(edge * 4f));
                c = Color.Lerp(new Color(0.16f, 0.14f, 0.12f), c, Mathf.SmoothStep(0, 1, (edge - mortar) / 0.04f));
                c.a = 1; px[y * n + x] = c;
            }
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true, false) { wrapMode = TextureWrapMode.Repeat, anisoLevel = 4, name = name };
            tex.SetPixels(px); tex.Apply();
            AssetDatabase.CreateAsset(tex, Dir + name + ".asset");
            return tex;
        }

        // leaf dapple: hundreds of small leaf-shaped ellipses in mixed greens, tileable
        static Texture2D Dapple(string name, Color dark, Color mid, Color light, int count, int seed)
        {
            int n = 512; var px = new Color[n * n]; var rnd = new System.Random(seed);
            for (int i = 0; i < px.Length; i++) px[i] = dark;
            for (int d = 0; d < count; d++)
            {
                float cx = (float)rnd.NextDouble() * n, cy = (float)rnd.NextDouble() * n, a = (float)rnd.NextDouble() * Mathf.PI;
                float rx = 5f + (float)rnd.NextDouble() * 9f, ry = 2f + (float)rnd.NextDouble() * 3f;
                var c = Color.Lerp(mid, light, (float)rnd.NextDouble() * (float)rnd.NextDouble()) * (0.75f + 0.4f * (float)rnd.NextDouble());
                int r = Mathf.CeilToInt(rx);
                for (int y = -r; y <= r; y++) for (int x = -r; x <= r; x++)
                {
                    float u = (x * Mathf.Cos(a) + y * Mathf.Sin(a)) / rx, w = (-x * Mathf.Sin(a) + y * Mathf.Cos(a)) / ry;
                    if (u * u + w * w > 1f) continue;
                    int px0 = ((int)cx + x + n) % n, py0 = ((int)cy + y + n) % n;
                    px[py0 * n + px0] = c * (0.85f + 0.15f * u);
                }
            }
            for (int i = 0; i < px.Length; i++) px[i].a = 1;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true, false) { wrapMode = TextureWrapMode.Repeat, anisoLevel = 4, name = name };
            tex.SetPixels(px); tex.Apply();
            AssetDatabase.CreateAsset(tex, Dir + name + ".asset");
            return tex;
        }

        // spherical UVs for the noise blobs (MeshKit meshes have none)
        static Mesh WithUV(Mesh m, float tiling)
        {
            var v = m.vertices; var uv = new Vector2[v.Length];
            for (int i = 0; i < v.Length; i++) uv[i] = new Vector2(Mathf.Atan2(v[i].z, v[i].x) / (2f * Mathf.PI) + 0.5f, v[i].y + 0.5f) * tiling;
            m.uv = uv; return m;
        }

        static Material Mat(string name, Color c, float smooth = 0.25f, Texture tex = null, float tiling = 1f, bool twoSided = false)
        {
            var m = SceneKit.Mat(Dir, name, Lit, c, 0f, smooth);
            if (tex != null) { m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", Vector2.one * tiling); }
            if (twoSided) m.SetFloat("_Cull", 0f);
            return m;
        }

        static Mesh Blob(string name, float amount, float freq, float seed, float smoothDeg)
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var src = tmp.GetComponent<MeshFilter>().sharedMesh; var v = src.vertices; var t = src.triangles;
            Object.DestroyImmediate(tmp);
            Vector3 D(Vector3 p) { p = p.normalized * 0.5f; return p * (1f + amount * (N3(p * freq + Vector3.one * seed) - 0.5f) * 2f); }
            var k = new MeshKit();
            for (int i = 0; i < t.Length; i += 3) { Vector3 a = D(v[t[i]]), b = D(v[t[i + 1]]), c = D(v[t[i + 2]]); k.Tri(a, b, c, a + b + c); }
            return k.Build(name, smoothDeg);
        }

        static GameObject Prim(PrimitiveType type, string name, Material m, Vector3 pos, Vector3 scale, float yaw = 0)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = m;
            go.transform.position = pos; go.transform.localScale = scale; go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            return go;
        }

        public static void Build()
        {
            SceneKit.Ambient(new Color(0.50f, 0.58f, 0.74f), new Color(0.46f, 0.44f, 0.40f), new Color(0.22f, 0.19f, 0.15f));
            SceneKit.Shadows(35f);
            var sun = SceneKit.Sun("Sun", new Vector3(32, -128, 0), new Color(1f, 0.86f, 0.68f), 2.0f, true);
            RenderSettings.sun = sun;
            var sky = new Material(Shader.Find("Skybox/Procedural"));
            sky.SetFloat("_SunSize", 0.04f); sky.SetFloat("_AtmosphereThickness", 1.25f); sky.SetFloat("_Exposure", 1.2f);
            sky.SetColor("_GroundColor", new Color(0.38f, 0.34f, 0.30f));
            AssetDatabase.CreateAsset(sky, Dir + "Sky.mat");
            RenderSettings.skybox = sky;

            // ---- ground, wall, columns
            var cobbles = Stones("Cobbles", 1024, 12, new Color(0.62f, 0.58f, 0.52f), new Color(0.47f, 0.43f, 0.40f), 0.05f, 11);
            var blocks = Stones("Blocks", 512, 5, new Color(0.78f, 0.70f, 0.58f), new Color(0.66f, 0.60f, 0.52f), 0.03f, 23);
            Prim(PrimitiveType.Plane, "Ground", Mat("Ground", Color.white, 0.2f, cobbles, 6f), Vector3.zero, new Vector3(3, 1, 3));
            var wallMat = Mat("Wall", Color.white, 0.15f, blocks, 2f);
            for (int i = 0; i < 3; i++)
            {
                float yaw = -38f + i * 38f; var dir = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
                Prim(PrimitiveType.Cube, "Wall", wallMat, dir * 5.2f + Vector3.up * 0.6f, new Vector3(3.4f, 1.2f, 0.45f), yaw);
            }
            var colMat = Mat("Column", new Color(0.90f, 0.87f, 0.80f), 0.35f);
            var colProfile = new[] { new Vector2(0, 0), new Vector2(0, 0.34f), new Vector2(0.22f, 0.34f), new Vector2(0.28f, 0.26f), new Vector2(2.7f, 0.22f), new Vector2(2.78f, 0.3f), new Vector2(2.95f, 0.34f), new Vector2(2.95f, 0) };
            var colMesh = new MeshKit().Lathe(colProfile, 20).Build("Column", 40f);
            foreach (float yaw in new[] { -58f, -19f, 19f, 58f })
                SceneKit.Part("Column", colMesh, colMat, Quaternion.Euler(0, yaw, 0) * Vector3.forward * 5.4f, Quaternion.identity);

            // ---- terracotta pots with flowers
            var potMat = Mat("Pot", new Color(0.72f, 0.36f, 0.22f), 0.2f);
            var leafMat = Mat("Foliage", Color.white, 0.15f, Dapple("FoliageTex", new Color(0.07f, 0.16f, 0.05f), new Color(0.22f, 0.42f, 0.12f), new Color(0.65f, 0.75f, 0.25f), 900, 3), 2f);
            var potProfile = new[] { new Vector2(0, 0), new Vector2(0, 0.17f), new Vector2(0.35f, 0.25f), new Vector2(0.5f, 0.28f), new Vector2(0.55f, 0.24f), new Vector2(0.55f, 0) };
            var potMesh = new MeshKit().Lathe(potProfile, 24).Build("Pot", 50f);
            Color[] petals = { new Color(0.85f, 0.15f, 0.18f), new Color(0.95f, 0.78f, 0.15f), new Color(0.55f, 0.25f, 0.75f), new Color(0.95f, 0.92f, 0.88f) };
            var petalMats = new Material[petals.Length];
            for (int i = 0; i < petals.Length; i++) petalMats[i] = Mat("Petal" + i, petals[i], 0.3f);
            var sphere = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
            var rnd = new System.Random(5);
            var pots = new[] { new Vector3(-2.6f, 0, 2.6f), new Vector3(-1.7f, 0, 3.2f), new Vector3(1.9f, 0, 3.0f), new Vector3(2.9f, 0, 2.1f), new Vector3(-3.3f, 0, 0.9f) };
            for (int p = 0; p < pots.Length; p++)
            {
                float s = 0.9f + 0.4f * (float)rnd.NextDouble();
                var pot = SceneKit.Part("Pot", potMesh, potMat, pots[p], Quaternion.identity); pot.transform.localScale = Vector3.one * s;
                var bush = SceneKit.Part("Bush", WithUV(Blob("Bush", 0.25f, 3f, p * 3.3f, 60f), 1f), leafMat, pots[p] + Vector3.up * 0.6f * s, Quaternion.identity);
                bush.transform.localScale = new Vector3(0.75f, 0.55f, 0.75f) * s;
                var pm = petalMats[p % petalMats.Length];
                for (int f = 0; f < 34; f++)
                {
                    var dv = Random.onUnitSphere; dv.y = Mathf.Abs(dv.y) * 0.8f + 0.2f; dv.Normalize();
                    var fl = SceneKit.Part("Flower", sphere, pm, pots[p] + Vector3.up * 0.6f * s + Vector3.Scale(dv, new Vector3(0.36f, 0.28f, 0.36f)) * s, Quaternion.identity);
                    fl.transform.localScale = Vector3.one * (0.06f + 0.03f * (float)rnd.NextDouble()) * s;
                    fl.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                }
            }

            // ---- bench
            var wood = Mat("Wood", new Color(0.45f, 0.30f, 0.18f), 0.3f);
            Prim(PrimitiveType.Cube, "Bench seat", wood, new Vector3(0.6f, 0.45f, 3.6f), new Vector3(1.8f, 0.08f, 0.45f), 4f);
            foreach (float x in new[] { -0.2f, 1.4f }) Prim(PrimitiveType.Cube, "Bench leg", wood, new Vector3(x, 0.22f, 3.6f), new Vector3(0.1f, 0.44f, 0.4f), 4f);

            // ---- rocks and a tree
            var rockMat = Mat("Rock", Color.white, 0.2f, Stones("RockTex", 256, 6, new Color(0.55f, 0.53f, 0.50f), new Color(0.42f, 0.40f, 0.38f), -1f, 31), 2f);
            var rocks = new[] { new Vector4(-1.2f, -0.8f, 0.7f, 1), new Vector4(1.5f, -1.4f, 0.5f, 2), new Vector4(0.4f, 1.2f, 0.9f, 3), new Vector4(-0.3f, -2.2f, 0.4f, 4), new Vector4(2.4f, 0.4f, 0.6f, 5) };
            foreach (var r in rocks)
            {
                var go = SceneKit.Part("Rock", WithUV(Blob("Rock", 0.3f, 2.2f, r.w * 3.1f, 25f), 1f), rockMat, new Vector3(r.x, r.z * 0.2f, r.y), Quaternion.Euler(0, r.w * 57f, 0));
                go.transform.localScale = new Vector3(r.z * 1.3f, r.z * 0.75f, r.z);
            }
            Vector3 treePos = new Vector3(3.6f, 0, 3.9f);
            var trunk = new[] { new Vector2(0, 0), new Vector2(0, 0.3f), new Vector2(0.6f, 0.2f), new Vector2(2.0f, 0.15f), new Vector2(3.1f, 0.11f), new Vector2(3.1f, 0) };
            SceneKit.Part("Trunk", new MeshKit().Lathe(trunk, 10).Build("Trunk", 30f), Mat("Bark", new Color(0.36f, 0.25f, 0.17f), 0.2f), treePos, Quaternion.identity);
            var crownMat = Mat("Crown", Color.white, 0.15f, Dapple("CrownTex", new Color(0.06f, 0.14f, 0.05f), new Color(0.20f, 0.40f, 0.12f), new Color(0.75f, 0.72f, 0.22f), 1100, 9), 3f);
            var crown = new[] { new Vector4(0, 3.3f, 0, 2.4f), new Vector4(1.0f, 2.9f, 0.4f, 1.6f), new Vector4(-0.9f, 3.0f, -0.5f, 1.7f), new Vector4(0.2f, 4.1f, -0.2f, 1.6f), new Vector4(-1.5f, 2.6f, 0.9f, 1.3f) };
            for (int i = 0; i < crown.Length; i++)
            {
                var l = crown[i];
                var go = SceneKit.Part("Crown", WithUV(Blob("Crown", 0.2f, 1.8f, i * 4.7f, 70f), 1f), crownMat, treePos + new Vector3(l.x, l.y, l.z), Quaternion.Euler(0, i * 70f, 0));
                go.transform.localScale = new Vector3(l.w, l.w * 0.75f, l.w);
            }

            // ---- grass tufts along the walls: single-triangle blades
            int blades = 14000; var gv = new Vector3[blades * 3]; var gn = new Vector3[blades * 3]; var gt = new int[blades * 3];
            for (int i = 0; i < blades; i++)
            {
                float a = (-75f + 150f * (float)rnd.NextDouble()) * Mathf.Deg2Rad, r = 3.9f + 1.0f * (float)rnd.NextDouble();
                if (i % 3 == 0) { a = (float)rnd.NextDouble() * Mathf.PI * 2; r = 5.6f + 4f * (float)rnd.NextDouble(); }
                float x = Mathf.Sin(a) * r, z = Mathf.Cos(a) * r, yaw = (float)rnd.NextDouble() * Mathf.PI, h = 0.12f + 0.22f * (float)rnd.NextDouble();
                Vector3 p0 = new Vector3(x, 0, z), w = new Vector3(Mathf.Cos(yaw), 0, Mathf.Sin(yaw)) * 0.025f;
                Vector3 lean = new Vector3((float)rnd.NextDouble() - 0.5f, 0, (float)rnd.NextDouble() - 0.5f) * 0.15f;
                gv[i * 3] = p0 - w; gv[i * 3 + 1] = p0 + w; gv[i * 3 + 2] = p0 + Vector3.up * h + lean;
                for (int j = 0; j < 3; j++) { gn[i * 3 + j] = Vector3.up; gt[i * 3 + j] = i * 3 + j; }
            }
            var grass = new Mesh { name = "Blades", indexFormat = IndexFormat.UInt32 }; grass.vertices = gv; grass.normals = gn; grass.triangles = gt; grass.RecalculateBounds();
            var bladesGo = SceneKit.Part("Blades", grass, Mat("Blades", new Color(0.33f, 0.52f, 0.17f), 0.1f, null, 1f, true), Vector3.zero, Quaternion.identity);
            bladesGo.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            // ---- cameras: the scene camera renders into a texture (the director sets that up), the display camera shows the result
            var srcCam = SceneKit.Cam("Scene camera", new Rect(0, 0, 1, 1), 40, Color.black, -10);
            srcCam.clearFlags = CameraClearFlags.Skybox; srcCam.cullingMask = ~(1 << 8); srcCam.farClipPlane = 300f;
            SceneKit.Grade(Dir, 0.25f, 0.1f, 12f, 8f, 0.22f, 0f);
            var display = SceneKit.Cam("Display camera", new Rect(0, 0, 1, 1), 40, Color.black, 10, false);
            display.cullingMask = 1 << 8; display.transform.position = new Vector3(0, -50, 0); display.tag = "MainCamera";

            var compose = new Material(Shader.Find("Hidden/GT/KuwaharaCompose")); AssetDatabase.CreateAsset(compose, Dir + "Compose.mat");
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Display quad"; Object.DestroyImmediate(quad.GetComponent<Collider>()); quad.layer = 8;
            var qm = Object.Instantiate(quad.GetComponent<MeshFilter>().sharedMesh); qm.name = "DisplayQuad"; qm.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
            AssetDatabase.CreateAsset(qm, Dir + "DisplayQuad.asset");
            quad.GetComponent<MeshFilter>().sharedMesh = qm; quad.GetComponent<Renderer>().sharedMaterial = compose;
            quad.transform.position = display.transform.position + Vector3.forward;

            Material M(string shader, string name) { var m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, Dir + name + ".mat"); return m; }
            var d = new GameObject("Director").AddComponent<KuwaharaDirector>();
            d.src = srcCam; d.compose = compose;
            d.basic = M("Hidden/Kuwahara", "KuwaharaBasic");
            d.general = M("Hidden/GeneralizedKuwahara", "KuwaharaGeneralized");
            d.aniso = M("Hidden/AnisotropicKuwahara", "KuwaharaAnisotropic");
            d.tensorView = M("Hidden/GT/TensorView", "TensorView");
            d.left = LabelSpec.Create("", 0.03f, 0.05f, 32);
            d.right = LabelSpec.Create("", 0.53f, 0.05f, 32);
        }
    }
}
