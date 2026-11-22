// Builds the pixel sort scene from code: a stone colonnade with a low warm sun at the far end (long column shadows
// across the floor, a bright sky gradient), the stand-in mannequin standing in the back light, a scene camera that
// renders into a texture, and an orthographic display camera with a full-screen quad that shows the sorted result.
using UnityEditor;
using UnityEngine;

namespace GameTech.Posts
{
    public static class PixelSortScene
    {
        const string Dir = "Assets/Techniques/bitonic-pixel-sort/";

        static float Hash(int x, int y) { uint h = (uint)(x * 374761393 + y * 668265263); h = (h ^ (h >> 13)) * 1274126177u; return (h ^ (h >> 16)) / 4294967295f; }
        static float Noise(float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y); float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            return Mathf.Lerp(Mathf.Lerp(Hash(xi, yi), Hash(xi + 1, yi), fx), Mathf.Lerp(Hash(xi, yi + 1), Hash(xi + 1, yi + 1), fx), fy);
        }
        static float Fbm(float x, float y) { float s = 0, a = 0.5f; for (int i = 0; i < 4; i++) { s += a * Noise(x, y); x *= 2.03f; y *= 2.03f; a *= 0.5f; } return s; }

        static Texture2D Tex(string name, int n, System.Func<float, float, Color> f)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true); var px = new Color[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) px[y * n + x] = f(x / (float)n, y / (float)n);
            t.SetPixels(px); t.Apply(true); t.wrapMode = TextureWrapMode.Repeat; t.filterMode = FilterMode.Trilinear; t.anisoLevel = 8;
            AssetDatabase.CreateAsset(t, Dir + name + ".asset"); return t;
        }

        static GameObject Box(string name, Mesh m, Material mat, Vector3 p, Vector3 s, Transform parent, int layer = 0)
        {
            var go = new GameObject(name); go.layer = layer; go.transform.SetParent(parent, false); go.transform.localPosition = p; go.transform.localScale = s;
            go.AddComponent<MeshFilter>().sharedMesh = m; go.AddComponent<MeshRenderer>().sharedMaterial = mat; return go;
        }
        static Mesh Prim(PrimitiveType p) { var g = GameObject.CreatePrimitive(p); var m = g.GetComponent<MeshFilter>().sharedMesh; Object.DestroyImmediate(g); return m; }

        public static void Build()
        {
            RenderSettings.skybox = null; RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Exponential; RenderSettings.fogDensity = 0.012f;
            RenderSettings.fogColor = new Color(0.55f, 0.42f, 0.32f);
            SceneKit.Ambient(new Color(0.5f, 0.52f, 0.62f), new Color(0.42f, 0.36f, 0.3f), new Color(0.2f, 0.15f, 0.12f));
            SceneKit.Shadows(45f);

            var stoneT = Tex("Stone", 512, (x, y) => {
                float gx = Mathf.Abs(Mathf.Repeat(x * 4f, 1f) - 0.5f), gy = Mathf.Abs(Mathf.Repeat(y * 4f, 1f) - 0.5f);
                float grout = Mathf.Clamp01((Mathf.Max(gx, gy) - 0.475f) * 40f);
                float n = Fbm(x * 16, y * 16), tile = Hash(Mathf.FloorToInt(x * 4), Mathf.FloorToInt(y * 4));
                var c = Color.Lerp(new Color(0.62f, 0.52f, 0.42f), new Color(0.78f, 0.68f, 0.55f), n * 0.7f + tile * 0.3f);
                return Color.Lerp(c, new Color(0.3f, 0.25f, 0.2f), grout); });
            var colT = Tex("Plaster", 256, (x, y) => { float n = Fbm(x * 10, y * 20); return Color.Lerp(new Color(0.8f, 0.74f, 0.64f), new Color(0.9f, 0.85f, 0.76f), n); });
            var skyT = Tex("Sky", 1024, (x, y) => {
                if (y < 0.5f) return new Color(0.25f, 0.2f, 0.16f);
                float h = (y - 0.5f) * 2f;
                var c = Color.Lerp(new Color(1.0f, 0.62f, 0.3f), new Color(0.95f, 0.45f, 0.5f), Mathf.Clamp01(h * 4f));
                c = Color.Lerp(c, new Color(0.35f, 0.32f, 0.7f), Mathf.Clamp01(h * 2.2f - 0.2f));
                c = Color.Lerp(c, new Color(0.1f, 0.14f, 0.36f), Mathf.Clamp01(h * 1.6f - 0.6f));
                float cl = Mathf.Clamp01((Fbm(x * 60f, y * 140f) - 0.45f) * 6f) * Mathf.Clamp01(h * 20f) * Mathf.Clamp01(1.4f - h * 1.6f);
                var cloud = Color.Lerp(new Color(1.0f, 0.85f, 0.6f), new Color(0.55f, 0.3f, 0.45f), Mathf.Clamp01(h * 2.5f));
                return Color.Lerp(c, cloud, cl); });
            var glowT = Tex("Glow", 128, (x, y) => { float r = Vector2.Distance(new Vector2(x, y), new Vector2(0.5f, 0.5f)) * 2f; float g = Mathf.Exp(-r * r * 6f); return new Color(g, g, g, 1); });

            var floorM = SceneKit.Mat(Dir, "Floor", "Universal Render Pipeline/Lit", Color.white, 0, 0.35f); floorM.SetTexture("_BaseMap", stoneT); floorM.SetTextureScale("_BaseMap", new Vector2(10, 10));
            var colM = SceneKit.Mat(Dir, "Column", "Universal Render Pipeline/Lit", Color.white, 0, 0.15f); colM.SetTexture("_BaseMap", colT);
            var skyM = new Material(Shader.Find("Universal Render Pipeline/Unlit")); skyM.SetTexture("_BaseMap", skyT); skyM.SetColor("_BaseColor", new Color(1.6f, 1.6f, 1.6f)); skyM.SetFloat("_Cull", 1);
            AssetDatabase.CreateAsset(skyM, Dir + "Sky.mat");
            var glowM = new Material(Shader.Find("Universal Render Pipeline/Unlit")); glowM.SetTexture("_BaseMap", glowT); glowM.SetColor("_BaseColor", new Color(3.2f, 2.1f, 1.1f));
            glowM.SetFloat("_Surface", 1); glowM.SetFloat("_Blend", 2); glowM.SetOverrideTag("RenderType", "Transparent"); glowM.renderQueue = 3000;
            glowM.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One); glowM.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One); glowM.SetInt("_ZWrite", 0);
            glowM.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            AssetDatabase.CreateAsset(glowM, Dir + "Glow.mat");

            var cube = Prim(PrimitiveType.Cube); var cyl = Prim(PrimitiveType.Cylinder); var sph = Prim(PrimitiveType.Sphere); var quad = Prim(PrimitiveType.Quad);
            var world = new GameObject("Courtyard").transform;
            Box("Floor", cube, floorM, new Vector3(0, -0.05f, 10f), new Vector3(30f, 0.1f, 60f), world);
            var sky = Box("Sky", sph, skyM, new Vector3(0, 0, 10f), new Vector3(400f, 400f, 400f), world);
            sky.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var sun = Box("Sun glow", quad, glowM, new Vector3(1.5f, 5.5f, 120f), new Vector3(46f, 46f, 1f), world);
            sun.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            for (int i = 0; i < 12; i++)
            {
                float z = -6f + i * 1.8f;
                foreach (float x in new[] { -1.7f, 1.7f })
                {
                    Box("Column", cyl, colM, new Vector3(x, 1.6f, z), new Vector3(0.36f, 1.6f, 0.36f), world);
                    Box("Base", cube, colM, new Vector3(x, 0.1f, z), new Vector3(0.52f, 0.2f, 0.52f), world);
                    Box("Capital", cube, colM, new Vector3(x, 3.25f, z), new Vector3(0.5f, 0.14f, 0.5f), world);
                }
                if (i % 3 == 0) Box("Beam", cube, colM, new Vector3(0, 3.5f, z), new Vector3(3.9f, 0.2f, 0.22f), world);
            }
            foreach (float x in new[] { -1.7f, 1.7f }) Box("Architrave", cube, colM, new Vector3(x, 3.42f, 3.9f), new Vector3(0.5f, 0.3f, 20f), world);

            var ch = CharacterKit.Spawn("Universal Render Pipeline/Lit", Dir);
            ch.transform.position = new Vector3(0.1f, 0f, -2.4f); ch.transform.rotation = Quaternion.Euler(0, 180, 0);

            var key = SceneKit.Sun("Low sun", new Vector3(16f, 262f, 0f), new Color(1f, 0.76f, 0.5f), 3.0f, true);
            key.shadowNormalBias = 0.4f;
            SceneKit.Grade(Dir, 0.35f, 0.25f, 14f, 12f, 0.3f, 0.1f);

            var dir = new GameObject("Director").AddComponent<PixelSortDirector>();
            dir.character = ch.transform;
            dir.sorter = AssetDatabase.LoadAssetAtPath<ComputeShader>(Dir + "Compute/BitonicPixelSorter.compute");
            dir.sceneCam = SceneKit.Cam("Scene cam", new Rect(0, 0, 1, 1), 42f, new Color(0.3f, 0.3f, 0.35f), -5, true);
            dir.sceneCam.nearClipPlane = 0.05f; dir.sceneCam.farClipPlane = 500f; dir.sceneCam.cullingMask = ~(1 << 10);

            var disp = new Material(Shader.Find("GameTech/SortComposite")); AssetDatabase.CreateAsset(disp, Dir + "Display.mat"); dir.display = disp;
            var dcam = SceneKit.Cam("Display cam", new Rect(0, 0, 1, 1), 60f, Color.black, 0, false);
            dcam.orthographic = true; dcam.orthographicSize = 0.5f; dcam.cullingMask = 1 << 10; dcam.transform.position = new Vector3(500f, 0f, -1f);
            dcam.nearClipPlane = 0.01f; dcam.farClipPlane = 5f;
            var dq = Box("Display quad", quad, disp, new Vector3(500f, 0f, 0f), new Vector3(16f / 9f, 1f, 1f), null, 10);
            dq.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            dir.title = LabelSpec.Create("", 0.012f, 0.93f, 40);
            dir.l1 = LabelSpec.Create("", 0.012f, 0.058f, 34); dir.l2 = LabelSpec.Create("", 0.012f, 0.018f, 30);
            dir.m1 = LabelSpec.Create("", 0.512f, 0.058f, 34); dir.m2 = LabelSpec.Create("", 0.512f, 0.018f, 30);
            AssetDatabase.SaveAssets();
            Debug.Log($"[gt] built: colonnade, mannequin, sorter {(dir.sorter != null)}, display shader {disp.shader.isSupported}");
        }
    }
}
