// 82 ASCII render: a small lit set (torus, sphere, cube, steps, pillars, a moving warm light) rendered by a scene camera
// into a texture; AsciiDirector turns it into glyphs and a display camera shows the split screen.
// The glyph atlases are drawn here from 8 x 8 bitmaps (no font, no image files).
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    public static class AsciiScene
    {
        const string Dir = "Assets/Techniques/ascii-render/";
        const string Lit = "Universal Render Pipeline/Lit";

        // fill ramp, darkest to brightest; ink pixels per glyph: 0 4 8 12 16 20 26 28 32 34
        static readonly string[][] Ramp = {
            new[] { "........", "........", "........", "........", "........", "........", "........", "........" }, // space
            new[] { "........", "........", "........", "........", "........", "...##...", "...##...", "........" }, // .
            new[] { "........", "...##...", "...##...", "........", "........", "...##...", "...##...", "........" }, // :
            new[] { "........", "........", "........", ".######.", ".######.", "........", "........", "........" }, // -
            new[] { "........", "........", "########", "........", "........", "########", "........", "........" }, // =
            new[] { "........", "...##...", "...##...", ".######.", ".######.", "...##...", "...##...", "........" }, // +
            new[] { "........", ".#.##.#.", "..####..", "########", "..####..", ".#.##.#.", "...##...", "........" }, // *
            new[] { "..#..#..", "..#..#..", "########", "..#..#..", "..#..#..", "########", "..#..#..", "..#..#.." }, // #
            new[] { "###...##", "###..##.", "###.##..", "...##...", "..##.###", ".##..###", "##...###", "........" }, // %
            new[] { ".######.", "##....##", "##.####.", "##.#..#.", "##.####.", "##......", ".######.", "........" }, // @
        };

        // edge atlas slots, in the order the compute pass indexes them: blank, vertical, horizontal, two diagonals
        static readonly string[][] Edges = {
            new[] { "........", "........", "........", "........", "........", "........", "........", "........" },
            new[] { "...##...", "...##...", "...##...", "...##...", "...##...", "...##...", "...##...", "...##..." }, // |
            new[] { "........", "........", "........", "########", "########", "........", "........", "........" }, // -
            new[] { "##......", ".##.....", "..##....", "...##...", "....##..", ".....##.", "......##", ".......#" }, // \
            new[] { "......##", ".....##.", "....##..", "...##...", "..##....", ".##.....", "##......", "#......." }, // /
        };

        // glyph rows are written top to bottom; texture row 0 is the bottom row
        static Texture2D Atlas(string name, string[][] glyphs, int[] slots)
        {
            int w = slots.Length * 8;
            var tex = new Texture2D(w, 8, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = name };
            var px = new Color[w * 8];
            for (int s = 0; s < slots.Length; s++)
                for (int row = 0; row < 8; row++)
                    for (int x = 0; x < 8; x++)
                    {
                        float v = glyphs[slots[s]][row][x] == '#' ? 1f : 0f;
                        px[(7 - row) * w + s * 8 + x] = new Color(v, v, v, 1);
                    }
            tex.SetPixels(px); tex.Apply(false, false);
            AssetDatabase.CreateAsset(tex, Dir + name + ".asset");
            return tex;
        }

        static Texture2D Tiles(string name)
        {
            int n = 512; var px = new Color[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                bool line = (x % 128) < 4 || (y % 128) < 4;
                float g = 0.55f + 0.08f * Mathf.PerlinNoise(x * 0.02f, y * 0.02f);
                px[y * n + x] = line ? new Color(0.18f, 0.18f, 0.2f, 1) : new Color(g, g, g * 1.04f, 1);
            }
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true, false) { wrapMode = TextureWrapMode.Repeat, anisoLevel = 4, name = name };
            tex.SetPixels(px); tex.Apply();
            AssetDatabase.CreateAsset(tex, Dir + name + ".asset");
            return tex;
        }

        static Material Mat(string name, Color c, float smooth = 0.35f, Texture tex = null, float tiling = 1f)
        {
            var m = SceneKit.Mat(Dir, name, Lit, c, 0f, smooth);
            if (tex != null) { m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", Vector2.one * tiling); }
            return m;
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
            SceneKit.Ambient(new Color(0.30f, 0.34f, 0.46f), new Color(0.24f, 0.24f, 0.26f), new Color(0.10f, 0.09f, 0.08f));
            SceneKit.Shadows(30f);
            var sun = SceneKit.Sun("Sun", new Vector3(38, -60, 0), new Color(1f, 0.93f, 0.82f), 1.7f, true);
            RenderSettings.sun = sun;
            var sky = new Material(Shader.Find("Skybox/Procedural"));
            sky.SetFloat("_SunSize", 0.03f); sky.SetFloat("_AtmosphereThickness", 0.8f); sky.SetFloat("_Exposure", 0.55f);
            sky.SetColor("_GroundColor", new Color(0.12f, 0.12f, 0.13f));
            AssetDatabase.CreateAsset(sky, Dir + "Sky.mat");
            RenderSettings.skybox = sky;

            Prim(PrimitiveType.Plane, "Floor", Mat("Floor", Color.white, 0.25f, Tiles("FloorTiles"), 6f), Vector3.zero, new Vector3(3, 1, 3));

            // torus from a circle profile (axial, radius) turned around Y
            var prof = new Vector2[28];
            for (int i = 0; i < prof.Length; i++) { float a = i * Mathf.PI * 2f / prof.Length; prof[i] = new Vector2(0.36f * Mathf.Sin(a), 1.05f + 0.36f * Mathf.Cos(a)); }
            var torus = SceneKit.Part("Torus", new MeshKit().Lathe(prof, 56).Build("Torus", 60f), Mat("Torus", new Color(0.95f, 0.45f, 0.12f), 0.55f), new Vector3(0, 1.75f, 0), Quaternion.identity);

            Prim(PrimitiveType.Sphere, "Sphere", Mat("Sphere", new Color(0.15f, 0.65f, 0.70f), 0.6f), new Vector3(-2.5f, 0.85f, 1.0f), Vector3.one * 1.7f);
            var cube = Prim(PrimitiveType.Cube, "Cube", Mat("Cube", new Color(0.80f, 0.18f, 0.30f), 0.3f), new Vector3(2.5f, 0.7f, -0.6f), Vector3.one * 1.4f, 30f);
            Prim(PrimitiveType.Capsule, "Capsule", Mat("Capsule", new Color(0.75f, 0.80f, 0.20f), 0.4f), new Vector3(1.6f, 1.0f, 2.2f), new Vector3(0.8f, 1.0f, 0.8f));

            // steps: horizontal edges
            var stepMat = Mat("Steps", new Color(0.85f, 0.83f, 0.78f), 0.2f);
            for (int i = 0; i < 4; i++)
                Prim(PrimitiveType.Cube, "Step", stepMat, new Vector3(-0.6f, 0.125f + i * 0.125f, 3.0f + i * 0.4f), new Vector3(2.0f, 0.25f + i * 0.25f, 0.4f));
            // ring of pillars: vertical edges
            var pillarMat = Mat("Pillar", new Color(0.90f, 0.88f, 0.84f), 0.3f);
            for (int i = 0; i < 9; i++)
            {
                float a = i * Mathf.PI * 2f / 9f + 0.2f;
                Prim(PrimitiveType.Cylinder, "Pillar", pillarMat, new Vector3(Mathf.Cos(a) * 9.5f, 1.6f, Mathf.Sin(a) * 9.5f), new Vector3(0.5f, 1.6f, 0.5f));
            }

            var warm = new GameObject("Orbit light").AddComponent<Light>();
            warm.type = LightType.Point; warm.color = new Color(1f, 0.62f, 0.30f); warm.intensity = 4f; warm.range = 7f; warm.shadows = LightShadows.None;

            // ---- cameras: the scene camera renders into a texture (the director sets that up), the display camera shows the result
            var srcCam = SceneKit.Cam("Scene camera", new Rect(0, 0, 1, 1), 42, Color.black, -10);
            srcCam.clearFlags = CameraClearFlags.Skybox; srcCam.cullingMask = ~(1 << 8); srcCam.farClipPlane = 300f;
            SceneKit.Grade(Dir, 0.2f, 0.15f, 14f, 10f, 0.2f, 0f);
            var display = SceneKit.Cam("Display camera", new Rect(0, 0, 1, 1), 40, Color.black, 10, false);
            display.cullingMask = 1 << 8; display.transform.position = new Vector3(0, -50, 0); display.tag = "MainCamera";

            var compose = new Material(Shader.Find("Hidden/GT/AsciiCompose")); AssetDatabase.CreateAsset(compose, Dir + "Compose.mat");
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Display quad"; Object.DestroyImmediate(quad.GetComponent<Collider>()); quad.layer = 8;
            var qm = Object.Instantiate(quad.GetComponent<MeshFilter>().sharedMesh); qm.name = "DisplayQuad"; qm.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
            AssetDatabase.CreateAsset(qm, Dir + "DisplayQuad.asset");
            quad.GetComponent<MeshFilter>().sharedMesh = qm; quad.GetComponent<Renderer>().sharedMaterial = compose;
            quad.transform.position = display.transform.position + Vector3.forward;

            var asciiMat = new Material(Shader.Find("Hidden/ASCII")); AssetDatabase.CreateAsset(asciiMat, Dir + "Ascii.mat");
            var d = new GameObject("Director").AddComponent<AsciiDirector>();
            d.src = srcCam; d.compose = compose; d.ascii = asciiMat;
            d.cs = AssetDatabase.LoadAssetAtPath<ComputeShader>(Dir + "ASCII.compute");
            d.fill10 = Atlas("Fill10", Ramp, new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 });
            d.fill5 = Atlas("Fill5", Ramp, new[] { 0, 0, 2, 2, 5, 5, 7, 7, 9, 9 });
            d.edgeAtlas = Atlas("EdgeGlyphs", Edges, new[] { 0, 1, 2, 3, 4 });
            d.torus = torus.transform; d.cube = cube.transform; d.orbitLight = warm;
            d.left = LabelSpec.Create("", 0.03f, 0.05f, 32);
            d.right = LabelSpec.Create("", 0.53f, 0.05f, 32);
        }
    }
}
