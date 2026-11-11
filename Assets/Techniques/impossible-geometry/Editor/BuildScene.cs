// 72 impossible geometry: one pavilion with three doors. Three complete room sets sit in the same 4.5 m interior.
// Each door holds a portal quad that writes a stencil ID; each set is drawn only where the stencil equals its ID.
using UnityEditor;
using UnityEngine;

namespace GameTech.Posts
{
    public static class PortalScene
    {
        const string Dir = "Assets/Techniques/impossible-geometry/";
        const string Lit = "Universal Render Pipeline/Lit";
        static Mesh sphere, cyl;
        static int partCount;

        class Ctx { public Transform root; public int layer, id; public float comp; public Color light; public string tag; public Transform crystal; }

        static Material LM(Ctx c, string name, Color col, float pattern = 0, float gloss = 0.15f, Color? emis = null)
        {
            var m = new Material(Shader.Find("GameTech/StencilLayerLit"));
            m.SetColor("_BaseColor", col); m.SetColor("_Emission", emis ?? Color.black); m.SetColor("_LightColor", c.light);
            m.SetFloat("_Gloss", gloss); m.SetFloat("_Pattern", pattern); m.SetFloat("_StencilID", c.id); m.SetFloat("_StencilComp", c.comp);
            AssetDatabase.CreateAsset(m, Dir + "L" + c.id + c.tag + "_" + name + ".mat");
            return m;
        }

        static GameObject P(Ctx c, string name, Mesh mesh, Material m, Vector3 pos, Vector3? scale = null, Quaternion? rot = null)
        {
            var go = SceneKit.Part(name, mesh, m, pos, rot ?? Quaternion.identity, c.root);
            if (scale.HasValue) go.transform.localScale = scale.Value;
            go.layer = c.layer;
            var r = go.GetComponent<Renderer>(); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            partCount++;
            return go;
        }
        static GameObject PB(Ctx c, string name, MeshKit k, Material m) => P(c, name, k.Build(name), m, Vector3.zero);

        static Vector2[] SphereProfile(int n)
        {
            var p = new Vector2[n + 1];
            for (int i = 0; i <= n; i++) { float a = Mathf.PI * i / n; p[i] = new Vector2(-Mathf.Cos(a) * 0.5f, Mathf.Sin(a) * 0.5f); }
            return p;
        }

        static void Shell(Ctx c, float e, Color wall, Color floor, float pattern, float floorGloss)
        {
            float hw = 2.24f - e;
            PB(c, "Room", new MeshKit().Box(new Vector3(0, 1.70f, 0), new Vector3(hw * 2, 3.36f - 2 * e, hw * 2), true), LM(c, "Wall", wall));
            PB(c, "Floor", new MeshKit().Box(new Vector3(0, 0.03f + e, 0), new Vector3(hw * 2 - 0.01f, 0.02f, hw * 2 - 0.01f)), LM(c, "Floor", floor, pattern, floorGloss));
        }

