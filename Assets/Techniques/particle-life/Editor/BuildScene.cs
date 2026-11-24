// Builds the particle life scene from code: black background, bloom, orthographic cameras and labels. The
// simulations, their particle renderers and the rule-matrix insets are created at play time by LifeDirector.
using UnityEditor;
using UnityEngine;

namespace GameTech.Posts
{
    public static class LifeScene
    {
        const string Dir = "Assets/Techniques/particle-life/";

        public static void Build()
        {
            RenderSettings.skybox = null; RenderSettings.fog = false;
            SceneKit.Ambient(Color.black, Color.black, Color.black);
            SceneKit.Grade(Dir, 0.9f, 0f, 10f, 8f, 0.25f, 0.06f);
            var dir = new GameObject("Director").AddComponent<LifeDirector>();
            dir.cs = AssetDatabase.LoadAssetAtPath<ComputeShader>(Dir + "Shaders/ParticleLife.compute");
            var dot = new Material(Shader.Find("GameTech/LifeParticles")); AssetDatabase.CreateAsset(dot, Dir + "Dots.mat"); dir.dotMat = dot;
            dir.cellMat = SceneKit.Mat(Dir, "Cell", "Universal Render Pipeline/Unlit", Color.white, 0f, 0f);
            var bg = new Color(0.008f, 0.01f, 0.018f);
            dir.camBack = SceneKit.Cam("Divider", new Rect(0, 0, 1, 1), 35f, new Color(0.35f, 0.35f, 0.37f), -10, false); dir.camBack.cullingMask = 0;
            dir.camFull = SceneKit.Cam("Full", new Rect(0, 0, 1, 1), 35f, bg, 0, true);
            dir.camL = SceneKit.Cam("Left", new Rect(0, 0, 0.4985f, 1), 35f, bg, 1, true);
            dir.camR = SceneKit.Cam("Right", new Rect(0.5015f, 0, 0.4985f, 1), 35f, bg, 2, true);
            foreach (var c in new[] { dir.camFull, dir.camL, dir.camR }) { c.nearClipPlane = 0.1f; c.farClipPlane = 40f; }
            dir.title = LabelSpec.Create("", 0.012f, 0.93f, 40);
            dir.l1 = LabelSpec.Create("", 0.012f, 0.058f, 32); dir.l2 = LabelSpec.Create("", 0.012f, 0.018f, 30);
            dir.m1 = LabelSpec.Create("", 0.512f, 0.058f, 32); dir.m2 = LabelSpec.Create("", 0.512f, 0.018f, 30);
            AssetDatabase.SaveAssets();
            Debug.Log("[gt] built particle life scene; code optimization " + UnityEditor.Compilation.CompilationPipeline.codeOptimization);
        }
    }
}
