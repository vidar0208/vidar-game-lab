// Drives the GPU trails post: walks the mannequin, moves the "main" camera that the trail systems cull, LOD and
// face against, and sets which trail system each output camera sees (by layer) for the comparisons.
// Three trail systems get the same input: main (culling + 3 LODs), noCull (culling off, 1 LOD, only for the
// culling shot) and lodDebug (same as main, one flat colour per LOD).
using System.Collections.Generic;
using GpuTrailSystem;
using UnityEngine;

namespace GameTech.GpuTrails
{
    [DefaultExecutionOrder(-100)]
    public class TrailDirector : MonoBehaviour
    {
        public const int LayerMain = 20, LayerNoCull = 21, LayerLod = 22, LayerLines = 23;
        public Camera camA, camB, camMain;
        public Transform character;
        public TrailSwarm swarmMain;
        public GpuTrailRenderer main, noCull, lodDebug;
        public LineRenderer edgeL, edgeR;
        public Transform camMarker;
        public Mesh dotMesh;
        public Material dotMat;
        public float dotSize = 0.02f;
        public float width = 0.022f, overheadWidth = 0.06f;
        public float[] lodStart = { 0f, 7.5f, 10.5f };
        public int[] lodStep = { 1, 2, 4 };
        public LabelSpec title, l1, l2, m1, m2;

        readonly Dictionary<Transform, Quaternion> bind = new Dictionary<Transform, Quaternion>();
        readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        Matrix4x4[] dots;
        int logged = -1; float nextLog = 0f;
        static readonly Vector3 C = new Vector3(0f, 1.3f, 3f);

        void Awake()
        {
            foreach (var t in character.GetComponentsInChildren<Transform>(true)) { bones[t.name] = t; if (t != character) bind[t] = t.localRotation; }
            dots = new Matrix4x4[swarmMain.swarmCount];
            gameObject.AddComponent<TrailCountLabels>().director = this;
        }

        void Turn(string bone, float deg, Vector3 axis)
        {
            if (!bones.TryGetValue(bone, out var b)) return;
            b.rotation = Quaternion.AngleAxis(deg, character.TransformDirection(axis)) * b.rotation;
        }

        // walk along x through the middle of the swarm (x -2.2 .. 2.2 at z 3), turning at the ends; simple gait
        void Walk(float t)
        {
            const float speed = 0.9f, x0 = -2.2f, x1 = 2.2f, len = x1 - x0;
            float s = Mathf.Repeat(t * speed + 1.4f, 2 * len);
            bool back = s > len;
            float x = back ? x1 - (s - len) : x0 + s;
            character.position = new Vector3(x, 0f, 3f);
            character.rotation = Quaternion.Euler(0, back ? -90f : 90f, 0);
            foreach (var kv in bind) kv.Key.localRotation = kv.Value;
            float ph = t * speed / 0.62f * Mathf.PI;   // one stride = 1.24 m
            float sw = Mathf.Sin(ph);
            Turn("Upperarm_L", 72f, Vector3.forward); Turn("Upperarm_R", -72f, Vector3.forward);
            Turn("Upperarm_L", 30f * sw, Vector3.right); Turn("Upperarm_R", -30f * sw, Vector3.right);
            Turn("Lowerarm_L", -20f, Vector3.right); Turn("Lowerarm_R", -20f, Vector3.right);
            Turn("Upperleg_L", -26f * sw, Vector3.right); Turn("Upperleg_R", 26f * sw, Vector3.right);
            Turn("Lowerleg_L", 30f * Mathf.Max(0f, -Mathf.Cos(ph)), Vector3.right); Turn("Lowerleg_R", 30f * Mathf.Max(0f, Mathf.Cos(ph)), Vector3.right);
            Turn("Head", 4f * Mathf.Sin(t * 0.7f), Vector3.up);
        }

        static void Look(Camera c, Vector3 pos, Vector3 target) { c.transform.position = pos; c.transform.LookAt(target); }
        static void Copy(Camera dst, Camera src) { dst.transform.SetPositionAndRotation(src.transform.position, src.transform.rotation); dst.fieldOfView = src.fieldOfView; }

