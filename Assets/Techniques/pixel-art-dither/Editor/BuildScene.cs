// 85 pixel-art dither: rolling terrain, trees, rocks, a few solids and an orbiting light under a gradient sky, rendered by a
// scene camera into a texture; PixelDirector runs the pixel-art passes and a display camera shows the bands.
using UnityEditor;
using UnityEngine;

namespace GameTech.Posts
{
    public static class PixelScene
    {
        const string Dir = "Assets/Techniques/pixel-art-dither/";
        const string Lit = "Universal Render Pipeline/Lit";

        static Material Mat(string name, Color c, float smooth = 0.3f) => SceneKit.Mat(Dir, name, Lit, c, 0f, smooth);

        static GameObject Prim(PrimitiveType type, string name, Material m, Vector3 pos, Vector3 scale, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = m;
            go.transform.position = pos; go.transform.localScale = scale; go.transform.rotation = Quaternion.Euler(euler);
            return go;
        }

        // flat around the centre, rolling hills further out
        public static float Height(float x, float z)
        {
            float h = 1.4f * Mathf.Sin(0.11f * x + 0.3f) * Mathf.Cos(0.09f * z - 0.4f) + 0.8f * Mathf.Sin(0.21f * x - 0.16f * z + 1.0f)
                    + 0.3f * Mathf.Sin(0.47f * x + 0.39f * z);
            float r = Mathf.Sqrt(x * x + z * z);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(6f, 12f, r));
            return Mathf.Lerp(0f, h + 0.6f + r * 0.05f, k);
        }

