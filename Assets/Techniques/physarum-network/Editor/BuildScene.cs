// Builds the slime mold scene from code: the display camera with the composite quad and the colour grade, the
// director that runs the two simulations, and the labels. The simulations draw straight into textures.
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    public static class PhysarumScene
    {
        const string Dir = "Assets/Techniques/physarum-network/";

        public static void Build()
        {
            RenderSettings.skybox = null; RenderSettings.fog = false;
            SceneKit.Grade(Dir, 0.6f, 0f, 10f, 8f, 0.3f, 0f);

            var dir = new GameObject("Director").AddComponent<PhysarumDirector>();
            dir.sim = AssetDatabase.LoadAssetAtPath<ComputeShader>(Dir + "Shaders/Physarum.compute");

            var disp = new Material(Shader.Find("GameTech/PhysarumComposite")); AssetDatabase.CreateAsset(disp, Dir + "Display.mat"); dir.display = disp;
            var dcam = SceneKit.Cam("Display cam", new Rect(0, 0, 1, 1), 60f, Color.black, 0, true);
            dcam.orthographic = true; dcam.orthographicSize = 0.5f; dcam.cullingMask = 1 << 10; dcam.transform.position = new Vector3(500f, 0f, -1f);
            dcam.nearClipPlane = 0.01f; dcam.farClipPlane = 5f;
            var dq = GameObject.CreatePrimitive(PrimitiveType.Quad); dq.name = "Display quad"; dq.layer = 10; Object.DestroyImmediate(dq.GetComponent<Collider>());
            dq.transform.position = new Vector3(500f, 0f, 0f); dq.transform.localScale = new Vector3(16f / 9f, 1f, 1f);
            var dmr = dq.GetComponent<MeshRenderer>(); dmr.sharedMaterial = disp; dmr.shadowCastingMode = ShadowCastingMode.Off;

            dir.title = LabelSpec.Create("", 0.012f, 0.93f, 40);
            dir.l1 = LabelSpec.Create("", 0.012f, 0.058f, 34); dir.l2 = LabelSpec.Create("", 0.012f, 0.018f, 30);
            dir.m1 = LabelSpec.Create("", 0.512f, 0.058f, 34); dir.m2 = LabelSpec.Create("", 0.512f, 0.018f, 30);
            AssetDatabase.SaveAssets();
            Debug.Log($"[gt] built: sim {(dir.sim != null)}, composite {disp.shader.isSupported}");
        }
    }
}