        void Show(Camera c, bool on, Rect r, int mask)
        {
            c.enabled = on;
            if (!on) return;
            c.rect = r; c.cullingMask = mask; c.aspect = r.width * 1920f / (r.height * 1080f);
        }

        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }

        static int Drawn(GpuTrailRenderer r, int lod) => r.Lods.Count > lod ? r.Lods[lod].ReadDrawnTrailCount() : 0;

        void FrustumLines(bool on)
        {
            edgeL.enabled = edgeR.enabled = on; camMarker.gameObject.SetActive(on);
            if (!on) return;
            var p = camMain.transform.position; p.y = 0.04f;
            var f = camMain.transform.forward; f.y = 0; f.Normalize();
            float half = Mathf.Atan(Mathf.Tan(camMain.fieldOfView * 0.5f * Mathf.Deg2Rad) * camMain.aspect) * Mathf.Rad2Deg;
            edgeL.SetPosition(0, p); edgeL.SetPosition(1, p + Quaternion.Euler(0, -half, 0) * f * 11f);
            edgeR.SetPosition(0, p); edgeR.SetPosition(1, p + Quaternion.Euler(0, half, 0) * f * 11f);
            camMarker.position = p + Vector3.up * 0.1f; camMarker.rotation = Quaternion.LookRotation(f);
        }

        void LateUpdate()
        {
            float t = FrameCapturer.T;
            int total = swarmMain.TrailCount, nodes = swarmMain.NodesPerTrail;
            Walk(t);

            var full = new Rect(0, 0, 1, 1);
            var h0 = new Rect(0, 0, 0.4985f, 1); var h1 = new Rect(0.5015f, 0, 0.4985f, 1);
            int baseMask = ~((1 << LayerMain) | (1 << LayerNoCull) | (1 << LayerLod) | (1 << LayerLines));
            int phase;
            float theta = -1.95f + 0.07f * t;
            camMain.fieldOfView = 42f; camMain.aspect = 16f / 9f;
            Look(camMain, C + new Vector3(8.5f * Mathf.Cos(theta), 0.5f, 8.5f * Mathf.Sin(theta)), C + new Vector3(0, 0.1f, 0));
            main.startWidth = lodDebug.startWidth = noCull.startWidth = width;

            if (t < 4f)
            {
                phase = 0;
                float u0 = Mathf.SmoothStep(0f, 1f, t / 4f);
                Look(camMain, C + new Vector3(Mathf.Lerp(2.2f, 1.0f, u0), Mathf.Lerp(0.2f, 0.5f, u0), -5.4f), C + new Vector3(0f, 0.3f, 0f));
                Show(camA, true, full, baseMask | 1 << LayerMain); Show(camB, false, full, 0);
                Copy(camA, camMain);
                Labels("GPU trails", $"{total:N0} ribbons. Nodes and vertices written by compute shaders, nothing rebuilt on the CPU", "", "", "");
            }
            else if (t < 10f)
            {
                phase = 1;
                Show(camA, true, h0, baseMask); Show(camB, true, h1, baseMask | 1 << LayerMain);
                Copy(camA, camMain); Copy(camB, camMain);
                Labels("GPU trails off vs on",
                    "Off", "only the moving points",
                    "On", $"{total:N0} trails, up to {nodes} nodes each, {swarmMain.life:0.0} s life");
            }
            else if (t < 17f)
            {
                phase = 2;
                float u = (t - 10f) / 7f;
                float th2 = 0.9f + 0.25f * t;   // stands near the middle and looks outward, turning
                Look(camMain, C + new Vector3(0.8f * Mathf.Cos(th2), 0.35f, 0.8f * Mathf.Sin(th2)), C + new Vector3(6f * Mathf.Cos(th2), 0.6f, 6f * Mathf.Sin(th2)));
                main.startWidth = noCull.startWidth = overheadWidth;
                Show(camA, true, h0, baseMask | 1 << LayerNoCull | 1 << LayerLines); Show(camB, true, h1, baseMask | 1 << LayerMain | 1 << LayerLines);
                foreach (var c in new[] { camA, camB }) { Look(c, new Vector3(Mathf.Lerp(-0.6f, 0.6f, u), 14f, -2.6f), new Vector3(0f, 0f, 3.3f)); c.fieldOfView = 46f; }
                Labels("Frustum culling, seen from above (white lines: the main camera's view)",
                    "Culling off", "", "Culling on", "");   // counts filled in by AfterTrails
            }
            else if (t < 22f)
            {
                phase = 3;
                Show(camA, true, h0, baseMask | 1 << LayerLod); Show(camB, true, h1, baseMask | 1 << LayerMain);
                Copy(camA, camMain); Copy(camB, camMain);
                Labels($"LOD by distance: green every node, yellow every {lodStep[1]}nd from {lodStart[1]:0.#} m, red every {lodStep[2]}th from {lodStart[2]:0.#} m",
                    "LOD colours", "",
                    "Final look", $"same LODs: far trails use 1/{lodStep[2]} of the vertices");
            }
            else
            {
                phase = 4;
                Show(camA, true, full, baseMask | 1 << LayerMain); Show(camB, false, full, 0);
                float u = Mathf.SmoothStep(0f, 1f, (t - 22f) / 4f);
                Look(camA, C + new Vector3(Mathf.Lerp(-1.2f, -0.4f, u), Mathf.Lerp(0.9f, 0.5f, u), -4.6f), C + new Vector3(0f, 0.2f, 0f));
                camA.fieldOfView = 40f;
                Labels("GPU trails", "Unity 6.3 URP, ported from fuqunaga's GpuTrail: node append, vertex update, culling and LOD in compute shaders", "", "", "");
            }
            if (phase == 4) Look(camMain, camA.transform.position, C + new Vector3(0f, 0.2f, 0f));
            FrustumLines(phase == 2);

            // the points themselves (both halves show them)
            for (int s = 0; s < dots.Length; s++) dots[s] = Matrix4x4.TRS(TrailSwarm.SwarmPos(s, Time.time, swarmMain.centre), Quaternion.identity, Vector3.one * dotSize);
            var rp = new RenderParams(dotMat) { layer = 0 };
            for (int start = 0; start < dots.Length; start += 768)
                Graphics.RenderMeshInstanced(rp, dotMesh, 0, dots, Mathf.Min(768, dots.Length - start), start);

            if (phase == 0 || phase == 4) l1.anchor = new Vector2(0.012f, 0.03f);
            else { l1.anchor = new Vector2(0.012f, 0.058f); l2.anchor = new Vector2(0.012f, 0.018f); }
            m1.anchor = new Vector2(0.512f, 0.058f); m2.anchor = new Vector2(0.512f, 0.018f);
            currentPhase = phase;
        }

