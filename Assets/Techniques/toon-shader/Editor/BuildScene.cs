// 68 toon shading: the character on a grassy rise with rocks and a tree, everything shaded by GameTech/ZeldaToon.
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GameTech.Posts
{
    public static class ToonScene
    {
        const string Dir = "Assets/Techniques/toon-shader/";
        const string Toon = "GameTech/ZeldaToon";

        static float N3(Vector3 p) => (Mathf.PerlinNoise(p.x + 7.1f, p.y + 3.3f) + Mathf.PerlinNoise(p.y + 1.7f, p.z + 9.2f) + Mathf.PerlinNoise(p.z + 5.5f, p.x + 2.9f)) / 3f;
        static float H0(float x, float z) => 2.6f * (Mathf.PerlinNoise(x * 0.035f + 10f, z * 0.035f + 20f) - 0.5f) * Mathf.Clamp01((x * x + z * z) / 400f)
            + 0.5f * Mathf.Exp(-(x * x + z * z) / 90f) + 0.05f * Mathf.PerlinNoise(x * 0.5f, z * 0.5f);
        static float H(float x, float z) => H0(x, z) - H0(0, 0);

        static Material Mat(string name, Color c, float cull = 2f, float rim = 0f)
        {
            var m = SceneKit.Mat(Dir, name, Toon, c);
            m.SetFloat("_SpecStrength", 0f); m.SetFloat("_Cull", cull); m.SetFloat("_Rim", rim);
            return m;
        }

        // blob built from Unity's sphere: displaced by noise, faceted (rocks) or smooth (foliage)
        static Mesh Blob(string name, float amount, float freq, float seed, float smoothDeg)
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var src = tmp.GetComponent<MeshFilter>().sharedMesh; var v = src.vertices; var t = src.triangles;
            Object.DestroyImmediate(tmp);
            Vector3 D(Vector3 p) { p = p.normalized * 0.5f; return p * (1f + amount * (N3(p * freq + Vector3.one * seed) - 0.5f) * 2f); }
            var k = new MeshKit();
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = D(v[t[i]]), b = D(v[t[i + 1]]), c = D(v[t[i + 2]]);
                k.Tri(a, b, c, a + b + c);
            }
            return k.Build(name, smoothDeg);
        }

        static Texture2D Dabs()
        {
            // brush-dab mask for the painted highlight: soft random ellipses, tileable
            int n = 128; var px = new float[n * n]; var rnd = new System.Random(7);
            for (int d = 0; d < 26; d++)
            {
                float cx = (float)rnd.NextDouble() * n, cy = (float)rnd.NextDouble() * n, a = (float)rnd.NextDouble() * Mathf.PI;
                float rx = 9f + (float)rnd.NextDouble() * 12f, ry = 4f + (float)rnd.NextDouble() * 4f;
                for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                {
                    float dx = Mathf.Repeat(x - cx + n / 2f, n) - n / 2f, dy = Mathf.Repeat(y - cy + n / 2f, n) - n / 2f;
                    float u = (dx * Mathf.Cos(a) + dy * Mathf.Sin(a)) / rx, w = (-dx * Mathf.Sin(a) + dy * Mathf.Cos(a)) / ry;
                    px[y * n + x] = Mathf.Max(px[y * n + x], Mathf.Clamp01((1f - (u * u + w * w)) * 3f));
                }
            }
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Repeat, name = "Dabs" };
            var cols = new Color[n * n];
            for (int i = 0; i < cols.Length; i++) cols[i] = new Color(px[i], px[i], px[i], 1);
            tex.SetPixels(cols); tex.Apply();
            AssetDatabase.CreateAsset(tex, Dir + "Dabs.asset");
            return tex;
        }

        public static void Build()
        {
            SceneKit.Ambient(new Color(0.46f, 0.56f, 0.78f), new Color(0.42f, 0.44f, 0.42f), new Color(0.20f, 0.22f, 0.15f));
            SceneKit.Shadows(40f);
            var sun = SceneKit.Sun("Sun", new Vector3(27, -68, 0), new Color(1f, 0.90f, 0.76f), 1.7f, true);
            RenderSettings.sun = sun;
            var sky = new Material(Shader.Find("Skybox/Procedural"));
            sky.SetFloat("_SunSize", 0.05f); sky.SetFloat("_AtmosphereThickness", 1.15f); sky.SetFloat("_Exposure", 1.25f);
            sky.SetColor("_GroundColor", new Color(0.36f, 0.42f, 0.30f));
            AssetDatabase.CreateAsset(sky, Dir + "Sky.mat");
            RenderSettings.skybox = sky;

            // ---- terrain
            int n = 180; float size = 150f;
            var verts = new Vector3[(n + 1) * (n + 1)]; var tri = new int[n * n * 6];
            for (int z = 0; z <= n; z++) for (int x = 0; x <= n; x++)
            {
                // denser grid near the centre
                float fx = (x / (float)n - 0.5f) * 2f, fz = (z / (float)n - 0.5f) * 2f;
                float px = Mathf.Sign(fx) * fx * fx * size * 0.5f, pz = Mathf.Sign(fz) * fz * fz * size * 0.5f;
                verts[z * (n + 1) + x] = new Vector3(px, H(px, pz), pz);
            }
            for (int z = 0, i = 0; z < n; z++) for (int x = 0; x < n; x++)
            {
                int a = z * (n + 1) + x, b = a + 1, c = a + n + 1, e = c + 1;
                tri[i++] = a; tri[i++] = c; tri[i++] = b; tri[i++] = b; tri[i++] = c; tri[i++] = e;
            }
            var terrain = new Mesh { name = "Terrain" }; terrain.vertices = verts; terrain.triangles = tri; terrain.RecalculateNormals(); terrain.RecalculateBounds();
            SceneKit.Part("Terrain", terrain, Mat("Grass", new Color(0.31f, 0.54f, 0.18f)), Vector3.zero, Quaternion.identity);

            // ---- grass blades: single triangles, normals pointing up so they band like the ground
            var rnd = new System.Random(3);
            int blades = 9000; var gv = new Vector3[blades * 3]; var gn = new Vector3[blades * 3]; var gt = new int[blades * 3];
            for (int i = 0; i < blades; i++)
            {
                float r = 0.6f + 13f * Mathf.Pow((float)rnd.NextDouble(), 0.8f), a = (float)rnd.NextDouble() * Mathf.PI * 2;
                float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r, yaw = (float)rnd.NextDouble() * Mathf.PI, h = 0.10f + 0.16f * (float)rnd.NextDouble();
                Vector3 p = new Vector3(x, H(x, z) - 0.01f, z), w = new Vector3(Mathf.Cos(yaw), 0, Mathf.Sin(yaw)) * 0.022f;
                Vector3 lean = new Vector3((float)rnd.NextDouble() - 0.5f, 0, (float)rnd.NextDouble() - 0.5f) * 0.12f;
                gv[i * 3] = p - w; gv[i * 3 + 1] = p + w; gv[i * 3 + 2] = p + Vector3.up * h + lean;
                for (int j = 0; j < 3; j++) { gn[i * 3 + j] = Vector3.up; gt[i * 3 + j] = i * 3 + j; }
            }
            var grass = new Mesh { name = "Blades" }; grass.vertices = gv; grass.normals = gn; grass.triangles = gt; grass.RecalculateBounds();
            var bladeMat = Mat("Blades", new Color(0.40f, 0.64f, 0.21f), 0f); bladeMat.SetFloat("_FlipBack", 0f);
            var bladesGo = SceneKit.Part("Blades", grass, bladeMat, Vector3.zero, Quaternion.identity);
            bladesGo.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            // ---- rocks, tree, far hills
            var rockMat = Mat("Rock", new Color(0.56f, 0.56f, 0.53f), 2f, 1f);
            var rocks = new[] { new Vector4(-1.5f, -0.9f, 0.9f, 1), new Vector4(-2.2f, -0.2f, 0.5f, 2), new Vector4(1.3f, -1.8f, 0.7f, 3), new Vector4(-0.6f, -2.6f, 1.3f, 4), new Vector4(2.6f, 1.2f, 0.45f, 5), new Vector4(-3.4f, 1.6f, 0.8f, 6) };
            foreach (var r in rocks)
            {
                var go = SceneKit.Part("Rock", Blob("Rock", 0.28f, 2.3f, r.w * 3.1f, 22f), rockMat, new Vector3(r.x, H(r.x, r.y) + r.z * 0.22f, r.y), Quaternion.Euler(0, r.w * 57f, 0));
                go.transform.localScale = new Vector3(r.z * 1.3f, r.z * 0.8f, r.z);
            }
            Vector3 treePos = new Vector3(3.21f, 0, 1.49f); treePos.y = H(treePos.x, treePos.z) - 0.05f;
            var trunk = new[] { new Vector2(0, 0), new Vector2(0, 0.27f), new Vector2(0.5f, 0.18f), new Vector2(1.5f, 0.14f), new Vector2(2.3f, 0.10f), new Vector2(2.3f, 0) };
            SceneKit.Part("Trunk", new MeshKit().Lathe(trunk, 9).Build("Trunk", 30f), Mat("Trunk", new Color(0.46f, 0.31f, 0.20f), 2f, 1f), treePos, Quaternion.identity);
            var leafMat = Mat("Leaves", new Color(0.24f, 0.52f, 0.20f), 2f, 1f);
            var leaves = new[] { new Vector4(0, 2.5f, 0, 1.9f), new Vector4(0.8f, 2.2f, 0.3f, 1.3f), new Vector4(-0.7f, 2.3f, -0.4f, 1.4f), new Vector4(0.1f, 3.1f, -0.2f, 1.3f), new Vector4(-1.28f, 2.03f, 0.81f, 1.1f) };
            for (int i = 0; i < leaves.Length; i++)
            {
                var l = leaves[i];
                var go = SceneKit.Part("Leaves", Blob("Leaves", 0.16f, 1.6f, i * 4.7f, 180f), leafMat, treePos + new Vector3(l.x, l.y, l.z), Quaternion.Euler(0, i * 70f, 0));
                go.transform.localScale = new Vector3(l.w, l.w * 0.75f, l.w);
            }
            var hillMat = Mat("FarHill", new Color(0.33f, 0.53f, 0.36f));
            for (int i = 0; i < 9; i++)
            {
                float a = i * 40f * Mathf.Deg2Rad + 0.3f, r = 62f + 14f * Mathf.Sin(i * 2.1f);
                var go = SceneKit.Part("Far hill", Blob("FarHill", 0.2f, 1.2f, i * 2.3f, 180f), hillMat, new Vector3(Mathf.Cos(a) * r, -4f, Mathf.Sin(a) * r), Quaternion.Euler(0, i * 50f, 0));
                go.transform.localScale = new Vector3(48f, 20f + 8f * Mathf.Sin(i * 1.3f), 36f);
            }

            // ---- character
            var dabs = Dabs();
            var chr = CharacterKit.Spawn(Toon, Dir, (m, name) =>
            {
                m.SetTexture("_DabsTex", dabs);
                m.SetFloat("_SpecStrength", name.StartsWith("face") ? 0f : 1f);
                if (name == "face_alpha" || name == "Halo") m.SetFloat("_Cutoff", 0.5f);
            });
            chr.transform.position = new Vector3(0, H(0, 0), 0);

            // ---- cameras, post, director, labels
            var d = new GameObject("Director").AddComponent<ToonDirector>();
            d.sun = sun.transform;
            d.camL = SceneKit.Cam("Camera OFF", new Rect(0, 0, 0.5f, 1), 34, Color.black, 0);
            d.camR = SceneKit.Cam("Camera ON", new Rect(0.5f, 0, 0.5f, 1), 34, Color.black, 1);
            d.camFull = SceneKit.Cam("Camera full", new Rect(0, 0, 1, 1), 34, Color.black, 2); d.camFull.tag = "MainCamera";
            foreach (var c in new[] { d.camL, d.camR, d.camFull }) { c.clearFlags = CameraClearFlags.Skybox; c.farClipPlane = 400f; }
            var profile = SceneKit.Grade(Dir, 0.25f, 0.0f, 8f, 10f, 0.25f, 0.12f);
            if (profile.TryGet<Tonemapping>(out var tone)) { tone.mode.Override(TonemappingMode.Neutral); EditorUtility.SetDirty(profile); AssetDatabase.SaveAssets(); }

            LabelSpec.Create("OFF: URP PBR lighting", 0.03f, 0.05f).During(2.2f, 9f);
            LabelSpec.Create("ON: toon lighting model", 0.53f, 0.05f).During(2.2f, 9f);
            string[] steps = { "1/6  Lambert: N.L x light colour", "2/6  2 bands, edge at N.L = 0.31", "3/6  3 bands: 0.45 / 0.66 / 1.0", "4/6  + rim light (Fresnel)", "5/6  + painted highlight (stepped specular x dab mask)", "6/6  + received shadows" };
            for (int i = 0; i < 6; i++) LabelSpec.Create(steps[i], 0.03f, 0.05f).During(9f + i * 8f / 6f, 9f + (i + 1) * 8f / 6f);
            LabelSpec.Create("sun orbit, source values: lit band from N.L 0.31, backlight 2.3", 0.03f, 0.05f).During(17f, 19.6f);
            LabelSpec.Create("sun behind her, backlight 2.3 -> 3.0: wider back rim", 0.03f, 0.05f).During(19.6f, 21.4f);
            LabelSpec.Create("backlight 2.3 again, lit band from N.L 0.00 (source: 0.31)", 0.03f, 0.05f).During(21.4f, 24.1f);
        }
    }
}
