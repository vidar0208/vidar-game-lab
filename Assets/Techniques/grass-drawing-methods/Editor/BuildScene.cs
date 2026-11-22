// Builds the grass scene from code: a meadow ground, a gradient sky, a low warm evening sun from the front right (back light through
// the tips), warm haze, a procedural card texture (alpha mask of 14 curved blades), a procedural blade mesh, the two
// grass materials, two scene cameras that render into textures and the display camera with the composite quad.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GameTech.Posts
{
    public static class GrassScene
    {
        const string Dir = "Assets/Techniques/grass-drawing-methods/";

        static float Hash(int x, int y) { uint h = (uint)(x * 374761393 + y * 668265263); h = (h ^ (h >> 13)) * 1274126177u; return (h ^ (h >> 16)) / 4294967295f; }
        static float Noise(float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y); float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            return Mathf.Lerp(Mathf.Lerp(Hash(xi, yi), Hash(xi + 1, yi), fx), Mathf.Lerp(Hash(xi, yi + 1), Hash(xi + 1, yi + 1), fx), fy);
        }
        static float Fbm(float x, float y) { float s = 0, a = 0.5f; for (int i = 0; i < 4; i++) { s += a * Noise(x, y); x *= 2.03f; y *= 2.03f; a *= 0.5f; } return s; }

        // alpha mask of a grass tuft: 14 tapered, curved blades rising from the bottom edge
        static Texture2D CardTexture()
        {
            int n = 256; var px = new Color[n * n];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(1, 1, 1, 0);
            for (int b = 0; b < 14; b++)
            {
                float x0 = 0.12f + 0.76f * Hash(b, 1), hgt = 0.55f + 0.43f * Hash(b, 2), lean = (Hash(b, 3) - 0.5f) * 0.35f, w0 = 0.018f + 0.012f * Hash(b, 4);
                for (int y = 0; y < n; y++)
                {
                    float v = y / (float)n; if (v > hgt) break;
                    float t = v / hgt, cx = x0 + lean * t * t, hw = w0 * (1f - t) + 0.002f;
                    int xa = Mathf.Max(0, Mathf.FloorToInt((cx - hw) * n)), xb = Mathf.Min(n - 1, Mathf.CeilToInt((cx + hw) * n));
                    for (int x = xa; x <= xb; x++) px[y * n + x] = new Color(1, 1, 1, 1);
                }
            }
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true); tex.SetPixels(px); tex.Apply(true);
            tex.wrapMode = TextureWrapMode.Clamp; tex.filterMode = FilterMode.Trilinear;
            AssetDatabase.CreateAsset(tex, Dir + "CardMask.asset"); return tex;
        }

        // one blade: 4 rows of two vertices tapering upwards plus a tip, 7 triangles, height 1, base width 0.12
        static Mesh BladeMesh()
        {
            float[] ys = { 0f, 0.3f, 0.58f, 0.82f }; var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            foreach (float y in ys) { float hw = 0.06f * (1f - y * 0.85f); v.Add(new Vector3(-hw, y, 0)); v.Add(new Vector3(hw, y, 0)); uv.Add(new Vector2(0, y)); uv.Add(new Vector2(1, y)); }
            v.Add(new Vector3(0, 1f, 0)); uv.Add(new Vector2(0.5f, 1f));
            for (int i = 0; i < 3; i++) { int a = i * 2; tri.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 }); }
            tri.AddRange(new[] { 6, 8, 7 });
            var m = new Mesh { name = "Blade" }; m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            var nr = new Vector3[v.Count]; for (int i = 0; i < nr.Length; i++) nr[i] = new Vector3(0, 0, -1); m.normals = nr;
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 100f);
            AssetDatabase.CreateAsset(m, Dir + "Blade.asset"); return m;
        }
        static Mesh CardMesh()
        {
            var m = new Mesh { name = "Card" };
            m.SetVertices(new List<Vector3> { new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(-0.5f, 1, 0), new Vector3(0.5f, 1, 0) });
            m.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
            m.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0); m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 100f);
            AssetDatabase.CreateAsset(m, Dir + "Card.asset"); return m;
        }
        static Material GrassMat(string name, bool card, Texture2D tex)
        {
            var m = new Material(Shader.Find("GameTech/GrassInstanced"));
            m.SetTexture("_CardTex", tex); m.SetFloat("_Card", card ? 1 : 0); m.SetFloat("_CardWidth", 1.3f);
            m.SetColor("_BaseColor", new Color(0.09f, 0.2f, 0.05f)); m.SetColor("_TipColor", new Color(0.72f, 0.8f, 0.32f));
            AssetDatabase.CreateAsset(m, Dir + name + ".mat"); return m;
        }

        public static void Build()
        {
            RenderSettings.skybox = null;
            var haze = new Color(0.86f, 0.66f, 0.46f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogStartDistance = 8f; RenderSettings.fogEndDistance = 42f; RenderSettings.fogColor = haze;
            SceneKit.Ambient(new Color(0.55f, 0.58f, 0.68f), new Color(0.5f, 0.44f, 0.36f), new Color(0.18f, 0.15f, 0.1f));
            SceneKit.Shadows(25f);

            int n = 512; var gp = new Color[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float f = Fbm(x / 24f, y / 24f), g = Fbm(x / 6f + 9f, y / 6f + 3f);
                gp[y * n + x] = Color.Lerp(new Color(0.16f, 0.12f, 0.07f), new Color(0.12f, 0.2f, 0.06f), Mathf.Clamp01(f * 1.4f - 0.2f)) * (0.8f + 0.4f * g);
            }
            var gt = new Texture2D(n, n, TextureFormat.RGBA32, true); gt.SetPixels(gp); gt.Apply(true); gt.wrapMode = TextureWrapMode.Repeat;
            AssetDatabase.CreateAsset(gt, Dir + "Ground.asset");
            var groundM = SceneKit.Mat(Dir, "Ground", "Universal Render Pipeline/Lit", Color.white, 0, 0.1f); groundM.SetTexture("_BaseMap", gt); groundM.SetTextureScale("_BaseMap", new Vector2(8, 8));
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane); plane.name = "Ground"; plane.transform.position = new Vector3(0, 0, 10f); plane.transform.localScale = new Vector3(8f, 1f, 8f);
            plane.GetComponent<MeshRenderer>().sharedMaterial = groundM; Object.DestroyImmediate(plane.GetComponent<Collider>());

            var sun = SceneKit.Sun("Evening sun", new Vector3(26f, 206f, 0f), new Color(1f, 0.8f, 0.56f), 2.6f, true);
            sun.shadowNormalBias = 0.3f; sun.shadowBias = 0.05f;
            SceneKit.Grade(Dir, 0.45f, 0.1f, 12f, 10f, 0.3f, 0.06f);

            var skyM = new Material(Shader.Find("GameTech/SkyGradient")); skyM.SetColor("_Horizon", haze); AssetDatabase.CreateAsset(skyM, Dir + "Sky.mat");
            var sky = GameObject.CreatePrimitive(PrimitiveType.Sphere); sky.name = "Sky"; Object.DestroyImmediate(sky.GetComponent<Collider>());
            sky.transform.position = new Vector3(0, 0, 8f); sky.transform.localScale = Vector3.one * 100f;
            var smr = sky.GetComponent<MeshRenderer>(); smr.sharedMaterial = skyM; smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; smr.receiveShadows = false;

            var tex = CardTexture();
            var dir = new GameObject("Director").AddComponent<GrassDirector>();
            dir.cardMesh = CardMesh(); dir.bladeMesh = BladeMesh();
            dir.cardMat = GrassMat("Cards", true, tex); dir.bladeMat = GrassMat("Blades", false, tex);
            dir.camA = SceneKit.Cam("Cam A", new Rect(0, 0, 1, 1), 50f, haze, -5, false);
            dir.camB = SceneKit.Cam("Cam B", new Rect(0, 0, 1, 1), 50f, haze, -6, false);
            dir.camA.cullingMask = ~((1 << 9) | (1 << 10)); dir.camB.cullingMask = ~((1 << 8) | (1 << 10));
            foreach (var c in new[] { dir.camA, dir.camB }) { c.nearClipPlane = 0.03f; c.farClipPlane = 80f; }

            var disp = new Material(Shader.Find("GameTech/GrassComposite")); AssetDatabase.CreateAsset(disp, Dir + "Display.mat"); dir.display = disp;
            var dcam = SceneKit.Cam("Display cam", new Rect(0, 0, 1, 1), 60f, Color.black, 0, true);
            dcam.orthographic = true; dcam.orthographicSize = 0.5f; dcam.cullingMask = 1 << 10; dcam.transform.position = new Vector3(500f, 0f, -1f);
            dcam.nearClipPlane = 0.01f; dcam.farClipPlane = 5f;
            var dq = GameObject.CreatePrimitive(PrimitiveType.Quad); dq.name = "Display quad"; dq.layer = 10; Object.DestroyImmediate(dq.GetComponent<Collider>());
            dq.transform.position = new Vector3(500f, 0f, 0f); dq.transform.localScale = new Vector3(16f / 9f, 1f, 1f);
            var dmr = dq.GetComponent<MeshRenderer>(); dmr.sharedMaterial = disp; dmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            dir.title = LabelSpec.Create("", 0.012f, 0.93f, 40);
            dir.l1 = LabelSpec.Create("", 0.012f, 0.058f, 34); dir.l2 = LabelSpec.Create("", 0.012f, 0.018f, 30);
            dir.m1 = LabelSpec.Create("", 0.512f, 0.058f, 34); dir.m2 = LabelSpec.Create("", 0.512f, 0.018f, 30);
            AssetDatabase.SaveAssets();
            Debug.Log($"[gt] built: grass shader {dir.bladeMat.shader.isSupported}, composite {disp.shader.isSupported}");
        }
    }
}
