// Circle crossing, as in the library's Circle example: agents start evenly on a ring and walk to the opposite point.
// The RVO2 library (RVO folder, Apache-2.0) is used as a library. It is a singleton, so each scenario is simulated
// in turn at start (Clear() between runs) and recorded; the video plays the recordings back.
// "Seek + push apart" is my own baseline: walk straight at the goal, then separate overlapping pairs.
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;
using V2 = RVO.Vector2;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class CrowdDirector : MonoBehaviour
    {
        public int count = 250, maxNeighbors = 10, maxSteps = 2400;
        public float ring = 60f, radius = 0.4f, maxSpeed = 1.4f, dt = 0.1f, neighborDist = 10f, thSmall = 0.5f, thLarge = 5f, speedup = 10f;
        public Material bodyMat, headMat;
        public Camera camFull, camL, camR, camBack;
        public LabelSpec title, l1, l2, m1, m2;
        public Transform groundA, groundB;

        class Run
        {
            public string name; public readonly List<Vector2[]> pos = new List<Vector2[]>(); public readonly List<int> contacts = new List<int>(), arrived = new List<int>();
            public int doneStep = -1; public double ms;
            public int Last => pos.Count - 1;
        }

        Run seek, small, large;
        Transform[][] walkers = new Transform[2][];
        Vector2[] start, goal;
        int phase = -1;

        int Contacts(Vector2[] p)
        {
            int c = 0; float lim = 2 * radius - 0.05f, lim2 = lim * lim;
            for (int i = 0; i < p.Length; i++) for (int j = i + 1; j < p.Length; j++) if ((p[i] - p[j]).sqrMagnitude < lim2) c++;
            return c;
        }
        int Arrived(Vector2[] p) { int a = 0; for (int i = 0; i < p.Length; i++) if ((p[i] - goal[i]).sqrMagnitude < radius * radius) a++; return a; }

        void Record(Run r, Vector2[] p)
        {
            r.pos.Add((Vector2[])p.Clone()); r.contacts.Add(Contacts(p)); int a = Arrived(p); r.arrived.Add(a);
            if (a == count && r.doneStep < 0) r.doneStep = r.pos.Count - 1;
        }

        Vector2 Pref(Vector2 p, int i)
        {
            var g = goal[i] - p;
            return g.magnitude > maxSpeed * dt ? g.normalized * maxSpeed : g / dt;
        }

        Run RunRVO(float th)
        {
            var r = new Run { name = "RVO2 " + th };
            var sim = RVO.Simulator.Instance;
            sim.Clear();
            sim.TimeStep = dt;
            sim.SetAgentDefaults(neighborDist, maxNeighbors, th, th, radius, maxSpeed, new V2(0, 0));
            for (int i = 0; i < count; i++) sim.AddAgent(new V2(start[i].x, start[i].y));
            var p = new Vector2[count]; var sw = new Stopwatch();
            for (int s = 0; s <= maxSteps; s++)
            {
                for (int i = 0; i < count; i++) { var q = sim.GetAgentPosition(i); p[i] = new Vector2(q.X, q.Y); }
                Record(r, p);
                if (r.doneStep >= 0 && s > r.doneStep + 30) break;
                for (int i = 0; i < count; i++) { var v = Pref(p[i], i); sim.SetAgentPrefVelocity(i, new V2(v.x, v.y)); }
                sw.Start(); sim.DoStep(); sw.Stop();
            }
            r.ms = sw.Elapsed.TotalMilliseconds / Mathf.Max(1, r.Last);
            return r;
        }

        Run RunSeek()
        {
            var r = new Run { name = "seek" };
            var p = (Vector2[])start.Clone();
            float d = 2 * radius;
            for (int s = 0; s <= maxSteps; s++)
            {
                Record(r, p);
                if (r.doneStep >= 0 && s > r.doneStep + 30) break;
                for (int i = 0; i < count; i++) p[i] += Pref(p[i], i) * dt;
                for (int it = 0; it < 2; it++)
                    for (int i = 0; i < count; i++) for (int j = i + 1; j < count; j++)
                    {
                        var e = p[j] - p[i]; float m = e.magnitude;
                        if (m < d && m > 1e-5f) { var push = e / m * ((d - m) * 0.5f); p[i] -= push; p[j] += push; }
                    }
            }
            return r;
        }

        Transform[] Crowd(Transform ground)
        {
            var t = new Transform[count]; var mpb = new MaterialPropertyBlock();
            for (int i = 0; i < count; i++)
            {
                var root = new GameObject("Walker " + i).transform; root.SetParent(ground, false);
                var body = GameObject.CreatePrimitive(PrimitiveType.Capsule); Destroy(body.GetComponent<Collider>());
                body.transform.SetParent(root, false); body.transform.localPosition = new Vector3(0, 0.75f, 0); body.transform.localScale = new Vector3(0.8f, 0.75f, 0.6f);
                var head = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(head.GetComponent<Collider>());
                head.transform.SetParent(root, false); head.transform.localPosition = new Vector3(0, 1.62f, 0.06f); head.transform.localScale = Vector3.one * 0.36f;
                var c = Color.HSVToRGB(i / (float)count, 0.85f, 0.95f);
                var br = body.GetComponent<Renderer>(); br.sharedMaterial = bodyMat; mpb.SetColor("_BaseColor", c); br.SetPropertyBlock(mpb);
                head.GetComponent<Renderer>().sharedMaterial = headMat;
                t[i] = root;
            }
            return t;
        }

        void Start()
        {
            start = new Vector2[count]; goal = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                float a = i * 2f * Mathf.PI / count;
                start[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * ring; goal[i] = -start[i];
            }
            var sw = Stopwatch.StartNew();
            seek = RunSeek(); small = RunRVO(thSmall); large = RunRVO(thLarge);
            Debug.Log($"[gt] runs in {sw.ElapsedMilliseconds} ms. RVO step {large.ms:F3} ms (th {thLarge}), {small.ms:F3} ms (th {thSmall})");
            foreach (var r in new[] { seek, small, large })
            {
                int maxC = 0, sumC = 0; foreach (int c in r.contacts) { maxC = Mathf.Max(maxC, c); sumC += c; }
                Debug.Log($"[gt] {r.name}: steps {r.Last}, all arrived at {(r.doneStep < 0 ? "never" : (r.doneStep * dt).ToString("F1") + " s")}, arrived at end {r.arrived[r.Last]}, max contacts {maxC}, mean {sumC / (float)r.contacts.Count:F1}");
            }
            walkers[0] = Crowd(groundA); walkers[1] = Crowd(groundB);
            Update();
        }

        // place crowd k at simulation time ts of run r
        int Pose(int k, Run r, float ts)
        {
            float f = Mathf.Clamp(ts / dt, 0, r.Last); int i0 = Mathf.Min((int)f, r.Last), i1 = Mathf.Min(i0 + 1, r.Last); float u = f - i0;
            var a = r.pos[i0]; var b = r.pos[i1]; var g = k == 0 ? groundA : groundB;
            for (int i = 0; i < count; i++)
            {
                var p = Vector2.Lerp(a[i], b[i], u);
                var w = walkers[k][i]; w.localPosition = new Vector3(p.x, 0, p.y);
                int j0 = Mathf.Max(0, i0 - 2), j1 = Mathf.Min(r.Last, i0 + 2);
                var v = r.pos[j1][i] - r.pos[j0][i];
                if (v.sqrMagnitude < 1e-4f) v = goal[i] - p;
                if (v.sqrMagnitude > 1e-6f) w.localRotation = Quaternion.LookRotation(new Vector3(v.x, 0, v.y));
            }
            return i0;
        }

        void Look(Camera c, Transform g, float yaw, float pitch, float dist, float fov)
        {
            c.enabled = true; c.fieldOfView = fov;
            var rot = Quaternion.Euler(pitch, yaw, 0);
            c.transform.SetPositionAndRotation(g.position - rot * Vector3.forward * dist, rot);
        }

        string Arr(Run r, int i) => r.doneStep >= 0 && i >= r.doneStep ? $"all {count} arrived at {r.doneStep * dt:F0} s" : $"{r.arrived[i]} of {count} arrived";
        int Peak(Run r, int i) { int m = 0; for (int k = 0; k <= i; k++) m = Mathf.Max(m, r.contacts[k]); return m; }
        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }

        void Update()
        {
            if (large == null) return;
            float t = FrameCapturer.T;
            camFull.enabled = camL.enabled = camR.enabled = camBack.enabled = false;
            int p = t < 3f ? 0 : t < 12f ? 1 : t < 21f ? 2 : 3;
            float yaw = 20f + t * 1.2f;
            if (p == 0 || p == 3)
            {
                float ts = p == 0 ? 25f + t * 2f : 30f + (t - 21f) * 2f;
                int i = Pose(0, large, ts);
                float zoom = p == 0 ? 80f : Mathf.Lerp(80f, 42f, Mathf.SmoothStep(0, 1, (t - 21f) / 3.5f));
                Look(camFull, groundA, yaw, 48f, zoom, 35f);
                if (p == 0) Labels($"RVO2: {count} walkers crossing a circle", "each one picks a velocity that won't hit anyone", $"in the next {thLarge:0} s (time horizon)", "", "");
                else Labels("Reciprocal velocity obstacles (ORCA)", "Unity 6.3 URP, RVO2-CS library (Apache-2.0), my own scene",
                    $"{count} agents: {large.ms:F2} ms per step on an Apple M4 Mac mini (CPU)", "", "");
            }
            else
            {
                float ts = 20f + Mathf.Max(0, (t - (p == 1 ? 3.3f : 12.3f)) * speedup);
                Run a = p == 1 ? seek : small, b = p == 1 ? large : large;
                int ia = Pose(0, a, ts), ib = Pose(1, b, ts);
                camBack.enabled = true;
                Look(camL, groundA, yaw, 52f, 105f, 35f); Look(camR, groundB, yaw, 52f, 105f, 35f);
                string sp = $"{speedup:0}x speed, t = {ts:F0} s";
                if (p == 1) Labels($"Same {count} walkers, same start and goals ({sp})",
                    $"Seek + push apart: overlapping pairs {a.contacts[ia]} (peak {Peak(a, ia)})", Arr(a, ia),
                    $"RVO2 (time horizon {thLarge:0} s): overlapping pairs {b.contacts[ib]} (peak {Peak(b, ib)})", Arr(b, ib));
                else Labels($"Time horizon: how far ahead each walker looks ({sp})",
                    $"{thSmall} s: overlapping pairs {a.contacts[ia]} (peak {Peak(a, ia)})", Arr(a, ia),
                    $"{thLarge:0} s: overlapping pairs {b.contacts[ib]} (peak {Peak(b, ib)})", Arr(b, ib));
            }
            if (p != phase) { phase = p; Debug.Log($"[gt] t {t:F2} phase {p} '{title.text}' | {l1.text} | {m1.text}"); }
        }
    }
}
