// Mesh deformers with the Deform library (MIT, folder Deform/, used as-is). Each deformer is a Burst-compiled
// IJobParallelFor; a Deformable chains them (Twist -> Bend -> Ripple -> Melt), then recalculates normals and bounds
// with more jobs and uploads the result to a copy of the mesh.
// My code: the procedural fluted sculpture, the animation of the deformer settings, the timing (Burst on/off,
// 0 or all job workers), the 16.7 ms budget split, cameras and labels. All deformables run in UpdateMode.Custom
// and are updated by this script with Deformable.ForceImmediateUpdate (schedule the chain, complete it, apply it).
using System.Collections.Generic;
using System.Diagnostics;
using Deform;
using Unity.Burst;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class DeformDirector : MonoBehaviour
    {
        public Material heroMat, gridMat;
        public Camera camFull, camL, camR, camBack;
        public LabelSpec title, l1, l2, m1, m2;
        const float H = 3f, Budget = 1000f / 60f;

        class Sculpt
        {
            public Deformable d; public TwistDeformer tw; public BendDeformer be; public RippleDeformer ri; public MeltDeformer me;
            public float phase;
        }
        Sculpt hero, normOff, normOn;
        readonly List<Sculpt> gridL = new List<Sculpt>(), gridR = new List<Sculpt>();
        int rrL, rrR, sumL, sumR, frames;
        readonly Dictionary<string, double> ms = new Dictionary<string, double>();
        int workersMax, phaseShown = -1;
        int verts, gridVerts;

        // ---------- fluted, bulging column; one ring per height step, seam vertices shared so normals stay smooth ----------
        static Mesh Column(int seg, int rings)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            System.Func<float, float, float> R = (y, a) =>
            {
                float u = y / H;
                float prof = 0.36f + 0.16f * Mathf.Sin(u * Mathf.PI * 1.6f + 0.3f) + 0.22f * Mathf.Pow(1 - u, 6f) + 0.06f * Mathf.Sin(u * Mathf.PI * 9f) * u;
                return prof * (1f + 0.075f * Mathf.Cos(a * 10f));
            };
            for (int j = 0; j <= rings; j++)
                for (int i = 0; i < seg; i++)
                {
                    float y = H * j / rings, a = i * Mathf.PI * 2 / seg, r = R(y, a);
                    v.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
                }
            for (int j = 0; j < rings; j++)
                for (int i = 0; i < seg; i++)
                {
                    int i1 = (i + 1) % seg, a = j * seg + i, b = a + seg, c = j * seg + i1, d = c + seg;
                    t.Add(a); t.Add(b); t.Add(c); t.Add(c); t.Add(b); t.Add(d);
                }
            foreach (var top in new[] { false, true })
            {
                float y = top ? H : 0; int c0 = v.Count;
                v.Add(new Vector3(0.0005f, y, 0.0003f)); // centre a hair off the axis: Melt normalizes the radial direction
                for (int i = 0; i < seg; i++) { float a = i * Mathf.PI * 2 / seg, r = R(y, a); v.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r)); }
                for (int i = 0; i < seg; i++) { int p = c0 + 1 + i, q = c0 + 1 + (i + 1) % seg; if (top) { t.Add(c0); t.Add(q); t.Add(p); } else { t.Add(c0); t.Add(p); t.Add(q); } }
            }
            var m = new Mesh { indexFormat = IndexFormat.UInt32 };
            m.SetVertices(v); m.SetTriangles(t, 0);
            Outward(m); m.RecalculateNormals(); m.RecalculateBounds(); return m;
        }

        static void Outward(Mesh m)
        {
            var v = m.vertices; var t = m.triangles; double vol = 0;
            for (int i = 0; i < t.Length; i += 3) vol += Vector3.Dot(v[t[i]], Vector3.Cross(v[t[i + 1]], v[t[i + 2]]));
            if (vol < 0) { for (int i = 0; i < t.Length; i += 3) { int k = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = k; } m.triangles = t; }
        }

        static T Axis<T>(Transform parent, string name, Vector3 localPos, Vector3 euler) where T : Deformer
        {
            var g = new GameObject(name); g.transform.SetParent(parent, false);
            g.transform.localPosition = localPos; g.transform.localRotation = Quaternion.Euler(euler);
            return g.AddComponent<T>();
        }

        Sculpt Make(string name, Mesh mesh, Material mat, Vector3 pos, float scale, float yaw, bool normals, float phase)
        {
            var go = new GameObject(name); go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0)); go.transform.localScale = Vector3.one * scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            var s = new Sculpt { phase = phase };
            s.d = go.AddComponent<Deformable>();
            s.d.UpdateMode = UpdateMode.Custom;
            s.d.NormalsRecalculation = normals ? NormalsRecalculation.Auto : NormalsRecalculation.None;
            // Twist / Melt use the axis' local Z (pointed up), Bend uses local Y, Ripple pushes along local Z (towards the viewer)
            s.tw = Axis<TwistDeformer>(go.transform, "Twist", Vector3.zero, new Vector3(-90, 0, 0));
            s.tw.Mode = BoundsMode.Limited; s.tw.Bottom = 0f; s.tw.Top = H; s.tw.StartAngle = 0f;
            s.be = Axis<BendDeformer>(go.transform, "Bend", Vector3.zero, new Vector3(0, 0, 0));
            s.be.Bottom = 0.25f; s.be.Top = H;
            s.ri = Axis<RippleDeformer>(go.transform, "Ripple", new Vector3(0, 1.55f, 0), new Vector3(0, 180, 0));
            s.ri.Frequency = 3.2f; s.ri.Amplitude = 0f; s.ri.Mode = BoundsMode.Limited; s.ri.InnerRadius = 0f; s.ri.OuterRadius = 1.6f; s.ri.Falloff = 0.7f;
            s.me = Axis<MeltDeformer>(go.transform, "Melt", Vector3.zero, new Vector3(-90, 0, 0));
            s.me.Top = 1.4f; s.me.Bottom = 0f; s.me.Falloff = 2.2f; s.me.Radius = 1f; s.me.Factor = 0f;
            s.me.VerticalFrequency = 2.2f; s.me.VerticalMagnitude = 0.25f; s.me.RadialFrequency = 3f; s.me.RadialMagnitude = 0.06f;
            s.d.AddDeformer(s.tw); s.d.AddDeformer(s.be); s.d.AddDeformer(s.ri); s.d.AddDeformer(s.me);
            return s;
        }

        static float Ramp(float t, float t0) => Mathf.SmoothStep(0, 1, Mathf.Clamp01((t - t0) / 0.8f));

        // chain settings at time t; 'k' = how much of each deformer is switched on (the hero adds them one by one)
        static void Pose(Sculpt s, float t, float kTw, float kBe, float kRi, float kMe)
        {
            float u = t + s.phase;
            s.tw.EndAngle = (170f * Mathf.Sin(u * 0.7f) + 40f) * kTw;
            s.be.Angle = 38f * Mathf.Sin(u * 0.55f + 0.6f) * kBe;
            s.be.transform.localRotation = Quaternion.Euler(0, 40f * Mathf.Sin(u * 0.31f), 0);
            s.ri.Amplitude = 0.055f * kRi; s.ri.Offset = u * 0.9f;
            s.me.Factor = (0.55f + 0.25f * Mathf.Sin(u * 0.8f)) * kMe;
        }

        static void Pose(Sculpt s, float t) => Pose(s, t, 1, 1, 1, 1);

        static void SetJobs(bool burst, int workers)
        {
            if (BurstCompiler.Options.EnableBurstCompilation != burst) BurstCompiler.Options.EnableBurstCompilation = burst;
            if (JobsUtility.JobWorkerCount != workers) JobsUtility.JobWorkerCount = workers;
        }

        double Median(Sculpt s, bool burst, int workers, bool full)
        {
            SetJobs(burst, workers);
            var list = new List<double>();
            for (int k = 0; k < 18; k++)
            {
                Pose(s, 2.3f + k * 0.37f);
                var sw = Stopwatch.StartNew();
                if (full) s.d.ForceImmediateUpdate();
                else { s.d.Complete(); s.d.PreSchedule(true); s.d.Schedule(true).Complete(); sw.Stop(); s.d.ApplyData(true); }
                sw.Stop();
                if (k >= 3) list.Add(sw.Elapsed.TotalMilliseconds);
            }
            list.Sort(); return list[list.Count / 2];
        }

        void Start()
        {
            BurstCompiler.Options.EnableBurstSafetyChecks = false;
            JobsUtility.JobDebuggerEnabled = false;
            Unity.Collections.NativeLeakDetection.Mode = Unity.Collections.NativeLeakDetectionMode.Disabled;
            workersMax = JobsUtility.JobWorkerCount;
            var big = Column(640, 384); var small = Column(192, 128);
            verts = big.vertexCount; gridVerts = small.vertexCount;
            Debug.Log($"[gt] meshes: hero {verts} verts {big.triangles.Length / 3} tris, grid {gridVerts} verts; workers {workersMax}, burst {BurstCompiler.IsEnabled}, cores {SystemInfo.processorCount}");

            hero = Make("Hero", big, heroMat, new Vector3(0, 0, 0), 1f, 0, true, 0f);
            normOff = Make("Normals off", big, heroMat, new Vector3(100, 0, 0), 1f, 0, false, 0f);
            normOn = Make("Normals on", big, heroMat, new Vector3(200, 0, 0), 1f, 0, true, 0f);
            for (int side = 0; side < 2; side++)
                for (int r = 0; r < 3; r++)
                    for (int c = 0; c < 4; c++)
                    {
                        var p = new Vector3(300 + side * 100 + (c - 1.5f) * 1.4f, 0, r * 1.9f);
                        (side == 0 ? gridL : gridR).Add(Make($"Grid {side} {r} {c}", small, gridMat, p, 0.4f, 0, true, (r * 4 + c) * 0.9f));
                    }

            // timing on the hero mesh, full chain on
            ms["c1"] = Median(hero, false, 0, false);
            ms["cj"] = Median(hero, false, workersMax, false);
            ms["b1"] = Median(hero, true, 0, false);
            ms["bj"] = Median(hero, true, workersMax, false);
            ms["bjFull"] = Median(hero, true, workersMax, true);
            ms["c1Full"] = Median(hero, false, 0, true);
            ms["gc1"] = Median(gridL[0], false, 0, true);
            ms["gbj"] = Median(gridL[0], true, workersMax, true);
            var swT = Stopwatch.StartNew(); SetJobs(false, 0); SetJobs(true, workersMax); swT.Stop();
            foreach (var kv in ms) Debug.Log($"[gt] timing {kv.Key}: {kv.Value:F2} ms (median of 15)");
            Debug.Log($"[gt] toggle burst+workers: {swT.Elapsed.TotalMilliseconds:F2} ms; workers now {JobsUtility.JobWorkerCount}");
            foreach (var s in new[] { hero, normOff, normOn }) { Pose(s, 0f, 1, 0, 0, 0); s.d.ForceImmediateUpdate(); }
            foreach (var s in gridL) { Pose(s, 15f); s.d.ForceImmediateUpdate(); }
            foreach (var s in gridR) { Pose(s, 15f); s.d.ForceImmediateUpdate(); }
            Update();
        }

        void Look(Camera c, Vector3 target, float yaw, float pitch, float dist)
        {
            c.enabled = true;
            var r = Quaternion.Euler(pitch, yaw, 0);
            c.transform.SetPositionAndRotation(target - r * Vector3.forward * dist, r);
        }

        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }

        // updates as many sculptures as fit into the 16.7 ms budget (at least one), round-robin; returns the count
        static int Budgeted(List<Sculpt> list, ref int rr, float t)
        {
            var sw = Stopwatch.StartNew(); int n = 0;
            do { var s = list[rr]; Pose(s, t); s.d.ForceImmediateUpdate(); rr = (rr + 1) % list.Count; n++; }
            while (n < list.Count && sw.Elapsed.TotalMilliseconds < Budget);
            return n;
        }

        void Update()
        {
            if (hero == null) return;
            float t = FrameCapturer.T;
            camFull.enabled = camL.enabled = camR.enabled = camBack.enabled = false;
            int p = t < 8f ? 0 : t < 15f ? 1 : t < 22f ? 2 : 3;
            string bj = $"{ms["bj"]:F1}";
            if (p == 0 || p == 3)
            {
                SetJobs(true, workersMax);
                if (p == 0) Pose(hero, t, 1, Ramp(t, 1.6f), Ramp(t, 3.4f), Ramp(t, 5.2f)); else Pose(hero, t);
                hero.d.ForceImmediateUpdate();
                float k = p == 0 ? t / 8f : (t - 22f) / 3f;
                Look(camFull, new Vector3(0.2f, 1.45f, 0), p == 0 ? -24f + 30f * k : 8f - 10f * k, 9f, p == 0 ? Mathf.Lerp(8.4f, 7.4f, k) : 7.8f);
                if (p == 0)
                {
                    string chain = t < 1.6f ? "Twist" : t < 3.4f ? "Twist + Bend" : t < 5.2f ? "Twist + Bend + Ripple" : "Twist + Bend + Ripple + Melt";
                    Labels("Mesh deformers as a chain of Burst jobs", $"Chain: {chain}",
                        $"{verts:N0} vertices, normals and bounds recomputed every frame", "", "");
                }
                else Labels("Deform (MIT): every deformer is a Burst-compiled parallel job",
                    $"One update of {verts:N0} vertices (4 deformers + normals), median of 15, Apple M4 Mac mini, Unity 6.3 Editor",
                    $"C# on 1 thread {ms["c1"]:N0} ms   |   Burst on 1 thread {ms["b1"]:F1} ms   |   Burst + Job System ({workersMax} workers) {bj} ms",
                    "", "");
            }
            else if (p == 1)
            {
                SetJobs(true, workersMax);
                Pose(normOff, t); Pose(normOn, t); normOff.d.ForceImmediateUpdate(); normOn.d.ForceImmediateUpdate();
                camBack.enabled = true;
                float k = (t - 8f) / 7f;
                Look(camL, new Vector3(100.1f, 1.45f, 0), -14f + 10f * k, 7f, 7.2f); Look(camR, new Vector3(200.1f, 1.45f, 0), -14f + 10f * k, 7f, 7.2f);
                Labels("Same chain, same frame: what the normals do",
                    "Normals not recalculated", "lighting still follows the undeformed column",
                    "Normals recalculated (two more jobs)", "ripples and twist catch the light");
            }
            else
            {
                camBack.enabled = true;
                SetJobs(false, 0); int nL = Budgeted(gridL, ref rrL, t);
                SetJobs(true, workersMax); int nR = Budgeted(gridR, ref rrR, t);
                sumL += nL; sumR += nR; frames++;
                Look(camL, new Vector3(300f, 0.3f, 1.9f), 0f, 32f, 10.2f); Look(camR, new Vector3(400f, 0.3f, 1.9f), 0f, 32f, 10.2f);
                Labels($"12 sculptures, {gridVerts:N0} vertices each, one 60 fps frame budget (16.7 ms)",
                    "C# only: Burst off, 1 thread", $"{ms["gc1"]:F0} ms per sculpture: {sumL / (float)frames:F0} of 12 updated per frame",
                    $"Burst + Job System ({workersMax} workers)", $"{ms["gbj"]:F1} ms per sculpture: {sumR / (float)frames:F0} of 12 updated per frame");
                if (frames % 30 == 1) Debug.Log($"[gt] t {t:F2} budget: C# {nL}, Burst {nR}");
            }
            if (p != phaseShown) { phaseShown = p; Debug.Log($"[gt] t {t:F2} phase {p} '{title.text}' | {l1.text} | {l2.text} | {m1.text} | {m2.text}"); }
        }
    }
}
