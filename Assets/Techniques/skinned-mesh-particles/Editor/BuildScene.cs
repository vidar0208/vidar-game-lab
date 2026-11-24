// Builds the skinned-mesh particle scene from code: three dancing stand-in mannequins (plus a hidden bind-pose copy),
// one Smrvfx SkinnedMeshBaker per particle system, a dark floor, cameras and labels.
using UnityEditor;
using UnityEngine;

namespace GameTech.Posts
{
    public static class MeshParticlesScene
    {
        const string Dir = "Assets/Techniques/skinned-mesh-particles/";
        const int Hidden = 30;

        static Smrvfx.SkinnedMeshBaker Baker(string name, GameObject character, ComputeShader cs)
        {
            var b = new GameObject(name).AddComponent<Smrvfx.SkinnedMeshBaker>();
            var so = new SerializedObject(b);
            var src = so.FindProperty("_sources"); src.arraySize = 1;
            src.GetArrayElementAtIndex(0).objectReferenceValue = character.GetComponentInChildren<SkinnedMeshRenderer>();
            so.FindProperty("_pointCount").intValue = 8192;
            so.FindProperty("_compute").objectReferenceValue = cs;
            so.ApplyModifiedPropertiesWithoutUndo();
            return b;
        }

        static void SetLayer(Transform t, int layer) { t.gameObject.layer = layer; foreach (Transform c in t) SetLayer(c, layer); }

        public static void Build()
        {
            RenderSettings.skybox = null; RenderSettings.fog = false;
            SceneKit.Ambient(new Color(0.05f, 0.055f, 0.07f), new Color(0.03f, 0.03f, 0.04f), new Color(0.01f, 0.01f, 0.012f));
            SceneKit.Shadows(30f);
            SceneKit.Grade(Dir, 0.5f, 0f, 12f, 8f, 0.3f, 0.03f);
            SceneKit.Sun("Key", new Vector3(38f, 150f, 0f), new Color(0.75f, 0.8f, 1f), 0.22f, true);

            var floorMat = SceneKit.Mat(Dir, "Floor", "Universal Render Pipeline/Lit", new Color(0.025f, 0.025f, 0.03f), 0f, 0.3f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.name = "Floor";
            floor.transform.localScale = new Vector3(40f, 1f, 4f); floor.GetComponent<Renderer>().sharedMaterial = floorMat;

            // stand-in mannequin (Shared/Character/Editor/Mannequin), dark so the particles carry the image
            var man = Mannequin.Build("Universal Render Pipeline/Lit", Dir, (m, part) =>
            {
                m.SetColor("_BaseColor", part == "skin" ? new Color(0.16f, 0.14f, 0.13f) : new Color(0.07f, 0.08f, 0.1f));
                m.SetFloat("_Smoothness", 0.55f);
            });
            var dir = new GameObject("Director").AddComponent<MeshParticlesDirector>();
            dir.baseA = new Vector3(-60f, 0, 0); dir.baseB = Vector3.zero; dir.baseC = new Vector3(60f, 0, 0);
            man.name = "Dancer B"; man.transform.position = dir.baseB;
            var a = Object.Instantiate(man); a.name = "Dancer A"; a.transform.position = dir.baseA;
            var c = Object.Instantiate(man); c.name = "Dancer C"; c.transform.position = dir.baseC;
            var a0 = Object.Instantiate(man); a0.name = "Dancer A bind pose (hidden)"; a0.transform.position = dir.baseA; SetLayer(a0.transform, Hidden);
            dir.rigA = a.AddComponent<CharacterRig>(); dir.rigB = man.AddComponent<CharacterRig>(); dir.rigC = c.AddComponent<CharacterRig>();
            foreach (var r in new[] { dir.rigA, dir.rigB, dir.rigC }) { r.idle = false; r.blink = false; }
            dir.rootA0 = a0.transform;

            var cs = AssetDatabase.LoadAssetAtPath<ComputeShader>(Dir + "Smrvfx/Resources/SkinnedMeshBaker.compute");
            dir.bakerA = Baker("Baker A (bind pose)", a0, cs); dir.bakerB = Baker("Baker B", man, cs); dir.bakerC = Baker("Baker C", c, cs);
            dir.sim = AssetDatabase.LoadAssetAtPath<ComputeShader>(Dir + "Shaders/MeshParticles.compute");
            var draw = new Material(Shader.Find("GameTech/MeshParticleDraw")); AssetDatabase.CreateAsset(draw, Dir + "Particles.mat"); dir.drawMat = draw;

            var bg = new Color(0.01f, 0.011f, 0.016f);
            dir.camBack = SceneKit.Cam("Divider", new Rect(0, 0, 1, 1), 32f, new Color(0.35f, 0.35f, 0.37f), -10, false); dir.camBack.cullingMask = 0;
            dir.camFull = SceneKit.Cam("Full", new Rect(0, 0, 1, 1), 32f, bg, 0, true);
            dir.camL = SceneKit.Cam("Left", new Rect(0, 0, 0.4985f, 1), 32f, bg, 1, true);
            dir.camR = SceneKit.Cam("Right", new Rect(0.5015f, 0, 0.4985f, 1), 32f, bg, 2, true);
            foreach (var cam in new[] { dir.camFull, dir.camL, dir.camR }) { cam.nearClipPlane = 0.1f; cam.farClipPlane = 30f; cam.cullingMask = ~(1 << Hidden); }
            dir.title = LabelSpec.Create("", 0.012f, 0.93f, 40);
            dir.l1 = LabelSpec.Create("", 0.012f, 0.058f, 32); dir.l2 = LabelSpec.Create("", 0.012f, 0.018f, 30);
            dir.m1 = LabelSpec.Create("", 0.512f, 0.058f, 32); dir.m2 = LabelSpec.Create("", 0.512f, 0.018f, 30);
            AssetDatabase.SaveAssets();
            Debug.Log("[gt] built skinned-mesh particle scene");
        }
    }
}
