// Builds the DFAO scene from code: a stone courtyard (ground, back wall, side wall), two columns with a lintel,
// three steps, a stone bench, a big tilted block and a round boulder that rolls slowly. Each prop gets its analytic
// shape (box, cylinder, sphere, steps) for the local SDF bake, a low warm sun from behind and a blue sky ambient.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    public static class DFScene
    {
        const string Dir = "Assets/Techniques/dfao/";
        static readonly List<DFProp> Props = new List<DFProp>();

        static Material M(string name, Color c, float noise, float smooth)
        {
            var m = new Material(Shader.Find("GameTech/DFLit")); m.SetColor("_BaseColor", c); m.SetFloat("_Noise", noise); m.SetFloat("_Smoothness", smooth);
            AssetDatabase.CreateAsset(m, Dir + name + ".mat"); return m;
        }
        static GameObject Cube(string name, Material m, Vector3 pos, Vector3 size, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; Object.DestroyImmediate(go.GetComponent<Collider>());
            if (parent) go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = size; go.GetComponent<MeshRenderer>().sharedMaterial = m; return go;
        }
        static Transform Prop(string name, int shape, Vector3 size, Vector3 pos, float yaw)
        {
            var root = new GameObject(name).transform; root.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            Props.Add(new DFProp { t = root, shape = shape, size = size }); return root;
        }
        static Mesh Sphere(int seg, int rings)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int r = 0; r <= rings; r++) for (int s = 0; s <= seg; s++)
            {
                float th = Mathf.PI * r / rings, ph = 2 * Mathf.PI * s / seg;
                v.Add(new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph)) * 0.5f);
            }
            for (int r = 0; r < rings; r++) for (int s = 0; s < seg; s++)
            {
                int a = r * (seg + 1) + s, b = a + seg + 1;
                t.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
            }
            var m = new Mesh { name = "Sphere" }; m.SetVertices(v); m.SetTriangles(t, 0);
            var n = new Vector3[v.Count]; for (int i = 0; i < n.Length; i++) n[i] = v[i].normalized; m.normals = n; m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, Dir + "Sphere.asset"); return m;
        }

        public static void Build()
        {
            Props.Clear();
            RenderSettings.skybox = null; RenderSettings.fog = false;
            SceneKit.Shadows(25f);
            var stone = M("Stone", new Color(0.66f, 0.6f, 0.52f), 0.28f, 0.15f);
            var wallM = M("Wall", new Color(0.62f, 0.56f, 0.5f), 0.22f, 0.1f);
            var ground = M("Ground", new Color(0.55f, 0.52f, 0.48f), 0.3f, 0.2f);
            var dark = M("Dark stone", new Color(0.4f, 0.38f, 0.36f), 0.3f, 0.25f);

            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane); plane.name = "Ground"; Object.DestroyImmediate(plane.GetComponent<Collider>());
            plane.transform.position = new Vector3(0, 0, 2.5f); plane.transform.localScale = new Vector3(2.4f, 1, 2.4f); plane.GetComponent<MeshRenderer>().sharedMaterial = ground;

            Cube("Back wall", wallM, Vector3.zero, Vector3.one, Prop("Back wall", 0, new Vector3(12f, 3.2f, 0.4f), new Vector3(0, 1.6f, 7.2f), 0)).transform.localScale = new Vector3(12f, 3.2f, 0.4f);
            Cube("Side wall", wallM, Vector3.zero, Vector3.one, Prop("Side wall", 0, new Vector3(0.4f, 3.2f, 9f), new Vector3(-6.2f, 1.6f, 2.5f), 0)).transform.localScale = new Vector3(0.4f, 3.2f, 9f);
            foreach (float x in new[] { -2f, 2f })
            {
                var col = GameObject.CreatePrimitive(PrimitiveType.Cylinder); col.name = "Column"; Object.DestroyImmediate(col.GetComponent<Collider>());
                col.transform.SetParent(Prop("Column", 1, new Vector3(0.7f, 3.2f, 0.7f), new Vector3(x, 1.6f, 3f), 0), false);
                col.transform.localScale = new Vector3(0.7f, 1.6f, 0.7f); col.GetComponent<MeshRenderer>().sharedMaterial = stone;
            }
            Cube("Lintel", stone, Vector3.zero, new Vector3(4.9f, 0.4f, 0.7f), Prop("Lintel", 0, new Vector3(4.9f, 0.4f, 0.7f), new Vector3(0, 3.4f, 3f), 0));
            var steps = Prop("Steps", 3, new Vector3(4.2f, 0.6f, 1.2f), new Vector3(0, 0.3f, 5.8f), 0);
            for (int k = 0; k < 3; k++)
            {
                float y0 = -0.3f, y1 = -0.3f + (k + 1) * 0.2f, z0 = -0.6f + k * 0.4f, z1 = 0.6f;
                Cube("Step " + k, stone, new Vector3(0, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f), new Vector3(4.2f, y1 - y0, z1 - z0), steps);
            }
            Cube("Bench", stone, Vector3.zero, new Vector3(2.0f, 0.45f, 0.6f), Prop("Bench", 0, new Vector3(2.0f, 0.45f, 0.6f), new Vector3(-3.2f, 0.225f, 0.9f), 8f));
            Cube("Block", dark, Vector3.zero, new Vector3(1.6f, 1.2f, 1.2f), Prop("Block", 0, new Vector3(1.6f, 1.2f, 1.2f), new Vector3(3.3f, 0.6f, 0.9f), 25f));
            var boulder = Prop("Boulder", 2, new Vector3(1.1f, 1.1f, 1.1f), new Vector3(-1.1f, 0.55f, 0.9f), 0);
            var bs = new GameObject("Boulder mesh"); bs.transform.SetParent(boulder, false); bs.transform.localScale = Vector3.one * 1.1f;
            bs.AddComponent<MeshFilter>().sharedMesh = Sphere(96, 48); bs.AddComponent<MeshRenderer>().sharedMaterial = dark;

            var sun = SceneKit.Sun("Sun", new Vector3(30f, -145f, 0f), new Color(1f, 0.8f, 0.6f), 2.8f, true);
            sun.shadowBias = 0.05f; sun.shadowNormalBias = 0.4f;
            SceneKit.Grade(Dir, 0.3f, 0f, 14f, 6f, 0.3f, 0.05f);

            var dir = new GameObject("Director").AddComponent<DFDirector>();
            dir.props = Props.ToArray(); dir.mover = boulder;
            dir.build = AssetDatabase.LoadAssetAtPath<ComputeShader>(Dir + "Shaders/DFBuild.compute");
            var bg = new Color(0.45f, 0.55f, 0.72f);
            dir.camA = SceneKit.Cam("Cam A", new Rect(0, 0, 1, 1), 42f, bg, -5, false);
            dir.camB = SceneKit.Cam("Cam B", new Rect(0, 0, 1, 1), 42f, bg, -6, false);
            foreach (var c in new[] { dir.camA, dir.camB }) { c.cullingMask = ~(1 << 10); c.nearClipPlane = 0.05f; c.farClipPlane = 60f; }

            var disp = new Material(Shader.Find("GameTech/DFComposite")); AssetDatabase.CreateAsset(disp, Dir + "Display.mat"); dir.display = disp;
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
            Debug.Log($"[gt] built: {Props.Count} props, compute {(dir.build != null)}, lit {Shader.Find("GameTech/DFLit").isSupported}");
        }
    }
}
