// Builds the crowd scene from code: two light plazas (one per side of the split screen), a sun with soft shadows,
// stand-in walker materials, cameras and labels. The walkers themselves are created at play time.
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    public static class CrowdScene
    {
        const string Dir = "Assets/Techniques/rvo-crowd-avoidance/";

        static Transform Plaza(string name, float x, Material ground, Material ring)
        {
            var root = new GameObject(name).transform; root.position = new Vector3(x, 0, 0);
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Object.DestroyImmediate(g.GetComponent<Collider>());
            g.name = "Ground"; g.transform.SetParent(root, false); g.transform.localPosition = new Vector3(0, -0.05f, 0); g.transform.localScale = new Vector3(160f, 0.05f, 160f);
            g.GetComponent<Renderer>().sharedMaterial = ground;
            // start/goal ring: 120 flat dashes
            for (int i = 0; i < 120; i++)
            {
                if (i % 2 == 1) continue;
                float a = i * Mathf.PI * 2 / 120f;
                var d = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.DestroyImmediate(d.GetComponent<Collider>());
                d.transform.SetParent(root, false); d.transform.localPosition = new Vector3(Mathf.Cos(a), 0.005f, Mathf.Sin(a)) * 61.2f;
                d.transform.localRotation = Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0); d.transform.localScale = new Vector3(0.25f, 0.02f, 2.6f);
                d.GetComponent<Renderer>().sharedMaterial = ring; d.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            return root;
        }

        public static void Build()
        {
            RenderSettings.skybox = null; RenderSettings.fog = false;
            SceneKit.Ambient(new Color(0.62f, 0.66f, 0.75f), new Color(0.45f, 0.45f, 0.45f), new Color(0.25f, 0.24f, 0.22f));
            SceneKit.Shadows(220f);
            SceneKit.Grade(Dir, 0.25f, 0f, 10f, 10f, 0.22f, 0.1f);
            SceneKit.Sun("Sun", new Vector3(52f, 35f, 0f), new Color(1f, 0.96f, 0.9f), 1.8f, true);
            var ground = SceneKit.Mat(Dir, "Ground", "Universal Render Pipeline/Lit", new Color(0.8f, 0.79f, 0.76f), 0f, 0.15f);
            var ring = SceneKit.Mat(Dir, "Ring", "Universal Render Pipeline/Lit", new Color(0.45f, 0.45f, 0.47f), 0f, 0.1f);
            var body = SceneKit.Mat(Dir, "Body", "Universal Render Pipeline/Lit", Color.white, 0f, 0.35f);
            var head = SceneKit.Mat(Dir, "Head", "Universal Render Pipeline/Lit", new Color(0.93f, 0.9f, 0.86f), 0f, 0.3f);
            var dir = new GameObject("Director").AddComponent<CrowdDirector>();
            dir.bodyMat = body; dir.headMat = head;
            dir.groundA = Plaza("Plaza A", 0f, ground, ring); dir.groundB = Plaza("Plaza B", 300f, ground, ring);
            var bg = new Color(0.55f, 0.6f, 0.68f);
            dir.camBack = SceneKit.Cam("Divider", new Rect(0, 0, 1, 1), 35f, new Color(0.15f, 0.15f, 0.17f), -10, false); dir.camBack.cullingMask = 0;
            dir.camFull = SceneKit.Cam("Full", new Rect(0, 0, 1, 1), 35f, bg, 0, true);
            dir.camL = SceneKit.Cam("Left", new Rect(0, 0, 0.4985f, 1), 35f, bg, 1, true);
            dir.camR = SceneKit.Cam("Right", new Rect(0.5015f, 0, 0.4985f, 1), 35f, bg, 2, true);
            foreach (var c in new[] { dir.camFull, dir.camL, dir.camR }) { c.nearClipPlane = 1f; c.farClipPlane = 900f; }
            dir.title = LabelSpec.Create("", 0.012f, 0.93f, 40);
            dir.l1 = LabelSpec.Create("", 0.012f, 0.058f, 32); dir.l2 = LabelSpec.Create("", 0.012f, 0.018f, 30);
            dir.m1 = LabelSpec.Create("", 0.512f, 0.058f, 32); dir.m2 = LabelSpec.Create("", 0.512f, 0.018f, 30);
            AssetDatabase.SaveAssets();
            Debug.Log("[gt] built crowd scene");
        }
    }
}
