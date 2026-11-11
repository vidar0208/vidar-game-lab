// Timeline for the footprint post: cameras, live read-outs from the force model, and the debug overlay
// (contact cells, ring cells, force arrows in the colours of the source's figure: weight blue, momentum red, total black).
using System.Collections.Generic;
using UnityEngine;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(200)]
    public class FootprintDirector : MonoBehaviour
    {
        public Camera camL, camR, camFull;
        public FootWalker off, on;
        public LabelSpec live;
        public MeshFilter contactCells, ringCells;
        public Transform[] shafts = new Transform[6], heads = new Transform[6];   // per foot: weight, momentum, total
        public float forceScale = 0.0008f;   // metres of arrow per newton
        Mesh mc, mr;
        readonly List<Vector3> v = new List<Vector3>(); readonly List<int> tr = new List<int>();

        static float S(float a, float b, float t) { t = Mathf.Clamp01((t - a) / (b - a)); return t * t * (3 - 2 * t); }
        static void Aim(Camera c, FootWalker w, Vector3 offset, Vector3 look)
        {
            Vector3 b = new Vector3(w.transform.position.x, 0, w.rootZ);
            c.transform.position = b + offset; c.transform.LookAt(b + look);
        }

        void Cells(Mesh m, bool ring)
        {
            v.Clear(); tr.Clear();
            foreach (var f in on.feet)
            {
                if (f == null || f.g == null) continue;
                float r = f.g.cell * 0.32f;
                foreach (int i in ring ? f.ring : f.contact)
                {
                    Vector3 p = f.g.World(i) + Vector3.up * 0.004f; int n = v.Count;
                    v.Add(p + new Vector3(-r, 0, -r)); v.Add(p + new Vector3(-r, 0, r)); v.Add(p + new Vector3(r, 0, r)); v.Add(p + new Vector3(r, 0, -r));
                    tr.Add(n); tr.Add(n + 1); tr.Add(n + 2); tr.Add(n); tr.Add(n + 2); tr.Add(n + 3);
                }
            }
            m.Clear(); m.SetVertices(v); m.SetTriangles(tr, 0); m.RecalculateBounds();
        }

        void LateUpdate()
        {
            float t = FrameCapturer.T;
            bool A = t < 9f, B = t >= 9f && t < 17f, C = t >= 17f;
            camL.enabled = camR.enabled = A; camFull.enabled = !A;
            if (A)
            {
                Aim(camL, off, new Vector3(1.0f, 0.95f, -2.0f), new Vector3(0f, 0f, -0.35f));
                Aim(camR, on, new Vector3(1.0f, 0.95f, -2.0f), new Vector3(0f, 0f, -0.35f));
            }
            else if (B) Aim(camFull, on, new Vector3(1.3f, 0.6f, Mathf.Lerp(0.75f, -0.35f, S(9f, 17f, t))), new Vector3(0f, 0.05f, -0.12f));
            else Aim(camFull, on, new Vector3(0.95f, 0.8f, Mathf.Lerp(-0.65f, -0.45f, S(17f, 24f, t))), new Vector3(-0.02f, 0.02f, 0.12f));

            if (on.feet[0] == null) return;
            // most recent footfall
            FootWalker.Foot f = null;
            foreach (var k in on.feet) if (k.planted && (f == null || k.tContact < f.tContact)) f = k;
            if (f != null && f.g != null && live != null)
            {
                if (B) live.text = $"{f.g.presetName}: force {f.totalF:F0} N on {f.area * 1e4f:F0} cm2, footprint depth {f.depth * 100f:F1} cm";
                else if (C) live.text = $"force {f.totalF:F0} N = weight {f.weightF:F0} + momentum {f.momentumF:F0}, {f.pressure / 1000f:F0} kPa, depth {f.depth * 100f:F1} cm";
            }

            contactCells.gameObject.SetActive(C); ringCells.gameObject.SetActive(C);
            if (C)
            {
                if (mc == null) { mc = new Mesh(); mr = new Mesh(); contactCells.sharedMesh = mc; ringCells.sharedMesh = mr; }
                Cells(mc, false); Cells(mr, true);
            }
            for (int s = 0; s < 2; s++)
            {
                var k = on.feet[s];
                for (int j = 0; j < 3; j++)
                {
                    int a = s * 3 + j;
                    float force = j == 0 ? k.weightF : j == 1 ? k.momentumF : k.totalF;
                    bool show = C && k.planted && k.g != null && force > 2f;
                    float len = force * forceScale;
                    heads[a].gameObject.SetActive(show); shafts[a].gameObject.SetActive(show && len > 0.07f);
                    if (!show) continue;
                    // all arrows stand on the camera side of the track, level with the foot they belong to
                    Vector3 p = new Vector3(on.transform.position.x + 0.22f, 0.012f, k.plantZ + 0.06f + (j - 1) * 0.07f);
                    heads[a].position = p;
                    float sl = Mathf.Max(0.001f, len - 0.05f);
                    shafts[a].position = p + Vector3.up * (0.05f + sl * 0.5f); shafts[a].localScale = new Vector3(0.012f, sl * 0.5f, 0.012f);
                }
            }
        }
    }
}
