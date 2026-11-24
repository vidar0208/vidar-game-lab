// Drives the slime mold post. Two simulations run side by side (A left of the split, B right of it), each with
// 8,388,608 agents on a 1920 x 1080 trail grid, two steps per video frame. Each phase restarts them with the settings
// being compared. At Start it times one step of one simulation (GPU, waited). Runs before FrameCapturer (-100).
using UnityEngine;
using GameTech;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class PhysarumDirector : MonoBehaviour
    {
        public Material display; public ComputeShader sim;
        public int agents = 1 << 23, stepsPerFrame = 2, blurRadius = 1, blurPasses = 2;
        [Header("Source values (min = max in the source config)")]
        public float deposition = 5f, decay = 0.1f;
        [Header("My picks inside the source's random ranges")]
        public float sensorAngle = 45f, sensorDistance = 18f, rotationAngle = 45f, stepDistance = 1.3f;
        public LabelSpec title, l1, l2, m1, m2;

        class Sim
        {
            public ComputeBuffer agents, data, tmp, temp, count, cfg, tbl; public int S, N, per, sense; public uint seed, step;
        }

        RenderTexture rtA, rtB; Sim A, B; int W = 1920, H = 1080, phase = -1; float msStep;
        int kIA, kID, kC, kM, kD, kBH, kBV, kDr;
        static readonly Color[] pal = { new Color(1.00f, 0.70f, 0.28f), new Color(0.25f, 0.80f, 1.00f), new Color(1.00f, 0.30f, 0.58f) };

        static float S(float x) { x = Mathf.Clamp01(x); return x * x * x * (x * (x * 6f - 15f) + 10f); }
        static RenderTexture RT(int w, int h) { var r = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { enableRandomWrite = true }; r.Create(); return r; }

        Sim Make()
        {
            int wh = W * H; var s = new Sim();
            s.agents = new ComputeBuffer(agents + 4, 16); s.data = new ComputeBuffer(3 * wh, 4); s.tmp = new ComputeBuffer(3 * wh, 4);
            s.temp = new ComputeBuffer(3 * wh, 4); s.count = new ComputeBuffer(3 * wh, 4); s.cfg = new ComputeBuffer(3, 16); s.tbl = new ComputeBuffer(9, 4);
            return s;
        }

        void Bind(Sim s, int k)
        {
            sim.SetBuffer(k, "agents", s.agents); sim.SetBuffer(k, "data", s.data); sim.SetBuffer(k, "tmp", s.tmp); sim.SetBuffer(k, "temp", s.temp);
            sim.SetBuffer(k, "count", s.count); sim.SetBuffer(k, "cfg", s.cfg); sim.SetBuffer(k, "tbl", s.tbl);
        }

        void Uniforms(Sim s)
        {
            sim.SetInt("_W", W); sim.SetInt("_H", H); sim.SetInt("_S", s.S); sim.SetInt("_N", s.N); sim.SetInt("_Per", s.per);
            sim.SetInt("_Sense", s.sense); sim.SetInt("_R", blurRadius); sim.SetInt("_Seed", (int)s.seed); sim.SetInt("_Step", (int)s.step);
            sim.SetFloat("_Deposit", deposition); sim.SetFloat("_Max", agents / (float)(W * H) * 10f); // source viewer: particles / (w*h) * 10
        }

        // species: 1 or 3; angles in degrees per species; table self +1, others -1 (the means of the source's random table)
        void Reset(Sim s, int species, float[] sensorDeg, int sense, uint seed)
        {
            s.S = species; s.per = Mathf.CeilToInt(agents / (float)species); s.N = s.per * species; s.sense = sense; s.seed = seed; s.step = 0;
            var cf = new Vector4[3]; for (int i = 0; i < 3; i++) cf[i] = new Vector4(sensorDeg[Mathf.Min(i, sensorDeg.Length - 1)] * Mathf.Deg2Rad, sensorDistance, rotationAngle * Mathf.Deg2Rad, stepDistance);
            s.cfg.SetData(cf);
            var t = new float[9]; for (int c = 0; c < species; c++) for (int i = 0; i < species; i++) t[c * species + i] = c == i ? 1f : -1f;
            s.tbl.SetData(t);
            Uniforms(s);
            Bind(s, kIA); sim.Dispatch(kIA, (s.N + 255) / 256, 1, 1);
            Bind(s, kID); sim.Dispatch(kID, (s.S * W * H + 255) / 256, 1, 1);
        }

        void Step(Sim s)
        {
            int wh = W * H, g1 = (wh + 255) / 256, gs = (s.S * wh + 255) / 256;
            Uniforms(s);
            Bind(s, kC); sim.Dispatch(kC, g1, 1, 1);
            Bind(s, kM); sim.Dispatch(kM, (s.N + 255) / 256, 1, 1);
            Bind(s, kD); sim.Dispatch(kD, gs, 1, 1);
            for (int i = 0; i < blurPasses; i++)
            {
                sim.SetFloat("_Scale", i == blurPasses - 1 ? decay : 1f);
                Bind(s, kBH); sim.Dispatch(kBH, gs, 1, 1);
                Bind(s, kBV); sim.Dispatch(kBV, gs, 1, 1);
            }
            s.step++;
        }

        void Draw(Sim s, RenderTexture rt)
        {
            Uniforms(s);
            sim.SetVectorArray("_Pal", new Vector4[] { pal[0], pal[1], pal[2] });
            Bind(s, kDr); sim.SetTexture(kDr, "Dest", rt); sim.Dispatch(kDr, (W + 7) / 8, (H + 7) / 8, 1);
        }

        void Start()
        {
            kIA = sim.FindKernel("InitAgents"); kID = sim.FindKernel("InitData"); kC = sim.FindKernel("Combine"); kM = sim.FindKernel("Move");
            kD = sim.FindKernel("Deposit"); kBH = sim.FindKernel("BlurH"); kBV = sim.FindKernel("BlurV"); kDr = sim.FindKernel("Draw");
            rtA = RT(W, H); rtB = RT(W, H); display.SetTexture("_TexA", rtA); display.SetTexture("_TexB", rtB);
            A = Make(); B = Make();

            // time one step of one simulation (1 species, sensing on)
            Reset(B, 1, new[] { sensorAngle }, 1, 1);
            var probe = new float[1];
            for (int i = 0; i < 5; i++) Step(B);
            B.data.GetData(probe, 0, 0, 1);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 20; i++) Step(B);
            B.data.GetData(probe, 0, 0, 1);
            msStep = (float)sw.Elapsed.TotalMilliseconds / 20f;
            Debug.Log($"[gt] physarum: {agents} agents, {W}x{H} grid: {msStep:F2} ms per step (GPU, waited, avg of 20)");

            // phase 0-1: same agents and grid, sensing off vs on; 150 steps before the first frame
            Reset(A, 1, new[] { sensorAngle }, 0, 7); Reset(B, 1, new[] { sensorAngle }, 1, 7);
            for (int i = 0; i < 150; i++) { Step(A); Step(B); }
        }

        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }

        void LateUpdate()
        {
            if (rtA == null) return;
            float t = FrameCapturer.T;
            int p = t < 3f ? 0 : t < 10f ? 1 : t < 17.5f ? 2 : t < 23f ? 3 : 4;
            if (p != phase)
            {
                if (p == 2) { Reset(A, 1, new[] { 22.5f }, 1, 11); Reset(B, 1, new[] { 60f }, 1, 11); }
                if (p == 3) { Reset(A, 1, new[] { sensorAngle }, 1, 13); Reset(B, 3, new[] { sensorAngle }, 1, 13); }
            }
            float split = 0.5f, line = 1;
            string set = $"sensor {sensorDistance:0} px, rotate {rotationAngle:0}°, step {stepDistance} px, deposit {deposition:0}, decay x{decay}";
            switch (p)
            {
                case 0: Labels($"Slime mold model: {A.N:N0} agents on the GPU", "Sensing off", "agents turn at random", "Sensing on", "agents turn toward the strongest trail"); break;
                case 1: Labels("Same agents, same trail map, one rule changed", "Sensing off", "agents turn at random", "Sensing on", set); break;
                case 2: Labels("Same rule, one setting changed", "Sensor angle 22.5°", $"distance {sensorDistance:0} px, rotate {rotationAngle:0}°, step {stepDistance} px", "Sensor angle 60°", "same start, same settings otherwise"); break;
                case 3: Labels($"Same {B.N:N0} agents", "1 species", "every agent follows one trail map", "3 species", "follow their own trail, avoid the others"); break;
                default:
                    split = 0; line = 0;
                    Labels("Physarum transport network", "Unity 6.3 URP, after fogleman/physarum (MIT)", $"{B.N:N0} agents, {W}x{H} trail map: {msStep:F1} ms per step on an Apple M4 Mac mini (Metal)", "", "");
                    break;
            }
            for (int i = 0; i < stepsPerFrame; i++) { if (split > 0) Step(A); Step(B); }
            if (split > 0) Draw(A, rtA); Draw(B, rtB);
            display.SetFloat("_Split", split); display.SetFloat("_Line", line);
            if (p != phase) { phase = p; Debug.Log($"[gt] t {t:F2} phase {p} '{title.text}' steps A {A.step} B {B.step}"); }
        }

        void OnDestroy()
        {
            foreach (var s in new[] { A, B }) if (s != null) foreach (var b in new[] { s.agents, s.data, s.tmp, s.temp, s.count, s.cfg, s.tbl }) b?.Release();
            foreach (var r in new[] { rtA, rtB }) if (r) r.Release();
        }
    }
}
