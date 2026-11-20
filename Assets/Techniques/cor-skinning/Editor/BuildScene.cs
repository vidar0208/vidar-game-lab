// Builds the CoR skinning scene from code. The figure is the stand-in mannequin's layout (same bone names), but its
// arms are continuous tubes with blended weights (chest/upper arm at the shoulder, upper arm/forearm at the elbow,
// forearm/hand at the wrist), which is where linear blend skinning loses volume. Torso, head and legs are rigid
// capsules. The centres of rotation are precomputed here in the editor with the source's CorAsset (GPU path).
// Two copies of the figure: linear blend (CoR weight 0) on layer 20 and CoR (weight 1) on layer 21.
using System.Collections.Generic;
using CoR;
using GameTech;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    public static class CorScene
    {
        const string Dir = "Assets/Techniques/cor-skinning/";
        public static readonly string[] Names = { "Hips", "Spine", "Chest", "Neck", "Head",
            "Upperarm_L", "Lowerarm_L", "Hand_L", "Upperarm_R", "Lowerarm_R", "Hand_R",
            "Upperleg_L", "Lowerleg_L", "Foot_L", "Upperleg_R", "Lowerleg_R", "Foot_R" };
        static readonly int[] Parent = { -1, 0, 1, 2, 3, 2, 5, 6, 2, 8, 9, 0, 11, 12, 0, 14, 15 };
        // shoulder at 0.15, elbow at 0.40, wrist at 0.63 from the centre line; arm height 1.10
        const float ArmY = 1.10f, XS = 0.15f, XE = 0.40f, XW = 0.63f, X0 = 0.10f, XT = 0.75f;
        static readonly Vector3[] Pos = {
            new Vector3(0, 0.74f, 0), new Vector3(0, 0.84f, 0), new Vector3(0, 0.98f, 0), new Vector3(0, 1.13f, 0), new Vector3(0, 1.17f, 0),
            new Vector3(-XS, ArmY, 0), new Vector3(-XE, ArmY, 0), new Vector3(-XW, ArmY, 0),
            new Vector3(XS, ArmY, 0), new Vector3(XE, ArmY, 0), new Vector3(XW, ArmY, 0),
            new Vector3(-0.085f, 0.70f, 0), new Vector3(-0.085f, 0.39f, 0), new Vector3(-0.085f, 0.07f, 0),
            new Vector3(0.085f, 0.70f, 0), new Vector3(0.085f, 0.39f, 0), new Vector3(0.085f, 0.07f, 0) };
        const int S = 40;                       // segments around a limb ring
        static readonly Color Suit = new Color(0.50f, 0.54f, 0.62f), Skin = new Color(0.90f, 0.78f, 0.68f), Dark = new Color(0.30f, 0.33f, 0.40f);

        static readonly List<Vector3> V = new List<Vector3>(), N = new List<Vector3>();
        static readonly List<Vector2> UV = new List<Vector2>();
        static readonly List<Color> C = new List<Color>();
        static readonly List<BoneWeight> W = new List<BoneWeight>();
        static readonly List<int> T = new List<int>();
        public static int WristRing, ElbowRing, ArmTriStart, ArmTriEnd;   // left arm, for the measurements

        static int B(string n) => System.Array.IndexOf(Names, n);

        static BoneWeight BW(int a, int b, float wb)
        {
            if (wb <= 0.0001f) return new BoneWeight { boneIndex0 = a, weight0 = 1 };
            if (wb >= 0.9999f) return new BoneWeight { boneIndex0 = b, weight0 = 1 };
            return new BoneWeight { boneIndex0 = a, weight0 = 1 - wb, boneIndex1 = b, weight1 = wb };
        }

        // add a triangle facing away from 'inside'
        static void Tri(int a, int b, int c, Vector3 outward)
        {
            var n = Vector3.Cross(V[b] - V[a], V[c] - V[a]);
            if (Vector3.Dot(n, outward) >= 0) { T.Add(a); T.Add(b); T.Add(c); } else { T.Add(a); T.Add(c); T.Add(b); }
        }

        // rigid capsule (same construction as the stand-in mannequin)
        static void Capsule(Vector3 a, Vector3 b, float r, int bone, Color col)
        {
            const int CS = 24, H = 7;
            var d = b - a; var q = d.sqrMagnitude < 1e-8f ? Quaternion.identity : Quaternion.FromToRotation(Vector3.up, d.normalized);
            int start = V.Count, rings = 2 * (H + 1);
            for (int j = 0; j < rings; j++)
            {
                bool top = j > H; float phi = top ? (j - H - 1) / (float)H * Mathf.PI * 0.5f : (j / (float)H - 1) * Mathf.PI * 0.5f;
                var c = top ? b : a;
                for (int s = 0; s <= CS; s++)
                {
                    float th = s / (float)CS * Mathf.PI * 2;
                    var n = q * new Vector3(Mathf.Cos(phi) * Mathf.Cos(th), Mathf.Sin(phi), Mathf.Cos(phi) * Mathf.Sin(th));
                    V.Add(c + n * r); N.Add(n); UV.Add(Vector2.zero); C.Add(new Color(col.r, col.g, col.b, 0)); W.Add(BW(bone, bone, 0));
                }
            }
            for (int j = 0; j < rings - 1; j++)
                for (int s = 0; s < CS; s++)
                {
                    int v0 = start + j * (CS + 1) + s, v1 = v0 + 1, v2 = v0 + CS + 1, v3 = v2 + 1;
                    var mid = (V[v0] + V[v3]) * 0.5f; var axisPt = Vector3.Lerp(a, b, Mathf.Clamp01(Vector3.Dot(mid - a, d) / Mathf.Max(d.sqrMagnitude, 1e-8f)));
                    var o = mid - axisPt;
                    Tri(v0, v2, v1, o); Tri(v1, v2, v3, o);
                }
        }

        // limb radii at distance x from the centre line: (thickness along y, width along z)
        static Vector2 Radius(float x)
        {
            if (x < XE) { float t = Mathf.InverseLerp(X0, XE, x); float r = Mathf.Lerp(0.047f, 0.037f, t) + 0.005f * Mathf.Sin(Mathf.PI * t); return new Vector2(r, r); }
            if (x < XW) { float t = Mathf.InverseLerp(XE, XW, x); float r = Mathf.Lerp(0.037f, 0.028f, t) + 0.007f * Mathf.Sin(Mathf.PI * Mathf.Min(t * 1.6f, 1f)); return new Vector2(r, r); }
            float u = Mathf.InverseLerp(XW, XT, x);
            float e = Mathf.SmoothStep(0f, 1f, u / 0.35f);
            float k = u > 0.55f ? Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((u - 0.55f) / 0.45f, 2))) : 1f;
            return new Vector2(Mathf.Lerp(0.028f, 0.019f, e), Mathf.Lerp(0.028f, 0.047f, e)) * Mathf.Max(k, 0.02f);
        }

        // continuous arm tube with blended weights, closed at both ends
        static void Arm(int side)
        {
            float sx = side == 0 ? -1 : 1; string n = side == 0 ? "_L" : "_R";
            int chest = B("Chest"), up = B("Upperarm" + n), lo = B("Lowerarm" + n), hand = B("Hand" + n);
            const float step = 0.004f;
            int rings = Mathf.RoundToInt((XT - X0) / step) + 1;
            int start = V.Count, tri0 = T.Count;
            for (int j = 0; j < rings; j++)
            {
                float x = X0 + j * step;
                var r = Radius(x);
                var dr = (Radius(x + 0.002f) - Radius(x - 0.002f)) / 0.004f;
                BoneWeight w;
                if (x < (XS + XE) / 2) w = BW(chest, up, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(X0 + 0.01f, XS + 0.05f, x)));
                else if (x < (XE + XW) / 2) w = BW(up, lo, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(XE - 0.045f, XE + 0.045f, x)));
                else w = BW(lo, hand, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(XW - 0.035f, XW + 0.035f, x)));
                float skin = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(XW - 0.01f, XW + 0.02f, x));
                var col = Color.Lerp(Suit, Skin, skin); col.a = 1f - skin;
                if (side == 0 && Mathf.Abs(x - XW) <= step * 0.5f + 1e-4f) WristRing = V.Count;
                if (side == 0 && Mathf.Abs(x - XE) <= step * 0.5f + 1e-4f) ElbowRing = V.Count;
                for (int s = 0; s <= S; s++)
                {
                    float th = s / (float)S * Mathf.PI * 2;
                    float cy = Mathf.Cos(th), sz = Mathf.Sin(th);
                    V.Add(new Vector3(sx * x, ArmY + r.x * cy, r.y * sz));
                    // normal of the elliptic ring, tilted by the radius change along the arm
                    var radial = new Vector3(0, cy / Mathf.Max(r.x, 1e-4f), sz / Mathf.Max(r.y, 1e-4f)).normalized;
                    float slope = dr.x * cy * cy + dr.y * sz * sz;
                    N.Add((radial - new Vector3(sx * slope, 0, 0)).normalized);
                    UV.Add(new Vector2(s / (float)S, (x - X0) / (XT - X0))); C.Add(col); W.Add(w);
                }
            }
            for (int j = 0; j < rings - 1; j++)
                for (int s = 0; s < S; s++)
                {
                    int v0 = start + j * (S + 1) + s, v1 = v0 + 1, v2 = v0 + S + 1, v3 = v2 + 1;
                    var o = (V[v0] + V[v3]) * 0.5f; o -= new Vector3(o.x, ArmY, 0);
                    Tri(v0, v2, v1, o); Tri(v1, v2, v3, o);
                }
            // caps: shoulder end (inside the chest) and fingertip end
            for (int e = 0; e < 2; e++)
            {
                int ring = e == 0 ? start : start + (rings - 1) * (S + 1);
                int c = V.Count;
                V.Add(new Vector3(sx * (e == 0 ? X0 : XT), ArmY, 0)); N.Add(new Vector3(e == 0 ? -sx : sx, 0, 0)); UV.Add(Vector2.zero);
                C.Add(C[ring]); W.Add(W[ring]);
                var outward = new Vector3(e == 0 ? -sx : sx, 0, 0);
                for (int s = 0; s < S; s++) Tri(c, ring + s, ring + s + 1, outward);
            }
            if (side == 0) { ArmTriStart = tri0; ArmTriEnd = T.Count; }
        }

        public static Transform[] Figure(string name, Mesh mesh, Material mat, int layer, out CorGpuSkinner skinner, CorAsset asset, ComputeShader cs)
        {
            var root = new GameObject(name);
            var bones = new Transform[Names.Length];
            for (int i = 0; i < Names.Length; i++)
            {
                bones[i] = new GameObject(Names[i]).transform;
                bones[i].SetParent(Parent[i] < 0 ? root.transform : bones[Parent[i]], false);
                bones[i].position = Pos[i];
            }
            var body = new GameObject("Body"); body.transform.SetParent(root.transform, false); body.layer = layer;
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = body.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.On;
            skinner = body.AddComponent<CorGpuSkinner>();
            skinner.corAsset = asset; skinner.skinningShader = cs; skinner.allBones = bones; skinner.material = mat;
            return bones;
        }

        public static void Build()
        {
            RenderSettings.skybox = null; RenderSettings.fog = false;
            SceneKit.Ambient(new Color(0.2f, 0.23f, 0.3f), new Color(0.12f, 0.12f, 0.13f), new Color(0.08f, 0.07f, 0.06f));
            SceneKit.Shadows(12f);

            // mesh: rigid capsules for torso, head and legs, blended tubes for the arms
            V.Clear(); N.Clear(); UV.Clear(); C.Clear(); W.Clear(); T.Clear();
            Capsule(new Vector3(-0.05f, 0.75f, 0), new Vector3(0.05f, 0.75f, 0), 0.085f, B("Hips"), Dark);
            Capsule(new Vector3(0, 0.80f, 0), new Vector3(0, 0.94f, 0), 0.08f, B("Spine"), Suit);
            Capsule(new Vector3(0, 0.98f, 0), new Vector3(0, 1.06f, 0), 0.105f, B("Chest"), Suit);
            Capsule(new Vector3(-0.11f, 1.09f, 0), new Vector3(0.11f, 1.09f, 0), 0.058f, B("Chest"), Suit);
            Capsule(new Vector3(0, 1.11f, 0), new Vector3(0, 1.17f, 0), 0.036f, B("Neck"), Skin);
            Capsule(new Vector3(0, 1.265f, 0.01f), new Vector3(0, 1.275f, 0.01f), 0.11f, B("Head"), Skin);
            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? -1 : 1; string n = side == 0 ? "_L" : "_R";
                Arm(side);
                Capsule(new Vector3(0.085f * sx, 0.69f, 0), new Vector3(0.085f * sx, 0.41f, 0), 0.062f, B("Upperleg" + n), Dark);
                Capsule(new Vector3(0.085f * sx, 0.38f, 0), new Vector3(0.085f * sx, 0.10f, 0), 0.046f, B("Lowerleg" + n), Dark);
                Capsule(new Vector3(0.085f * sx, 0.04f, -0.03f), new Vector3(0.085f * sx, 0.04f, 0.10f), 0.04f, B("Foot" + n), Dark);
            }
            var mesh = new Mesh { name = "CorFigure", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(V); mesh.SetNormals(N); mesh.SetUVs(0, UV); mesh.SetColors(C); mesh.boneWeights = W.ToArray();
            mesh.SetTriangles(T, 0);
            var bind = new Matrix4x4[Names.Length];
            for (int i = 0; i < Names.Length; i++) bind[i] = Matrix4x4.Translate(-Pos[i]);   // bones unrotated, figure at the origin
            mesh.bindposes = bind; mesh.RecalculateTangents();
            mesh.bounds = new Bounds(new Vector3(0, 0.9f, 0), new Vector3(3f, 3f, 3f));      // skinned in a compute shader: keep it from being culled
            AssetDatabase.CreateAsset(mesh, Dir + "CorFigure.asset");

            // precompute the centres of rotation (source CorAsset, GPU path, sigma 0.1)
            var asset = ScriptableObject.CreateInstance<CorAsset>();
            asset.sigma = 0.1f;
            var t0 = System.Diagnostics.Stopwatch.StartNew();
            asset.PreProcess(true, mesh, null, null);
            t0.Stop();
            AssetDatabase.CreateAsset(asset, Dir + "CorFigureCoR.asset");
            int nonZero = 0; foreach (var p in asset.pStar) if (p != Vector3.zero) nonZero++;
            Debug.Log($"[gt] figure {mesh.vertexCount} vertices, {T.Count / 3} triangles; CoR precompute {t0.ElapsedMilliseconds} ms, {nonZero} vertices with a centre (others are rigid: same as linear blend); used bones {asset.usedBones.Length}");

            var cs = AssetDatabase.LoadAssetAtPath<ComputeShader>(Dir + "CoR/Resources/CorSkinning.compute");
            var mat = new Material(AssetDatabase.LoadAssetAtPath<Shader>(Dir + "Shaders/CorFigure.shader")); AssetDatabase.CreateAsset(mat, Dir + "CorFigure.mat");
            var dir = new GameObject("Director").AddComponent<CorDirector>();
            dir.lbsBones = Figure("Figure linear blend", mesh, mat, 20, out dir.lbs, asset, cs);
            dir.corBones = Figure("Figure CoR", mesh, mat, 21, out dir.cor, asset, cs);
            dir.lbs.corWeight = 0f; dir.cor.corWeight = 1f;
            dir.wristRing = WristRing; dir.elbowRing = ElbowRing; dir.ringSize = S + 1;
            var armTris = new int[ArmTriEnd - ArmTriStart]; T.CopyTo(ArmTriStart, armTris, 0, armTris.Length); dir.armTris = armTris;

            // stage: floor and a soft backdrop
            var floorM = SceneKit.Mat(Dir, "Floor", "Universal Render Pipeline/Lit", new Color(0.13f, 0.13f, 0.14f), 0f, 0.25f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.name = "Floor"; floor.transform.localScale = new Vector3(3f, 1f, 3f);
            floor.GetComponent<Renderer>().sharedMaterial = floorM; Object.DestroyImmediate(floor.GetComponent<Collider>());
            var wallM = SceneKit.Mat(Dir, "Backdrop", "Universal Render Pipeline/Lit", new Color(0.16f, 0.17f, 0.2f), 0f, 0.1f);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Quad); wall.name = "Backdrop"; wall.transform.position = new Vector3(0, 3f, -2.5f);
            wall.transform.rotation = Quaternion.Euler(0, 180f, 0); wall.transform.localScale = new Vector3(14f, 8f, 1f);
            wall.GetComponent<Renderer>().sharedMaterial = wallM; Object.DestroyImmediate(wall.GetComponent<Collider>());

            SceneKit.Sun("Key", new Vector3(32f, 150f, 0f), new Color(1f, 0.95f, 0.88f), 1.6f, true);
            SceneKit.Grade(Dir, 0.2f, 0f, 10f, 4f, 0.3f, 0.06f);
            AssetDatabase.SaveAssets();

            var bg = new Color(0.07f, 0.075f, 0.09f);
            dir.camA = SceneKit.Cam("Cam A", new Rect(0, 0, 1, 1), 40f, bg, 0, true);
            dir.camB = SceneKit.Cam("Cam B", new Rect(0, 0, 1, 1), 40f, bg, 1, true);
            foreach (var c in new[] { dir.camA, dir.camB }) { c.nearClipPlane = 0.02f; c.farClipPlane = 30f; }
            var clear = SceneKit.Cam("Clear", new Rect(0, 0, 1, 1), 60f, Color.black, -10, false); clear.cullingMask = 0;

            dir.title = LabelSpec.Create("", 0.012f, 0.93f, 40);
            dir.l1 = LabelSpec.Create("", 0.012f, 0.058f, 34); dir.l2 = LabelSpec.Create("", 0.012f, 0.018f, 30);
            dir.m1 = LabelSpec.Create("", 0.512f, 0.058f, 34); dir.m2 = LabelSpec.Create("", 0.512f, 0.018f, 30);
            Debug.Log($"[gt] built: wrist ring {WristRing}, elbow ring {ElbowRing}, left arm triangles {armTris.Length / 3}");
        }
    }
}
