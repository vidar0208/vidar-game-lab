// 89 per-object shadows: a tiled courtyard with static props (columns, benches, planters, a low wall) and four
// movers: the stand-in mannequin (no character model) walking a loop, a bouncing ball, a hovering drone and a
// lantern swinging from a static bracket. Everything is built from code; no external assets.
using UnityEditor;
using UnityEngine;

namespace GameTech.Posts
{
    public static class POSScene
    {
        const string Dir = "Assets/Techniques/per-object-shadows/";
        const string Lit = "Universal Render Pipeline/Lit";

        static Material Mat(string name, Color c, float smooth = 0.3f, float metal = 0f) => SceneKit.Mat(Dir, name, Lit, c, metal, smooth);

        static GameObject Prim(PrimitiveType type, string name, Material m, Vector3 pos, Vector3 scale, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = m;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = scale;
            return go;
        }

        static Material Resolve(string name)
        {
            var m = new Material(Shader.Find("Hidden/GT/PerObjectShadowResolve")) { enableInstancing = true };
            m.EnableKeyword("_SOFTSHADOW_ON");   // the shader's Toggle defaults to 1, but a material made from code has no keyword yet
            m.SetFloat("_SoftShadow", 1);
            m.SetFloat("_Cull", 1);              // GT: draw back faces only, once per pixel (the default Cull Off draws both faces and blends twice)
            // GT: the shader's defaults are 10 / 5 with the comments "SrcAlpha" / "OneMinusSrcAlpha", but in Unity's BlendMode
            // 10 is OneMinusSrcAlpha and 5 is SrcAlpha; set what the comments describe
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            AssetDatabase.CreateAsset(m, Dir + name + ".mat");
            return m;
        }

