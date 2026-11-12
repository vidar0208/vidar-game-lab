// 80 parallax mapping: a courtyard corner built from code. A brick wall, the stone ground with a hole in it and a
// landscape window in the wall are each ONE flat quad with GameTech/ParallaxMarch. Each of the three quads exists
// twice (layers 8 and 9) with two materials, so a split screen can compare two settings.
// Brick and ground textures: Poly Haven red_brick_03 and rocky_terrain_02 (CC0), in Tex/.
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace GameTech.Posts
{
    public static class ParallaxScene
    {
        const string Dir = "Assets/Techniques/parallax-mapping/";
        const string Lit = "Universal Render Pipeline/Lit";

        // flat quad with UVs in metres and a fixed diagonal from uv (0,0) to (w,h). ground: XZ plane facing up.
        static Mesh Quad(string name, float w, float h, bool ground)
        {
            var m = new Mesh { name = name };
            Vector3 P(float u, float v) => ground ? new Vector3(u - w / 2, 0, h / 2 - v) : new Vector3(u - w / 2, v, 0);
            m.vertices = new[] { P(0, 0), P(w, 0), P(w, h), P(0, h) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(w, 0), new Vector2(w, h), new Vector2(0, h) };
            var n = ground ? Vector3.up : Vector3.forward;
            m.normals = new[] { n, n, n, n };
            var tg = new Vector4(1, 0, 0, 1);
            m.tangents = new[] { tg, tg, tg, tg };
            m.triangles = ground ? new[] { 0, 2, 1, 0, 3, 2 } : new[] { 0, 2, 1, 0, 3, 2 };
            if (Vector3.Dot(Vector3.Cross(m.vertices[2] - m.vertices[0], m.vertices[1] - m.vertices[0]), n) < 0)
                m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateBounds();
            m.bounds = new Bounds(m.bounds.center, m.bounds.size + Vector3.one * 4f);   // the march draws "below" the quad
            AssetDatabase.CreateAsset(m, Dir + name + ".asset");
            return m;
        }

        static Texture2D Tex(string file, bool linear, bool normal)
        {
            string path = Dir + "Tex/" + file;
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = !linear; imp.anisoLevel = 8; imp.maxTextureSize = 2048; imp.wrapMode = TextureWrapMode.Repeat;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material PM(string name, int variant)
        {
            var m = new Material(Shader.Find("GameTech/ParallaxMarch")); m.SetFloat("_Variant", variant);
            AssetDatabase.CreateAsset(m, Dir + name + ".mat");
            return m;
        }

        static void Pair(string name, Mesh mesh, Material l, Material r, Vector3 pos, Quaternion rot)
        {
            var a = SceneKit.Part(name + " L", mesh, l, pos, rot); a.layer = 8;
            var b = SceneKit.Part(name + " R", mesh, r, pos, rot); b.layer = 9;
        }

        public static void Build()
        {
            var brickC = Tex("red_brick_03_diff_2k.jpg", false, false);
            var brickH = Tex("red_brick_03_disp_2k.jpg", true, false);
            var brickN = Tex("red_brick_03_nor_gl_2k.jpg", true, true);
            var rockC = Tex("rocky_terrain_02_diff_2k.jpg", false, false);
            var rockN = Tex("rocky_terrain_02_nor_gl_2k.jpg", true, true);

            SceneKit.Ambient(new Color(0.42f, 0.48f, 0.58f), new Color(0.36f, 0.36f, 0.36f), new Color(0.18f, 0.16f, 0.14f));
            SceneKit.Shadows(25f);
            var sun = SceneKit.Sun("Sun", new Vector3(24, 140, 0), new Color(1f, 0.9f, 0.76f), 2.2f, true);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.62f, 0.68f, 0.76f); RenderSettings.fogDensity = 0.012f;
            var sky = new Material(Shader.Find("Skybox/Procedural"));
            sky.SetFloat("_SunSize", 0.03f); sky.SetFloat("_AtmosphereThickness", 0.9f); sky.SetFloat("_Exposure", 1.1f);
            sky.SetColor("_SkyTint", new Color(0.5f, 0.55f, 0.65f)); sky.SetColor("_GroundColor", new Color(0.35f, 0.33f, 0.3f));
            AssetDatabase.CreateAsset(sky, Dir + "Sky.mat");
            RenderSettings.skybox = sky; RenderSettings.sun = sun;

            // ---- the three parallax quads
            var wallL = PM("WallLeft", 0); var wallR = PM("WallRight", 0);
            foreach (var m in new[] { wallL, wallR })
            {
                m.SetTexture("_ColorTex", brickC); m.SetTexture("_HeightTex", brickH); m.SetTexture("_NormalTex", brickN);
                m.SetFloat("_ColorTiling", 1f); m.SetFloat("_Depth", ParallaxDirector.BrickDepth); m.SetVector("_QuadSize", new Vector4(7, 3, 0, 0));
            }
            Pair("Brick wall", Quad("WallQuad", 7f, 3f, false), wallL, wallR, new Vector3(0, 0, -2f), Quaternion.identity);

            var gL = PM("GroundLeft", 1); var gR = PM("GroundRight", 1);
            // hole centre at world (-1.2, 0, 1.0); ground quad 10 x 10 m centred at (0, 0, 3): uv = (x + 5, 8 - z)
            foreach (var m in new[] { gL, gR })
            {
                m.SetTexture("_ColorTex", rockC); m.SetTexture("_NormalTex", rockN);
                m.SetFloat("_ColorTiling", 0.2f); m.SetFloat("_NoiseTiling", 0.5f); m.SetColor("_Tint", new Color(1.3f, 1.25f, 1.2f));
                m.SetFloat("_HoleDepth", ParallaxDirector.HoleDepth); m.SetFloat("_HoleSize", 4f);
                m.SetVector("_MaskRect", new Vector4(3.8f - 1.3f, 7f - 1.3f, 2.6f, 2.6f)); m.SetVector("_QuadSize", new Vector4(10, 10, 0, 0));
            }
            Pair("Ground", Quad("GroundQuad", 10f, 10f, true), gL, gR, new Vector3(0, 0, 3f), Quaternion.identity);

            var wL = PM("WindowLeft", 2); var wR = PM("WindowRight", 2);
            foreach (var m in new[] { wL, wR })
            {
                m.SetFloat("_Height", ParallaxDirector.MountainHeight); m.SetColor("_WaterColor", new Color(0.30f, 0.52f, 0.66f));
                m.SetVector("_QuadSize", new Vector4(1.6f, 1.0f, 0, 0));
            }
            Pair("Landscape window", Quad("WindowQuad", 1.6f, 1.0f, false), wL, wR, new Vector3(1.7f, 1.05f, -1.975f), Quaternion.identity);

            // ---- frame, coping, pillars, bench (ordinary lit meshes)
            var wood = SceneKit.Mat(Dir, "Wood", Lit, new Color(0.16f, 0.11f, 0.08f), 0f, 0.35f);
            var stone = SceneKit.Mat(Dir, "Stone", Lit, new Color(0.55f, 0.52f, 0.48f), 0f, 0.2f);
            var frame = new MeshKit();
            frame.Box(new Vector3(0, 0.56f, 0), new Vector3(1.84f, 0.12f, 0.12f)); frame.Box(new Vector3(0, -0.56f, 0), new Vector3(1.84f, 0.12f, 0.16f));
            frame.Box(new Vector3(-0.86f, 0, 0), new Vector3(0.12f, 1.0f, 0.12f)); frame.Box(new Vector3(0.86f, 0, 0), new Vector3(0.12f, 1.0f, 0.12f));
            SceneKit.Part("Window frame", frame.Build("Frame", 10f), wood, new Vector3(1.7f, 1.55f, -1.95f), Quaternion.identity);
            SceneKit.Part("Coping", new MeshKit().Box(new Vector3(0, 3.06f, -2.05f), new Vector3(7.4f, 0.12f, 0.4f)).Build("Coping", 10f), stone, Vector3.zero, Quaternion.identity);
            SceneKit.Part("Wall back", new MeshKit().Box(new Vector3(0, 1.5f, -2.18f), new Vector3(7.0f, 3.0f, 0.34f)).Build("WallBack", 10f), stone, Vector3.zero, Quaternion.identity);
            foreach (float x in new[] { -3.7f, 3.7f })
                SceneKit.Part("Pillar", new MeshKit().Box(new Vector3(x, 1.6f, -2.0f), new Vector3(0.45f, 3.2f, 0.5f)).Build("Pillar", 10f), stone, Vector3.zero, Quaternion.identity);
            var bench = new MeshKit();
            bench.Box(new Vector3(0, 0.42f, 0), new Vector3(1.6f, 0.1f, 0.45f)); bench.Box(new Vector3(-0.6f, 0.19f, 0), new Vector3(0.14f, 0.38f, 0.4f)); bench.Box(new Vector3(0.6f, 0.19f, 0), new Vector3(0.14f, 0.38f, 0.4f));
            SceneKit.Part("Bench", bench.Build("Bench", 10f), stone, new Vector3(2.4f, 0, -1.45f), Quaternion.identity);
            var outer = GameObject.CreatePrimitive(PrimitiveType.Plane); Object.DestroyImmediate(outer.GetComponent<Collider>());
            outer.name = "Outer ground"; outer.transform.position = new Vector3(0, -0.01f, 3f); outer.transform.localScale = Vector3.one * 8f;
            outer.GetComponent<Renderer>().sharedMaterial = SceneKit.Mat(Dir, "OuterGround", Lit, new Color(0.34f, 0.31f, 0.27f), 0f, 0.1f);

            var dr = new GameObject("Director").AddComponent<ParallaxDirector>();
            dr.wallL = wallL; dr.wallR = wallR; dr.groundL = gL; dr.groundR = gR; dr.winL = wL; dr.winR = wR;
            var bg = new Color(0.6f, 0.66f, 0.74f);
            dr.camL = SceneKit.Cam("Camera left", new Rect(0, 0, 0.5f, 1), 45, bg, 0); dr.camL.cullingMask = ~(1 << 9);
            dr.camR = SceneKit.Cam("Camera right", new Rect(0.5f, 0, 0.5f, 1), 45, bg, 1); dr.camR.cullingMask = ~(1 << 8);
            dr.camFull = SceneKit.Cam("Camera full", new Rect(0, 0, 1, 1), 45, bg, 2); dr.camFull.cullingMask = ~(1 << 8); dr.camFull.tag = "MainCamera";
            foreach (var cam in new[] { dr.camL, dr.camR, dr.camFull }) cam.clearFlags = CameraClearFlags.Skybox;

            SceneKit.Grade(Dir, 0.25f, 0.0f, 12f, 6f, 0.25f, 0.1f);

            const float LX = 0.03f, RX = 0.53f, Y = 0.05f;
            LabelSpec.Create("OFF: normal map only", LX, Y).During(1.0f, 8f);
            LabelSpec.Create($"ON: ray-marched parallax, {ParallaxDirector.BrickSteps} steps x 1 mm", RX, Y).During(1.0f, 8f);
            LabelSpec.Create($"{ParallaxDirector.FewSteps} steps x 16 cm, no interpolation", LX, Y).During(8.2f, 10.2f);
            LabelSpec.Create($"{ParallaxDirector.FewSteps} steps x 16 cm + interpolate the last two steps", LX, Y).During(10.2f, 12.4f);
            LabelSpec.Create($"{ParallaxDirector.HoleSteps} steps x 5 mm + interpolation", LX, Y).During(12.4f, 14.5f);
            LabelSpec.Create($"A landscape inside one quad: {ParallaxDirector.MountainSteps} steps x 1 cm, 0.97 m deep", LX, Y).During(14.7f, 19f);
            LabelSpec.Create("The geometry: 3 flat quads, 2 triangles each", LX, Y).During(19.2f, 21.6f);
            LabelSpec.Create("The height maps the rays march through", RX, Y).During(19.2f, 21.6f);
            LabelSpec.Create("Hole size animated on the same flat ground quad", LX, Y).During(21.8f, 24.1f);
            Debug.Log("[gt] parallax scene built");
        }
    }
}