        // ---- set 1: warm study, seen through the front door (-Z), the viewer looks towards +Z
        static void Study(Ctx c)
        {
            var rng = new System.Random(11); float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float hw = 2.23f;
            Shell(c, 0.01f, new Color(0.80f, 0.66f, 0.48f), new Color(0.52f, 0.32f, 0.16f), 1, 0.35f);
            var darkWood = LM(c, "DarkWood", new Color(0.25f, 0.14f, 0.07f), 0, 0.3f);
            var wood = LM(c, "Wood", new Color(0.40f, 0.23f, 0.11f), 0, 0.4f);
            var k = new MeshKit();
            k.Box(new Vector3(0, 0.5f, hw - 0.03f), new Vector3(hw * 2 - 0.02f, 0.9f, 0.04f));
            k.Box(new Vector3(hw - 0.03f, 0.5f, 0), new Vector3(0.04f, 0.9f, hw * 2 - 0.02f)); k.Box(new Vector3(-hw + 0.03f, 0.5f, 0), new Vector3(0.04f, 0.9f, hw * 2 - 0.02f));
            float sz = hw - 0.22f;                                         // bookshelf on the far wall
            k.Box(new Vector3(-1.95f, 1.45f, sz), new Vector3(0.06f, 2.8f, 0.36f)); k.Box(new Vector3(1.95f, 1.45f, sz), new Vector3(0.06f, 2.8f, 0.36f));
            k.Box(new Vector3(0, 1.45f, sz + 0.16f), new Vector3(3.9f, 2.8f, 0.03f));
            for (int i = 0; i < 6; i++) k.Box(new Vector3(0, 0.08f + i * 0.55f, sz), new Vector3(3.9f, 0.05f, 0.36f));
            PB(c, "Panels and shelves", k, darkWood);
            var books = new[] { new MeshKit(), new MeshKit(), new MeshKit(), new MeshKit(), new MeshKit() };
            for (int i = 0; i < 5; i++)
            {
                float x = -1.88f;
                while (x < 1.82f)
                {
                    float w = R(0.045f, 0.10f), h = R(0.30f, 0.44f);
                    if (R(0, 1) > 0.12f) books[rng.Next(5)].Box(new Vector3(x + w / 2, 0.105f + i * 0.55f + h / 2, sz - 0.03f), new Vector3(w - 0.006f, h, 0.24f));
                    x += w;
                }
            }
            var bookCol = new[] { new Color(0.55f, 0.12f, 0.10f), new Color(0.12f, 0.28f, 0.45f), new Color(0.18f, 0.38f, 0.22f), new Color(0.72f, 0.58f, 0.30f), new Color(0.30f, 0.18f, 0.36f) };
            for (int i = 0; i < 5; i++) PB(c, "Books " + i, books[i], LM(c, "Book" + i, bookCol[i], 0, 0.25f));

            var rug = new MeshKit().Box(new Vector3(0, 0.055f, -0.25f), new Vector3(2.8f, 0.02f, 2.0f));
            PB(c, "Rug", rug, LM(c, "Rug", new Color(0.50f, 0.10f, 0.10f), 0, 0.02f));
            PB(c, "Rug inner", new MeshKit().Box(new Vector3(0, 0.062f, -0.25f), new Vector3(2.3f, 0.02f, 1.5f)), LM(c, "RugIn", new Color(0.66f, 0.48f, 0.22f), 0, 0.02f));

            var d = new MeshKit();
            d.Box(new Vector3(0, 0.76f, 0.55f), new Vector3(1.7f, 0.06f, 0.8f));
            foreach (float sx in new[] { -0.78f, 0.78f }) foreach (float dz in new[] { 0.2f, 0.9f }) d.Box(new Vector3(sx, 0.38f, dz), new Vector3(0.07f, 0.74f, 0.07f));
            d.Box(new Vector3(0.52f, 0.52f, 0.55f), new Vector3(0.46f, 0.42f, 0.7f));
            d.Box(new Vector3(-0.1f, 0.46f, 1.25f), new Vector3(0.5f, 0.06f, 0.5f)); d.Box(new Vector3(-0.1f, 0.85f, 1.48f), new Vector3(0.5f, 0.75f, 0.06f));
            foreach (float sx in new[] { -0.32f, 0.12f }) foreach (float dz in new[] { 1.03f, 1.47f }) d.Box(new Vector3(sx, 0.22f, dz), new Vector3(0.05f, 0.44f, 0.05f));
            d.Box(new Vector3(1.62f, 0.3f, -1.1f), new Vector3(0.5f, 0.6f, 0.5f));
            PB(c, "Desk and chair", d, wood);

            var green = LM(c, "Armchair", new Color(0.16f, 0.34f, 0.24f), 0, 0.1f);
            var ac = new MeshKit();
            ac.Box(new Vector3(-1.5f, 0.24f, -0.9f), new Vector3(0.85f, 0.44f, 0.85f)); ac.Box(new Vector3(-1.86f, 0.72f, -0.9f), new Vector3(0.18f, 0.95f, 0.85f));
            ac.Box(new Vector3(-1.5f, 0.52f, -1.26f), new Vector3(0.8f, 0.3f, 0.14f)); ac.Box(new Vector3(-1.5f, 0.52f, -0.54f), new Vector3(0.8f, 0.3f, 0.14f));
            PB(c, "Armchair", ac, green);

            var brass = LM(c, "Brass", new Color(0.75f, 0.55f, 0.22f), 0, 0.8f);
            P(c, "Lamp base", cyl, brass, new Vector3(-0.55f, 0.79f, 0.5f), new Vector3(0.18f, 0.03f, 0.18f));
            P(c, "Lamp pole", cyl, brass, new Vector3(-0.55f, 0.79f, 0.5f), new Vector3(0.03f, 0.42f, 0.03f));
            P(c, "Lamp shade", sphere, LM(c, "Shade", new Color(0.9f, 0.6f, 0.3f), 0, 0, new Color(1f, 0.62f, 0.28f) * 2.6f), new Vector3(-0.55f, 1.27f, 0.5f), new Vector3(0.3f, 0.2f, 0.3f));
            P(c, "Globe stand", cyl, brass, new Vector3(0.55f, 0.79f, 0.6f), new Vector3(0.05f, 0.14f, 0.05f));
            P(c, "Globe", sphere, LM(c, "Globe", new Color(0.12f, 0.42f, 0.55f), 0, 0.5f), new Vector3(0.55f, 1.07f, 0.6f), Vector3.one * 0.3f);
            P(c, "Plant", sphere, LM(c, "Plant", new Color(0.16f, 0.42f, 0.16f)), new Vector3(1.62f, 0.9f, -1.1f), new Vector3(0.6f, 0.7f, 0.6f));
            P(c, "Pendant cord", cyl, darkWood, new Vector3(0, 2.75f, 0), new Vector3(0.02f, 0.6f, 0.02f));
            P(c, "Pendant", sphere, LM(c, "Pendant", new Color(1f, 0.85f, 0.6f), 0, 0, new Color(1f, 0.8f, 0.5f) * 2.2f), new Vector3(0, 2.7f, 0), Vector3.one * 0.28f);
            var frame = new MeshKit();
            frame.Box(new Vector3(hw - 0.04f, 1.85f, -0.4f), new Vector3(0.05f, 0.8f, 1.1f)); frame.Box(new Vector3(-hw + 0.04f, 1.85f, 0.5f), new Vector3(0.05f, 0.9f, 0.7f));
            PB(c, "Frames", frame, brass);
            var canvas = new MeshKit();
            canvas.Box(new Vector3(hw - 0.07f, 1.85f, -0.4f), new Vector3(0.03f, 0.66f, 0.96f)); canvas.Box(new Vector3(-hw + 0.07f, 1.85f, 0.5f), new Vector3(0.03f, 0.76f, 0.56f));
            PB(c, "Canvases", canvas, LM(c, "Canvas", new Color(0.20f, 0.30f, 0.42f)));
        }

