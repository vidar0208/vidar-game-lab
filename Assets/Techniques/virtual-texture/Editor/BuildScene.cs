// Builds the virtual texture scene from code: three copies of a 64 m ground quad (layers 25, 26, 27), two
// virtual texture systems (64-tile and 16-tile cache, everything else equal), the debug panels on an
// orthographic overlay camera, the cameras and labels. No character.
using GameTech;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VirtualTexture;

namespace GameTech.Posts
{
    public static class VTScene
    {
        const string Dir = "Assets/Techniques/virtual-texture/";
        static readonly Color Haze = new Color(0.30f, 0.20f, 0.24f);

        static T Load<T>(string p) where T : Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(Dir + p);
            if (a == null) Debug.LogError("[gt] missing " + p);
            return a;
        }

        static Material Mat(string name, Shader s)
        {
            var m = new Material(s); AssetDatabase.CreateAsset(m, Dir + name + ".mat"); return m;
        }

        static Renderer Ground(string name, Material m, int layer)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // u along +x, v along +z, facing up
            go.transform.localScale = new Vector3(64f, 64f, 1f);
            go.layer = layer;
            var r = go.GetComponent<Renderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off;
            return r;
        }

        static GameObject MakeSystem(string name, Vector2Int region, Renderer ground, Material groundMat, Material pageMat)
        {
            var go = new GameObject(name);
            go.AddComponent<VTBinding>().materials.Add(groundMat);
            var tt = go.AddComponent<TiledTexture>(); tt.RegionSize = region; tt.pageMaterial = pageMat;
            go.AddComponent<ProceduralLoader>().pagesPerFrame = 8;
            var fb = go.AddComponent<FeedbackRendererURP>(); fb.m_FeedbackShader = Load<Shader>("VirtualTexture/Shaders/VTFeedback.shader"); fb.targets = new[] { ground };
            go.AddComponent<FeedbackReader>().m_DownScaleShader = Load<Shader>("VirtualTexture/Shaders/VTFeedbackDownScale.shader");
            go.AddComponent<PageTable>();
            return go;
        }

        public static void Build()
        {
            RenderSettings.skybox = null; RenderSettings.fog = false;
            SceneKit.Ambient(Haze, Haze, Haze);
            var dir = new GameObject("Director").AddComponent<VTDirector>();

            var groundSh = Load<Shader>("Shaders/VTGround.shader");
            dir.pageMat = Mat("VTPage", Load<Shader>("Shaders/VTPage.shader"));
            Material GroundMat(string n, float mode)
            {
                var m = Mat(n, groundSh); m.SetFloat("_Mode", mode); m.SetColor("_HazeColor", Haze); m.SetFloat("_HazeDensity", 0.03f); return m;
            }
            dir.mat64 = GroundMat("Ground VT64", 0); var mat16 = GroundMat("Ground VT16", 0); dir.matReg = GroundMat("Ground regular", 1);
            var g64 = Ground("Ground VT64", dir.mat64, VTDirector.L64);
            var g16 = Ground("Ground VT16", mat16, VTDirector.L16);
            Ground("Ground regular", dir.matReg, VTDirector.LReg);

            dir.sys64 = MakeSystem("Virtual texture 64", new Vector2Int(8, 8), g64, dir.mat64, dir.pageMat);
            dir.sys16 = MakeSystem("Virtual texture 16", new Vector2Int(4, 4), g16, mat16, dir.pageMat);

            SceneKit.Grade(Dir, 0.25f, 0f, 8f, 4f, 0.3f, 0.06f);
            AssetDatabase.SaveAssets();

            dir.camA = SceneKit.Cam("Cam A", new Rect(0, 0, 1, 1), 50f, Haze, 0, true);
            dir.camB = SceneKit.Cam("Cam B", new Rect(0, 0, 1, 1), 50f, Haze, 1, true);
            foreach (var c in new[] { dir.camA, dir.camB }) { c.nearClipPlane = 0.05f; c.farClipPlane = 150f; }
            var clear = SceneKit.Cam("Clear", new Rect(0, 0, 1, 1), 60f, Color.black, -10, false); clear.cullingMask = 0;

            // overlay: orthographic, one unit = one output pixel, panels on layer 29 far away from the ground
            var ov = SceneKit.Cam("Overlay", new Rect(0.6215f, 0, 0.3785f, 1), 60f, new Color(0.012f, 0.012f, 0.016f), 2, false);
            ov.orthographic = true; ov.orthographicSize = 540f; ov.cullingMask = 1 << 29;
            ov.transform.position = new Vector3(5000f, 0f, -10f); ov.transform.rotation = Quaternion.identity;
            ov.nearClipPlane = 1f; ov.farClipPlane = 50f;
            dir.overlay = ov;
            var dbg = Load<Shader>("Shaders/VTDebugView.shader");
            Renderer Panel(string n, float view, Vector2 centre, Vector2 size)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = n;
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.layer = 29;
                go.transform.position = new Vector3(5000f + centre.x, centre.y, 0f);
                go.transform.localScale = new Vector3(size.x, size.y, 1f);
                var m = Mat(n, dbg); m.SetFloat("_View", view);
                var r = go.GetComponent<Renderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off;
                return r;
            }
            dir.panels = new[]
            {
                Panel("Panel feedback", 0, new Vector2(0f, 202f), new Vector2(480f, 436f)),
                Panel("Panel page table", 1, new Vector2(-175f, -210f), new Vector2(300f, 300f)),
                Panel("Panel tile cache", 2, new Vector2(175f, -210f), new Vector2(300f, 300f)),
            };

            dir.title = LabelSpec.Create("", 0.012f, 0.93f, 40);
            dir.l1 = LabelSpec.Create("", 0.012f, 0.058f, 34); dir.l2 = LabelSpec.Create("", 0.012f, 0.018f, 30);
            dir.m1 = LabelSpec.Create("", 0.512f, 0.058f, 34); dir.m2 = LabelSpec.Create("", 0.512f, 0.018f, 30);
            // panel captions: overlay centre is at x 0.6215 + 0.3785 / 2 of the screen; one overlay unit = 1 / 1920 or 1 / 1080
            float cx = (0.6215f + 0.3785f / 2f) * 1920f;
            dir.p1 = LabelSpec.Create("", (cx - 240f) / 1920f, (540f + 202f + 218f + 10f) / 1080f, 26);
            dir.p2 = LabelSpec.Create("", (cx - 325f) / 1920f, (540f - 210f + 150f + 10f) / 1080f, 26);
            dir.p3 = LabelSpec.Create("", (cx + 25f) / 1920f, (540f - 210f + 150f + 10f) / 1080f, 26);
            Debug.Log("[gt] built: 3 ground copies, 2 virtual texture systems (8x8 and 4x4 tiles), 3 debug panels");
        }
    }
}
