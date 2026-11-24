// Particle life on the GPU. The rule, the constants (radius 80, viscosity 0.7, wall repel 40), the seeded
// mulberry32 generator and the order in which it draws the rules and start positions are ported from
// particle_life.html (hunar4321/particle-life, MIT); the step runs in ParticleLife.compute.
// My code: the compute port, more particles on a larger field at the original's density, the rule-matrix inset,
// the symmetric version of a matrix (same averaging as the original's symmetricRules), timing, cameras and labels.
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class LifeDirector : MonoBehaviour
    {
        public ComputeShader cs;
        public Material dotMat, cellMat;
        public Camera camFull, camL, camR, camBack;
        public LabelSpec title, l1, l2, m1, m2;
        public int stepsPerFrame = 2;

        const int K = 4;
        const float Scale = 0.02f; // world units per sim pixel (1 sim pixel = 1 screen pixel at 1080p)
        public const ulong SeedHero = 91651088029UL, SeedA = 1906214UL, SeedB = 77031UL;
        static readonly Color[] Palette = { new Color(0.2f, 1f, 0.3f), new Color(1f, 0.16f, 0.16f), new Color(1f, 0.6f, 0.08f), new Color(0.1f, 0.85f, 1f) };

        // mulberry32 as in the original (seed taken modulo 2^32, like JavaScript's 32-bit integer ops)
        class Mulberry
        {
            uint s;
            public Mulberry(ulong seed) { s = (uint)(seed & 0xffffffffUL); }
            public float Next()
            {
                unchecked
                {
                    s += 0x6D2B79F5u; uint t = s;
                    t = (t ^ (t >> 15)) * (t | 1u);
                    t ^= t + (t ^ (t >> 7)) * (t | 61u);
                    return (float)((double)(t ^ (t >> 14)) / 4294967296.0);
                }
            }
        }

        class Life
        {
            public readonly int n; public readonly float w, h; public readonly float[] rules = new float[K * K];
            public ComputeBuffer[] pos = new ComputeBuffer[2]; public ComputeBuffer col, rul, rad;
            public Vector4[] start; public uint[] colours;
            public Material mat; public Vector3 origin;
            readonly ComputeShader cs; readonly int kern;
            public Life(ComputeShader cs, ulong seed, int perColour, float w, float h, bool symmetric, Vector3 origin, Material dot)
            {
                this.cs = cs; kern = cs.FindKernel("Step"); n = perColour * K; this.w = w; this.h = h; this.origin = origin;
                var rng = new Mulberry(seed);
                for (int i = 0; i < K; i++) for (int j = 0; j < K; j++) rules[i * K + j] = rng.Next() * 2 - 1;
                if (symmetric)
                    for (int i = 0; i < K; i++) for (int j = 0; j < i; j++) { float v = 0.5f * (rules[i * K + j] + rules[j * K + i]); rules[i * K + j] = rules[j * K + i] = v; }
                start = new Vector4[n]; colours = new uint[n];
                for (int c = 0, k = 0; c < K; c++)
                    for (int i = 0; i < perColour; i++, k++)
                    { float x = rng.Next() * (w - 100) + 50, y = rng.Next() * (h - 100) + 50; start[k] = new Vector4(x, y, 0, 0); colours[k] = (uint)c; }
                for (int b = 0; b < 2; b++) { pos[b] = new ComputeBuffer(n, 16); pos[b].SetData(start); }
                col = new ComputeBuffer(n, 4); col.SetData(colours);
                rul = new ComputeBuffer(K * K, 4); rul.SetData(rules);
                rad = new ComputeBuffer(K, 4); var r2 = new float[K]; for (int i = 0; i < K; i++) r2[i] = 80f * 80f; rad.SetData(r2);
                mat = new Material(dot);
                mat.SetFloat("_Size", 2.6f); mat.SetFloat("_Scale", Scale); mat.SetFloat("_Gain", 0.7f);
                mat.SetVector("_Origin", origin); mat.SetBuffer("_Colour", col);
                var pal = new Vector4[8]; for (int i = 0; i < K; i++) pal[i] = Palette[i]; mat.SetVectorArray("_Palette", pal);
                mat.SetBuffer("_Pos", pos[0]);
                var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
                mesh.vertices = new Vector3[n * 6];
                var idx = new int[n * 6]; for (int i = 0; i < idx.Length; i++) idx[i] = i;
                mesh.SetIndices(idx, MeshTopology.Triangles, 0, false);
                mesh.bounds = new Bounds(origin + new Vector3(w, h, 0) * Scale * 0.5f, new Vector3(w, h, 10) * Scale * 2f);
                var go = new GameObject("Particles " + seed);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            }
            public void Step(int count)
            {
                cs.SetInt("_N", n); cs.SetInt("_K", K); cs.SetFloat("_W", w); cs.SetFloat("_H", h);
                cs.SetFloat("_Viscosity", 0.7f); cs.SetFloat("_WallRepel", 40f); cs.SetFloat("_TimeScale", 1f);
                cs.SetBuffer(kern, "_Colour", col); cs.SetBuffer(kern, "_Rules", rul); cs.SetBuffer(kern, "_Radius2", rad);
                for (int s = 0; s < count; s++)
                {
                    cs.SetBuffer(kern, "_Read", pos[0]); cs.SetBuffer(kern, "_Write", pos[1]);
                    cs.Dispatch(kern, n / 256, 1, 1);
                    var t = pos[0]; pos[0] = pos[1]; pos[1] = t;
                }
                mat.SetBuffer("_Pos", pos[0]);
            }
            public Vector3 Centre => origin + new Vector3(w, h, 0) * Scale * 0.5f;
        }

        Life hero, sym, asym, simA, simB;
        readonly Dictionary<string, double> ms = new Dictionary<string, double>();
        int phaseShown = -1;
        float spL = -1, spR = -1; int spFrames;
        readonly Dictionary<Life, Vector4[]> snap = new Dictionary<Life, Vector4[]>();

        // mean distance each particle travelled over the last second (60 steps), read back from the GPU.
        // Jitter inside a cluster cancels out over a second; clusters that keep moving add up.
        float Travel(Life l)
        {
            var req = AsyncGPUReadback.Request(l.pos[0]); req.WaitForCompletion();
            var d = req.GetData<Vector4>().ToArray(); float r = -1;
            if (snap.TryGetValue(l, out var old))
            {
                double sum = 0; for (int i = 0; i < d.Length; i++) sum += Vector2.Distance(d[i], old[i]);
                r = (float)(sum / d.Length);
            }
            snap[l] = d; return r;
        }

        void Speeds(Life a, Life b, float t)
        {
            if (spFrames % 30 == 0) { spL = Travel(a); spR = Travel(b); if (spL >= 0) Debug.Log($"[gt] t {t:F2} travel per second L {spL:F1} R {spR:F1}"); }
            spFrames++;
        }

        static string Move(float v) => v < 0 ? "" : $"moved {v:F0} px in the last second (mean)";

        // the rule matrix as 4 x 4 cells: blue = pull (negative), pink = push (positive); colour dots label rows and columns
        void Inset(Life l, Vector3 topRight)
        {
            float c = 0.62f; var root = new GameObject("Matrix").transform;
            Quad(root, topRight + new Vector3(-(K + 1) * c - 0.25f, -(K + 1) * c - 0.25f, 0.5f), new Vector2((K + 1) * c + 0.5f, (K + 1) * c + 0.5f), new Color(0.03f, 0.035f, 0.05f));
            for (int i = 0; i < K; i++)
            {
                Vector3 row = topRight + new Vector3(-(K + 1) * c, -(i + 2) * c, 0), colH = topRight + new Vector3(-(K - i) * c, -c, 0);
                Quad(root, row + new Vector3(c * 0.3f, c * 0.3f, 0), Vector2.one * c * 0.4f, Palette[i] * 1.4f);
                Quad(root, colH + new Vector3(c * 0.3f, c * 0.3f, 0), Vector2.one * c * 0.4f, Palette[i] * 1.4f);
                for (int j = 0; j < K; j++)
                {
                    float v = l.rules[i * K + j];
                    var col = v < 0 ? new Color(0.35f, 0.62f, 1f) * (-v) : new Color(1f, 0.32f, 0.68f) * v;
                    Quad(root, topRight + new Vector3(-(K - j) * c + 0.04f, -(i + 2) * c + 0.04f, 0), Vector2.one * (c - 0.08f), col + new Color(0.04f, 0.04f, 0.05f));
                }
            }
        }

        void Quad(Transform root, Vector3 corner, Vector2 size, Color c)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(g.GetComponent<Collider>()); g.transform.SetParent(root);
            g.transform.position = corner + new Vector3(size.x, size.y, 0) * 0.5f; g.transform.localScale = new Vector3(size.x, size.y, 1);
            var m = new Material(cellMat); m.SetColor("_BaseColor", c); m.renderQueue = 3100; g.GetComponent<Renderer>().sharedMaterial = m;
            g.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        double GpuMs(Life l, int steps)
        {
            l.Step(20); AsyncGPUReadback.Request(l.pos[0]).WaitForCompletion();
            var sw = Stopwatch.StartNew(); l.Step(steps); AsyncGPUReadback.Request(l.pos[0]).WaitForCompletion(); sw.Stop();
            return sw.Elapsed.TotalMilliseconds / steps;
        }

        // the same step on one CPU thread in plain C#, for comparison
        static double CpuMs(Life l)
        {
            var p = (Vector4[])l.start.Clone(); var q = new Vector4[p.Length]; var best = double.MaxValue;
            for (int rep = 0; rep < 3; rep++)
            {
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < p.Length; i++)
                {
                    var a = p[i]; int ca = (int)l.colours[i]; float fx = 0, fy = 0;
                    for (int j = 0; j < p.Length; j++)
                    {
                        float dx = a.x - p[j].x, dy = a.y - p[j].y, d = dx * dx + dy * dy;
                        if (d > 0 && d < 6400f) { float F = l.rules[ca * K + (int)l.colours[j]] / Mathf.Sqrt(d); fx += F * dx; fy += F * dy; }
                    }
                    a.z = a.z * 0.3f + fx; a.w = a.w * 0.3f + fy; a.x += a.z; a.y += a.w; q[i] = a;
                }
                sw.Stop(); best = System.Math.Min(best, sw.Elapsed.TotalMilliseconds);
            }
            return best;
        }

        void Start()
        {
            // hero: 8,192 particles on a 1920 x 1080 field (the original's slider stops at 4 x 1,000 on its canvas)
            hero = new Life(cs, SeedHero, 2048, 1920, 1080, false, new Vector3(0, 0, 0), dotMat);
            // halves: 4,096 particles on 956 x 1080 each, same density
            sym = new Life(cs, SeedA, 1024, 956, 1080, true, new Vector3(100, 0, 0), dotMat);
            asym = new Life(cs, SeedA, 1024, 956, 1080, false, new Vector3(200, 0, 0), dotMat);
            simA = new Life(cs, SeedHero, 1024, 956, 1080, false, new Vector3(300, 0, 0), dotMat);
            simB = new Life(cs, SeedB, 1024, 956, 1080, false, new Vector3(400, 0, 0), dotMat);
            ms["gpu8k"] = GpuMs(hero, 300); ms["gpu4k"] = GpuMs(simA, 300); ms["cpu8k"] = CpuMs(hero);
            foreach (var kv in ms) Debug.Log($"[gt] timing {kv.Key}: {kv.Value:F3} ms per step");
            foreach (var l in new[] { hero, sym, asym, simA, simB })
                Debug.Log($"[gt] seed {(l == sym ? "sym " : "")}{l.n}: rules {string.Join(" ", System.Array.ConvertAll(l.rules, v => v.ToString("F2")))}");
            hero.Step(600); sym.Step(40); asym.Step(40); simA.Step(300); simB.Step(300);
            foreach (var l in new[] { hero, sym, asym, simA, simB }) Inset(l, l.origin + new Vector3(l.w * Scale - 0.3f, l.h * Scale - 2.6f, -0.2f));
            foreach (var c in new[] { camFull, camL, camR }) { c.orthographic = true; c.orthographicSize = 10.8f; }
            Update();
        }

        void Look(Camera c, Life l) { c.enabled = true; c.transform.SetPositionAndRotation(l.Centre + new Vector3(0, 0, -10f), Quaternion.identity); }

        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }

        void Update()
        {
            if (hero == null) return;
            float t = FrameCapturer.T;
            camFull.enabled = camL.enabled = camR.enabled = camBack.enabled = false;
            int p = t < 7f ? 0 : t < 15f ? 1 : t < 22f ? 2 : 3;
            if (p == 0 || p == 3)
            {
                hero.Step(stepsPerFrame); Look(camFull, hero);
                if (p == 0) Labels("Particle life: 4 colours, 16 random rules, nothing else",
                    "8,192 particles; each one checks all 8,191 others every step",
                    "rule matrix (top right): row = who moves, blue = pull, pink = push", "", "");
                else Labels("GPU port of particle life (hunar4321, MIT)",
                    $"{ms["gpu8k"]:F2} ms per step for 8,192 particles, every pair, Apple M4 GPU",
                    $"the same all-pairs force loop in plain C# on 1 CPU thread: {ms["cpu8k"]:F0} ms", "", "Unity 6.3 URP compute shader, 2 steps per frame");
            }
            else if (p == 1)
            {
                camBack.enabled = true; sym.Step(stepsPerFrame); asym.Step(stepsPerFrame); Look(camL, sym); Look(camR, asym);
                if (phaseShown != 1) { spFrames = 0; spL = spR = -1; }
                Speeds(sym, asym, t);
                Labels($"Seed {SeedA}: same start positions, 4,096 particles each",
                    "Symmetric: A acts on B as B acts on A", Move(spL),
                    "Asymmetric: the random rules as drawn", Move(spR));
            }
            else
            {
                camBack.enabled = true; simA.Step(stepsPerFrame); simB.Step(stepsPerFrame); Look(camL, simA); Look(camR, simB);
                if (phaseShown != 2) { spFrames = 0; spL = spR = -1; }
                Speeds(simA, simB, t);
                Labels("Two random rule matrices, same code, 4,096 particles each",
                    $"seed {SeedHero}", Move(spL), $"seed {SeedB}", Move(spR));
            }
            if (p != phaseShown) { phaseShown = p; Debug.Log($"[gt] t {t:F2} phase {p} '{title.text}' | {l1.text} | {l2.text} | {m1.text} | {m2.text}"); }
        }

        void OnDestroy()
        {
            foreach (var l in new[] { hero, sym, asym, simA, simB })
                if (l != null) { l.pos[0].Release(); l.pos[1].Release(); l.col.Release(); l.rul.Release(); l.rad.Release(); }
        }
    }
}