        // ---- set 2: cold stone hall, seen through the left door (-X), the viewer looks towards +X
        static void Hall(Ctx c)
        {
            float hw = 2.22f;
            Shell(c, 0.02f, new Color(0.50f, 0.53f, 0.58f), new Color(0.40f, 0.43f, 0.48f), 2, 0.5f);
            var stone = LM(c, "Stone", new Color(0.62f, 0.64f, 0.68f), 0, 0.2f);
            var darkStone = LM(c, "DarkStone", new Color(0.13f, 0.14f, 0.18f), 0, 0.3f);
            var column = new MeshKit().Lathe(new[] { new Vector2(0, 0), new Vector2(0, 0.34f), new Vector2(0.12f, 0.34f), new Vector2(0.16f, 0.26f), new Vector2(0.22f, 0.21f),
                new Vector2(2.92f, 0.19f), new Vector2(3.0f, 0.26f), new Vector2(3.06f, 0.34f), new Vector2(3.26f, 0.34f), new Vector2(3.26f, 0) }, 24).Build("Column");
            foreach (float x in new[] { -1.45f, 1.45f }) foreach (float z in new[] { -1.45f, 1.45f }) P(c, "Column", column, stone, new Vector3(x, 0.05f, z));
            var k = new MeshKit();
            k.Box(new Vector3(0, 0.13f, 0), new Vector3(1.7f, 0.16f, 1.7f)); k.Box(new Vector3(0, 0.29f, 0), new Vector3(1.2f, 0.16f, 1.2f)); k.Box(new Vector3(0, 0.72f, 0), new Vector3(0.5f, 0.7f, 0.5f));
            foreach (float sz in new[] { -1f, 1f })
            {
                k.Box(new Vector3(0, 0.42f, sz * (hw - 0.3f)), new Vector3(1.9f, 0.1f, 0.45f));
                k.Box(new Vector3(-0.7f, 0.2f, sz * (hw - 0.3f)), new Vector3(0.2f, 0.36f, 0.4f)); k.Box(new Vector3(0.7f, 0.2f, sz * (hw - 0.3f)), new Vector3(0.2f, 0.36f, 0.4f));
            }
            PB(c, "Dais and benches", k, stone);
            PB(c, "Niche", new MeshKit().Box(new Vector3(hw - 0.05f, 1.5f, 0), new Vector3(0.06f, 2.5f, 1.5f)), darkStone);
            var obelisk = new MeshKit().Lathe(new[] { new Vector2(0, 0), new Vector2(0, 0.34f), new Vector2(0.3f, 0.34f), new Vector2(0.3f, 0.22f), new Vector2(1.9f, 0.1f), new Vector2(2.15f, 0) }, 4).Build("Obelisk", 5f);
            P(c, "Obelisk", obelisk, stone, new Vector3(1.75f, 0.05f, 0), null, Quaternion.Euler(0, 45, 0));
            var crystal = new MeshKit().Lathe(new[] { new Vector2(-0.42f, 0), new Vector2(-0.1f, 0.2f), new Vector2(0.1f, 0.2f), new Vector2(0.42f, 0) }, 6).Build("Crystal", 5f);
            c.crystal = P(c, "Crystal", crystal, LM(c, "Crystal", new Color(0.10f, 0.50f, 0.70f), 0, 0.9f, new Color(0.15f, 0.75f, 1f) * 1.6f), new Vector3(0, 1.62f, 0)).transform;
            var flame = LM(c, "Flame", new Color(0.3f, 0.6f, 1f), 0, 0, new Color(0.3f, 0.62f, 1f) * 3f);
            foreach (float z in new[] { -0.8f, 0.8f })
            {
                P(c, "Brazier stand", cyl, darkStone, new Vector3(1.15f, 0.05f, z), new Vector3(0.1f, 0.85f, 0.1f));
                P(c, "Brazier bowl", sphere, darkStone, new Vector3(1.15f, 0.92f, z), new Vector3(0.5f, 0.24f, 0.5f));
                P(c, "Brazier flame", sphere, flame, new Vector3(1.15f, 1.12f, z), new Vector3(0.24f, 0.4f, 0.24f));
            }
            var banner = new MeshKit();
            foreach (float x in new[] { -0.75f, 0.75f }) foreach (float sz in new[] { -1f, 1f }) banner.Box(new Vector3(x, 2.05f, sz * (hw - 0.03f)), new Vector3(0.5f, 1.7f, 0.03f));
            PB(c, "Banners", banner, LM(c, "Banner", new Color(0.10f, 0.18f, 0.48f)));
        }

