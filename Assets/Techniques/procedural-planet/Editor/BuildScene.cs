// Builds the moon scene from code: black space, one low sun with shadows, a plain grey rock material (no textures),
// cameras and labels. The moons themselves are generated at play time by PlanetDirector.
using UnityEditor;
using UnityEngine;

namespace GameTech.Posts
{
    public static class PlanetScene
    {
        const string Dir = "Assets/Techniques/procedural-planet/";

        public static void Build()
        {
            RenderSettings.skybox = null; RenderSettings.fog = false;
            SceneKit.Ambient(new Color(0.02f, 0.022f, 0.03f), new Color(0.015f, 0.015f, 0.02f), new Color(0.01f, 0.01f, 0.01f));
            SceneKit.Shadows(70f);
            SceneKit.Grade(Dir, 0.15f, 0f, 12f, 0f, 0.25f, 0.08f);
            var sun = SceneKit.Sun("Sun", Vector3.zero, new Color(1f, 0.95f, 0.88f), 3.2f, true);
            sun.transform.rotation = Quaternion.LookRotation(new Vector3(1f, -0.1f, 0.15f));
            sun.shadowBias = 0.02f; sun.shadowNormalBias = 0.3f;
            var rock = SceneKit.Mat(Dir, "Rock", "Universal Render Pipeline/Lit", new Color(0.62f, 0.6f, 0.57f), 0f, 0.08f);
            var dir = new GameObject("Director").AddComponent<PlanetDirector>();
            dir.rockMat = rock;
            dir.heightCS = AssetDatabase.LoadAssetAtPath<ComputeShader>(Dir + "Shaders/PlanetHeight.compute");
            var bg = new Color(0.004f, 0.005f, 0.008f);
            dir.camBack = SceneKit.Cam("Divider", new Rect(0, 0, 1, 1), 35f, new Color(0.35f, 0.35f, 0.37f), -10, false); dir.camBack.cullingMask = 0;
            dir.camFull = SceneKit.Cam("Full", new Rect(0, 0, 1, 1), 35f, bg, 0, true);
            dir.camL = SceneKit.Cam("Left", new Rect(0, 0, 0.4985f, 1), 35f, bg, 1, true);
            dir.camR = SceneKit.Cam("Right", new Rect(0.5015f, 0, 0.4985f, 1), 35f, bg, 2, true);
            foreach (var c in new[] { dir.camFull, dir.camL, dir.camR }) { c.nearClipPlane = 0.3f; c.farClipPlane = 200f; }
            dir.title = LabelSpec.Create("", 0.012f, 0.93f, 40);
            dir.l1 = LabelSpec.Create("", 0.012f, 0.058f, 32); dir.l2 = LabelSpec.Create("", 0.012f, 0.018f, 30);
            dir.m1 = LabelSpec.Create("", 0.512f, 0.058f, 32); dir.m2 = LabelSpec.Create("", 0.512f, 0.018f, 30);
            AssetDatabase.SaveAssets();
            Debug.Log($"[gt] built planet scene, compute {dir.heightCS != null}");
        }
    }
}