        static Mesh Terrain(int n, float size)
        {
            var v = new Vector3[(n + 1) * (n + 1)]; var tri = new int[n * n * 6];
            for (int z = 0; z <= n; z++) for (int x = 0; x <= n; x++)
            {
                float px = -size / 2 + size * x / n, pz = -size / 2 + size * z / n;
                v[z * (n + 1) + x] = new Vector3(px, Height(px, pz), pz);
            }
            int k = 0;
            for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
            {
                int a = z * (n + 1) + x, b = a + 1, c = a + n + 1, d = c + 1;
                tri[k++] = a; tri[k++] = c; tri[k++] = b; tri[k++] = b; tri[k++] = c; tri[k++] = d;
            }
            var m = new Mesh { name = "Terrain", vertices = v, triangles = tri };
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static Mesh Save(Mesh m) { AssetDatabase.CreateAsset(m, Dir + m.name + ".asset"); return m; }

        public static void Build()
        {
            SceneKit.Ambient(new Color(0.50f, 0.60f, 0.80f), new Color(0.55f, 0.55f, 0.52f), new Color(0.28f, 0.26f, 0.22f));
            SceneKit.Shadows(40f);
            var sun = SceneKit.Sun("Sun", new Vector3(40, 20, 0), new Color(1f, 0.94f, 0.84f), 2.0f, true);
            RenderSettings.sun = sun;
            var sky = new Material(Shader.Find("GT/GradientSky")); AssetDatabase.CreateAsset(sky, Dir + "Sky.mat");
            RenderSettings.skybox = sky;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogStartDistance = 22f; RenderSettings.fogEndDistance = 75f;
            RenderSettings.fogColor = new Color(0.80f, 0.87f, 0.95f);

            SceneKit.Part("Terrain", Save(Terrain(120, 100f)), Mat("Grass", new Color(0.36f, 0.56f, 0.24f), 0.1f), Vector3.zero, Quaternion.identity);

            Prim(PrimitiveType.Sphere, "Red sphere", Mat("Red", new Color(0.88f, 0.18f, 0.12f), 0.55f), new Vector3(-2.3f, 1.2f, 0.6f), Vector3.one * 2.4f);
            Prim(PrimitiveType.Sphere, "Blue sphere", Mat("Blue", new Color(0.15f, 0.38f, 0.88f), 0.55f), new Vector3(2.3f, 0.8f, -1.4f), Vector3.one * 1.6f);
            var prof = new Vector2[28];
            for (int i = 0; i < prof.Length; i++) { float a = i * Mathf.PI * 2f / prof.Length; prof[i] = new Vector2(0.3f * Mathf.Sin(a), 1.0f + 0.3f * Mathf.Cos(a)); }
            var torus = SceneKit.Part("Torus", Save(new MeshKit().Lathe(prof, 56).Build("Torus", 60f)), Mat("Yellow", new Color(0.98f, 0.78f, 0.12f), 0.5f), new Vector3(0.5f, 1.45f, 2.0f), Quaternion.Euler(70, 0, 0));
            var cone = new[] { new Vector2(0, 0), new Vector2(0, 0.9f), new Vector2(1.8f, 0.02f), new Vector2(1.8f, 0) };
            SceneKit.Part("Cone", Save(new MeshKit().Lathe(cone, 48).Build("Cone", 50f)), Mat("Magenta", new Color(0.80f, 0.18f, 0.60f), 0.4f), new Vector3(3.4f, 0, 2.4f), Quaternion.identity);
            Prim(PrimitiveType.Cube, "Block", Mat("White", new Color(0.92f, 0.91f, 0.88f), 0.2f), new Vector3(-0.6f, 0.6f, -2.8f), new Vector3(1.6f, 1.2f, 1.2f), new Vector3(0, 25, 0));
            Prim(PrimitiveType.Cylinder, "Pillar", Mat("Teal", new Color(0.12f, 0.62f, 0.62f), 0.3f), new Vector3(-3.8f, 1.5f, -1.8f), new Vector3(0.7f, 1.5f, 0.7f));

            var trunk = Mat("Trunk", new Color(0.36f, 0.24f, 0.14f), 0.1f);
            var leaves = Mat("Leaves", new Color(0.12f, 0.36f, 0.16f), 0.1f);
            var crown = Save(new MeshKit().Lathe(new[] { new Vector2(0, 0), new Vector2(0.15f, 1.3f), new Vector2(1.4f, 0.7f), new Vector2(1.5f, 0.95f), new Vector2(3.2f, 0.01f), new Vector2(3.2f, 0) }, 10).Build("Crown", 30f));
            for (int i = 0; i < 18; i++)
            {
                float a = i * 2.39996f, r = 16f + (i * 37 % 13);
                float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r, y = Height(x, z);
                float s = 0.8f + (i * 53 % 7) * 0.08f;
                Prim(PrimitiveType.Cylinder, "Trunk", trunk, new Vector3(x, y + 0.6f * s, z), new Vector3(0.3f, 0.6f, 0.3f) * s);
                var c = SceneKit.Part("Crown", crown, leaves, new Vector3(x, y + 1.0f * s, z), Quaternion.Euler(0, i * 40, 0));
                c.transform.localScale = Vector3.one * s;
            }
            var rock = Mat("Rock", new Color(0.55f, 0.53f, 0.50f), 0.2f);
            for (int i = 0; i < 10; i++)
            {
                float a = i * 1.7f + 0.5f, r = 5.5f + (i * 29 % 4);
                float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                Prim(PrimitiveType.Cube, "Rock", rock, new Vector3(x, Height(x, z) + 0.1f, z), new Vector3(1.2f, 0.8f, 1.0f) * (0.6f + (i % 3) * 0.3f), new Vector3(i * 23, i * 47, i * 13));
            }

            // the orbiting light: a small glowing ball with a point light
            var glow = Mat("Glow", new Color(1f, 0.62f, 0.28f), 0.5f);
            glow.EnableKeyword("_EMISSION"); glow.SetColor("_EmissionColor", new Color(1f, 0.55f, 0.22f) * 6f);
            var orb = Prim(PrimitiveType.Sphere, "Orb", glow, new Vector3(3.4f, 1.7f, 0), Vector3.one * 0.4f);
            var pl = new GameObject("Orb light").AddComponent<Light>();
            pl.type = LightType.Point; pl.color = new Color(1f, 0.6f, 0.3f); pl.intensity = 8f; pl.range = 7f; pl.shadows = LightShadows.None;
            pl.transform.SetParent(orb.transform, false);

            // ---- cameras: the scene camera renders into a texture (the director sets that up), the display camera shows the result
            var srcCam = SceneKit.Cam("Scene camera", new Rect(0, 0, 1, 1), 45, Color.black, -10);
            srcCam.clearFlags = CameraClearFlags.Skybox; srcCam.cullingMask = ~(1 << 8); srcCam.farClipPlane = 300f;
            SceneKit.Grade(Dir, 0.4f, 0f, 12f, 10f, 0.2f, 0f);
            var display = SceneKit.Cam("Display camera", new Rect(0, 0, 1, 1), 40, Color.black, 10, false);
            display.cullingMask = 1 << 8; display.transform.position = new Vector3(0, -50, 0); display.tag = "MainCamera";

            var compose = new Material(Shader.Find("Hidden/GT/PixelCompose")); AssetDatabase.CreateAsset(compose, Dir + "Compose.mat");
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Display quad"; Object.DestroyImmediate(quad.GetComponent<Collider>()); quad.layer = 8;
            var qm = Object.Instantiate(quad.GetComponent<MeshFilter>().sharedMesh); qm.name = "DisplayQuad"; qm.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
            AssetDatabase.CreateAsset(qm, Dir + "DisplayQuad.asset");
            quad.GetComponent<MeshFilter>().sharedMesh = qm; quad.GetComponent<Renderer>().sharedMaterial = compose;
            quad.transform.position = display.transform.position + Vector3.forward;

            Material M(string shader, string name) { var m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, Dir + name + ".mat"); return m; }
            var dir = new GameObject("Director").AddComponent<PixelDirector>();
            dir.src = srcCam; dir.compose = compose; dir.sun = sun; dir.orb = orb.transform; dir.torus = torus.transform;
            dir.pixelArt = M("Hidden/GT/PixelArtFilter", "PixelArtFilter"); dir.dither = M("Hidden/GT/Dither", "Dither"); dir.palette = M("Hidden/GT/PaletteSwapper", "PaletteSwapper");
            dir.title = LabelSpec.Create("", 0.012f, 0.935f, 30);
            dir.zTop = LabelSpec.Create("", 0.012f, 0.522f, 28);
            dir.l1 = new LabelSpec[4]; dir.l2 = new LabelSpec[4];
            for (int i = 0; i < 4; i++) { dir.l1[i] = LabelSpec.Create("", 0.012f + 0.25f * i, 0.058f, 28); dir.l2[i] = LabelSpec.Create("", 0.012f + 0.25f * i, 0.018f, 22); }
        }
    }
}
