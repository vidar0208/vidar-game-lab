// Builds the WFC town scene from code: three towns side by side (WFC result, random tiles, heat map bars),
// a sun with soft shadows, one full-screen camera and two half-screen cameras for the comparisons.
// The towns themselves are generated at play time by WFCDirector.
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    public static class WFCScene
    {
        const string Dir = "Assets/Techniques/wave-function-collapse/";

        static Material Lit(string name, Color c, float smooth, float metallic = 0)
        {
            var m = SceneKit.Mat(Dir, name, "Universal Render Pipeline/Lit", c, metallic, smooth);
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(m);
            return m;
        }

        static WFCTown Town(string name, float x, Material[] mats, Material[] roofs, Material plate, Material bar, Material baseM)
        {
            var t = new GameObject(name).AddComponent<WFCTown>();
            t.transform.position = new Vector3(x, 0, 0);
            t.mats = mats; t.roofs = roofs; t.plateMat = plate; t.barMat = bar; t.baseMat = baseM;
            return t;
        }

        public static void Build()
        {
            RenderSettings.skybox = null; RenderSettings.fog = false;
            SceneKit.Ambient(new Color(0.55f, 0.62f, 0.78f), new Color(0.38f, 0.4f, 0.42f), new Color(0.18f, 0.16f, 0.14f));
            SceneKit.Shadows(110f);
            SceneKit.Grade(Dir, 0.35f, 0.1f, 12f, 12f, 0.25f, 0.12f);
            SceneKit.Sun("Sun", new Vector3(46f, 32f, 0f), new Color(1f, 0.95f, 0.86f), 1.7f, true);

            var mats = new[]
            {
                Lit("Grass", new Color(0.3f, 0.5f, 0.17f), 0.12f),
                Lit("Road", new Color(0.19f, 0.19f, 0.21f), 0.25f),
                Lit("Water", new Color(0.08f, 0.42f, 0.86f), 0.88f),
                Lit("Wall", new Color(0.94f, 0.91f, 0.85f), 0.2f),
                Lit("Roof", new Color(0.86f, 0.2f, 0.13f), 0.3f),
                Lit("Deck", new Color(0.58f, 0.4f, 0.22f), 0.2f),
                Lit("Leaves", new Color(0.14f, 0.43f, 0.17f), 0.1f),
                Lit("Trunk", new Color(0.36f, 0.24f, 0.14f), 0.1f),
            };
            var roofs = new[] { mats[4], Lit("Roof orange", new Color(0.97f, 0.55f, 0.12f), 0.3f), Lit("Roof blue", new Color(0.2f, 0.34f, 0.78f), 0.3f), Lit("Roof red dark", new Color(0.6f, 0.12f, 0.12f), 0.3f) };
            var plate = Lit("Undecided", new Color(0.12f, 0.13f, 0.17f), 0.3f);
            var bar = Lit("Bar", Color.white, 0.25f);
            var baseM = Lit("Base", new Color(0.3f, 0.22f, 0.15f), 0.1f);

            var dir = new GameObject("Director").AddComponent<WFCDirector>();
            dir.townW = Town("Town WFC", 0f, mats, roofs, plate, bar, baseM);
            dir.townR = Town("Town random", 100f, mats, roofs, plate, bar, baseM);
            dir.townH = Town("Heat map", 200f, mats, roofs, plate, bar, baseM);

            var bg = new Color(0.05f, 0.06f, 0.09f);
            dir.camBack = SceneKit.Cam("Divider cam", new Rect(0, 0, 1, 1), 30f, new Color(0.85f, 0.85f, 0.85f), -10, false);
            dir.camBack.cullingMask = 0;
            dir.camFull = SceneKit.Cam("Full cam", new Rect(0, 0, 1, 1), 30f, bg, 0, true);
            dir.camL = SceneKit.Cam("Left cam", new Rect(0, 0, 0.4985f, 1), 31f, bg, 1, true);
            dir.camR = SceneKit.Cam("Right cam", new Rect(0.5015f, 0, 0.4985f, 1), 31f, bg, 2, true);
            foreach (var c in new[] { dir.camFull, dir.camL, dir.camR }) { c.nearClipPlane = 1f; c.farClipPlane = 200f; }

            dir.title = LabelSpec.Create("", 0.012f, 0.93f, 40);
            dir.l1 = LabelSpec.Create("", 0.012f, 0.058f, 34); dir.l2 = LabelSpec.Create("", 0.012f, 0.018f, 30);
            dir.m1 = LabelSpec.Create("", 0.512f, 0.058f, 34); dir.m2 = LabelSpec.Create("", 0.512f, 0.018f, 30);
            AssetDatabase.SaveAssets();
            Debug.Log("[gt] built WFC scene");
        }
    }
}
