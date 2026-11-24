// Builds the deformer scene from code: a dark floor, warm key light with shadows, cool rim light, a glazed coral
// material for the sculptures, cameras and labels. The sculptures and their deformers are created at play time
// by DeformDirector.
using UnityEditor;
using UnityEngine;

namespace GameTech.Posts
{
    public static class DeformScene
    {
        const string Dir = "Assets/Techniques/mesh-deformers/";

        public static void Build()
        {
            RenderSettings.skybox = null; RenderSettings.fog = false;
            SceneKit.Ambient(new Color(0.17f, 0.18f, 0.23f), new Color(0.09f, 0.09f, 0.1f), new Color(0.04f, 0.04f, 0.04f));
            SceneKit.Shadows(40f);
            SceneKit.Grade(Dir, 0.5f, 0f, 14f, 6f, 0.3f, 0.08f);
            SceneKit.Sun("Key", new Vector3(38f, -40f, 0f), new Color(1f, 0.9f, 0.78f), 2.7f, true);
            SceneKit.Sun("Rim", new Vector3(22f, 150f, 0f), new Color(0.5f, 0.68f, 1f), 1.6f, false);
            var dir = new GameObject("Director").AddComponent<DeformDirector>();
            dir.heroMat = SceneKit.Mat(Dir, "Glaze", "Universal Render Pipeline/Lit", new Color(0.93f, 0.36f, 0.24f), 0f, 0.78f);
            dir.gridMat = SceneKit.Mat(Dir, "GlazeGrid", "Universal Render Pipeline/Lit", new Color(0.93f, 0.36f, 0.24f), 0f, 0.72f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Floor";
            floor.transform.position = new Vector3(200f, -0.5f, 0f); floor.transform.localScale = new Vector3(700f, 1f, 80f);
            floor.GetComponent<Renderer>().sharedMaterial = SceneKit.Mat(Dir, "Floor", "Universal Render Pipeline/Lit", new Color(0.075f, 0.075f, 0.08f), 0f, 0.1f);
            var bg = new Color(0.02f, 0.022f, 0.03f);
            dir.camBack = SceneKit.Cam("Divider", new Rect(0, 0, 1, 1), 35f, new Color(0.35f, 0.35f, 0.37f), -10, false); dir.camBack.cullingMask = 0;
            dir.camFull = SceneKit.Cam("Full", new Rect(0, 0, 1, 1), 35f, bg, 0, true);
            dir.camL = SceneKit.Cam("Left", new Rect(0, 0, 0.4985f, 1), 35f, bg, 1, true);
            dir.camR = SceneKit.Cam("Right", new Rect(0.5015f, 0, 0.4985f, 1), 35f, bg, 2, true);
            foreach (var c in new[] { dir.camFull, dir.camL, dir.camR }) { c.nearClipPlane = 0.1f; c.farClipPlane = 60f; }
            dir.title = LabelSpec.Create("", 0.012f, 0.93f, 40);
            dir.l1 = LabelSpec.Create("", 0.012f, 0.058f, 32); dir.l2 = LabelSpec.Create("", 0.012f, 0.018f, 30);
            dir.m1 = LabelSpec.Create("", 0.512f, 0.058f, 32); dir.m2 = LabelSpec.Create("", 0.512f, 0.018f, 30);
            AssetDatabase.SaveAssets();
            Debug.Log("[gt] built deformer scene; code optimization " + UnityEditor.Compilation.CompilationPipeline.codeOptimization);
        }
    }
}
