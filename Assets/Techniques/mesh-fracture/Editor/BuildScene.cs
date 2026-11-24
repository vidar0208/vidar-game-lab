// Builds the fracture scene from code: a dark floor, warm key light with shadows, cool rim light, procedural
// materials (glazed pot, stone, teal tablet, orange cut faces, glowing projectile), cameras and labels.
// Targets, projectiles and pieces are created at play time by FractureDirector.
using UnityEditor;
using UnityEngine;

namespace GameTech.Posts
{
    public static class FractureScene
    {
        const string Dir = "Assets/Techniques/mesh-fracture/";

        static Material Glow(string name, Color baseC, Color emit)
        {
            var m = SceneKit.Mat(Dir, name, "Universal Render Pipeline/Lit", baseC, 0f, 0.35f);
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emit);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; EditorUtility.SetDirty(m);
            return m;
        }

        public static void Build()
        {
            RenderSettings.skybox = null; RenderSettings.fog = false;
            SceneKit.Ambient(new Color(0.16f, 0.17f, 0.21f), new Color(0.09f, 0.09f, 0.1f), new Color(0.04f, 0.04f, 0.04f));
            SceneKit.Shadows(30f);
            SceneKit.Grade(Dir, 0.6f, 0f, 14f, 5f, 0.3f, 0.08f);
            SceneKit.Sun("Key", new Vector3(42f, -35f, 0f), new Color(1f, 0.9f, 0.78f), 2.6f, true);
            SceneKit.Sun("Rim", new Vector3(28f, 160f, 0f), new Color(0.55f, 0.7f, 1f), 1.4f, false);
            var dir = new GameObject("Director").AddComponent<FractureDirector>();
            dir.potMat = SceneKit.Mat(Dir, "Pot", "Universal Render Pipeline/Lit", new Color(0.86f, 0.84f, 0.8f), 0f, 0.7f);
            dir.stoneMat = SceneKit.Mat(Dir, "Stone", "Universal Render Pipeline/Lit", new Color(0.5f, 0.5f, 0.52f), 0f, 0.15f);
            dir.slabMat = SceneKit.Mat(Dir, "Tablet", "Universal Render Pipeline/Lit", new Color(0.1f, 0.42f, 0.45f), 0f, 0.55f);
            dir.insideMat = Glow("Inside", new Color(1f, 0.45f, 0.12f), new Color(1f, 0.38f, 0.08f) * 1.6f);
            dir.ballMat = Glow("Projectile", new Color(0.7f, 0.9f, 1f), new Color(0.6f, 0.85f, 1f) * 8f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Floor";
            floor.transform.position = new Vector3(250f, -0.5f, 0f); floor.transform.localScale = new Vector3(1100f, 1f, 60f);
            floor.GetComponent<Renderer>().sharedMaterial = SceneKit.Mat(Dir, "Floor", "Universal Render Pipeline/Lit", new Color(0.08f, 0.075f, 0.075f), 0f, 0.08f);
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
            Debug.Log("[gt] built fracture scene");
        }
    }
}
