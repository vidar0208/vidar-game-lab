// 79 texture painting: a dark warehouse corner built from code. Wall, floor, sphere, barrel and a twisted column
// each exist twice: layer 9 = painted (URP Lit showing its own colour / normal RenderTextures, mesh collider,
// InkSurface), layer 8 = plain twin for the decal-quad comparison. A second set-up at x = 30 holds three wall
// panels for the colour / normal / drip comparison. All meshes, textures and brushes are generated from code.
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    public static class TexturePaintScene
    {
        const string Dir = "Assets/Techniques/texture-painting/";
        const string Lit = "Universal Render Pipeline/Lit";

        // ---------------------------------------------------------------- meshes with non-overlapping UVs
        class MB
        {
            public readonly System.Collections.Generic.List<Vector3> v = new System.Collections.Generic.List<Vector3>(), n = new System.Collections.Generic.List<Vector3>();
            public readonly System.Collections.Generic.List<Vector2> uv = new System.Collections.Generic.List<Vector2>();
            public readonly System.Collections.Generic.List<int> t = new System.Collections.Generic.List<int>();
            public int V(Vector3 p, Vector3 nn, Vector2 u) { v.Add(p); n.Add(nn); uv.Add(u); return v.Count - 1; }
            public void Tri(int a, int b, int c)
            {   // front face = cross(b - a, c - a) along the vertex normals
                if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), n[a] + n[b] + n[c]) < 0) { int k = b; b = c; c = k; }
                t.Add(a); t.Add(b); t.Add(c);
            }
            public void Grid(int cols, int rows, System.Func<int, int, int> idx)
            {
                for (int j = 0; j < rows; j++) for (int i = 0; i < cols; i++)
                { int a = idx(i, j), b = idx(i + 1, j), c = idx(i + 1, j + 1), d = idx(i, j + 1); Tri(a, b, c); Tri(a, c, d); }
            }
            public Mesh Build(string name)
            {
                var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
                m.RecalculateTangents(); m.RecalculateBounds();
                AssetDatabase.CreateAsset(m, Dir + name + ".asset");
                return m;
            }
        }

        static Mesh Plane(string name, float w, float h, bool ground)
        {
            var b = new MB(); var nn = ground ? Vector3.up : Vector3.forward;
            int I(int i, int j) => i + j * 2;
            for (int j = 0; j <= 1; j++) for (int i = 0; i <= 1; i++)
                b.V(ground ? new Vector3((i - 0.5f) * w, 0, (0.5f - j) * h) : new Vector3((i - 0.5f) * w, (j - 0.5f) * h, 0), nn, new Vector2(i, j));
            b.Grid(1, 1, I);
            return b.Build(name);
        }

        // UV sphere, u around (seam at local +x), v from bottom to top
        static Mesh Sphere(string name, float r, int seg = 96, int rings = 48)
        {
            var b = new MB();
            for (int j = 0; j <= rings; j++) for (int i = 0; i <= seg; i++)
            {
                float th = Mathf.PI * (1 - (float)j / rings), ph = 2 * Mathf.PI * i / seg;
                var d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                b.V(d * r, d, new Vector2((float)i / seg, (float)j / rings));
            }
            b.Grid(seg, rings, (i, j) => i + j * (seg + 1));
            return b.Build(name);
        }

        // barrel side, u around (seam at local -z, the back), v up
        static Mesh Barrel(string name, float r, float h, int seg = 96, int rows = 24)
        {
            var b = new MB();
            for (int j = 0; j <= rows; j++) for (int i = 0; i <= seg; i++)
            {
                float y = h * j / rows, ph = 2 * Mathf.PI * i / seg - Mathf.PI / 2, rr = r * (1 + 0.06f * Mathf.Sin(Mathf.PI * j / rows));
                var d = new Vector3(Mathf.Cos(ph), 0, Mathf.Sin(ph));
                b.V(d * rr + Vector3.up * y, d, new Vector2((float)i / seg, (float)j / rows));
            }
            b.Grid(seg, rows, (i, j) => i + j * (seg + 1));
            return b.Build(name);
        }

        static Mesh Cap(string name, float r, float y, int seg = 96)
        {
            var b = new MB(); int c = b.V(new Vector3(0, y, 0), Vector3.up, new Vector2(0.5f, 0.5f));
            for (int i = 0; i <= seg; i++) { float ph = 2 * Mathf.PI * i / seg; b.V(new Vector3(Mathf.Cos(ph) * r, y, Mathf.Sin(ph) * r), Vector3.up, Vector2.zero); }
            for (int i = 0; i < seg; i++) b.Tri(c, 1 + i, 2 + i);
            return b.Build(name);
        }

        // twisted column: rounded square section, half width a, twisted by 'twist' degrees over its height
        static Mesh Column(string name, float a, float h, float twist, int seg = 128, int rows = 72)
        {
            var b = new MB();
            Vector2 P(float s)   // s in [0,1) around a rounded square (superellipse)
            {
                float ph = 2 * Mathf.PI * s - Mathf.PI / 2, c = Mathf.Cos(ph), sn = Mathf.Sin(ph);
                return new Vector2(Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 0.5f), Mathf.Sign(sn) * Mathf.Pow(Mathf.Abs(sn), 0.5f)) * a;
            }
            for (int j = 0; j <= rows; j++) for (int i = 0; i <= seg; i++)
            {
                float y = h * j / rows, s = (float)i / seg; var q = Quaternion.Euler(0, twist * j / rows, 0);
                var p = P(s); var p2 = P(s + 0.002f); var tng = new Vector3(p2.x - p.x, 0, p2.y - p.y);
                var nn = q * new Vector3(tng.z, 0, -tng.x).normalized;
                if (Vector3.Dot(nn, q * new Vector3(p.x, 0, p.y)) < 0) nn = -nn;
                b.V(q * new Vector3(p.x, 0, p.y) + Vector3.up * y, nn, new Vector2(s, (float)j / rows));
            }
            b.Grid(seg, rows, (i, j) => i + j * (seg + 1));
            return b.Build(name);
        }

        static Material PaintMat(string name)
        {
            var m = SceneKit.Mat(Dir, name, Lit, Color.white, 0f, 0.85f);
            m.SetFloat("_SmoothnessTextureChannel", 1); m.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            m.EnableKeyword("_NORMALMAP"); m.SetFloat("_BumpScale", 1f);
            return m;
        }

        static GameObject Obj(string name, Mesh mesh, Material mat, Vector3 pos, Quaternion rot, int layer)
        {
            var go = SceneKit.Part(name, mesh, mat, pos, rot); go.layer = layer; return go;
        }

        public static void Build()
        {
            SceneKit.Ambient(new Color(0.16f, 0.18f, 0.22f), new Color(0.10f, 0.10f, 0.11f), new Color(0.05f, 0.05f, 0.05f));
            SceneKit.Shadows(30f);
            SceneKit.Sun("Sun", new Vector3(36, 205, 0), new Color(1f, 0.93f, 0.84f), 1.9f, true);
            RenderSettings.fog = false;

            var surfs = new System.Collections.Generic.List<InkSurface>(); var plains = new System.Collections.Generic.List<Renderer>(); var kinds = new System.Collections.Generic.List<string>();
            InkSurface Pair(string name, Mesh mesh, string kind, Vector2 metres, Vector3 pos, Quaternion rot, bool twin = true, bool normal = true, bool fluid = false)
            {
                Renderer plain = null;
                if (twin) plain = Obj(name + " plain", mesh, PaintMat(name + "Plain"), pos, rot, 8).GetComponent<Renderer>();
                var go = Obj(name, mesh, PaintMat(name + "Paint"), pos, rot, 9);
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
                var s = go.AddComponent<InkSurface>(); s.uvToMetres = metres; s.paintNormal = normal; s.fluid = fluid;
                surfs.Add(s); plains.Add(plain); kinds.Add(kind);
                return s;
            }

            // ---- main corner
            Pair("Wall", Plane("WallQuad", 8f, 4.5f, false), "wall", new Vector2(8f, 4.5f), new Vector3(0, 2.25f, -3f), Quaternion.identity);
            Pair("Floor", Plane("FloorQuad", 8f, 8f, true), "floor", new Vector2(8f, 8f), new Vector3(0, 0, 1f), Quaternion.identity);
            var sph = Pair("Sphere", Sphere("SphereMesh", 0.55f), "plaster", new Vector2(2 * Mathf.PI * 0.55f, Mathf.PI * 0.55f), new Vector3(-1.6f, 0.55f, -1.2f), Quaternion.Euler(0, -45, 0));
            Pair("Barrel", Barrel("BarrelMesh", 0.4f, 1.0f), "metal", new Vector2(2 * Mathf.PI * 0.4f, 1.0f), new Vector3(1.3f, 0, -1.6f), Quaternion.identity);
            Pair("Column", Column("ColumnMesh", 0.22f, 1.8f, 180f), "stone", new Vector2(1.76f, 1.8f), new Vector3(0.1f, 0, -2.0f), Quaternion.identity);
            var dark = SceneKit.Mat(Dir, "Dark", Lit, new Color(0.08f, 0.08f, 0.09f), 0f, 0.3f);
            var metal = SceneKit.Mat(Dir, "Metal", Lit, new Color(0.09f, 0.1f, 0.11f), 0.8f, 0.55f);
            SceneKit.Part("Barrel lid", Cap("BarrelCap", 0.4f, 1.0f), metal, new Vector3(1.3f, 0, -1.6f), Quaternion.identity);
            SceneKit.Part("Column cap", new MeshKit().Box(new Vector3(0, 1.84f, 0), new Vector3(0.62f, 0.08f, 0.62f)).Build("ColumnCap", 10f), dark, new Vector3(0.1f, 0, -2f), Quaternion.Euler(0, 180, 0));
            SceneKit.Part("Side wall", new MeshKit().Box(new Vector3(-4.15f, 2.25f, 0.5f), new Vector3(0.3f, 4.5f, 7f)).Build("SideWall", 10f), dark, Vector3.zero, Quaternion.identity);
            SceneKit.Part("Wall back", new MeshKit().Box(new Vector3(0, 2.25f, -3.16f), new Vector3(8.6f, 4.5f, 0.3f)).Build("WallBack", 10f), dark, Vector3.zero, Quaternion.identity);

            // ---- the launcher
            var launcher = new GameObject("Launcher").transform; launcher.position = new Vector3(3.2f, 1.1f, 3.6f); launcher.LookAt(new Vector3(-0.5f, 1f, -1.5f));
            var noz = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Object.DestroyImmediate(noz.GetComponent<Collider>());
            noz.transform.SetParent(launcher, false); noz.transform.localRotation = Quaternion.Euler(90, 0, 0); noz.transform.localScale = new Vector3(0.16f, 0.5f, 0.16f);
            noz.GetComponent<Renderer>().sharedMaterial = metal;
            SceneKit.Part("Launcher stand", new MeshKit().Box(new Vector3(0, 0.5f, 0), new Vector3(0.3f, 1.0f, 0.3f)).Build("Stand", 10f), dark, new Vector3(3.2f, 0, 3.6f), Quaternion.identity);

            // ---- panels for the colour / normal / drip comparison (only the painted set, seen by the full camera)
            Pair("Panel colour", Plane("PanelQuad", 1.8f, 1.8f, false), "panel", new Vector2(1.8f, 1.8f), new Vector3(32.1f, 1.15f, -3f), Quaternion.identity, false, false, false);
            Pair("Panel normal", AssetDatabase.LoadAssetAtPath<Mesh>(Dir + "PanelQuad.asset"), "panel", new Vector2(1.8f, 1.8f), new Vector3(30f, 1.15f, -3f), Quaternion.identity, false, true, false);
            var drip = Pair("Panel drip", AssetDatabase.LoadAssetAtPath<Mesh>(Dir + "PanelQuad.asset"), "panel", new Vector2(1.8f, 1.8f), new Vector3(27.9f, 1.15f, -3f), Quaternion.identity, false, true, true);
            drip.easeOfDripping = InkDirector.EaseOfDripping; drip.flowingForce = InkDirector.FlowingForce; drip.horizontalSpread = InkDirector.HorizontalSpread;
            SceneKit.Part("Panel backing", new MeshKit().Box(new Vector3(30f, 1.6f, -3.2f), new Vector3(8f, 3.2f, 0.3f)).Build("Backing", 10f), dark, Vector3.zero, Quaternion.identity);
            SceneKit.Part("Panel floor", new MeshKit().Box(new Vector3(30f, -0.05f, 0f), new Vector3(9f, 0.1f, 7f)).Build("PanelFloor", 10f), SceneKit.Mat(Dir, "PanelFloorMat", Lit, new Color(0.12f, 0.12f, 0.12f), 0f, 0.3f), Vector3.zero, Quaternion.identity);

            // ---- RenderTexture view (layer 10, its own orthographic camera)
            var view = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(view.GetComponent<Collider>());
            view.name = "RT view"; view.layer = 10; view.transform.position = new Vector3(0, -50, 0); view.transform.localScale = new Vector3(2f, 1f, 1f);
            var unlit = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(unlit, Dir + "RTView.mat");
            view.GetComponent<Renderer>().sharedMaterial = unlit; view.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            var dr = new GameObject("Director").AddComponent<InkDirector>();
            dr.surfaces = surfs.ToArray(); dr.plain = plains.ToArray(); dr.kinds = kinds.ToArray(); dr.sphere = sph; dr.launcher = launcher; dr.rtView = view.GetComponent<Renderer>();
            dr.blobMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(dr.blobMat, Dir + "Blob.mat");
            dr.decalMat = new Material(Shader.Find("GameTech/InkDecalQuad")); AssetDatabase.CreateAsset(dr.decalMat, Dir + "Decal.mat");
            var bg = new Color(0.035f, 0.038f, 0.045f);
            dr.camL = SceneKit.Cam("Camera left", new Rect(0, 0, 0.5f, 1), 50, bg, 0); dr.camL.cullingMask = ~((1 << 9) | (1 << 10));
            dr.camR = SceneKit.Cam("Camera right", new Rect(0.5f, 0, 0.5f, 1), 50, bg, 1); dr.camR.cullingMask = ~((1 << 8) | (1 << 10));
            dr.camFull = SceneKit.Cam("Camera full", new Rect(0, 0, 1, 1), 45, bg, 2); dr.camFull.cullingMask = ~((1 << 8) | (1 << 10)); dr.camFull.tag = "MainCamera";
            dr.camRT = SceneKit.Cam("Camera RT view", new Rect(0.56f, 0.13f, 0.40f, 0.3556f), 30, new Color(0.02f, 0.02f, 0.02f), 5, false);
            dr.camRT.orthographic = true; dr.camRT.orthographicSize = 0.5f; dr.camRT.cullingMask = 1 << 10;
            dr.camRT.transform.position = new Vector3(0, -50, -1); dr.camRT.transform.rotation = Quaternion.identity;

            SceneKit.Grade(Dir, 0.6f, 0.1f, 14f, 8f, 0.3f, 0.12f);

            const float LX = 0.03f, RX = 0.53f, Y = 0.05f;
            LabelSpec.Create("OFF: one flat decal quad per hit, gone after 2 s", LX, Y, 34).During(0.6f, 8f);
            LabelSpec.Create("ON: ink written into each object's RenderTexture", RX, Y, 34).During(0.6f, 8f);
            LabelSpec.Create("22 stamps per panel, each panel one 1024 x 1024 RenderTexture", LX, 0.92f, 34).During(8.2f, 15.5f);
            LabelSpec.Create("Colour only", 0.12f, Y, 34).During(8.2f, 15.5f);
            LabelSpec.Create("Colour + normal", 0.42f, Y, 34).During(8.2f, 15.5f);
            LabelSpec.Create("Colour + normal + height drip", 0.67f, Y, 34).During(8.2f, 15.5f);
            LabelSpec.Create("Sphere colour RenderTexture, 1024 x 512", 0.56f, 0.52f, 32).During(15.6f, 21.5f);
            LabelSpec.Create("u = 0 and u = 1 meet at the seam: a stamp there is cut", LX, 0.92f, 32).During(17.5f, 21.5f);
            dr.stampLabel = LabelSpec.Create("", LX, Y, 34).During(15.6f, 21.5f);
            dr.totalLabel = LabelSpec.Create("", LX, Y, 34).During(21.7f, 26f);
            Debug.Log("[gt] texture painting scene built");
        }
    }
}
