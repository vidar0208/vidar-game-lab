// Builds the deep shadow map scene from code: a dark stage, a light floor that receives the deep shadow map, a wooden
// frame (rod + two posts), three fibre tassels hanging from the rod (generated on the GPU by the director), a hard warm
// back light, the two scene cameras and the display camera with the composite quad.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    public static class DSMScene
    {
        const string Dir = "Assets/Techniques/deep-shadow-map/";

        static Mesh StrandMesh(int segs)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (int k = 0; k < segs; k++) { v.Add(Vector3.zero); v.Add(Vector3.zero); uv.Add(new Vector2(-1, k)); uv.Add(new Vector2(1, k)); }
            for (int k = 0; k < segs - 1; k++) { int a = k * 2; tri.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 }); }
            var m = new Mesh { name = "Strand", indexFormat = IndexFormat.UInt32 }; m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 100f);
            AssetDatabase.CreateAsset(m, Dir + "Strand.asset"); return m;
        }

        public static void Build()
        {
            RenderSettings.skybox = null;
            var bg = new Color(0.025f, 0.03f, 0.04f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogStartDistance = 4.5f; RenderSettings.fogEndDistance = 13f; RenderSettings.fogColor = bg;
            SceneKit.Ambient(new Color(0.08f, 0.09f, 0.11f), new Color(0.07f, 0.07f, 0.08f), new Color(0.05f, 0.045f, 0.04f));

            var dir = new GameObject("Director").AddComponent<DSMDirector>();
            var f = dir.lightTravel.normalized;
            var sun = SceneKit.Sun("Back light", Quaternion.LookRotation(f).eulerAngles, new Color(1f, 0.83f, 0.62f), 2.2f, false);

            var floorM = new Material(Shader.Find("GameTech/DSMGround")); AssetDatabase.CreateAsset(floorM, Dir + "Floor.mat");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.name = "Floor"; Object.DestroyImmediate(floor.GetComponent<Collider>());
            floor.transform.localScale = new Vector3(2.4f, 1f, 2.4f); floor.GetComponent<MeshRenderer>().sharedMaterial = floorM;

            var wood = SceneKit.Mat(Dir, "Wood", "Universal Render Pipeline/Lit", new Color(0.16f, 0.09f, 0.05f), 0f, 0.45f);
            var rod = GameObject.CreatePrimitive(PrimitiveType.Cylinder); rod.name = "Rod"; Object.DestroyImmediate(rod.GetComponent<Collider>());
            rod.transform.SetPositionAndRotation(new Vector3(0f, 1.6f, 0f), Quaternion.Euler(0, 0, 90)); rod.transform.localScale = new Vector3(0.05f, 1.38f, 0.05f);
            rod.GetComponent<MeshRenderer>().sharedMaterial = wood;
            foreach (float x in new[] { -1.35f, 1.35f })
            {
                var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder); post.name = "Post"; Object.DestroyImmediate(post.GetComponent<Collider>());
                post.transform.position = new Vector3(x, 0.81f, 0f); post.transform.localScale = new Vector3(0.06f, 0.81f, 0.06f);
                post.GetComponent<MeshRenderer>().sharedMaterial = wood;
            }
            // wraps where the tassels are tied
            var wrapM = SceneKit.Mat(Dir, "Wrap", "Universal Render Pipeline/Lit", new Color(0.5f, 0.08f, 0.05f), 0f, 0.35f);
            foreach (float x in new[] { -0.72f, 0f, 0.72f })
            {
                var w = GameObject.CreatePrimitive(PrimitiveType.Cylinder); w.name = "Wrap"; Object.DestroyImmediate(w.GetComponent<Collider>());
                w.transform.position = new Vector3(x, 1.46f, 0f); w.transform.localScale = new Vector3(0.05f, 0.06f, 0.05f); w.GetComponent<MeshRenderer>().sharedMaterial = wrapM;
            }

            SceneKit.Grade(Dir, 0.55f, 0.1f, 14f, 8f, 0.32f, 0.05f);

            dir.cs = AssetDatabase.LoadAssetAtPath<ComputeShader>(Dir + "Resources/DSMFibres.compute");
            dir.fibreMat = new Material(Shader.Find("GameTech/DSMFibre")); AssetDatabase.CreateAsset(dir.fibreMat, Dir + "Fibre.mat");
            dir.strandMesh = StrandMesh(dir.segs);
            dir.camA = SceneKit.Cam("Cam A", new Rect(0, 0, 1, 1), 42f, bg, -5, false);
            dir.camB = SceneKit.Cam("Cam B", new Rect(0, 0, 1, 1), 42f, bg, -6, false);
            foreach (var c in new[] { dir.camA, dir.camB }) { c.nearClipPlane = 0.03f; c.farClipPlane = 40f; c.cullingMask = ~(1 << 10); c.allowMSAA = true; }

            var disp = new Material(Shader.Find("GameTech/DSMComposite")); AssetDatabase.CreateAsset(disp, Dir + "Display.mat"); dir.display = disp;
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
            Debug.Log($"[gt] built: fibre {dir.fibreMat.shader.isSupported}, ground {floorM.shader.isSupported}, compute {(dir.cs != null)}, composite {disp.shader.isSupported}");
        }
    }
}