        int currentPhase = -1;

        // runs after the trail renderers (TrailCountLabels, execution order 100): reads this frame's drawn counts
        public void AfterTrails()
        {
            float t = FrameCapturer.T;
            int m0 = Drawn(main, 0), m1c = Drawn(main, 1), m2c = Drawn(main, 2), nc = Drawn(noCull, 0);
            int total = swarmMain.TrailCount, nodes = swarmMain.NodesPerTrail;
            if (currentPhase == 2)
            {
                l2.text = $"{nc:N0} of {total:N0} trails drawn";
                m2.text = $"{m0 + m1c + m2c:N0} of {total:N0} drawn: a trail stays if any node is in view";
            }
            if (currentPhase == 3) l2.text = $"drawn now: {m0} / {m1c} / {m2c} trails";
            if (currentPhase != logged) { logged = currentPhase; Debug.Log($"[gt] t {t:F2} phase {currentPhase} '{title.text}' | '{l1.text}' '{l2.text}' | '{m1.text}' '{m2.text}'"); }
            if (t >= nextLog) { nextLog = t + 1f; Debug.Log($"[gt] t {t:F2} trails {total}, nodes/trail {nodes}, main drawn lod0/1/2 {m0}/{m1c}/{m2c} (sum {m0 + m1c + m2c}), noCull drawn {nc}"); }
        }
    }
}