        // ---- set 3: indoor garden, seen through the right door (+X), the viewer looks towards -X
        static void Garden(Ctx c)
        {
            var rng = new System.Random(33); float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float hw = 2.21f;
            Shell(c, 0.03f, new Color(0.50f, 0.64f, 0.60f), new Color(0.17f, 0.33f, 0.14f), 0, 0.05f);
            var bark = LM(c, "Bark", new Color(0.33f, 0.20f, 0.10f));
            var leafA = LM(c, "LeafA", new Color(0.16f, 0.46f, 0.14f)); var leafB = LM(c, "LeafB", new Color(0.30f, 0.56f, 0.16f));
            var trunk = new MeshKit().Lathe(new[] { new Vector2(0, 0), new Vector2(0, 0.24f), new Vector2(0.3f, 0.15f), new Vector2(1.6f, 0.10f), new Vector2(1.7f, 0) }, 16).Build("Trunk");
            Vector3 tree = new Vector3(-0.7f, 0.06f, 0.45f);
            P(c, "Trunk", trunk, bark, tree);
            for (int i = 0; i < 8; i++)
            {
                float a = i * 0.785f, rr = i == 0 ? 0 : 0.55f;
                P(c, "Foliage", sphere, i % 2 == 0 ? leafA : leafB, tree + new Vector3(Mathf.Cos(a) * rr, 1.95f + R(-0.2f, 0.35f), Mathf.Sin(a) * rr), Vector3.one * R(0.95f, 1.3f));
            }
            var terra = LM(c, "Terracotta", new Color(0.62f, 0.32f, 0.20f));
            PB(c, "Planter", new MeshKit().Box(new Vector3(-hw + 0.32f, 0.3f, 0), new Vector3(0.5f, 0.5f, 3.8f)), terra);
            PB(c, "Hedge", new MeshKit().Box(new Vector3(-hw + 0.32f, 0.82f, 0), new Vector3(0.44f, 0.56f, 3.7f)), leafA);
            var stone = LM(c, "Stone", new Color(0.62f, 0.62f, 0.58f));
            for (int i = 0; i < 5; i++) P(c, "Step", cyl, stone, new Vector3(1.75f - i * 0.55f, 0.05f, -0.15f + 0.22f * Mathf.Sin(i * 1.3f)), new Vector3(0.46f, 0.035f, 0.46f));
            P(c, "Pond", cyl, LM(c, "Water", new Color(0.08f, 0.32f, 0.45f), 0, 0.9f, new Color(0.04f, 0.22f, 0.36f)), new Vector3(0.75f, 0.05f, -1.25f), new Vector3(1.5f, 0.03f, 1.1f));
            for (int i = 0; i < 9; i++)
            {
                float a = i * 0.698f;
                P(c, "Pond stone", sphere, stone, new Vector3(0.75f + Mathf.Cos(a) * 0.8f, 0.09f, -1.25f + Mathf.Sin(a) * 0.6f), new Vector3(R(0.2f, 0.3f), 0.16f, R(0.2f, 0.3f)));
            }
            var pink = LM(c, "Pink", new Color(1f, 0.42f, 0.60f)); var white = LM(c, "White", new Color(0.95f, 0.93f, 0.85f));
            var bushPos = new[] { new Vector3(1.6f, 0.3f, 1.55f), new Vector3(0.6f, 0.28f, 1.75f), new Vector3(-1.4f, 0.3f, -1.5f), new Vector3(1.75f, 0.26f, -1.7f), new Vector3(-0.3f, 0.25f, -1.85f) };
            foreach (var b in bushPos)
            {
                float s = R(0.7f, 0.95f);
                P(c, "Bush", sphere, leafB, b, new Vector3(s, s * 0.75f, s));
                for (int f = 0; f < 5; f++)
                {
                    float a = R(0, 6.28f), el = R(0.2f, 1.2f);
                    P(c, "Flower", sphere, f % 2 == 0 ? pink : white, b + new Vector3(Mathf.Cos(a) * Mathf.Cos(el) * s * 0.5f, Mathf.Sin(el) * s * 0.38f, Mathf.Sin(a) * Mathf.Cos(el) * s * 0.5f), Vector3.one * 0.09f);
                }
            }
            var glow = LM(c, "Firefly", new Color(1f, 0.9f, 0.35f), 0, 0, new Color(1f, 0.85f, 0.3f) * 4f);
            for (int i = 0; i < 16; i++) P(c, "Firefly", sphere, glow, new Vector3(R(-1.7f, 1.8f), R(0.5f, 2.6f), R(-1.8f, 1.8f)), Vector3.one * 0.05f);
            var lantern = LM(c, "Lantern", new Color(1f, 0.8f, 0.5f), 0, 0, new Color(1f, 0.72f, 0.4f) * 2.4f);
            foreach (var p in new[] { new Vector3(1.1f, 2.5f, 0.9f), new Vector3(0.9f, 2.3f, -0.8f), new Vector3(-0.2f, 2.7f, -1.3f) })
            {
                P(c, "Lantern cord", cyl, bark, p, new Vector3(0.015f, 3.34f - p.y, 0.015f));
                P(c, "Lantern", sphere, lantern, p, new Vector3(0.22f, 0.28f, 0.22f));
            }
        }

