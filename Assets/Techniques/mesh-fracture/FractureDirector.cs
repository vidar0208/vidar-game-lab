// Runtime fracture with the OpenFracture library (MIT, folder OpenFracture/, used as-is). OpenFracture does NOT
// use Voronoi cells: it cuts the mesh in two through its bounds centre with a random plane, puts both halves in a
// queue and repeats until it has the requested number of pieces, triangulating every cut face.
// My code: procedural pot / pillar / tablet meshes, the projectile, the impact impulse, the pre-fractured
// baseline (fracture once, reuse copies), slow motion, timing, cameras and labels.
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class FractureDirector : MonoBehaviour
    {
        public Material potMat, stoneMat, slabMat, insideMat, ballMat;
        public Camera camFull, camL, camR, camBack;
        public LabelSpec title, l1, l2, m1, m2;
        public float slow = 0.25f, ballSpeed = 14f;

        Mesh potMesh, pillarMesh, slabMesh;
        readonly Dictionary<int, double> avgMs = new Dictionary<int, double>();
        int phase = -1;

        class Shot
        {
            public float tHit; public GameObject target; public Rigidbody ball; public Vector3 impact, dir;
            public int count, seed; public Transform prefab; // prefab != null: reuse pre-fractured pieces
            public bool done; public Transform pieces; public double ms;
        }
        readonly List<Shot> shots = new List<Shot>();

        // ---------- procedural meshes (closed, so cut faces can be filled) ----------
        static Mesh Lathe(System.Func<float, float> r, float h, int seg, int rings)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int j = 0; j <= rings; j++)
                for (int i = 0; i <= seg; i++)
                {
                    float y = h * j / rings, a = i * Mathf.PI * 2 / seg, rr = r(y);
                    v.Add(new Vector3(Mathf.Cos(a) * rr, y, Mathf.Sin(a) * rr)); uv.Add(new Vector2(i / (float)seg, j / (float)rings));
                }
            for (int j = 0; j < rings; j++)
                for (int i = 0; i < seg; i++)
                {
                    int a = j * (seg + 1) + i, b = a + seg + 1;
                    t.Add(a); t.Add(b); t.Add(a + 1); t.Add(a + 1); t.Add(b); t.Add(b + 1);
                }
            foreach (var top in new[] { false, true })
            {
                float y = top ? h : 0, rr = r(y); int c = v.Count;
                v.Add(new Vector3(0, y, 0)); uv.Add(new Vector2(0.5f, 0.5f));
                for (int i = 0; i <= seg; i++)
                {
                    float a = i * Mathf.PI * 2 / seg;
                    v.Add(new Vector3(Mathf.Cos(a) * rr, y, Mathf.Sin(a) * rr)); uv.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.5f + Vector2.one * 0.5f);
                }
                for (int i = 0; i < seg; i++) { if (top) { t.Add(c); t.Add(c + i + 2); t.Add(c + i + 1); } else { t.Add(c); t.Add(c + i + 1); t.Add(c + i + 2); } }
            }
            var m = new Mesh(); m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            Outward(m); m.RecalculateNormals(); m.RecalculateBounds(); return m;
        }

        // flips the winding if the mesh came out inside-out (signed volume < 0)
        static void Outward(Mesh m)
        {
            var v = m.vertices; var t = m.triangles; double vol = 0;
            for (int i = 0; i < t.Length; i += 3) vol += Vector3.Dot(v[t[i]], Vector3.Cross(v[t[i + 1]], v[t[i + 2]]));
            if (vol < 0) { for (int i = 0; i < t.Length; i += 3) { int k = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = k; } m.triangles = t; }
        }

        static Mesh Prism(int sides, float rad, float h, float taper)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2 / sides, a1 = (i + 1) * Mathf.PI * 2 / sides; int c = v.Count;
                v.Add(new Vector3(Mathf.Cos(a0) * rad, 0, Mathf.Sin(a0) * rad)); v.Add(new Vector3(Mathf.Cos(a1) * rad, 0, Mathf.Sin(a1) * rad));
                v.Add(new Vector3(Mathf.Cos(a1) * rad * taper, h, Mathf.Sin(a1) * rad * taper)); v.Add(new Vector3(Mathf.Cos(a0) * rad * taper, h, Mathf.Sin(a0) * rad * taper));
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                t.Add(c); t.Add(c + 2); t.Add(c + 1); t.Add(c); t.Add(c + 3); t.Add(c + 2);
            }
            foreach (var top in new[] { false, true })
            {
                int c = v.Count; float y = top ? h : 0, rr = top ? rad * taper : rad;
                v.Add(new Vector3(0, y, 0)); uv.Add(Vector2.one * 0.5f);
                for (int i = 0; i < sides; i++) { float a = i * Mathf.PI * 2 / sides; v.Add(new Vector3(Mathf.Cos(a) * rr, y, Mathf.Sin(a) * rr)); uv.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.5f + Vector2.one * 0.5f); }
                for (int i = 0; i < sides; i++) { int a = c + 1 + i, b = c + 1 + (i + 1) % sides; if (top) { t.Add(c); t.Add(b); t.Add(a); } else { t.Add(c); t.Add(a); t.Add(b); } }
            }
            var m = new Mesh(); m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            Outward(m); m.RecalculateNormals(); m.RecalculateBounds(); return m;
        }

        static Mesh Box(Vector3 size)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); var src = g.GetComponent<MeshFilter>().sharedMesh; Destroy(g);
            var v = src.vertices; for (int i = 0; i < v.Length; i++) v[i] = Vector3.Scale(v[i], size) + Vector3.up * size.y * 0.5f;
            var m = new Mesh(); m.vertices = v; m.uv = src.uv; m.triangles = src.triangles; m.RecalculateNormals(); m.RecalculateBounds(); return m;
        }

        GameObject Target(string name, Mesh mesh, Material mat, Vector3 pos, float yaw)
        {
            var go = new GameObject(name); go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            var rb = go.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.mass = 4f;
            return go;
        }

        // Same template the library's Fracture component builds: outer material in slot 0, cut-face material in slot 1.
        GameObject Template(Material outer)
        {
            var t = new GameObject("Fragment");
            t.AddComponent<MeshFilter>();
            t.AddComponent<MeshRenderer>().sharedMaterials = new[] { outer, insideMat };
            t.AddComponent<MeshCollider>();
            var rb = t.AddComponent<Rigidbody>(); rb.interpolation = RigidbodyInterpolation.Interpolate;
            return t;
        }

        // Calls the library: Fragmenter.Fracture(source, options, template, parent). Returns the time it took.
        Transform Fracture(GameObject target, int count, int seed, out double ms)
        {
            var root = new GameObject(target.name + " pieces").transform;
            root.SetPositionAndRotation(target.transform.position, target.transform.rotation);
            var opts = new FractureOptions { fragmentCount = count, detectFloatingFragments = true, insideMaterial = insideMat };
            var tmpl = Template(target.GetComponent<MeshRenderer>().sharedMaterial);
            Random.InitState(seed);
            var sw = Stopwatch.StartNew();
            Fragmenter.Fracture(target, opts, tmpl, root);
            sw.Stop(); ms = sw.Elapsed.TotalMilliseconds;
            Destroy(tmpl);
            return root;
        }

        Rigidbody Ball()
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Sphere); b.name = "Projectile"; b.transform.localScale = Vector3.one * 0.24f;
            b.GetComponent<Renderer>().sharedMaterial = ballMat;
            var rb = b.AddComponent<Rigidbody>(); rb.mass = 3f; rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; b.SetActive(false);
            return rb;
        }

        void AddShot(float tHit, GameObject target, float hitHeight, int count, int seed, Transform prefab = null)
        {
            var c = target.transform.position + Vector3.up * hitHeight;
            var dir = new Vector3(1f, -0.08f, 0.25f).normalized;
            var r = target.GetComponent<MeshFilter>().sharedMesh.bounds.extents.x;
            shots.Add(new Shot { tHit = tHit, target = target, ball = Ball(), impact = c - dir * (r + 0.1f), dir = dir, count = count, seed = seed, prefab = prefab });
        }

        void Start()
        {
            Time.timeScale = slow; Time.fixedDeltaTime = 1f / 120f * slow * 4f; // 1 physics step per captured frame at 30 fps
            potMesh = Lathe(y => 0.36f + 0.22f * Mathf.Sin(Mathf.Clamp01(y / 0.95f) * Mathf.PI) * (1 - y * 0.3f) - (y > 0.9f ? (y - 0.9f) * 0.4f : 0), 1.1f, 40, 22);
            pillarMesh = Prism(8, 0.34f, 1.9f, 0.86f);
            slabMesh = Box(new Vector3(1.2f, 1.25f, 0.2f));
            // timing: average of 5 fractures (after one warm-up) of a hidden pot / pillar, per piece count
            foreach (int n in new[] { 8, 24, 64 })
            {
                var tgt = Target("timing", n == 24 ? potMesh : pillarMesh, stoneMat, new Vector3(0, -50, 0), 0);
                double sum = 0;
                for (int k = 0; k < 6; k++) { var p = Fracture(tgt, n, 100 + k, out double ms); if (k > 0) sum += ms; Destroy(p.gameObject); }
                avgMs[n] = sum / 5; Destroy(tgt);
                Debug.Log($"[gt] fracture {n} pieces ({(n == 24 ? "pot" : "pillar")}): {avgMs[n]:F1} ms average of 5");
            }
            Debug.Log($"[gt] meshes: pot {potMesh.triangles.Length / 3} tris, pillar {pillarMesh.triangles.Length / 3}, tablet {slabMesh.triangles.Length / 3}");
            // station A (x 0): hero row; again for the end card
            BuildHero(0f, 0f);
            BuildHero(500f, 21.3f); // end card row
            // stations B / C: pre-fractured vs fractured at impact, 3 hits each
            var proto = Target("pre-fractured pot", potMesh, potMat, new Vector3(100, -50, 0), 20);
            var stored = Fracture(proto, 24, 7, out double preMs); stored.gameObject.SetActive(false); Destroy(proto);
            Debug.Log($"[gt] pre-fracture once: 24 pieces {preMs:F1} ms");
            for (int h = 0; h < 3; h++)
            {
                float t0 = 4.4f + h * 3f;
                AddShot(t0, Target("pot B" + h, potMesh, potMat, new Vector3(100, 0, 0), 20), 0.55f, 24, 7, stored);
                AddShot(t0, Target("pot C" + h, potMesh, potMat, new Vector3(200, 0, 0), 20), 0.55f, 24, 11 + h * 17);
            }
            // stations D / E: 8 vs 64 pieces
            AddShot(13.6f, Target("pillar D", pillarMesh, stoneMat, new Vector3(300, 0, 0), 10), 1.1f, 8, 5);
            AddShot(13.6f, Target("pillar E", pillarMesh, stoneMat, new Vector3(400, 0, 0), 10), 1.1f, 64, 5);
            foreach (var s in shots) if (s.tHit > 4f && s.tHit < 13f) s.target.SetActive(false);
            Update();
        }

        void BuildHero(float x, float tOff)
        {
            AddShot(tOff + 0.0f, Target("hero pot", potMesh, potMat, new Vector3(x - 2.3f, 0, 0), 20), 0.55f, 32, 3);
            AddShot(tOff + 1.1f, Target("hero pillar", pillarMesh, stoneMat, new Vector3(x, 0, 0), 10), 1.1f, 48, 4);
            AddShot(tOff + 2.2f, Target("hero tablet", slabMesh, slabMat, new Vector3(x + 2.3f, 0, 0), -70), 0.7f, 40, 6);
        }

        void Look(Camera c, Vector3 target, float yaw, float pitch, float dist)
        {
            c.enabled = true;
            var r = Quaternion.Euler(pitch, yaw, 0);
            c.transform.SetPositionAndRotation(target - r * Vector3.forward * dist, r);
        }

        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }

        void Hit(Shot s)
        {
            s.done = true;
            if (s.prefab != null)
            {
                var sw = Stopwatch.StartNew();
                s.pieces = Instantiate(s.prefab); s.pieces.SetPositionAndRotation(s.target.transform.position, s.target.transform.rotation);
                s.pieces.gameObject.SetActive(true); sw.Stop(); s.ms = sw.Elapsed.TotalMilliseconds;
            }
            else s.pieces = Fracture(s.target, s.count, s.seed, out s.ms);
            s.target.SetActive(false);
            int n = 0;
            foreach (var rb in s.pieces.GetComponentsInChildren<Rigidbody>()) { rb.AddExplosionForce(3.5f, s.impact, 1.3f, 0.2f, ForceMode.VelocityChange); n++; }
            s.ball.isKinematic = false; s.ball.linearVelocity = s.dir * ballSpeed;
            Debug.Log($"[gt] t {FrameCapturer.T:F2} hit {s.target.name}: {n} bodies, {s.ms:F2} ms{(s.prefab != null ? " (copy of pre-fractured set)" : "")}");
        }

        Shot Last(float x, float t)
        {
            Shot best = null;
            foreach (var s in shots) if (Mathf.Abs(s.target.transform.position.x - x) < 5f && s.tHit - 0.7f <= t && (best == null || s.tHit > best.tHit)) best = s;
            return best;
        }

        void Update()
        {
            if (shots.Count == 0) return;
            float t = FrameCapturer.T;
            // phase-1 stations reuse one spot: show the target for the next hit, remove the last hit's pieces and ball
            foreach (var s in shots)
            {
                if (s.done) continue;
                if (t >= s.tHit - 0.7f && !s.target.activeSelf)
                {
                    s.target.SetActive(true);
                    foreach (var o in shots)
                        if (o != s && o.done && Mathf.Abs(o.target.transform.position.x - s.target.transform.position.x) < 5f)
                        { if (o.pieces) Destroy(o.pieces.gameObject); o.ball.gameObject.SetActive(false); }
                }
                if (t >= s.tHit - 0.7f && s.target.activeSelf)
                {
                    s.ball.gameObject.SetActive(true);
                    s.ball.transform.position = s.impact - s.dir * ballSpeed * slow * Mathf.Max(0, s.tHit - t);
                }
                if (t >= s.tHit) Hit(s);
            }

            camFull.enabled = camL.enabled = camR.enabled = camBack.enabled = false;
            int p = t < 4f ? 0 : t < 13f ? 1 : t < 21f ? 2 : 3;
            if (p == 0 || p == 3)
            {
                float x = p == 0 ? 0f : 500f, k = p == 0 ? t / 4f : (t - 21f) / 4f;
                Look(camFull, new Vector3(x + 0.4f, 0.75f, 0), -12f + 8f * k, 13f, Mathf.Lerp(7.6f, 6.6f, k));
                if (p == 0) Labels("Fracture at impact: solid pieces with filled cut faces", "pot 32, pillar 48, tablet 40 pieces",
                    "cut faces get their own material (orange), 1/4 speed", "", "");
                else Labels("OpenFracture: recursive random plane cuts", "Unity 6.3 URP, OpenFracture library (MIT), my own props and scene",
                    $"24 pieces in {avgMs[24]:F1} ms per fracture on an Apple M4 Mac mini (CPU, main thread)", "", "");
            }
            else if (p == 1)
            {
                camBack.enabled = true;
                int h = Mathf.Clamp((int)((t - 4f) / 3f), 0, 2);
                var b = Last(200f, t);
                Look(camL, new Vector3(100.6f, 0.5f, 0), -18f, 14f, 5.2f); Look(camR, new Vector3(200.6f, 0.5f, 0), -18f, 14f, 5.2f);
                string ms = b != null && b.done ? $", {b.ms:F1} ms" : "";
                Labels($"Same pot, same impact, hit {h + 1} of 3 (1/4 speed)",
                    "Pre-fractured: 24 pieces cut once, reused", "every hit breaks the same way",
                    "Fractured at impact: 24 pieces", $"new random cuts every hit{ms}");
            }
            else
            {
                camBack.enabled = true;
                Look(camL, new Vector3(300.6f, 0.8f, 0), -16f, 14f, 6.6f); Look(camR, new Vector3(400.6f, 0.8f, 0), -16f, 14f, 6.6f);
                Labels("Piece count, same pillar, same impact (1/4 speed)",
                    $"8 pieces: {avgMs[8]:F1} ms per fracture", "average of 5, CPU main thread",
                    $"64 pieces: {avgMs[64]:F1} ms per fracture", "average of 5, CPU main thread");
            }
            if (p != phase) { phase = p; Debug.Log($"[gt] t {t:F2} phase {p} '{title.text}' | {l1.text} | {m1.text}"); }
        }
    }
}
