// Builds the reaction-diffusion scene from code: the director, the surface material and the cameras
// (full screen, two halves, three thirds, a 3 x 2 test grid, and a light grey background that shows as divider lines).
using UnityEditor;
using UnityEngine;

namespace GameTech.Posts
{
    public static class RDScene
    {
        const string Dir = "Assets/Techniques/reaction-diffusion/";

        static void Common(bool test)
        {
            RenderSettings.skybox = null; RenderSettings.fog = false;
            SceneKit.Grade(Dir, 0.45f, 0f, 10f, 8f, 0.3f, 0.1f);
            var dir = new GameObject("Director").AddComponent<RDDirector>();
            dir.cs = AssetDatabase.LoadAssetAtPath<ComputeShader>(Dir + "Shaders/GrayScott.compute");
            var m = new Material(Shader.Find("GameTech/RDSurface")); AssetDatabase.CreateAsset(m, Dir + "Surface.mat"); dir.surface = m;
            var bg = new Color(0.02f, 0.02f, 0.03f);
            var cams = new System.Collections.Generic.List<Camera>();
            cams.Add(SceneKit.Cam("Full", new Rect(0, 0, 1, 1), 40f, bg, 0, true));
            cams.Add(SceneKit.Cam("Half L", new Rect(0, 0, 0.4985f, 1), 40f, bg, 1, true));
            cams.Add(SceneKit.Cam("Half R", new Rect(0.5015f, 0, 0.4985f, 1), 40f, bg, 2, true));
            for (int i = 0; i < 3; i++) cams.Add(SceneKit.Cam("Third " + i, new Rect(i * 0.334f, 0, 0.332f, 1), 40f, bg, 3 + i, true));
            for (int i = 0; i < 6; i++) cams.Add(SceneKit.Cam("Test " + i, new Rect((i % 3) * 0.334f, (i / 3) * 0.5f, 0.332f, 0.497f), 40f, bg, 6 + i, true));
            var back = SceneKit.Cam("Divider", new Rect(0, 0, 1, 1), 40f, new Color(0.85f, 0.85f, 0.85f), -10, false); back.cullingMask = 0;
            cams.Add(back);
            foreach (var c in cams) { c.nearClipPlane = 0.3f; c.farClipPlane = 100f; }
            dir.cams = cams.ToArray();
            dir.test = test;
            dir.size = 512; dir.pop = 0.4f;
            if (!test) { dir.stepsPerFrame = 24; dir.sets = new[] { new Vector2(0.0367f, 0.0649f), new Vector2(0.0547f, 0.0632f), new Vector2(0.078f, 0.061f) };
                dir.setNames = new[] { "Spots", "Maze (the source's values)", "Slow fronts that grow into a maze" }; }
            if (test)
            {
                dir.sets = new[] { new Vector2(0.0547f, 0.0632f), new Vector2(0.0367f, 0.0649f), new Vector2(0.03f, 0.062f),
                                   new Vector2(0.025f, 0.06f), new Vector2(0.078f, 0.061f), new Vector2(0.039f, 0.058f) };
                dir.setNames = new[] { "BL .0547/.0632", "BM .0367/.0649", "BR .030/.062", "TL .025/.060", "TM .078/.061", "TR .039/.058" };
                dir.stepsPerFrame = 100;
            }
            dir.title = LabelSpec.Create("", 0.012f, 0.93f, 40);
            dir.l1 = LabelSpec.Create("", 0.012f, 0.058f, 34); dir.l2 = LabelSpec.Create("", 0.012f, 0.018f, 30);
            dir.m1 = LabelSpec.Create("", 0.512f, 0.058f, 34); dir.m2 = LabelSpec.Create("", 0.512f, 0.018f, 30);
            dir.n1 = LabelSpec.Create("", 0.68f, 0.058f, 34); dir.n2 = LabelSpec.Create("", 0.68f, 0.018f, 30);
            AssetDatabase.SaveAssets();
            Debug.Log($"[gt] built RD scene, compute {(dir.cs != null)}, surface {m.shader.isSupported}");
        }

        public static void BuildTest() => Common(true);
        public static void Build() => Common(false);
    }
}
