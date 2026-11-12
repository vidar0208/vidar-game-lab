// 78 ink painting: rocks, two pines, an arched bridge, a pavilion on a far mound and layered mountains, all built
// from code with welded (smooth) normals so the back-face outlines stay closed. IA stands on the bridge.
// Three copies of the scene: URP Lit (layer 8), ink with video switches (layer 8), ink (layer 9).
// Stroke texture: Stroke_3 from the source repository (MIT). Remap, noise and paper textures are generated here.
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GameTech.Posts
{
    public static class InkPaintScene
    {
        const string Dir = "Assets/Techniques/ink-painting/";
        const string Lit = "Universal Render Pipeline/Lit";
        const string Ink = "GameTech/InkPainting";

        // ---------------------------------------------------------------- noise
        static float H(int x, int y, int z, int s) { unchecked { uint h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177 + s * 1442695041); h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16; return (h & 0xFFFFFF) / 16777216f; } }
        static float N3(Vector3 p, int s)
        {
            int ix = Mathf.FloorToInt(p.x), iy = Mathf.FloorToInt(p.y), iz = Mathf.FloorToInt(p.z);
            float fx = p.x - ix, fy = p.y - iy, fz = p.z - iz; fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy); fz = fz * fz * (3 - 2 * fz);
            float L(int a, int b, int c) => H(ix + a, iy + b, iz + c, s);
            float x00 = Mathf.Lerp(L(0, 0, 0), L(1, 0, 0), fx), x10 = Mathf.Lerp(L(0, 1, 0), L(1, 1, 0), fx);
            float x01 = Mathf.Lerp(L(0, 0, 1), L(1, 0, 1), fx), x11 = Mathf.Lerp(L(0, 1, 1), L(1, 1, 1), fx);
            return Mathf.Lerp(Mathf.Lerp(x00, x10, fy), Mathf.Lerp(x01, x11, fy), fz);
        }
        static float F3(Vector3 p, int s) => 0.5f * N3(p, s) + 0.25f * N3(p * 2.1f, s + 1) + 0.125f * N3(p * 4.3f, s + 2) + 0.125f * N3(p * 8.7f, s + 3);
        static int Mod(int a, int n) => ((a % n) + n) % n;
        static float P2(float u, float v, int per, int s)
        {   // tileable 2D value noise, period 'per' cells over the unit square
            float x = u * per, y = v * per; int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y); float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float L(int a, int b) => H(Mod(ix + a, per), Mod(iy + b, per), 0, s);
            return Mathf.Lerp(Mathf.Lerp(L(0, 0), L(1, 0), fx), Mathf.Lerp(L(0, 1), L(1, 1), fx), fy);
        }

        // ---------------------------------------------------------------- textures (saved as assets)
        static Texture2D SaveTex(int w, int h, System.Func<float, float, Color> f, string name, bool linear, TextureWrapMode wrap)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true, linear) { name = name, wrapMode = wrap, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = f((x + 0.5f) / w, (y + 0.5f) / h);
            t.SetPixels(px); t.Apply(true);
            AssetDatabase.CreateAsset(t, Dir + name + ".asset");
            return t;
        }

        // ---------------------------------------------------------------- meshes (welded rings, smooth normals)
        class MB
        {
            public readonly List<Vector3> v = new List<Vector3>(); public readonly List<int> t = new List<int>();
            public int V(Vector3 p) { v.Add(p); return v.Count - 1; }
            public void Tri(int a, int b, int c) { t.Add(a); t.Add(b); t.Add(c); }
            // triangle facing away from a point inside the solid (front face = cross(b - a, c - a))
            public void OTri(int a, int b, int c, Vector3 inside)
            {
                var ctr = (v[a] + v[b] + v[c]) / 3f;
                if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), ctr - inside) < 0) { int k = b; b = c; c = k; }
                Tri(a, b, c);
            }
            // rows of 'seg' vertices (wrapping); rowCenter(j) is a point on the axis inside row j
            public void Rings(int start, int rows, int seg, System.Func<int, Vector3> rowCenter)
            {
                for (int j = 0; j < rows - 1; j++) for (int i = 0; i < seg; i++)
                {
                    int a = start + j * seg + i, b = start + j * seg + (i + 1) % seg, c = a + seg, d = b + seg;
                    var inside = (rowCenter(j) + rowCenter(j + 1)) * 0.5f;
                    OTri(a, c, b, inside); OTri(b, c, d, inside);
                }
            }
            public void Fan(int center, int ringStart, int seg, Vector3 inside)
            {
                for (int i = 0; i < seg; i++) OTri(center, ringStart + i, ringStart + (i + 1) % seg, inside);
            }
            public Mesh Build(string name)
            {
                var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals();
                m.RecalculateBounds();
                AssetDatabase.CreateAsset(m, Dir + name + ".asset");
                return m;
            }
        }

        // closed blob: sphere displaced by 3D noise, then scaled
        static Mesh Blob(string name, Vector3 scale, float amp, float freq, int seed, float flatBottom = -9f, int seg = 72, int rows = 40)
        {
            var b = new MB(); int top = b.V(Vector3.zero); int start = b.v.Count;
            for (int j = 1; j < rows; j++) for (int i = 0; i < seg; i++)
            {
                float th = Mathf.PI * j / rows, ph = 2 * Mathf.PI * i / seg;
                var d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                b.V(d);
            }
            int bot = b.V(Vector3.zero);
            for (int i = 0; i < b.v.Count; i++)
            {
                var d = i == top ? Vector3.up : i == bot ? Vector3.down : b.v[i];
                float r = 1 + amp * (F3(d * freq + Vector3.one * seed, seed) - 0.5f) * 2 + amp * 0.35f * (Mathf.Abs(N3(d * freq * 3.1f, seed + 7) - 0.5f) * 2 - 0.5f);
                var p = Vector3.Scale(d * r, scale);
                if (p.y < flatBottom) p.y = flatBottom;
                b.v[i] = p;
            }
            var mid = new Vector3(0, 0.0f, 0);
            b.Fan(top, start, seg, mid); b.Rings(start, rows - 1, seg, j => mid); b.Fan(bot, start + (rows - 2) * seg, seg, mid);
            return b.Build(name);
        }

        // tube along a polyline with radius per point, closed with end fans
        static void Tube(MB b, IList<Vector3> pts, System.Func<float, float> radius, int seg = 16)
        {
            int start = b.v.Count, n = pts.Count;
            Vector3 prevSide = Vector3.zero;
            for (int j = 0; j < n; j++)
            {
                var tng = (pts[Mathf.Min(j + 1, n - 1)] - pts[Mathf.Max(j - 1, 0)]).normalized;
                var side = j == 0 ? Vector3.Cross(tng, Mathf.Abs(tng.y) > 0.9f ? Vector3.right : Vector3.up).normalized : Vector3.Cross(Vector3.Cross(tng, prevSide), tng).normalized;
                prevSide = side; var up = Vector3.Cross(side, tng);
                float r = radius((float)j / (n - 1));
                for (int i = 0; i < seg; i++) { float a = 2 * Mathf.PI * i / seg; b.V(pts[j] + (side * Mathf.Cos(a) + up * Mathf.Sin(a)) * r); }
            }
            b.Rings(start, n, seg, j => pts[j]);
            int c0 = b.V(pts[0]), c1 = b.V(pts[n - 1]);
            b.Fan(c0, start, seg, Vector3.Lerp(pts[0], pts[1], 0.5f)); b.Fan(c1, start + (n - 1) * seg, seg, Vector3.Lerp(pts[n - 1], pts[n - 2], 0.5f));
        }

        static List<Vector3> Curve(Vector3 a, Vector3 b, Vector3 c, int n = 14)
        {   // quadratic Bezier a -> c with control b
            var l = new List<Vector3>(); for (int i = 0; i <= n; i++) { float t = (float)i / n; l.Add((1 - t) * (1 - t) * a + 2 * (1 - t) * t * b + t * t * c); } return l;
        }

        static Mesh Lathe(string name, Vector2[] prof, int seg = 48)
        {
            var b = new MB(); int start = b.v.Count;
            foreach (var p in prof) for (int i = 0; i < seg; i++) { float a = 2 * Mathf.PI * i / seg; b.V(new Vector3(Mathf.Cos(a) * p.x, p.y, Mathf.Sin(a) * p.x)); }
            float yMid = 0; foreach (var p in prof) yMid += p.y; yMid /= prof.Length;
            b.Rings(start, prof.Length, seg, j => new Vector3(0, prof[j].y, 0));
            int top = b.V(new Vector3(0, prof[prof.Length - 1].y, 0)), bot = b.V(new Vector3(0, prof[0].y, 0));
            b.Fan(top, start + (prof.Length - 1) * seg, seg, new Vector3(0, yMid, 0)); b.Fan(bot, start, seg, new Vector3(0, yMid, 0));
            return b.Build(name);
        }

        // ---------------------------------------------------------------- materials
        static Texture2D remap, fb, ocol, noise, paper, stroke;
        static readonly List<Material> inkLeft = new List<Material>();
        static Material InkMat(string name, Color rim, bool water = false)
        {
            var m = new Material(Shader.Find(Ink));
            m.SetTexture("_ColorRemap", remap); m.SetTexture("_OutlineNoiseFB", fb); m.SetTexture("_OutlineNoiseColor", ocol);
            m.SetTexture("_Stroke", stroke); m.SetTexture("_Noise", noise); m.SetTexture("_PaperTex", paper);
            m.SetColor("_RimColor", rim);
            if (water) { m.SetVector("_InlineControl", new Vector4(0.6f, 10f, 4.87f, 0.33f)); m.SetVector("_AreaBegin", new Vector4(2f, 2f, 2f, 5.15f)); }
            AssetDatabase.CreateAsset(m, Dir + name + ".mat");
            return m;
        }

        class Kind { public Material lit, inkL, inkR; }
        static Kind MakeKind(string name, Color litColor, Color rim, float smooth = 0.2f, bool water = false)
        {
            var k = new Kind { lit = SceneKit.Mat(Dir, name + "Lit", Lit, litColor, 0f, smooth), inkL = InkMat(name + "InkL", rim, water), inkR = InkMat(name + "InkR", rim, water) };
            inkLeft.Add(k.inkL); return k;
        }

        static Transform litRoot, inkLRoot, inkRRoot;
        static void Put(string name, Mesh mesh, Kind k, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            foreach (var (root, mat, layer) in new[] { (litRoot, k.lit, 8), (inkLRoot, k.inkL, 8), (inkRRoot, k.inkR, 9) })
            {
                var go = SceneKit.Part(name, mesh, mat, pos, rot, root); go.layer = layer; go.transform.localScale = scale;
                go.transform.position = pos; go.transform.rotation = rot;
            }
        }

        static void SetLayer(GameObject g, int layer) { foreach (var t in g.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer; }

        public static void Build()
        {
            inkLeft.Clear();
            var paperCol = new Color(0.90f, 0.87f, 0.80f);
            // ---- textures
            remap = SaveTex(256, 4, (u, v) => { float g = Mathf.Lerp(0.06f, 0.82f, Mathf.Pow(Mathf.SmoothStep(0.03f, 1f, u), 0.8f)); return new Color(g, g, g, 1); }, "ColorRemap", true, TextureWrapMode.Clamp);
            fb = SaveTex(512, 512, (u, v) => { float n = 0.55f * P2(u, v, 16, 3) + 0.3f * P2(u, v, 48, 4) + 0.15f * P2(u, v, 128, 5); n = Mathf.Clamp01((n - 0.22f) / 0.56f); return new Color(n, n, n, 1); }, "FeiBaiNoise", true, TextureWrapMode.Repeat);
            ocol = SaveTex(256, 256, (u, v) => { float n = P2(u, v, 24, 9); return new Color(n, n, n, 1); }, "OutlineColorNoise", true, TextureWrapMode.Repeat);
            noise = SaveTex(512, 512, (u, v) => { float n = 0.5f * P2(u, v, 8, 11) + 0.3f * P2(u, v, 32, 12) + 0.2f * P2(u, v, 96, 13); n = Mathf.Clamp01((n - 0.2f) / 0.6f); return new Color(n, n, n, 1); }, "CunNoise", true, TextureWrapMode.Repeat);
            paper = SaveTex(1024, 1024, (u, v) =>
            {
                float fib = 0.6f * P2(u * 1f, v * 0.25f, 256, 21) + 0.4f * P2(u, v, 512, 22);
                float blot = P2(u, v, 6, 23);
                float g = 1 - 0.05f * fib - 0.04f * blot;
                return new Color(paperCol.r * g, paperCol.g * g, paperCol.b * g, 1);
            }, "Paper", false, TextureWrapMode.Repeat);
            var simp = (TextureImporter)AssetImporter.GetAtPath(Dir + "Tex/Stroke_3.psd");
            simp.sRGBTexture = false; simp.wrapMode = TextureWrapMode.Repeat; simp.alphaIsTransparency = false; simp.SaveAndReimport();
            stroke = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "Tex/Stroke_3.psd");

            // ---- scene settings
            SceneKit.Ambient(new Color(0.55f, 0.58f, 0.62f), new Color(0.42f, 0.42f, 0.40f), new Color(0.25f, 0.24f, 0.22f));
            SceneKit.Shadows(40f);
            SceneKit.Sun("Sun", new Vector3(42, 28, 0), new Color(1f, 0.95f, 0.88f), 1.6f, true);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogColor = paperCol;
            RenderSettings.fogStartDistance = 22f; RenderSettings.fogEndDistance = 150f;

            litRoot = new GameObject("Lit set").transform; inkLRoot = new GameObject("Ink left set").transform; inkRRoot = new GameObject("Ink right set").transform;

            var rock = MakeKind("Rock", new Color(0.45f, 0.42f, 0.38f), new Color(0.236f, 0.236f, 0.236f));
            var foliage = MakeKind("Foliage", new Color(0.18f, 0.30f, 0.18f), new Color(0.20f, 0.27f, 0.22f));
            var bark = MakeKind("Bark", new Color(0.28f, 0.2f, 0.14f), new Color(0.26f, 0.21f, 0.17f));
            var stone = MakeKind("BridgeStone", new Color(0.62f, 0.6f, 0.56f), new Color(0.33f, 0.32f, 0.30f));
            var lacquer = MakeKind("Lacquer", new Color(0.55f, 0.12f, 0.08f), new Color(0.50f, 0.20f, 0.14f), 0.4f);
            var roof = MakeKind("Roof", new Color(0.16f, 0.16f, 0.17f), new Color(0.18f, 0.18f, 0.19f));
            var mount = MakeKind("Mountain", new Color(0.40f, 0.46f, 0.50f), new Color(0.30f, 0.36f, 0.42f));
            var water = MakeKind("Water", new Color(0.55f, 0.62f, 0.66f), Color.white, 0.7f, true);

            // ---- water / ground
            var plane = new MB(); int p0 = plane.V(new Vector3(-150, 0, -60)), p1 = plane.V(new Vector3(150, 0, -60)), p2 = plane.V(new Vector3(150, 0, 240)), p3 = plane.V(new Vector3(-150, 0, 240));
            plane.Tri(p0, p3, p2); plane.Tri(p0, p2, p1);
            Put("Water", plane.Build("WaterPlane"), water, Vector3.zero, Quaternion.identity, Vector3.one);

            // ---- rocks
            var rocks = new (Vector3 pos, Vector3 s, int seed, float rotY)[]
            {
                (new Vector3(-6.2f, 0, 3f), new Vector3(2.0f, 3.1f, 1.7f), 1, 10), (new Vector3(-4.5f, 0, 1.4f), new Vector3(1.3f, 1.5f, 1.2f), 2, 40),
                (new Vector3(-6.8f, 0, 6.2f), new Vector3(2.2f, 2.3f, 1.8f), 3, -20), (new Vector3(5.9f, 0, 5f), new Vector3(2.4f, 1.9f, 2.0f), 4, 70),
                (new Vector3(4.5f, 0, 2.6f), new Vector3(1.2f, 1.1f, 1.0f), 5, 0), (new Vector3(-3.4f, 0, 8.5f), new Vector3(1.0f, 0.8f, 0.9f), 6, 0),
                (new Vector3(-10f, 0, 16f), new Vector3(4.2f, 3.0f, 3.6f), 7, 15),
            };
            for (int i = 0; i < rocks.Length; i++)
                Put("Rock " + i, Blob("RockMesh" + i, rocks[i].s, 0.28f, 1.3f, rocks[i].seed, -0.6f), rock, rocks[i].pos, Quaternion.Euler(0, rocks[i].rotY, 0), Vector3.one);

            // ---- mountains (far, faded by the remap and the fog)
            var mts = new (Vector3 pos, Vector3 s)[] { (new Vector3(-38, -4, 62), new Vector3(20, 30, 13)), (new Vector3(8, -4, 82), new Vector3(30, 42, 16)), (new Vector3(42, -4, 56), new Vector3(17, 24, 12)),
                (new Vector3(-8, -4, 120), new Vector3(42, 52, 20)), (new Vector3(62, -4, 104), new Vector3(28, 36, 14)), (new Vector3(-72, -4, 95), new Vector3(30, 38, 15)) };
            for (int i = 0; i < mts.Length; i++) Put("Mountain " + i, Blob("MountainMesh" + i, mts[i].s, 0.3f, 1.6f, 30 + i, -0.4f * mts[i].s.y), mount, mts[i].pos, Quaternion.Euler(0, i * 47, 0), Vector3.one);

            // ---- pines: bent trunk, branches, foliage pads
            void Pine(string name, Vector3 root, Vector3 lean, float h, int seed)
            {
                var tb = new MB(); var rnd = new System.Random(seed);
                var trunk = Curve(root, root + Vector3.up * h * 0.55f + lean * 0.3f, root + Vector3.up * h + lean, 16);
                Tube(tb, trunk, t => Mathf.Lerp(0.2f, 0.05f, t), 14);
                var pads = new List<(Vector3, Vector3)>();
                for (int k = 0; k < 5; k++)
                {
                    float t = 0.42f + 0.13f * k; var p = trunk[Mathf.RoundToInt(t * (trunk.Count - 1))];
                    float a = (float)(k * 2.4 + rnd.NextDouble()); var dir = new Vector3(Mathf.Cos(a), 0.25f, Mathf.Sin(a)) * (1.5f - 0.18f * k) + lean.normalized * 0.4f;
                    var end = p + dir;
                    Tube(tb, Curve(p, p + dir * 0.5f + Vector3.up * 0.25f, end, 6), u => Mathf.Lerp(0.06f, 0.025f, u), 8);
                    pads.Add((end + Vector3.up * 0.12f, new Vector3(1.0f - 0.08f * k, 0.32f, 0.8f - 0.06f * k)));
                }
                pads.Add((trunk[trunk.Count - 1] + Vector3.up * 0.15f, new Vector3(0.9f, 0.35f, 0.75f)));
                Put(name + " trunk", tb.Build(name + "Trunk"), bark, Vector3.zero, Quaternion.identity, Vector3.one);
                for (int k = 0; k < pads.Count; k++)
                    Put(name + " pad " + k, Blob(name + "Pad" + k, pads[k].Item2, 0.32f, 2.2f, seed * 10 + k), foliage, pads[k].Item1, Quaternion.Euler(0, k * 61, 0), Vector3.one);
            }
            Pine("Pine right", new Vector3(5.6f, 1.2f, 4.6f), new Vector3(-1.6f, 0, 0.3f), 4.8f, 3);
            Pine("Pine left", new Vector3(-6.6f, 1.8f, 6.6f), new Vector3(0.6f, 0, 0.5f), 5.6f, 7);

            // ---- arched bridge: rounded section swept along the arch, rails and posts
            float Arch(float x) => 1.75f * Mathf.Pow(Mathf.Cos(Mathf.PI * x / 10.6f), 0.8f);
            var bridge = new MB(); int seg = 28, rows = 61; int bstart = bridge.v.Count;
            for (int j = 0; j < rows; j++)
            {
                float x = Mathf.Lerp(-5.2f, 5.2f, (float)j / (rows - 1)), y = Arch(x);
                var tng = new Vector3(0.02f, Arch(x + 0.01f) - Arch(x - 0.01f), 0).normalized; var up = new Vector3(-tng.y, tng.x, 0);
                for (int i = 0; i < seg; i++)
                {
                    float a = 2 * Mathf.PI * i / seg, c = Mathf.Cos(a), s = Mathf.Sin(a);
                    float zz = Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 0.35f) * 0.85f, yy = Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), 0.35f) * 0.2f;
                    bridge.V(new Vector3(x, y, 0) + up * yy + new Vector3(0, 0, zz));
                }
            }
            bridge.Rings(bstart, rows, seg, j => { float x = Mathf.Lerp(-5.2f, 5.2f, (float)j / (rows - 1)); return new Vector3(x, Arch(x), 0); });
            int e0 = bridge.V(new Vector3(-5.2f, Arch(-5.2f), 0)), e1 = bridge.V(new Vector3(5.2f, Arch(5.2f), 0));
            bridge.Fan(e0, bstart, seg, new Vector3(-5.0f, Arch(-5.0f), 0)); bridge.Fan(e1, bstart + (rows - 1) * seg, seg, new Vector3(5.0f, Arch(5.0f), 0));
            foreach (float z in new[] { -0.78f, 0.78f })
            {
                var rail = new List<Vector3>(); for (int j = 0; j <= 40; j++) { float x = Mathf.Lerp(-4.4f, 4.4f, j / 40f); rail.Add(new Vector3(x, Arch(x) + 0.85f, z)); }
                Tube(bridge, rail, u => 0.045f, 10);
                for (float x = -4.4f; x <= 4.41f; x += 1.1f) Tube(bridge, new[] { new Vector3(x, Arch(x) + 0.1f, z), new Vector3(x, Arch(x) + 0.95f, z) }, u => 0.055f, 10);
            }
            Put("Bridge", bridge.Build("BridgeMesh"), stone, new Vector3(0, 0, 6f), Quaternion.identity, Vector3.one);

            // ---- pavilion on the far mound
            var pv = new Vector3(-10f, 2.75f, 16f);
            Put("Pavilion base", Lathe("PavBase", new[] { new Vector2(1.7f, -0.3f), new Vector2(1.75f, 0.0f), new Vector2(1.6f, 0.22f), new Vector2(1.45f, 0.25f) }), stone, pv, Quaternion.identity, Vector3.one);
            var posts = new MB(); for (int i = 0; i < 6; i++) { float a = i * Mathf.PI / 3; var c = new Vector3(Mathf.Cos(a) * 1.25f, 0, Mathf.Sin(a) * 1.25f); Tube(posts, new[] { c + Vector3.up * 0.2f, c + Vector3.up * 2.35f }, u => 0.08f, 10); }
            Put("Pavilion posts", posts.Build("PavPosts"), lacquer, pv, Quaternion.identity, Vector3.one);
            Put("Pavilion roof", Lathe("PavRoof", new[] { new Vector2(0.05f, 2.25f), new Vector2(1.5f, 2.32f), new Vector2(2.05f, 2.2f), new Vector2(2.15f, 2.38f), new Vector2(1.75f, 2.5f), new Vector2(1.05f, 2.88f), new Vector2(0.4f, 3.3f), new Vector2(0.12f, 3.6f), new Vector2(0.02f, 3.75f) }, 6 * 8), roof, pv, Quaternion.identity, Vector3.one);

            // ---- paper sky (ink sets only)
            var skyMat = new Material(Shader.Find("GameTech/InkPaperSky")); skyMat.SetTexture("_PaperTex", paper); AssetDatabase.CreateAsset(skyMat, Dir + "PaperSky.mat");
            var skyMesh = Lathe("SkySphere", Enumerable.Range(0, 17).Select(i => { float th = Mathf.PI * i / 16; return new Vector2(Mathf.Sin(th) * 400f + 0.01f, -Mathf.Cos(th) * 400f); }).ToArray(), 32);
            foreach (var (root, layer) in new[] { (inkLRoot, 8), (inkRRoot, 9) }) { var g = SceneKit.Part("Paper sky", skyMesh, skyMat, Vector3.zero, Quaternion.identity, root); g.layer = layer; }

            // ---- IA on the bridge: Lit / ink left / ink right
            var iaPos = new Vector3(0.7f, 6f, 6f); iaPos.y = Arch(0.7f) + 0.2f;
            foreach (var (root, layer, shader, sub) in new[] { (litRoot, 8, Lit, "IALit"), (inkLRoot, 8, Ink, "IAInkL"), (inkRRoot, 9, Ink, "IAInkR") })
            {
                if (!AssetDatabase.IsValidFolder(Dir + sub)) AssetDatabase.CreateFolder(Dir.TrimEnd('/'), sub);
                var ia = IAKit.Spawn(shader, Dir + sub + "/", (m, n) =>
                {
                    if (shader == Lit) { m.SetFloat("_Smoothness", 0.25f); return; }
                    m.SetTexture("_ColorRemap", remap); m.SetTexture("_OutlineNoiseFB", fb); m.SetTexture("_OutlineNoiseColor", ocol);
                    m.SetTexture("_Stroke", stroke); m.SetTexture("_Noise", noise); m.SetTexture("_PaperTex", paper);
                    m.SetColor("_RimColor", new Color(0.30f, 0.30f, 0.32f)); m.SetFloat("_AlbedoMix", 0.35f);
                    m.SetVector("_OutlineCtrl", new Vector4(5f, 1.5f, 4f, 0.25f));   // thinner lines on the 1.39 m character
                    m.SetVector("_Size", new Vector4(2.5f, 10f, 50f, 10f));
                    if (sub == "IAInkL") inkLeft.Add(m);
                });
                ia.transform.SetParent(root, false); ia.transform.position = iaPos; ia.transform.rotation = Quaternion.Euler(0, 200, 0);
                SetLayer(ia, layer);
            }

            // ---- director, cameras, labels
            var dr = new GameObject("Director").AddComponent<InkPaintDirector>();
            dr.litSet = litRoot.gameObject; dr.inkLeftSet = inkLRoot.gameObject; dr.inkLeftMats = inkLeft.ToArray();
            dr.camL = SceneKit.Cam("Camera left", new Rect(0, 0, 0.5f, 1), 42, paperCol, 0); dr.camL.cullingMask = ~(1 << 9); dr.camL.farClipPlane = 600f;
            dr.camR = SceneKit.Cam("Camera right", new Rect(0.5f, 0, 0.5f, 1), 42, paperCol, 1, false); dr.camR.cullingMask = ~(1 << 8); dr.camR.farClipPlane = 600f;
            dr.camFull = SceneKit.Cam("Camera full", new Rect(0, 0, 1, 1), 40, paperCol, 2, false); dr.camFull.cullingMask = ~(1 << 8); dr.camFull.farClipPlane = 600f; dr.camFull.tag = "MainCamera";
            SceneKit.Grade(Dir, 0.2f, 0f, 6f, 0f, 0.15f, 0.08f);

            const float LX = 0.03f, RX = 0.53f, Y = 0.05f;
            LabelSpec.Create("URP Lit", LX, Y, 36).During(0.5f, 7f);
            LabelSpec.Create("Ink: Gou + CunCa + Ran", RX, Y, 36).During(0.5f, 7f);
            LabelSpec.Create("1. Gou (outline): back-face outlines + rim inline, broken by FeiBai noise", LX, Y, 34).During(7.1f, 9.7f);
            LabelSpec.Create("2. + CunCa (texture strokes): stroke map where light and rim are dark", LX, Y, 34).During(9.7f, 12.3f);
            LabelSpec.Create("3. + Ran (wash): ink tint from the Lambert / rim term", LX, Y, 34).During(12.3f, 15f);
            LabelSpec.Create("FeiBai noise off", LX, Y, 36).During(15.1f, 19.5f);
            LabelSpec.Create("FeiBai noise on: lines break up", RX, Y, 36).During(15.1f, 19.5f);
            LabelSpec.Create("Distance / 64 m through the remap", LX, Y, 34).During(19.6f, 23f);
            LabelSpec.Create("Line ink fades with distance", RX, Y, 34).During(19.6f, 23f);
            LabelSpec.Create("A procedural imitation of ink painting, on plain lit meshes", LX, Y, 34).During(23.2f, 28f);
            Debug.Log($"[gt] ink painting scene built, {inkLeft.Count} left ink materials");
        }
    }
}
