// Builds the galaxy collision scene from code: director, star material, a full-screen camera, two half-screen
// cameras and a light grey background camera that shows as the divider line.
using UnityEditor;
using UnityEngine;

namespace GameTech.Posts
{
    public static class GalaxyScene
    {
        const string Dir = "Assets/Techniques/nbody-galaxy/";

        public static void Build()
        {
            RenderSettings.skybox = null; RenderSettings.fog = false;
            SceneKit.Grade(Dir, 0.9f, 0f, 10f, 10f, 0.3f, 0.12f);
            var dir = new GameObject("Director").AddComponent<GalaxyDirector>();
            dir.cs = AssetDatabase.LoadAssetAtPath<ComputeShader>(Dir + "Shaders/IntegrateBodies.compute");
            var m = new Material(Shader.Find("GameTech/Stars"));
            m.SetFloat("_Size", 0.045f); m.SetFloat("_Gain", 0.55f); m.SetFloat("_MaxMass", 0.5f);
            m.SetColor("_ColA", new Color(0.45f, 0.65f, 1f)); m.SetColor("_ColB", new Color(1f, 0.6f, 0.28f));
            AssetDatabase.CreateAsset(m, Dir + "Stars.mat"); dir.starMat = m;
            var bg = new Color(0.008f, 0.01f, 0.02f);
            dir.camBack = SceneKit.Cam("Divider", new Rect(0, 0, 1, 1), 40f, new Color(0.85f, 0.85f, 0.85f), -10, false); dir.camBack.cullingMask = 0;
            dir.camFull = SceneKit.Cam("Full", new Rect(0, 0, 1, 1), 40f, bg, 0, true);
            dir.camL = SceneKit.Cam("Left", new Rect(0, 0, 0.4985f, 1), 40f, bg, 1, true);
            dir.camR = SceneKit.Cam("Right", new Rect(0.5015f, 0, 0.4985f, 1), 40f, bg, 2, true);
            foreach (var c in new[] { dir.camFull, dir.camL, dir.camR }) { c.nearClipPlane = 0.5f; c.farClipPlane = 500f; }
            dir.title = LabelSpec.Create("", 0.012f, 0.93f, 40);
            dir.l1 = LabelSpec.Create("", 0.012f, 0.058f, 34); dir.l2 = LabelSpec.Create("", 0.012f, 0.018f, 30);
            dir.m1 = LabelSpec.Create("", 0.512f, 0.058f, 34); dir.m2 = LabelSpec.Create("", 0.512f, 0.018f, 30);
            AssetDatabase.SaveAssets();
            Debug.Log($"[gt] built galaxy scene, compute {(dir.cs != null)}, stars {m.shader.isSupported}");
        }
    }
}
