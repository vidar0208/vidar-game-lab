// Builds the planar shadow scene from code: a sunlit paved courtyard with a row of trees, the stand-in mannequin,
// a three-legged stool, a picket fence, a lamp post and a bouncing ball (all shadow casters under "Casters"),
// two copies of the ground (layer 8 for camera A, layer 9 for camera B) with the receiver shader, two scene cameras
// that render into textures, and an orthographic display camera with the full-screen composite quad (layer 10).
using UnityEditor;
using UnityEngine;

namespace GameTech.Posts
{
    public static class PlanarShadowScene
    {
        const string Dir = "Assets/Techniques/planar-shadow-map/";

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
        static Mesh Prim(PrimitiveType p) { var g = GameObject.CreatePrimitive(p); var m = g.GetComponent<MeshFilter>().sharedMesh; Object.DestroyImmediate(g); return m; }
        static GameObject Obj(string name, Mesh m, Material mat, Vector3 p, Vector3 s, Transform parent, int layer = 0)
        {
            var go = new GameObject(name); go.layer = layer; go.transform.SetParent(parent, false); go.transform.localPosition = p; go.transform.localScale = s;
            go.AddComponent<MeshFilter>().sharedMesh = m; go.AddComponent<MeshRenderer>().sharedMaterial = mat; return go;
        }

