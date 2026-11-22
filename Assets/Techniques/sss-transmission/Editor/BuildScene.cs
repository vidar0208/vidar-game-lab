// Builds the subsurface/transmission scene from code: a wax pillar, a jade disc on a stand and a leaf sprig on a slate
// pedestal, all procedural, in two identical copies on layers 8 (left camera) and 9 (right camera). A warm sun sits
// behind the objects (back light) with a glowing backdrop, plus a weak cool fill and a rim light.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GameTech.Posts
{
    public static class SSSScene
    {
        const string Dir = "Assets/Techniques/sss-transmission/";

        static float Hash(int x, int y) { uint h = (uint)(x * 374761393 + y * 668265263); h = (h ^ (h >> 13)) * 1274126177u; return (h ^ (h >> 16)) / 4294967295f; }
        static float Noise(float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y); float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            return Mathf.Lerp(Mathf.Lerp(Hash(xi, yi), Hash(xi + 1, yi), fx), Mathf.Lerp(Hash(xi, yi + 1), Hash(xi + 1, yi + 1), fx), fy);
        }
        static float Fbm(float x, float y) { float s = 0, a = 0.5f; for (int i = 0; i < 5; i++) { s += a * Noise(x, y); x *= 2.03f; y *= 2.03f; a *= 0.5f; } return s; }

        static Texture2D Tex(string name, int n, System.Func<float, float, Color> f)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true);
            var px = new Color[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) px[y * n + x] = f(x / (float)n, y / (float)n);
            t.SetPixels(px); t.Apply(true); t.wrapMode = TextureWrapMode.Repeat;
            AssetDatabase.CreateAsset(t, Dir + name + ".asset"); return t;
        }

        static Mesh Lathe(string name, Vector2[] prof, int seg)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            int pn = prof.Length;
            for (int s = 0; s <= seg; s++)
            {
                float a = s / (float)seg * Mathf.PI * 2f;
                for (int i = 0; i < pn; i++) { v.Add(new Vector3(Mathf.Cos(a) * prof[i].x, prof[i].y, Mathf.Sin(a) * prof[i].x)); uv.Add(new Vector2(s / (float)seg * 3f, i / (float)(pn - 1))); }
            }
            for (int s = 0; s < seg; s++) for (int i = 0; i < pn - 1; i++)
            {
                int a = s * pn + i, b = (s + 1) * pn + i;
                tri.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
            }
            var m = new Mesh { name = name }; m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateNormals();
            // weld the seam normals
            var nr = m.normals; for (int i = 0; i < pn; i++) { var avg = (nr[i] + nr[seg * pn + i]).normalized; nr[i] = avg; nr[seg * pn + i] = avg; }
            m.normals = nr; m.RecalculateBounds(); AssetDatabase.CreateAsset(m, Dir + name + ".asset"); return m;
        }

        static Mesh Leaf(string name, float len, float wid, float bend, float cup)
        {
            int nu = 28, nv = 10; var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (int i = 0; i <= nu; i++) for (int j = 0; j <= nv; j++)
            {
                float u = i / (float)nu, s = j / (float)nv * 2f - 1f;
                float w = wid * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Pow(u, 0.75f))), 0.9f);
                float y = u * len, z = -bend * u * u * len + cup * s * s * w;
                v.Add(new Vector3(s * w, y, z)); uv.Add(new Vector2(s * 0.5f + 0.5f, u));
            }
            for (int i = 0; i < nu; i++) for (int j = 0; j < nv; j++)
            {
                int a = i * (nv + 1) + j, b = a + nv + 1;
                tri.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
            }
            var m = new Mesh { name = name }; m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, Dir + name + ".asset"); return m;
        }

        static Material SSS(string name, Texture2D tex, Color sss, float thick, float smooth, bool twoSided)
        {
            var m = new Material(Shader.Find("GameTech/SSSLit")); m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", Color.white);
            m.SetColor("_SubsurfaceColor", sss); m.SetFloat("_Thickness", thick); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Cull", twoSided ? 0 : 2);
            AssetDatabase.CreateAsset(m, Dir + name + ".mat"); return m;
        }

        static GameObject Obj(string name, Mesh mesh, Material mat, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale, int layer)
        {
            var go = new GameObject(name); go.layer = layer; go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localRotation = rot; go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = mat; return go;
        }

        static Mesh Prim(PrimitiveType p) { var g = GameObject.CreatePrimitive(p); var m = g.GetComponent<MeshFilter>().sharedMesh; Object.DestroyImmediate(g); return m; }

        public static void Build()
        {
            RenderSettings.skybox = null; RenderSettings.fog = false;
            SceneKit.Ambient(new Color(0.06f, 0.065f, 0.08f), new Color(0.035f, 0.035f, 0.04f), new Color(0.02f, 0.018f, 0.016f));
            SceneKit.Shadows(3f);

            var waxT = Tex("WaxAlbedo", 256, (x, y) => { float n = Fbm(x * 6, y * 10); return Color.Lerp(new Color(0.86f, 0.78f, 0.62f), new Color(0.95f, 0.88f, 0.74f), n); });
            var jadeT = Tex("JadeAlbedo", 256, (x, y) => { float n = Fbm(x * 5 + 3, y * 5); float vein = Mathf.Pow(1f - Mathf.Abs(Fbm(x * 3, y * 7 + 9) * 2f - 1f), 8f);
                return Color.Lerp(Color.Lerp(new Color(0.18f, 0.42f, 0.27f), new Color(0.38f, 0.66f, 0.46f), n), new Color(0.75f, 0.86f, 0.74f), vein * 0.5f); });
            var leafT = Tex("LeafAlbedo", 256, (x, y) => {
                float mid = Mathf.Exp(-Mathf.Pow((x - 0.5f) / 0.012f, 2));
                float side = 0; float d = Mathf.Abs(x - 0.5f); float k = (y - d * 1.2f) * 9f; float fk = k - Mathf.Floor(k);
                side = Mathf.Exp(-Mathf.Pow((Mathf.Min(fk, 1 - fk)) / 0.05f, 2)) * Mathf.Clamp01(1.3f - d * 2.2f);
                float n = Fbm(x * 12, y * 12);
                var c = Color.Lerp(new Color(0.16f, 0.34f, 0.05f), new Color(0.24f, 0.44f, 0.08f), n);
                return Color.Lerp(c, new Color(0.5f, 0.66f, 0.22f), Mathf.Clamp01(mid + side * 0.6f)); });
            var glowT = Tex("Glow", 128, (x, y) => { float r = Vector2.Distance(new Vector2(x, y), new Vector2(0.5f, 0.5f)) * 2f; float g = Mathf.Exp(-r * r * 4.5f); return new Color(g, g, g, 1); });

            var thick = new[] { 0.72f, 0.55f, 0.4f };
            var sssCol = new[] { new Color(1.0f, 0.45f, 0.18f), new Color(0.35f, 0.95f, 0.55f), new Color(0.6f, 0.95f, 0.12f) };

            // meshes (metres)
            var candle = Lathe("Candle", new[] { new Vector2(0.0f, 0.158f), new Vector2(0.045f, 0.162f), new Vector2(0.061f, 0.172f), new Vector2(0.067f, 0.171f), new Vector2(0.07f, 0.164f),
                new Vector2(0.07f, 0.006f), new Vector2(0.066f, 0.0f), new Vector2(0.0f, 0.0f) }, 72);
            var disc = Lathe("JadeDisc", new[] { new Vector2(0.032f, 0.0f), new Vector2(0.034f, 0.0105f), new Vector2(0.04f, 0.0125f), new Vector2(0.097f, 0.0125f), new Vector2(0.103f, 0.0105f),
                new Vector2(0.105f, 0.0f), new Vector2(0.103f, -0.0105f), new Vector2(0.097f, -0.0125f), new Vector2(0.04f, -0.0125f), new Vector2(0.034f, -0.0105f), new Vector2(0.032f, 0.0f) }, 96);
            var leafA = Leaf("LeafA", 0.12f, 0.034f, 0.25f, 0.18f);
            var leafB = Leaf("LeafB", 0.10f, 0.03f, 0.3f, 0.15f);
            var cube = Prim(PrimitiveType.Cube); var cyl = Prim(PrimitiveType.Cylinder); var quad = Prim(PrimitiveType.Quad);

            var slate = SceneKit.Mat(Dir, "Slate", "Universal Render Pipeline/Lit", new Color(0.04f, 0.04f, 0.045f), 0, 0.32f);
            var wood = SceneKit.Mat(Dir, "Stand", "Universal Render Pipeline/Lit", new Color(0.09f, 0.05f, 0.03f), 0, 0.4f);
            var wick = SceneKit.Mat(Dir, "Wick", "Universal Render Pipeline/Lit", new Color(0.03f, 0.025f, 0.02f), 0, 0.2f);
            var stem = SceneKit.Mat(Dir, "Stem", "Universal Render Pipeline/Lit", new Color(0.12f, 0.2f, 0.05f), 0, 0.3f);
            var floorM = SceneKit.Mat(Dir, "Floor", "Universal Render Pipeline/Lit", new Color(0.008f, 0.008f, 0.009f), 0, 0.15f);
            var glowM = new Material(Shader.Find("Universal Render Pipeline/Unlit")); glowM.SetTexture("_BaseMap", glowT); glowM.SetColor("_BaseColor", new Color(2.4f, 1.55f, 0.85f));
            AssetDatabase.CreateAsset(glowM, Dir + "Glow.mat");

            // shared: floor, pedestal, backdrop glow (layer 0, seen by both cameras)
            var root = new GameObject("Set shared").transform;
            Obj("Floor", cube, floorM, root, new Vector3(0, -0.05f, 1f), Quaternion.identity, new Vector3(8f, 0.02f, 8f), 0);
            Obj("Pedestal", cube, slate, root, new Vector3(0, -0.02f, 0.02f), Quaternion.identity, new Vector3(0.56f, 0.04f, 0.3f), 0);
            var g = Obj("Backdrop glow", quad, glowM, root, new Vector3(0.05f, 0.32f, 2.6f), Quaternion.identity, new Vector3(3.4f, 2.0f, 1f), 0);
            g.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var dir = new GameObject("Director").AddComponent<SSSDirector>();
            var sets = new[] { ("L", 8), ("R", 9) };
            var mats = new Material[2][];
            for (int si = 0; si < 2; si++)
            {
                var (tag, layer) = sets[si];
                var waxM = SSS("Wax" + tag, waxT, sssCol[0], thick[0], 0.35f, false);
                var jadeM = SSS("Jade" + tag, jadeT, sssCol[1], thick[1], 0.82f, false);
                var leafM = SSS("Leaf" + tag, leafT, sssCol[2], thick[2], 0.32f, true);
                mats[si] = new[] { waxM, jadeM, leafM };
                var set = new GameObject("Set " + tag).transform;
                Obj("Wax pillar", candle, waxM, set, new Vector3(-0.125f, 0f, -0.035f), Quaternion.identity, Vector3.one, layer);
                Obj("Wick", cyl, wick, set, new Vector3(-0.125f, 0.172f, -0.035f), Quaternion.Euler(0, 0, 6), new Vector3(0.004f, 0.012f, 0.004f), layer);
                Obj("Jade disc", disc, jadeM, set, new Vector3(0.005f, 0.128f, 0.07f), Quaternion.Euler(90f, 0f, 0f), Vector3.one, layer);
                Obj("Disc stand", cube, wood, set, new Vector3(0.005f, 0.012f, 0.07f), Quaternion.identity, new Vector3(0.09f, 0.024f, 0.045f), layer);
                // leaf sprig: a stem out of a small stone, five leaves
                var sprig = new GameObject("Leaf sprig").transform; sprig.SetParent(set, false); sprig.localPosition = new Vector3(0.13f, 0f, -0.01f);
                Obj("Stone", Prim(PrimitiveType.Sphere), slate, sprig, new Vector3(0, 0.008f, 0), Quaternion.identity, new Vector3(0.06f, 0.03f, 0.05f), layer);
                Obj("Stem", cyl, stem, sprig, new Vector3(0, 0.08f, 0), Quaternion.Euler(0, 0, -4), new Vector3(0.0045f, 0.075f, 0.0045f), layer);
                var leaves = new (Mesh m, Vector3 p, Vector3 e)[] {
                    (leafA, new Vector3(0.002f, 0.15f, 0f), new Vector3(-8f, 15f, -10f)),
                    (leafA, new Vector3(0.0f, 0.11f, 0f), new Vector3(-20f, 140f, 48f)),
                    (leafB, new Vector3(0.0f, 0.09f, 0f), new Vector3(-22f, 200f, -50f)),
                    (leafB, new Vector3(0.0f, 0.06f, 0f), new Vector3(-30f, -20f, 62f)),
                    (leafB, new Vector3(0.0f, 0.045f, 0f), new Vector3(-28f, 95f, -64f)) };
                for (int i = 0; i < leaves.Length; i++) Obj("Leaf " + i, leaves[i].m, leafM, sprig, leaves[i].p, Quaternion.Euler(leaves[i].e), Vector3.one, layer);
            }
            dir.matsL = mats[0]; dir.matsR = mats[1]; dir.baseThickness = thick;

            var back = SceneKit.Sun("Back sun", new Vector3(14f, 162f, 0f), new Color(1f, 0.82f, 0.62f), 1.7f, true);
            back.shadowBias = 0.01f; back.shadowNormalBias = 0.2f;
            SceneKit.Sun("Fill", new Vector3(30f, 40f, 0f), new Color(0.6f, 0.68f, 0.85f), 0.5f, false);
            SceneKit.Sun("Rim", new Vector3(10f, -150f, 0f), new Color(0.75f, 0.85f, 1f), 0.9f, false);
            SceneKit.Grade(Dir, 0.55f, 0f, 12f, 5f, 0.3f, 0.08f);
            AssetDatabase.SaveAssets();

            var bg = new Color(0.012f, 0.012f, 0.016f);
            dir.camA = SceneKit.Cam("Cam A", new Rect(0, 0, 0.5f, 1), 30f, bg, 0, true);
            dir.camB = SceneKit.Cam("Cam B", new Rect(0.5f, 0, 0.5f, 1), 30f, bg, 1, true);
            dir.camA.cullingMask = (1 << 0) | (1 << 8); dir.camB.cullingMask = (1 << 0) | (1 << 9);
            foreach (var c in new[] { dir.camA, dir.camB }) { c.nearClipPlane = 0.02f; c.farClipPlane = 30f; }
            var clear = SceneKit.Cam("Clear", new Rect(0, 0, 1, 1), 60f, Color.black, -10, false); clear.cullingMask = 0;

            dir.title = LabelSpec.Create("", 0.012f, 0.93f, 40);
            dir.l1 = LabelSpec.Create("", 0.012f, 0.058f, 34); dir.l2 = LabelSpec.Create("", 0.012f, 0.018f, 30);
            dir.m1 = LabelSpec.Create("", 0.512f, 0.058f, 34); dir.m2 = LabelSpec.Create("", 0.512f, 0.018f, 30);
            Debug.Log($"[gt] built: 2 sets x (wax, jade, leaf sprig), shader supported {mats[1][0].shader.isSupported}");
        }
    }
}