        static GameObject Sets(string tag, float comp, int layer, PortalDirector dr, bool on)
        {
            var root = new GameObject("Sets " + tag);
            Ctx Make(int id, Color light) { var r = new GameObject("Set " + id + " " + tag); r.transform.SetParent(root.transform, false); return new Ctx { root = r.transform, layer = layer, id = id, comp = comp, light = light, tag = tag }; }
            var c1 = Make(1, new Color(1.0f, 0.82f, 0.60f)); Study(c1);
            var c2 = Make(2, new Color(0.62f, 0.78f, 1.0f)); Hall(c2);
            var c3 = Make(3, new Color(0.92f, 1.0f, 0.84f)); Garden(c3);
            if (on) { dr.set1 = c1.root.gameObject; dr.crystalOn = c2.crystal; } else dr.crystalOff = c2.crystal;
            return root;
        }

        public static void Build()
        {
            partCount = 0;
            sphere = new MeshKit().Lathe(SphereProfile(14), 20).Build("UnitSphere", 60f);
            cyl = new MeshKit().Lathe(new[] { new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0) }, 24).Build("UnitCyl");

            SceneKit.Ambient(new Color(0.40f, 0.44f, 0.60f), new Color(0.30f, 0.28f, 0.32f), new Color(0.12f, 0.10f, 0.10f));
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.40f, 0.38f, 0.46f); RenderSettings.fogDensity = 0.010f;
            SceneKit.Shadows(45f);
            var sun = SceneKit.Sun("Sun", new Vector3(19, 48, 0), new Color(1f, 0.84f, 0.70f), 1.9f, true);
            var sky = new Material(Shader.Find("Skybox/Procedural"));
            sky.SetFloat("_SunSize", 0.05f); sky.SetFloat("_AtmosphereThickness", 1.05f); sky.SetFloat("_Exposure", 1.1f);
            sky.SetColor("_SkyTint", new Color(0.55f, 0.5f, 0.6f)); sky.SetColor("_GroundColor", new Color(0.25f, 0.22f, 0.22f));
            AssetDatabase.CreateAsset(sky, Dir + "Sky.mat");
            RenderSettings.skybox = sky; RenderSettings.sun = sun;