        public static void Build()
        {
            RenderSettings.skybox = null; RenderSettings.fog = false;
            SceneKit.Ambient(new Color(0.42f, 0.52f, 0.7f), new Color(0.42f, 0.41f, 0.4f), new Color(0.28f, 0.23f, 0.18f));

            var paveT = Tex("Pavers", 1024, (x, y) => {
                float u = x * 6f, v = y * 6f; float row = Mathf.Floor(v); u += (row % 2) * 0.5f;
                float gx = Mathf.Abs(Mathf.Repeat(u, 1f) - 0.5f), gy = Mathf.Abs(Mathf.Repeat(v, 1f) - 0.5f);
                float grout = Mathf.Clamp01((Mathf.Max(gx * 1f, gy * 2f * 0.5f) - 0.47f) * 40f);
                float tile = Hash(Mathf.FloorToInt(u), Mathf.FloorToInt(v)), n = Fbm(x * 40f, y * 40f);
                var c = Color.Lerp(new Color(0.72f, 0.6f, 0.46f), new Color(0.86f, 0.76f, 0.62f), tile * 0.6f + n * 0.4f);
                return Color.Lerp(c, new Color(0.42f, 0.36f, 0.3f), grout); });
            var wallT = Tex("Plaster", 512, (x, y) => { float n = Fbm(x * 12f, y * 12f); return Color.Lerp(new Color(0.86f, 0.8f, 0.72f), new Color(0.95f, 0.91f, 0.84f), n); });

            var gShader = Shader.Find("GameTech/PlanarShadowGround");
            Material GroundMat(string n) { var m = new Material(gShader); m.SetTexture("_BaseMap", paveT); m.SetTextureScale("_BaseMap", new Vector2(11.7f, 11.7f)); m.SetFloat("_Smoothness", 0.25f); AssetDatabase.CreateAsset(m, Dir + n + ".mat"); return m; }
            var gA = GroundMat("GroundA"); var gB = GroundMat("GroundB");
            var wallM = SceneKit.Mat(Dir, "Wall", "Universal Render Pipeline/Lit", Color.white, 0, 0.1f); wallM.SetTexture("_BaseMap", wallT);
            var woodM = SceneKit.Mat(Dir, "Wood", "Universal Render Pipeline/Lit", new Color(0.45f, 0.28f, 0.16f), 0, 0.35f);
            var paintM = SceneKit.Mat(Dir, "Paint", "Universal Render Pipeline/Lit", new Color(0.92f, 0.9f, 0.86f), 0, 0.4f);
            var ironM = SceneKit.Mat(Dir, "Iron", "Universal Render Pipeline/Lit", new Color(0.12f, 0.13f, 0.14f), 0.7f, 0.5f);
            var ballM = SceneKit.Mat(Dir, "Ball", "Universal Render Pipeline/Lit", new Color(0.85f, 0.12f, 0.08f), 0, 0.6f);

            var cube = Prim(PrimitiveType.Cube); var cyl = Prim(PrimitiveType.Cylinder); var sph = Prim(PrimitiveType.Sphere); var plane = Prim(PrimitiveType.Plane); var quad = Prim(PrimitiveType.Quad);
            var world = new GameObject("Courtyard").transform;
            Obj("Ground A", plane, gA, Vector3.zero, new Vector3(7f, 1f, 7f), world, 8).GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Obj("Ground B", plane, gB, Vector3.zero, new Vector3(7f, 1f, 7f), world, 9).GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var casters = new GameObject("Casters").transform;
            var ch = CharacterKit.Spawn("Universal Render Pipeline/Lit", Dir);
            ch.transform.SetParent(casters, true); ch.transform.position = new Vector3(-0.3f, 0f, 0.55f);
            // stool: round seat, three thin legs
            var stool = new GameObject("Stool").transform; stool.SetParent(casters, false); stool.localPosition = new Vector3(-1.05f, 0, -0.5f);
            Obj("Seat", cyl, woodM, new Vector3(0, 0.5f, 0), new Vector3(0.38f, 0.02f, 0.38f), stool);
            for (int i = 0; i < 3; i++) { float a = i * 120f * Mathf.Deg2Rad; Obj("Leg", cyl, woodM, new Vector3(Mathf.Cos(a) * 0.13f, 0.245f, Mathf.Sin(a) * 0.13f), new Vector3(0.03f, 0.245f, 0.03f), stool); }
            // picket fence: thin slats with gaps, two rails
            var fence = new GameObject("Fence").transform; fence.SetParent(casters, false); fence.localPosition = new Vector3(1.35f, 0, 1.45f);
            for (int i = 0; i < 11; i++) Obj("Slat", cube, paintM, new Vector3(-0.9f + i * 0.18f, 0.42f, 0), new Vector3(0.07f, 0.84f, 0.025f), fence);
            Obj("Rail", cube, paintM, new Vector3(0, 0.25f, 0.03f), new Vector3(1.95f, 0.06f, 0.03f), fence);
            Obj("Rail", cube, paintM, new Vector3(0, 0.66f, 0.03f), new Vector3(1.95f, 0.06f, 0.03f), fence);
            // lamp post
            var lamp = new GameObject("Lamp post").transform; lamp.SetParent(casters, false); lamp.localPosition = new Vector3(-2.2f, 0, 1.8f);
            Obj("Pole", cyl, ironM, new Vector3(0, 1.1f, 0), new Vector3(0.07f, 1.1f, 0.07f), lamp);
            Obj("Arm", cube, ironM, new Vector3(0.22f, 2.1f, 0), new Vector3(0.45f, 0.04f, 0.04f), lamp);
            Obj("Head", cube, ironM, new Vector3(0.42f, 1.98f, 0), new Vector3(0.16f, 0.22f, 0.16f), lamp);
            var ball = Obj("Ball", sph, ballM, new Vector3(0.7f, 0.16f, -1.2f), Vector3.one * 0.32f, casters);
            // a row of trees far back: they cast into both shadow paths too
            var treeM = SceneKit.Mat(Dir, "Tree", "Universal Render Pipeline/Lit", new Color(0.26f, 0.42f, 0.2f), 0, 0.15f);
            var capsule = Prim(PrimitiveType.Capsule);
            for (int i = 0; i < 9; i++)
            {
                float x = -9f + i * 2.25f + (Hash(i, 3) - 0.5f) * 0.8f, hgt = 2.4f + Hash(i, 7) * 1.4f;
                Obj("Tree", capsule, treeM, new Vector3(x, hgt * 0.5f, 9f + Hash(i, 11) * 2f), new Vector3(0.9f, hgt * 0.5f, 0.9f), casters);
            }

            var sun = SceneKit.Sun("Sun", new Vector3(52f, 128f, 0f), new Color(1f, 0.93f, 0.82f), 2.4f, true);
            sun.shadowNormalBias = 0.4f;
            SceneKit.Grade(Dir, 0.25f, 0.05f, 10f, 8f, 0.25f, 0.06f);

            var dir = new GameObject("Director").AddComponent<PlanarShadowDirector>();
            dir.sun = sun; dir.casterRoot = casters; dir.figure = ch.transform; dir.ball = ball.transform; dir.groundA = gA; dir.groundB = gB;
            dir.caster = new Material(Shader.Find("GameTech/PlanarShadowCaster")); AssetDatabase.CreateAsset(dir.caster, Dir + "Caster.mat");
            var sky = new Color(0.52f, 0.7f, 0.93f);
            dir.camA = SceneKit.Cam("Cam A", new Rect(0, 0, 1, 1), 38f, sky, -5, false);
            dir.camB = SceneKit.Cam("Cam B", new Rect(0, 0, 1, 1), 38f, sky, -6, false);
            dir.camA.cullingMask = ~((1 << 9) | (1 << 10)); dir.camB.cullingMask = ~((1 << 8) | (1 << 10));
            foreach (var c in new[] { dir.camA, dir.camB }) { c.nearClipPlane = 0.05f; c.farClipPlane = 80f; }

            var disp = new Material(Shader.Find("GameTech/PlanarComposite")); AssetDatabase.CreateAsset(disp, Dir + "Display.mat"); dir.display = disp;
            var dcam = SceneKit.Cam("Display cam", new Rect(0, 0, 1, 1), 60f, Color.black, 0, true);
            dcam.orthographic = true; dcam.orthographicSize = 0.5f; dcam.cullingMask = 1 << 10; dcam.transform.position = new Vector3(500f, 0f, -1f);
            dcam.nearClipPlane = 0.01f; dcam.farClipPlane = 5f;
            var dq = Obj("Display quad", quad, disp, new Vector3(500f, 0f, 0f), new Vector3(16f / 9f, 1f, 1f), null, 10);
            dq.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            dir.title = LabelSpec.Create("", 0.012f, 0.93f, 40);
            dir.l1 = LabelSpec.Create("", 0.012f, 0.058f, 34); dir.l2 = LabelSpec.Create("", 0.012f, 0.018f, 30);
            dir.m1 = LabelSpec.Create("", 0.512f, 0.058f, 34); dir.m2 = LabelSpec.Create("", 0.512f, 0.018f, 30);
            AssetDatabase.SaveAssets();
            Debug.Log($"[gt] built: ground {gShader.isSupported}, caster {dir.caster.shader.isSupported}, composite {disp.shader.isSupported}");
        }
    }
}
