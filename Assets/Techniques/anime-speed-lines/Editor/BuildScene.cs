// 88 anime speed lines: the stand-in mannequin (no character model) runs on a road at dusk; the road texture and two
// rows of posts stream past to sell the speed, and the camera pushes in. No external assets.
using UnityEditor;
using UnityEngine;

namespace GameTech.Posts
{
    public static class SpeedLinesScene
    {
        const string Dir = "Assets/Techniques/anime-speed-lines/";
        const string Lit = "Universal Render Pipeline/Lit";

        static Material Mat(string name, Color c, float smooth = 0.3f) => SceneKit.Mat(Dir, name, Lit, c, 0f, smooth);

        static GameObject Prim(PrimitiveType type, string name, Material m, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = m;
            go.transform.position = pos; go.transform.localScale = scale;
            return go;
        }

        public static void Build()
        {
            SceneKit.Ambient(new Color(0.35f, 0.38f, 0.55f), new Color(0.30f, 0.26f, 0.30f), new Color(0.10f, 0.09f, 0.09f));
            SceneKit.Shadows(30f);
            var sun = SceneKit.Sun("Sun", new Vector3(20, 150, 0), new Color(1f, 0.72f, 0.5f), 1.5f, true);
            RenderSettings.sun = sun;
            var sky = new Material(Shader.Find("Skybox/Procedural"));
            sky.SetFloat("_SunSize", 0.05f); sky.SetFloat("_AtmosphereThickness", 1.5f); sky.SetFloat("_Exposure", 0.4f);
            sky.SetColor("_SkyTint", new Color(0.30f, 0.30f, 0.55f)); sky.SetColor("_GroundColor", new Color(0.15f, 0.13f, 0.14f));
            AssetDatabase.CreateAsset(sky, Dir + "Sky.mat");
            RenderSettings.skybox = sky;

            // road: dark asphalt with dashed centre and solid edge lines, drawn into a small texture, tiled along z
            const int TW = 64, TH = 64;
            var tex = new Texture2D(TW, TH, TextureFormat.RGBA32, true) { name = "Road", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 8 };
            for (int y = 0; y < TH; y++)
                for (int x = 0; x < TW; x++)
                {
                    float g = 0.16f + 0.02f * Mathf.PerlinNoise(x * 0.4f, y * 0.4f);
                    var c = new Color(g, g, g * 1.05f, 1);
                    if ((x >= 31 && x <= 32 && y < 32) || x == 3 || x == 60) c = new Color(0.85f, 0.83f, 0.75f, 1);
                    tex.SetPixel(x, y, c);
                }
            tex.Apply(true);
            AssetDatabase.CreateAsset(tex, Dir + "Road.asset");
            var road = Mat("Road", Color.white, 0.25f); road.SetTexture("_BaseMap", tex); road.SetTextureScale("_BaseMap", new Vector2(1, 20));
            EditorUtility.SetDirty(road);
            Prim(PrimitiveType.Quad, "Road", road, new Vector3(0, 0, -30f), new Vector3(7f, 80f, 1f)).transform.rotation = Quaternion.Euler(90, 0, 0);
            var grass = Mat("Verge", new Color(0.22f, 0.24f, 0.18f), 0.1f);
            Prim(PrimitiveType.Plane, "Verge", grass, new Vector3(0, -0.01f, -30f), new Vector3(20f, 1f, 10f));

            // two rows of posts every 4 m, moved by the director
            var postMat = Mat("Post", new Color(0.85f, 0.83f, 0.78f), 0.4f);
            var capMat = Mat("Post cap", new Color(0.9f, 0.35f, 0.15f), 0.4f);
            var posts = new System.Collections.Generic.List<Transform>();
            for (int i = 0; i < 18; i++)
                foreach (var x in new[] { -4.2f, 4.2f })
                {
                    var p = Prim(PrimitiveType.Cube, "Post", postMat, new Vector3(x, 1.1f, 8f - i * SpeedLinesDirector.PostSpacing), new Vector3(0.18f, 2.2f, 0.18f));
                    var cap = Prim(PrimitiveType.Cube, "Post cap", capMat, Vector3.zero, Vector3.one);
                    cap.transform.SetParent(p.transform, false); cap.transform.localPosition = new Vector3(0, 0.45f, 0); cap.transform.localScale = new Vector3(1.3f, 0.12f, 1.3f);
                    posts.Add(p.transform);
                }

            // stand-in mannequin (Shared/Character/Editor/Mannequin), not the character model folder
            var man = Mannequin.Build(Lit, Dir, (m, part) =>
            {
                m.SetColor("_BaseColor", part == "skin" ? new Color(0.86f, 0.72f, 0.62f) : new Color(0.16f, 0.30f, 0.62f));
                m.SetFloat("_Smoothness", 0.35f);
            });
            man.transform.position = Vector3.zero;

            var srcCam = SceneKit.Cam("Scene camera", new Rect(0, 0, 1, 1), 45, Color.black, -10);
            srcCam.clearFlags = CameraClearFlags.Skybox; srcCam.cullingMask = ~(1 << 8); srcCam.farClipPlane = 300f;
            SceneKit.Grade(Dir, 0.4f, 0f, 10f, 5f, 0.25f, 0f);
            var display = SceneKit.Cam("Display camera", new Rect(0, 0, 1, 1), 40, Color.black, 10, false);
            display.cullingMask = 1 << 8; display.transform.position = new Vector3(0, -50, 0); display.tag = "MainCamera";

            var compose = new Material(Shader.Find("Hidden/GT/SpeedLinesCompose")); AssetDatabase.CreateAsset(compose, Dir + "Compose.mat");
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Display quad"; Object.DestroyImmediate(quad.GetComponent<Collider>()); quad.layer = 8;
            var qm = Object.Instantiate(quad.GetComponent<MeshFilter>().sharedMesh); qm.name = "DisplayQuad"; qm.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
            AssetDatabase.CreateAsset(qm, Dir + "DisplayQuad.asset");
            quad.GetComponent<MeshFilter>().sharedMesh = qm; quad.GetComponent<Renderer>().sharedMaterial = compose;
            quad.transform.position = display.transform.position + Vector3.forward;

            var dir = new GameObject("Director").AddComponent<SpeedLinesDirector>();
            dir.src = srcCam; dir.compose = compose; dir.road = road; dir.character = man.transform; dir.posts = posts.ToArray();
            dir.lines = new Material[3];
            for (int i = 0; i < 3; i++) { dir.lines[i] = new Material(Shader.Find("Hidden/GT/AnimeSpeedLines")); AssetDatabase.CreateAsset(dir.lines[i], Dir + "SpeedLines" + i + ".mat"); }
            dir.title = LabelSpec.Create("", 0.012f, 0.935f, 30);
            dir.l1 = new LabelSpec[3]; dir.l2 = new LabelSpec[3];
            for (int i = 0; i < 3; i++) { dir.l1[i] = LabelSpec.Create("", 0.012f, 0.058f, 28); dir.l2[i] = LabelSpec.Create("", 0.012f, 0.018f, 22); }
        }
    }
}