            var plaster = SceneKit.Mat(Dir, "Plaster", Lit, new Color(0.60f, 0.60f, 0.62f), 0f, 0.2f);
            var trim = SceneKit.Mat(Dir, "Trim", Lit, new Color(0.17f, 0.17f, 0.20f), 0.2f, 0.45f);
            var roofM = SceneKit.Mat(Dir, "Roof", Lit, new Color(0.30f, 0.17f, 0.14f), 0f, 0.3f);
            var paving = SceneKit.Mat(Dir, "Paving", Lit, new Color(0.36f, 0.33f, 0.31f), 0f, 0.3f);
            var stoneM = SceneKit.Mat(Dir, "PlazaStone", Lit, new Color(0.55f, 0.52f, 0.48f), 0f, 0.2f);
            var glowM = new Material(Shader.Find("GameTech/Glow")); glowM.SetColor("_BaseColor", new Color(1f, 0.75f, 0.45f) * 4f);
            AssetDatabase.CreateAsset(glowM, Dir + "LampGlow.mat");

            // ---- pavilion shell: 5 x 3.4 x 5 m, walls 0.25 m, doors 1.8 x 2.4 m on -Z, -X and +X
            float H = 2.5f, T = 0.25f;
            var w = new MeshKit();
            w.Box(new Vector3(0, 1.7f, H - T / 2), new Vector3(5, 3.4f, T));
            foreach (float s in new[] { -1f, 1f })
            {
                w.Box(new Vector3(s * 1.7f, 1.7f, -H + T / 2), new Vector3(1.6f, 3.4f, T));
                w.Box(new Vector3(-H + T / 2, 1.7f, s * 1.7f), new Vector3(T, 3.4f, 1.6f));
                w.Box(new Vector3(H - T / 2, 1.7f, s * 1.7f), new Vector3(T, 3.4f, 1.6f));
            }
            w.Box(new Vector3(0, 2.9f, -H + T / 2), new Vector3(1.8f, 1.0f, T));
            w.Box(new Vector3(-H + T / 2, 2.9f, 0), new Vector3(T, 1.0f, 1.8f)); w.Box(new Vector3(H - T / 2, 2.9f, 0), new Vector3(T, 1.0f, 1.8f));
            SceneKit.Part("Pavilion walls", w.Build("Walls"), plaster, Vector3.zero, Quaternion.identity);
            var roof = new MeshKit();
            roof.Box(new Vector3(0, 3.55f, 0), new Vector3(5.7f, 0.3f, 5.7f)); roof.Box(new Vector3(0, 3.8f, 0), new Vector3(4.7f, 0.2f, 4.7f));
            SceneKit.Part("Roof", roof.Build("Roof"), roofM, Vector3.zero, Quaternion.identity);
            var tr = new MeshKit();
            void DoorTrim(Vector3 n)        // n = outward direction of the wall
            {
                Vector3 side = new Vector3(-n.z, 0, n.x), o = n * (H + 0.03f);
                Vector3 thick = new Vector3(Mathf.Abs(n.x), 0, Mathf.Abs(n.z)) * 0.08f, wide = new Vector3(Mathf.Abs(side.x), 0, Mathf.Abs(side.z));
                tr.Box(o + side * 0.96f + Vector3.up * 1.2f, thick + wide * 0.12f + Vector3.up * 2.4f);
                tr.Box(o - side * 0.96f + Vector3.up * 1.2f, thick + wide * 0.12f + Vector3.up * 2.4f);
                tr.Box(o + Vector3.up * 2.46f, thick + wide * 2.04f + Vector3.up * 0.12f);
                tr.Box(n * (H + 0.35f) + Vector3.up * 0.04f, new Vector3(Mathf.Abs(n.x), 0, Mathf.Abs(n.z)) * 0.7f + wide * 2.3f + Vector3.up * 0.08f);
            }
            DoorTrim(Vector3.back); DoorTrim(Vector3.left); DoorTrim(Vector3.right);
            foreach (float px in new[] { -1f, 1f }) foreach (float pz in new[] { -1f, 1f })
            {
                tr.Box(new Vector3(px * (H + 0.02f), 1.7f, pz * (H + 0.02f)), new Vector3(0.42f, 3.4f, 0.42f));
                tr.Box(new Vector3(px * (H + 0.02f), 0.25f, pz * (H + 0.02f)), new Vector3(0.56f, 0.5f, 0.56f));
            }
            tr.Box(new Vector3(0, 3.33f, 0), new Vector3(5.34f, 0.16f, 5.34f));
            SceneKit.Part("Door trim", tr.Build("Trim"), trim, Vector3.zero, Quaternion.identity);

