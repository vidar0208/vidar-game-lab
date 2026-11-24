// Builds four moons from the same cube sphere and the same height function (PlanetHeight.compute, a port of the
// Solar-System moon shader). The settings come from the source project's moon asset ("Constant Companion": crater
// count, size range, rim, smoothing, ridge noise) and from the source's two randomiser presets
// ("a few large craters", "many small craters"). Crater placement follows CraterSettings.cs: Unity Random on the
// unit sphere for centres, PRNG.ValueBiasLower for sizes and floors. Cube sphere, welding, camera, lighting and
// timing are my own.
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class PlanetDirector : MonoBehaviour
    {
        public ComputeShader heightCS;
        public Material rockMat;
        public Camera camFull, camL, camR, camBack;
        public LabelSpec title, l1, l2, m1, m2;
        public int res = 320;
        public float radius = 10f, spin = 2.5f;

        struct Crater { public Vector3 centre; public float size, floorHeight, smoothness; }

        class Prng
        {
            readonly System.Random r;
            public Prng(int seed) { r = new System.Random(seed); }
            public float Value() => (float)(r.NextDouble() * 1.0000000004656612875245796924106);
            public float BiasLower(float s)
            {
                float t = Value(); if (s == 1) return 0;
                float k = Mathf.Clamp01(1 - s); k = k * k * k - 1;
                return Mathf.Clamp01((t + t * k) / (t * k + 1));
            }
        }

        Vector3[] unit; int[] tris;
        readonly List<Transform> planets = new List<Transform>();
        public double msHeights; int phase = -1;
        public const int MainCraters = 500;

        // Cube sphere: 6 grids of (res+1)^2 points pushed onto the sphere, shared edge points welded so normals have
        // no seams.
        void BuildSphere()
        {
            var map = new Dictionary<long, int>(); var verts = new List<Vector3>(); var idx = new List<int>();
            Vector3[] dirs = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            int n = res + 1;
            foreach (var up in dirs)
            {
                var a = new Vector3(up.y, up.z, up.x); var b = Vector3.Cross(up, a);
                var local = new int[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        var c = up + (x / (float)res * 2 - 1) * a + (y / (float)res * 2 - 1) * b;
                        float x2 = c.x * c.x, y2 = c.y * c.y, z2 = c.z * c.z;
                        var s = new Vector3(c.x * Mathf.Sqrt(1 - y2 / 2 - z2 / 2 + y2 * z2 / 3),
                                            c.y * Mathf.Sqrt(1 - x2 / 2 - z2 / 2 + x2 * z2 / 3),
                                            c.z * Mathf.Sqrt(1 - x2 / 2 - y2 / 2 + x2 * y2 / 3)).normalized;
                        long key = ((long)Mathf.RoundToInt(s.x * 100000) + 200000) * 400001L * 400001L
                                 + ((long)Mathf.RoundToInt(s.y * 100000) + 200000) * 400001L + Mathf.RoundToInt(s.z * 100000) + 200000;
                        if (!map.TryGetValue(key, out int vi)) { vi = verts.Count; verts.Add(s); map[key] = vi; }
                        local[y * n + x] = vi;
                    }
                for (int y = 0; y < res; y++)
                    for (int x = 0; x < res; x++)
                    {
                        int i0 = local[y * n + x], i1 = local[y * n + x + 1], i2 = local[(y + 1) * n + x], i3 = local[(y + 1) * n + x + 1];
                        var nrm = Vector3.Cross(verts[i2] - verts[i0], verts[i1] - verts[i0]);
                        if (Vector3.Dot(nrm, verts[i0]) > 0) { idx.Add(i0); idx.Add(i2); idx.Add(i1); idx.Add(i1); idx.Add(i2); idx.Add(i3); }
                        else { idx.Add(i0); idx.Add(i1); idx.Add(i2); idx.Add(i1); idx.Add(i3); idx.Add(i2); }
                    }
            }
            unit = verts.ToArray(); tris = idx.ToArray();
        }

        Crater[] MakeCraters(int count, Vector2 sizeMinMax, float sizeDistribution, int craterSeed, int masterSeed)
        {
            UnityEngine.Random.InitState(craterSeed + masterSeed);
            var prng = new Prng(masterSeed); var c = new Crater[Mathf.Max(count, 1)];
            if (count == 0) { c[0] = new Crater { centre = Vector3.up, size = 0, floorHeight = 0, smoothness = 1 }; return c; }
            for (int i = 0; i < count; i++)
            {
                float t = prng.BiasLower(sizeDistribution);
                c[i] = new Crater
                {
                    size = Mathf.Lerp(sizeMinMax.x, sizeMinMax.y, t),
                    floorHeight = Mathf.Lerp(-1.2f, -0.2f, t + prng.BiasLower(0.3f)),
                    smoothness = Mathf.Lerp(0.4f, 1.5f, 1 - t),
                    centre = UnityEngine.Random.onUnitSphere
                };
            }
            return c;
        }

        // Noise settings of the source moon asset; offsets drawn from PRNG(seed) in the source's order.
        void SetNoise(int seed)
        {
            var prng = new Prng(seed);
            float[] Off() { var o = new Vector3(prng.Value(), prng.Value(), prng.Value()) * prng.Value() * 10000; return new[] { o.x, o.y, o.z }; }
            var o1 = Off(); heightCS.SetFloats("noiseParams_shape", o1[0], o1[1], o1[2], 4, 0.5f, 2, 0.97f, 0f, 0, 0, 0, 0);
            var o2 = Off(); heightCS.SetFloats("noiseParams_ridge", o2[0], o2[1], o2[2], 4, 0.5f, 5, 1.82f, -2.84f, 2, 0.5f, 0, 3);
            var o3 = Off(); heightCS.SetFloats("noiseParams_ridge2", o3[0], o3[1], o3[2], 5, 0.5f, 2, 2f, 3f, 0.5f, 1, 0, 0);
        }

        float[] Heights(Crater[] craters, int count, ComputeBuffer vb, ComputeBuffer hb)
        {
            var cb = new ComputeBuffer(craters.Length, 24); cb.SetData(craters);
            heightCS.SetBuffer(0, "vertices", vb); heightCS.SetBuffer(0, "heights", hb); heightCS.SetBuffer(0, "craters", cb);
            heightCS.SetInt("numVertices", unit.Length); heightCS.SetInt("numCraters", count);
            heightCS.SetFloat("rimSteepness", 0.42f); heightCS.SetFloat("rimWidth", 1.23f);
            SetNoise(0);
            heightCS.Dispatch(0, (unit.Length + 255) / 256, 1, 1);
            var h = new float[unit.Length]; hb.GetData(h); cb.Release();
            return h;
        }

        Transform Planet(string name, float[] h, float x)
        {
            var v = new Vector3[unit.Length];
            for (int i = 0; i < v.Length; i++) v[i] = unit[i] * (h[i] * radius);
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.vertices = v; mesh.triangles = tris; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject(name); go.transform.position = new Vector3(0, x, 0); // stacked vertically so no moon falls in another one's shadow
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = rockMat; r.shadowCastingMode = ShadowCastingMode.On;
            planets.Add(go.transform);
            return go.transform;
        }

        void Start()
        {
            var sw = Stopwatch.StartNew();
            BuildSphere();
            Debug.Log($"[gt] cube sphere res {res}: {unit.Length} vertices, {tris.Length / 3} triangles, {sw.ElapsedMilliseconds} ms");
            var vb = new ComputeBuffer(unit.Length, 12); vb.SetData(unit);
            var hb = new ComputeBuffer(unit.Length, 4);
            var main = MakeCraters(MainCraters, new Vector2(0.01f, 0.15f), 0.675f, 5, 0);
            var few = MakeCraters(50, new Vector2(0.01f, 0.2f), 0.4f, 5, 0);
            var many = MakeCraters(1500, new Vector2(0.01f, 0.08f), 0.74f, 5, 0);
            var none = MakeCraters(0, Vector2.zero, 0, 5, 0);
            Heights(main, MainCraters, vb, hb); // warm-up (shader compile, buffer upload)
            sw.Restart(); for (int i = 0; i < 5; i++) Heights(main, MainCraters, vb, hb); msHeights = sw.Elapsed.TotalMilliseconds / 5;
            Debug.Log($"[gt] heights for {unit.Length} vertices x {MainCraters} craters: {msHeights:F1} ms (dispatch + readback, avg of 5)");
            Planet("Moon 500", Heights(main, MainCraters, vb, hb), 0);
            Planet("Moon noise only", Heights(none, 0, vb, hb), 100);
            Planet("Moon 50 large", Heights(few, 50, vb, hb), 200);
            Planet("Moon 1500 small", Heights(many, 1500, vb, hb), 300);
            var h = Heights(main, MainCraters, vb, hb); float lo = 9, hi = 0; foreach (var x in h) { lo = Mathf.Min(lo, x); hi = Mathf.Max(hi, x); }
            Debug.Log($"[gt] main moon height range {lo:F4} .. {hi:F4} (unit radius)");
            vb.Release(); hb.Release();
            Update();
        }

        void Look(Camera c, Vector3 target, float pitch, float yaw, float dist)
        {
            var r = Quaternion.Euler(pitch, 180f + yaw, 0);
            c.transform.SetPositionAndRotation(target - r * Vector3.forward * dist, r);
        }

        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }

        void Update()
        {
            if (planets.Count < 4) return;
            float t = FrameCapturer.T;
            foreach (var p in planets) p.rotation = Quaternion.Euler(12f, -spin * t - 40f, 0);
            camFull.enabled = camL.enabled = camR.enabled = camBack.enabled = false;
            int ph = t < 4f ? 0 : t < 12f ? 1 : t < 20f ? 2 : 3;
            var term = new Vector3(-1.2f, 0.5f, 9.0f);
            if (ph == 0 || ph == 3)
            {
                camFull.enabled = true;
                var c0 = planets[0].position;
                if (ph == 0)
                {
                    Look(camFull, c0, 6f, 4f, Mathf.Lerp(40f, 37f, t / 4f));
                    Labels($"A moon from noise + {MainCraters} craters, no textures",
                        "each crater is one height curve: bowl, flat floor, raised rim", "a low sun along the terminator shows the relief", "", "");
                }
                else
                {
                    float k = Mathf.SmoothStep(0, 1, (t - 20f) / 4.5f);
                    Look(camFull, c0 + term * k, Mathf.Lerp(6f, 30f, k), Mathf.Lerp(4f, 10f, k), Mathf.Lerp(40f, 8f, k));
                    Labels($"{unit.Length:N0} vertices, heights on the GPU in {msHeights:F1} ms",
                        $"{MainCraters} craters + 2 ridge noise layers, Apple M4 Mac mini (Metal), compute + readback",
                        "Unity 6.3 URP, port of an MIT-licensed planet generator", "", "");
                }
            }
            else
            {
                camBack.enabled = camL.enabled = camR.enabled = true;
                float k = ph == 1 ? Mathf.SmoothStep(0, 1, (t - 4f) / 6f) : 1f;
                float yaw = ph == 2 ? (t - 12f) * 0.6f : 0f;
                Vector3 off = term * 0.95f * k; float dist = Mathf.Lerp(36f, 17f, k), pitch = Mathf.Lerp(6f, 14f, k);
                Transform a = ph == 1 ? planets[1] : planets[2], b = ph == 1 ? planets[0] : planets[3];
                Look(camL, a.position + off, pitch, yaw, dist); Look(camR, b.position + off, pitch, yaw, dist);
                if (ph == 1) Labels("Same seed, same sun, same ridge noise", "Noise only", "0 craters",
                    $"Noise + {MainCraters} craters", "radius 1-15% of the moon's radius");
                else Labels("Same crater function, two settings from the source's randomiser",
                    "50 craters, radius 1-20%", "\"a few large craters\"", "1,500 craters, radius 1-8%", "\"many small craters\"");
            }
            if (ph != phase) { phase = ph; Debug.Log($"[gt] t {t:F2} phase {ph} '{title.text}' | {l1.text} | {m1.text}"); }
        }
    }
}
