// 90 rim light: the stand-in mannequin (no character model) with the URP rim light shader on both of its materials,
// on a dark pedestal in a dark space, a warm light from behind and to the side. Built from code; no external assets.
using UnityEditor;
using UnityEngine;

namespace GameTech.Posts
{
    public static class RimScene
    {
        const string Dir = "Assets/Techniques/rim-light/";
        const string Lit = "Universal Render Pipeline/Lit";

        public static void Build()
        {
            SceneKit.Ambient(new Color(0.10f, 0.11f, 0.15f), new Color(0.08f, 0.08f, 0.1f), new Color(0.04f, 0.04f, 0.05f));
            SceneKit.Shadows(20f);
            var sun = SceneKit.Sun("Back light", new Vector3(25, 35, 0), new Color(1f, 0.82f, 0.62f), 1.4f, true);
            RenderSettings.sun = sun;
            RenderSettings.skybox = null;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogColor = new Color(0.012f, 0.014f, 0.02f);
            RenderSettings.fogStartDistance = 3.5f; RenderSettings.fogEndDistance = 11f;

            var floor = SceneKit.Mat(Dir, "Floor", Lit, new Color(0.035f, 0.037f, 0.045f), 0f, 0.45f);
            var ped = SceneKit.Mat(Dir, "Pedestal", Lit, new Color(0.09f, 0.09f, 0.1f), 0f, 0.3f);
            var f = GameObject.CreatePrimitive(PrimitiveType.Plane); f.name = "Floor"; Object.DestroyImmediate(f.GetComponent<Collider>());
            f.GetComponent<Renderer>().sharedMaterial = floor; f.transform.localScale = new Vector3(6, 1, 6);
            var p = GameObject.CreatePrimitive(PrimitiveType.Cylinder); p.name = "Pedestal"; Object.DestroyImmediate(p.GetComponent<Collider>());
            p.GetComponent<Renderer>().sharedMaterial = ped; p.transform.position = new Vector3(0, 0.03f, 0); p.transform.localScale = new Vector3(0.9f, 0.03f, 0.9f);

            var man = Mannequin.Build("GameTech/Rimlight", Dir, (m, part) =>
            {
                m.SetColor("_BaseColor", part == "skin" ? new Color(0.72f, 0.6f, 0.52f) : new Color(0.16f, 0.18f, 0.23f));
                m.SetFloat("_Smoothness", 0.3f);
            });
            man.transform.position = new Vector3(0, 0.06f, 0);
            var mats = man.GetComponentInChildren<Renderer>().sharedMaterials;

            var srcCam = SceneKit.Cam("Scene camera", new Rect(0, 0, 1, 1), 34, new Color(0.012f, 0.014f, 0.02f), -10);
            srcCam.clearFlags = CameraClearFlags.SolidColor; srcCam.cullingMask = ~(1 << 8); srcCam.farClipPlane = 100f; srcCam.nearClipPlane = 0.05f;
            SceneKit.Grade(Dir, 0.6f, 0f, 10f, 5f, 0.3f, 0f);
            var display = SceneKit.Cam("Display camera", new Rect(0, 0, 1, 1), 40, Color.black, 10, false);
            display.cullingMask = 1 << 8; display.transform.position = new Vector3(0, -50, 0); display.tag = "MainCamera";

            var compose = new Material(Shader.Find("Hidden/GT/RimCompose")); AssetDatabase.CreateAsset(compose, Dir + "Compose.mat");
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Display quad"; Object.DestroyImmediate(quad.GetComponent<Collider>()); quad.layer = 8;
            var qm = Object.Instantiate(quad.GetComponent<MeshFilter>().sharedMesh); qm.name = "DisplayQuad"; qm.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
            AssetDatabase.CreateAsset(qm, Dir + "DisplayQuad.asset");
            quad.GetComponent<MeshFilter>().sharedMesh = qm; quad.GetComponent<Renderer>().sharedMaterial = compose;
            quad.transform.position = display.transform.position + Vector3.forward;

            var dir = new GameObject("Director").AddComponent<RimDirector>();
            dir.src = srcCam; dir.compose = compose; dir.mats = mats; dir.character = man.transform;
            dir.title = LabelSpec.Create("", 0.012f, 0.935f, 30);
            dir.l1 = new LabelSpec[3]; dir.l2 = new LabelSpec[3];
            for (int i = 0; i < 3; i++) { dir.l1[i] = LabelSpec.Create("", 0.012f, 0.058f, 28); dir.l2[i] = LabelSpec.Create("", 0.012f, 0.018f, 22); }
        }
    }
}
