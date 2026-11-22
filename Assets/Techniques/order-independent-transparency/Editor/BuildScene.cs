// Builds the OIT scene from code: a dark studio with a glossy floor and a gradient backdrop, a sculpture of six tinted
// glass panes that cross on one vertical axis, three two-sided tinted glass bottles (one mesh each), a matte white
// sphere on a plinth behind the glass as the solid subject, two scene cameras (all layers / opaque only) and an
// orthographic display camera with a full-screen composite quad. Glass is on layer 8, the display on layer 10.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GameTech.Posts
{
    public static class OITScene
    {
        const string Dir = "Assets/Techniques/order-independent-transparency/";

        static GameObject Obj(string name, Mesh m, Material mat, Vector3 p, Quaternion r, Vector3 s, int layer = 0)
        {
            var go = new GameObject(name); go.layer = layer; go.transform.SetPositionAndRotation(p, r); go.transform.localScale = s;
            go.AddComponent<MeshFilter>().sharedMesh = m; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            if (layer == 8) { mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false; }
            return go;
        }
        static Mesh Prim(PrimitiveType p) { var g = GameObject.CreatePrimitive(p); var m = g.GetComponent<MeshFilter>().sharedMesh; Object.DestroyImmediate(g); return m; }

        // closed lathe from a (radius, height) profile: the control points are smoothed with Catmull-Rom and the
        // normals come from the profile tangent, so the glass has no faceting and no seam
        static Mesh Lathe(string name, Vector2[] ctl, int seg)
        {
            var prof = new List<Vector2>();
            for (int i = 0; i < ctl.Length - 1; i++)
            {
                Vector2 p0 = ctl[Mathf.Max(i - 1, 0)], p1 = ctl[i], p2 = ctl[i + 1], p3 = ctl[Mathf.Min(i + 2, ctl.Length - 1)];
                for (int k = 0; k < 8; k++)
                {
                    float t = k / 8f, t2 = t * t, t3 = t2 * t;
                    prof.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }
            prof.Add(ctl[ctl.Length - 1]);
            int n = prof.Count; var pn = new Vector2[n];
            for (int i = 0; i < n; i++) { var d = prof[Mathf.Min(i + 1, n - 1)] - prof[Mathf.Max(i - 1, 0)]; pn[i] = new Vector2(d.y, -d.x).normalized; }
            var v = new List<Vector3>(); var nr = new List<Vector3>(); var tri = new List<int>();
            for (int s = 0; s <= seg; s++)
            {
                float a = s * Mathf.PI * 2f / seg, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                for (int i = 0; i < n; i++) { v.Add(new Vector3(ca * prof[i].x, prof[i].y, sa * prof[i].x)); nr.Add(new Vector3(ca * pn[i].x, pn[i].y, sa * pn[i].x)); }
            }
            for (int s = 0; s < seg; s++)
                for (int i = 0; i < n - 1; i++)
                {
                    int a = s * n + i, b = (s + 1) * n + i;
                    tri.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
                }
            var m = new Mesh { name = name }; m.SetVertices(v); m.SetNormals(nr); m.SetTriangles(tri, 0); m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, Dir + name + ".asset"); return m;
        }

        static Material Glass(string name, Color c)
        {
            var m = new Material(Shader.Find("GameTech/OITGlass")); m.SetColor("_Color", c); m.renderQueue = 3000;
            AssetDatabase.CreateAsset(m, Dir + name + ".mat"); return m;
        }

        static Texture2D Gradient()
        {
            int n = 256; var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, h = y / (float)n;
                var c = Color.Lerp(new Color(0.06f, 0.08f, 0.1f), new Color(0.015f, 0.02f, 0.035f), h);
                float g = Mathf.Exp(-(Mathf.Pow((u - 0.5f) * 2.4f, 2) + Mathf.Pow((h - 0.42f) * 3.2f, 2)));
                c += new Color(0.6f, 0.34f, 0.15f) * g * 0.6f;
                px[y * n + x] = Color.Lerp(new Color(0.03f, 0.028f, 0.028f), c, Mathf.SmoothStep(0f, 1f, h / 0.18f));
            }
            t.SetPixels(px); t.Apply(); t.wrapMode = TextureWrapMode.Clamp;
            AssetDatabase.CreateAsset(t, Dir + "Backdrop.asset"); return t;
        }

        public static void Build()
        {
            RenderSettings.skybox = null; RenderSettings.fog = false;
            SceneKit.Ambient(new Color(0.12f, 0.12f, 0.14f), new Color(0.09f, 0.08f, 0.08f), new Color(0.03f, 0.03f, 0.03f));
            SceneKit.Shadows(20f);
            var cube = Prim(PrimitiveType.Cube); var cyl = Prim(PrimitiveType.Cylinder); var sph = Prim(PrimitiveType.Sphere); var quad = Prim(PrimitiveType.Quad);

            var floorM = SceneKit.Mat(Dir, "Floor", "Universal Render Pipeline/Lit", new Color(0.04f, 0.038f, 0.037f), 0, 0.38f);
            var plinthM = SceneKit.Mat(Dir, "Plinth", "Universal Render Pipeline/Lit", new Color(0.2f, 0.19f, 0.18f), 0, 0.35f);
            var ceramicM = SceneKit.Mat(Dir, "Ceramic", "Universal Render Pipeline/Lit", new Color(0.86f, 0.84f, 0.8f), 0, 0.55f);
            var backM = new Material(Shader.Find("Universal Render Pipeline/Unlit")); backM.SetTexture("_BaseMap", Gradient()); backM.SetColor("_BaseColor", Color.white);
            AssetDatabase.CreateAsset(backM, Dir + "Backdrop.mat");

            Obj("Floor", cube, floorM, new Vector3(0, -0.05f, 0), Quaternion.identity, new Vector3(30f, 0.1f, 30f));
            var bd = Obj("Backdrop", quad, backM, new Vector3(0, 6.9f, 9f), Quaternion.identity, new Vector3(34f, 14f, 1f));
            bd.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // the solid subject behind the glass
            Obj("Plinth", cyl, plinthM, new Vector3(0f, 0.3f, 2.3f), Quaternion.identity, new Vector3(0.75f, 0.3f, 0.75f));
            Obj("Sphere", sph, ceramicM, new Vector3(0f, 1.02f, 2.3f), Quaternion.identity, Vector3.one * 0.85f);
            Obj("Base", cyl, plinthM, new Vector3(0, 0.08f, 0), Quaternion.identity, new Vector3(1.7f, 0.08f, 1.7f));

            // six crossing panes (one quad each, two-sided in the shader), all centred on the same axis
            var paneCols = new[] {
                new Color(1.0f, 0.12f, 0.16f, 0.58f), new Color(1.0f, 0.62f, 0.05f, 0.58f), new Color(0.05f, 0.7f, 1.0f, 0.58f), new Color(0.5f, 0.18f, 1.0f, 0.58f) };
            for (int i = 0; i < 4; i++)
                Obj("Pane " + i, quad, Glass("Pane" + i, paneCols[i]), new Vector3(0, 1.0f, 0), Quaternion.Euler(0, 20f + i * 45f, 0), new Vector3(1.5f, 1.65f, 1f), 8);

            // three bottles: one lathe mesh each, so front and back faces are drawn in index order
            var prof = new[] { new Vector2(0f, 0f), new Vector2(0.21f, 0f), new Vector2(0.24f, 0.04f), new Vector2(0.25f, 0.5f), new Vector2(0.22f, 0.62f),
                new Vector2(0.12f, 0.72f), new Vector2(0.085f, 0.8f), new Vector2(0.08f, 0.98f), new Vector2(0.095f, 1.0f), new Vector2(0.0f, 1.0f) };
            var bottle = Lathe("Bottle", prof, 64);
            Obj("Bottle A", bottle, Glass("BottleA", new Color(0.15f, 0.85f, 0.45f, 0.42f)), new Vector3(-1.35f, 0f, -0.45f), Quaternion.identity, new Vector3(1.1f, 1.25f, 1.1f), 8);
            Obj("Bottle B", bottle, Glass("BottleB", new Color(1.0f, 0.6f, 0.15f, 0.42f)), new Vector3(1.3f, 0f, -0.3f), Quaternion.identity, new Vector3(1.25f, 1.45f, 1.25f), 8);
            Obj("Bottle C", bottle, Glass("BottleC", new Color(0.25f, 0.45f, 1.0f, 0.42f)), new Vector3(-0.72f, 0f, -1.05f), Quaternion.identity, new Vector3(0.85f, 0.9f, 0.85f), 8);

            var key = SceneKit.Sun("Key", new Vector3(38f, -42f, 0f), new Color(1f, 0.83f, 0.66f), 1.5f, true);
            var fill = SceneKit.Sun("Fill", new Vector3(20f, 130f, 0f), new Color(0.6f, 0.7f, 0.9f), 0.25f, false);
            SceneKit.Grade(Dir, 0.45f, 0.1f, 12f, 8f, 0.32f, 0.08f);

            var dir = new GameObject("Director").AddComponent<OITDirector>();
            dir.key = key; dir.fill = fill; dir.ambient = new Color(0.16f, 0.18f, 0.22f);
            var target = new GameObject("Orbit target"); target.transform.position = new Vector3(0, 0.95f, 0); dir.orbitTarget = target.transform;
            dir.camOdt = SceneKit.Cam("Cam ODT", new Rect(0, 0, 1, 1), 40f, new Color(0.02f, 0.02f, 0.03f), -5, false);
            dir.camOpaque = SceneKit.Cam("Cam opaque", new Rect(0, 0, 1, 1), 40f, new Color(0.02f, 0.02f, 0.03f), -6, false);
            dir.camOdt.cullingMask = ~(1 << 10); dir.camOpaque.cullingMask = ~((1 << 10) | (1 << 8));
            foreach (var c in new[] { dir.camOdt, dir.camOpaque }) { c.nearClipPlane = 0.05f; c.farClipPlane = 60f; }
            dir.eyeDepth = new Material(Shader.Find("GameTech/OITEyeDepth")); AssetDatabase.CreateAsset(dir.eyeDepth, Dir + "EyeDepth.mat");

            var disp = new Material(Shader.Find("GameTech/OITComposite")); AssetDatabase.CreateAsset(disp, Dir + "Display.mat"); dir.display = disp;
            var dcam = SceneKit.Cam("Display cam", new Rect(0, 0, 1, 1), 60f, Color.black, 0, true);
            dcam.orthographic = true; dcam.orthographicSize = 0.5f; dcam.cullingMask = 1 << 10; dcam.transform.position = new Vector3(500f, 0f, -1f);
            dcam.nearClipPlane = 0.01f; dcam.farClipPlane = 5f;
            var dq = Obj("Display quad", quad, disp, new Vector3(500f, 0f, 0f), Quaternion.identity, new Vector3(16f / 9f, 1f, 1f), 10);
            dq.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            dir.title = LabelSpec.Create("", 0.012f, 0.93f, 40);
            dir.l1 = LabelSpec.Create("", 0.012f, 0.058f, 34); dir.l2 = LabelSpec.Create("", 0.012f, 0.018f, 30);
            dir.m1 = LabelSpec.Create("", 0.512f, 0.058f, 34); dir.m2 = LabelSpec.Create("", 0.512f, 0.018f, 30);
            AssetDatabase.SaveAssets();
            Debug.Log($"[gt] built: oit glass {Shader.Find("GameTech/OITGlass").isSupported}, composite {disp.shader.isSupported}, depth {dir.eyeDepth.shader.isSupported}");
        }
    }
}