            // ---- plaza
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Plaza"; ground.transform.localScale = Vector3.one * 12; ground.GetComponent<Renderer>().sharedMaterial = paving;
            var deco = new MeshKit();
            for (int i = 0; i < 14; i++)
            {
                float a = i * Mathf.PI * 2 / 14 + 0.22f; float hgt = 2.2f + 1.4f * Mathf.Abs(Mathf.Sin(i * 2.1f));
                deco.Box(new Vector3(Mathf.Sin(a) * 15f, hgt / 2, Mathf.Cos(a) * 15f), new Vector3(0.9f, hgt, 0.9f));
            }
            foreach (float sx in new[] { -1f, 1f }) foreach (float sz in new[] { -1f, 1f }) deco.Box(new Vector3(sx * 4.3f, 0.25f, sz * 4.3f), new Vector3(1.1f, 0.5f, 1.1f));
            SceneKit.Part("Plaza stones", deco.Build("Deco"), stoneM, Vector3.zero, Quaternion.identity);
            var lan = new MeshKit(); var bush = SceneKit.Mat(Dir, "Bush", Lit, new Color(0.16f, 0.30f, 0.14f), 0f, 0.1f);
            foreach (var n in new[] { Vector3.back, Vector3.left, Vector3.right })
                foreach (float sd in new[] { -1f, 1f })
                {
                    Vector3 side = new Vector3(-n.z, 0, n.x), p = n * (H + 0.1f) + side * (sd * 1.32f) + Vector3.up * 2.05f;
                    lan.Box(p, new Vector3(0.16f, 0.32f, 0.16f));
                    var l = new GameObject("Lantern light").AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.74f, 0.46f); l.intensity = 2.4f; l.range = 6f;
                    l.transform.position = p + n * 0.7f;
                }
            var lanGo = SceneKit.Part("Lanterns", lan.Build("Lanterns"), glowM, Vector3.zero, Quaternion.identity);
            lanGo.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            foreach (float sx in new[] { -1f, 1f }) foreach (float sz in new[] { 1f })
            {
                var b0 = SceneKit.Part("Bush", sphere, bush, new Vector3(sx * 4.3f, 0.75f, sz * 4.3f), Quaternion.identity); b0.transform.localScale = new Vector3(1.0f, 0.8f, 1.0f);
            }
            SceneKit.Part("Paving inset", new MeshKit().Box(new Vector3(0, 0.004f, 0), new Vector3(10.5f, 0.01f, 10.5f)).Build("Inset"),
                SceneKit.Mat(Dir, "Inset", Lit, new Color(0.46f, 0.44f, 0.42f), 0f, 0.35f), Vector3.zero, Quaternion.identity);

            // ---- portal quads (Unity's quad faces its local -Z, so -Z must point outwards)
            void Portal(int id, Vector3 pos, float yaw)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "Portal " + id; Object.DestroyImmediate(q.GetComponent<Collider>());
                q.transform.position = pos; q.transform.rotation = Quaternion.Euler(0, yaw, 0); q.transform.localScale = new Vector3(1.8f, 2.4f, 1);
                var m = new Material(Shader.Find("GameTech/StencilPortal")); m.SetFloat("_StencilID", id); AssetDatabase.CreateAsset(m, Dir + "Portal" + id + ".mat");
                var r = q.GetComponent<Renderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            Portal(1, new Vector3(0, 1.2f, -H), 0); Portal(2, new Vector3(-H, 1.2f, 0), 90); Portal(3, new Vector3(H, 1.2f, 0), -90);

            var dr = new GameObject("Director").AddComponent<PortalDirector>();
            Sets("ON", 3f, 8, dr, true);        // CompareFunction.Equal
            Sets("OFF", 8f, 9, dr, false);      // CompareFunction.Always
            int perCopy = partCount / 2;

            // ---- debug tint quads
            var tintCol = new[] { new Color(1f, 0.15f, 0.6f, 0.5f), new Color(0.1f, 0.9f, 1f, 0.5f), new Color(1f, 0.85f, 0.1f, 0.5f) };
            dr.tints = new GameObject[3];
            for (int i = 0; i < 3; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "Stencil tint " + (i + 1); Object.DestroyImmediate(q.GetComponent<Collider>());
                var qm = Object.Instantiate(q.GetComponent<MeshFilter>().sharedMesh); qm.name = "TintQuad"; qm.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
                q.GetComponent<MeshFilter>().sharedMesh = qm;
                var m = new Material(Shader.Find("GameTech/StencilTint")); m.SetFloat("_StencilID", i + 1); m.SetColor("_Color", tintCol[i]); AssetDatabase.CreateAsset(m, Dir + "Tint" + (i + 1) + ".mat");
                var r = q.GetComponent<Renderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                dr.tints[i] = q;
            }

            var bg = new Color(0.3f, 0.25f, 0.3f);
            dr.camL = SceneKit.Cam("Camera OFF", new Rect(0, 0, 0.5f, 1), 44, bg, 0); dr.camL.cullingMask = ~(1 << 8);
            dr.camR = SceneKit.Cam("Camera ON", new Rect(0.5f, 0, 0.5f, 1), 44, bg, 1); dr.camR.cullingMask = ~(1 << 9);
            dr.camFull = SceneKit.Cam("Camera full", new Rect(0, 0, 1, 1), 48, bg, 2); dr.camFull.cullingMask = ~(1 << 9); dr.camFull.tag = "MainCamera";
            foreach (var cam in new[] { dr.camL, dr.camR, dr.camFull }) cam.clearFlags = CameraClearFlags.Skybox;

            SceneKit.Grade(Dir, 0.45f, 0.1f, 10f, 6f, 0.28f, 0.18f);

            LabelSpec.Create("OFF: no stencil test, all 3 sets drawn", 0.03f, 0.05f).During(2.0f, 8f);
            LabelSpec.Create("ON: each door shows its own set", 0.53f, 0.05f).During(2.0f, 8f);
            LabelSpec.Create("3 doors, 3 stencil IDs, 3 sets of furniture in one 4.5 m room", 0.03f, 0.05f).During(8.3f, 17f);
            LabelSpec.Create("Stencil buffer: door quads write ID 1 (pink) and ID 2 (cyan)", 0.03f, 0.05f).During(17f, 19.5f);
            LabelSpec.Create("Set 1 switched off: its door shows the empty shell", 0.03f, 0.05f).During(19.5f, 21.7f);
            LabelSpec.Create("Set 1 back on: drawn only where stencil = 1", 0.03f, 0.05f).During(21.7f, 24.1f);
            Debug.Log($"[gt] portal scene: {perCopy} parts per copy of the 3 sets");
        }
    }
}