        public static void Build()
        {
            SceneKit.Ambient(new Color(0.42f, 0.46f, 0.58f), new Color(0.34f, 0.32f, 0.33f), new Color(0.14f, 0.12f, 0.11f));
            SceneKit.Shadows(50f);
            var sun = SceneKit.Sun("Sun", new Vector3(50, -30, 0), new Color(1f, 0.9f, 0.78f), 1.6f, true);
            RenderSettings.sun = sun;
            var sky = new Material(Shader.Find("Skybox/Procedural"));
            sky.SetFloat("_SunSize", 0.03f); sky.SetFloat("_AtmosphereThickness", 0.9f); sky.SetFloat("_Exposure", 1.0f);
            sky.SetColor("_SkyTint", new Color(0.5f, 0.55f, 0.65f)); sky.SetColor("_GroundColor", new Color(0.3f, 0.28f, 0.27f));
            AssetDatabase.CreateAsset(sky, Dir + "Sky.mat");
            RenderSettings.skybox = sky;

            // stone tiles: 2 x 2 tiles with grout per texture repeat, 1 m per tile
            const int T = 128;
            var tex = new Texture2D(T, T, TextureFormat.RGBA32, true) { name = "Tiles", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            for (int y = 0; y < T; y++)
                for (int x = 0; x < T; x++)
                {
                    int tx = x / 64, ty = y / 64;
                    float g = 0.62f + 0.05f * ((tx + ty) % 2) + 0.04f * Mathf.PerlinNoise(x * 0.15f + tx * 7, y * 0.15f + ty * 3);
                    bool grout = (x % 64) < 2 || (y % 64) < 2;
                    var c = grout ? new Color(0.36f, 0.34f, 0.32f) : new Color(g, g * 0.97f, g * 0.92f);
                    tex.SetPixel(x, y, c);
                }
            tex.Apply(true);
            AssetDatabase.CreateAsset(tex, Dir + "Tiles.asset");
            var ground = Mat("Ground", Color.white, 0.2f); ground.SetTexture("_BaseMap", tex); ground.SetTextureScale("_BaseMap", new Vector2(14, 14));
            EditorUtility.SetDirty(ground);
            Prim(PrimitiveType.Plane, "Ground", ground, new Vector3(0, 0, 0), new Vector3(2.8f, 1f, 2.8f));

            // static props (URP shadows in both bands)
            var stone = Mat("Stone", new Color(0.78f, 0.74f, 0.68f), 0.25f);
            var wood = Mat("Wood", new Color(0.45f, 0.30f, 0.2f), 0.35f);
            var leaf = Mat("Leaf", new Color(0.25f, 0.42f, 0.22f), 0.2f);
            foreach (var p in new[] { new Vector3(-4.5f, 0, -3.5f), new Vector3(4.5f, 0, -3.5f), new Vector3(-4.5f, 0, 4.5f), new Vector3(4.5f, 0, 4.5f) })
            {
                Prim(PrimitiveType.Cylinder, "Column", stone, p + new Vector3(0, 1.6f, 0), new Vector3(0.5f, 1.6f, 0.5f));
                Prim(PrimitiveType.Cube, "Column base", stone, p + new Vector3(0, 0.1f, 0), new Vector3(0.75f, 0.2f, 0.75f));
            }
            Prim(PrimitiveType.Cube, "Wall", stone, new Vector3(0, 0.55f, 6.2f), new Vector3(11f, 1.1f, 0.4f));
            Prim(PrimitiveType.Cube, "Wall", stone, new Vector3(-6.2f, 0.55f, 1f), new Vector3(0.4f, 1.1f, 9f));
            foreach (var p in new[] { new Vector3(-2.2f, 0, 4.2f), new Vector3(2.4f, 0, 4.2f) })
            {
                Prim(PrimitiveType.Cube, "Bench seat", wood, p + new Vector3(0, 0.45f, 0), new Vector3(1.8f, 0.08f, 0.45f));
                Prim(PrimitiveType.Cube, "Bench leg", stone, p + new Vector3(-0.7f, 0.21f, 0), new Vector3(0.12f, 0.42f, 0.4f));
                Prim(PrimitiveType.Cube, "Bench leg", stone, p + new Vector3(0.7f, 0.21f, 0), new Vector3(0.12f, 0.42f, 0.4f));
            }
            foreach (var p in new[] { new Vector3(-4.6f, 0, 1.2f), new Vector3(4.8f, 0, 1.4f) })
            {
                Prim(PrimitiveType.Cube, "Planter", stone, p + new Vector3(0, 0.3f, 0), new Vector3(0.9f, 0.6f, 0.9f));
                Prim(PrimitiveType.Sphere, "Bush", leaf, p + new Vector3(0, 0.95f, 0), new Vector3(0.95f, 0.8f, 0.95f));
            }
            // lantern bracket (static) at the back left
            var bracket = Mat("Iron", new Color(0.12f, 0.12f, 0.13f), 0.5f, 0.6f);
            Prim(PrimitiveType.Cube, "Lantern post", bracket, new Vector3(-1.6f, 1.4f, 3.2f), new Vector3(0.12f, 2.8f, 0.12f));
            Prim(PrimitiveType.Cube, "Lantern arm", bracket, new Vector3(-1.2f, 2.75f, 3.2f), new Vector3(0.9f, 0.08f, 0.08f));

            // movers
            var man = Mannequin.Build(Lit, Dir, (m, part) =>
            {
                m.SetColor("_BaseColor", part == "skin" ? new Color(0.86f, 0.72f, 0.62f) : new Color(0.62f, 0.2f, 0.16f));
                m.SetFloat("_Smoothness", 0.35f);
            });
            man.name = "Character";
            var ball = Prim(PrimitiveType.Sphere, "Ball", Mat("Ball", new Color(0.15f, 0.45f, 0.85f), 0.6f), new Vector3(-3f, 0.25f, -1.2f), Vector3.one * 0.5f);
            var drone = new GameObject("Drone");
            var dm = Mat("Drone", new Color(0.9f, 0.9f, 0.88f), 0.5f);
            var dark = Mat("Rotor", new Color(0.1f, 0.1f, 0.1f), 0.4f);
            Prim(PrimitiveType.Cube, "Body", dm, Vector3.zero, new Vector3(0.36f, 0.1f, 0.36f), drone.transform);
            for (int i = 0; i < 4; i++)
            {
                float a = (45f + 90f * i) * Mathf.Deg2Rad;
                var arm = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.32f;
                var armGo = Prim(PrimitiveType.Cube, "Arm", dm, arm * 0.5f, new Vector3(0.05f, 0.04f, 0.34f), drone.transform);
                armGo.transform.localRotation = Quaternion.LookRotation(arm);
                var rotor = Prim(PrimitiveType.Cylinder, "Rotor", dark, arm + new Vector3(0, 0.06f, 0), new Vector3(0.28f, 0.006f, 0.28f), drone.transform);
                rotor.name = "Rotor" + i;
                var blade = Prim(PrimitiveType.Cube, "Blade", dark, new Vector3(0, 1f, 0), new Vector3(0.95f, 1f, 0.12f), rotor.transform);
            }
            var lantern = new GameObject("Lantern");
            lantern.transform.position = new Vector3(-0.85f, 2.72f, 3.2f);
            var glass = Mat("Lantern glass", new Color(1f, 0.85f, 0.55f), 0.8f);
            Prim(PrimitiveType.Cylinder, "Chain", bracket, new Vector3(0, -0.3f, 0), new Vector3(0.025f, 0.3f, 0.025f), lantern.transform);
            Prim(PrimitiveType.Cube, "Lantern top", bracket, new Vector3(0, -0.62f, 0), new Vector3(0.3f, 0.06f, 0.3f), lantern.transform);
            Prim(PrimitiveType.Cube, "Lantern body", glass, new Vector3(0, -0.82f, 0), new Vector3(0.22f, 0.34f, 0.22f), lantern.transform);
            Prim(PrimitiveType.Cube, "Lantern base", bracket, new Vector3(0, -1.01f, 0), new Vector3(0.28f, 0.05f, 0.28f), lantern.transform);

            var srcCam = SceneKit.Cam("Scene camera", new Rect(0, 0, 1, 1), 42, Color.black, -10);
            srcCam.clearFlags = CameraClearFlags.Skybox; srcCam.cullingMask = ~(1 << 8); srcCam.farClipPlane = 200f; srcCam.nearClipPlane = 0.1f;
            SceneKit.Grade(Dir, 0.2f, 0f, 8f, 5f, 0.2f, 0f);
            var display = SceneKit.Cam("Display camera", new Rect(0, 0, 1, 1), 40, Color.black, 10, false);
            display.cullingMask = 1 << 8; display.transform.position = new Vector3(0, -50, 0); display.tag = "MainCamera";

            var compose = new Material(Shader.Find("Hidden/GT/POSCompose")); AssetDatabase.CreateAsset(compose, Dir + "Compose.mat");
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Display quad"; Object.DestroyImmediate(quad.GetComponent<Collider>()); quad.layer = 8;
            var qm = Object.Instantiate(quad.GetComponent<MeshFilter>().sharedMesh); qm.name = "DisplayQuad"; qm.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
            AssetDatabase.CreateAsset(qm, Dir + "DisplayQuad.asset");
            quad.GetComponent<MeshFilter>().sharedMesh = qm; quad.GetComponent<Renderer>().sharedMaterial = compose;
            quad.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            quad.transform.position = display.transform.position + Vector3.forward;

            var view = new Material(Shader.Find("Hidden/GT/POSAtlasView")); AssetDatabase.CreateAsset(view, Dir + "AtlasView.mat");
            var dir = new GameObject("Director").AddComponent<POSDirector>();
            dir.src = srcCam; dir.sun = sun; dir.compose = compose; dir.atlasViewMat = view;
            dir.resolveColour = Resolve("ResolveColour"); dir.resolveMask = Resolve("ResolveMask");
            dir.character = man.transform; dir.ball = ball.transform; dir.drone = drone.transform; dir.lantern = lantern.transform;
            dir.title = LabelSpec.Create("", 0.012f, 0.935f, 30);
            dir.l1 = new LabelSpec[2]; dir.l2 = new LabelSpec[2]; dir.slice = new LabelSpec[4];
            for (int i = 0; i < 2; i++) { dir.l1[i] = LabelSpec.Create("", 0.012f, 0.058f, 28); dir.l2[i] = LabelSpec.Create("", 0.012f, 0.018f, 22); }
            for (int i = 0; i < 4; i++) dir.slice[i] = LabelSpec.Create("", 0.1f, 0.5f, 20);
        }
    }
}
