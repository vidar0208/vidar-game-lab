// Runs four copies of the stable-fluids solver side by side, one step per captured frame, with identical scripted jets:
// advection only, and the full solver with 5, 20 and 60 Jacobi iterations per solve. Shows them as panels, as debug views,
// and on a screen in a small 3D stage. Mean |divergence| is read back from the GPU and printed in the labels.
using UnityEngine;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class FluidDirector : MonoBehaviour
    {
        public const int Grid = 256;
        public const float Viscosity = 0.01f;
        public static readonly int[] Iterations = { 5, 20, 60 };

        public Camera src;
        public Material compose;
        public ComputeShader navierStokes, solvers, boundary, utility, userInput, view;
        public Texture2D obstacle;
        public Material screen;
        public LabelSpec title, a2, b2, a3, b3, c3;
        public Vector3 screenCentre = new Vector3(0, 2.0f, 0);

        StableFluidSolver adv; readonly StableFluidSolver[] jac = new StableFluidSolver[3];
        RenderTexture srcRT, dyeAdv, velV, preV, divA, divB;
        readonly RenderTexture[] dyeJ = new RenderTexture[3];
        readonly float[] meanDiv = new float[4];
        bool[] solid;
        int W, H, logged = -1, kView, step;

        RenderTexture ViewRT()
        {
            var r = new RenderTexture(1024, 1024, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { enableRandomWrite = true, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            r.Create(); return r;
        }

        StableFluidSolver Make(int iterations)
        {
            var s = new StableFluidSolver(Grid, navierStokes, solvers, boundary, utility, userInput) { solverIterations = iterations, viscosity = Viscosity, dyeDissipation = 0.996f };
            s.UpdateArbitaryBoundaryOffsets(obstacle);
            return s;
        }

        void Start()
        {
            W = int.Parse(FrameCapturer.Arg("-gtW", "1920")); H = int.Parse(FrameCapturer.Arg("-gtH", "1080"));
            srcRT = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { filterMode = FilterMode.Bilinear };
            srcRT.Create(); src.targetTexture = srcRT; src.enabled = false;
            kView = view.FindKernel("View");
            var px = obstacle.GetPixels(); solid = new bool[px.Length];
            for (int i = 0; i < px.Length; i++) solid[i] = px[i].r > 0.1f;
            adv = Make(0);
            for (int i = 0; i < 3; i++) { jac[i] = Make(Iterations[i]); dyeJ[i] = ViewRT(); }
            dyeAdv = ViewRT(); velV = ViewRT(); preV = ViewRT(); divA = ViewRT(); divB = ViewRT();
            screen.SetTexture("_BaseMap", dyeJ[2]);
            for (int i = 0; i < 90; i++) StepAll(i / 30f - 3f);   // 3 s of pre-roll so the first frame already has ink
        }

        // ---- scripted jets (instead of the mouse): two wobbling force + dye sources, plus a third from the top after a while
        static Color Ink(float h)
        {
            // procedural cosine gradient: teal -> violet -> amber
            var c = new Vector3(0.5f, 0.5f, 0.5f) + Vector3.Scale(new Vector3(0.5f, 0.5f, 0.5f),
                new Vector3(Mathf.Cos(6.2832f * (h + 0.00f)), Mathf.Cos(6.2832f * (h + 0.33f)), Mathf.Cos(6.2832f * (h + 0.67f))));
            return new Color(c.x, c.y, c.z, 1) * 0.22f;
        }

        static void Sources(StableFluidSolver s, float t)
        {
            float a1 = (58f + 22f * Mathf.Sin(t * 0.9f)) * Mathf.Deg2Rad;
            float a2 = (196f + 18f * Mathf.Sin(t * 0.7f + 1f)) * Mathf.Deg2Rad;
            var p1 = new Vector2(34, 30); var p2 = new Vector2(224, 150);
            s.AddConstantForceSource(p1, new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * 0.32f, 9f, 5f);
            s.AddConstantForceSource(p2, new Vector2(Mathf.Cos(a2), Mathf.Sin(a2)) * 0.32f, 9f, 5f);
            s.AddConstantDyeSource(p1, Ink(0.08f + 0.05f * t), 8f, 4f);
            s.AddConstantDyeSource(p2, Ink(0.55f + 0.05f * t), 8f, 4f);
        }

        static void StepFull(StableFluidSolver s, float t)
        {
            var V = StableFluidSolver.FieldType.Velocity; var D = StableFluidSolver.FieldType.Dye;
            Sources(s, t);
            s.HandleCornerBoundaries(s.velocity, V); s.HandleArbitaryBoundary(s.velocity, V);
            s.Diffuse(s.velocity);
            s.HandleCornerBoundaries(s.velocity, V); s.HandleArbitaryBoundary(s.velocity, V);
            s.Project(s.velocity);
            s.Advect(s.velocity, s.velocity, s.velocityDissipation);
            s.HandleCornerBoundaries(s.velocity, V); s.HandleArbitaryBoundary(s.velocity, V);
            s.Project(s.velocity);
            s.Advect(s.dye, s.velocity, s.dyeDissipation);
            s.HandleCornerBoundaries(s.dye, D); s.HandleArbitaryBoundary(s.dye, D);
            s.Diffuse(s.dye);
            s.HandleCornerBoundaries(s.dye, D); s.HandleArbitaryBoundary(s.dye, D);
        }

        static void StepAdvectionOnly(StableFluidSolver s, float t)
        {
            var V = StableFluidSolver.FieldType.Velocity; var D = StableFluidSolver.FieldType.Dye;
            Sources(s, t);
            s.HandleCornerBoundaries(s.velocity, V); s.HandleArbitaryBoundary(s.velocity, V);
            s.Advect(s.velocity, s.velocity, s.velocityDissipation);
            s.HandleCornerBoundaries(s.velocity, V); s.HandleArbitaryBoundary(s.velocity, V);
            s.Advect(s.dye, s.velocity, s.dyeDissipation);
            s.HandleCornerBoundaries(s.dye, D); s.HandleArbitaryBoundary(s.dye, D);
        }

        void StepAll(float t)
        {
            StepAdvectionOnly(adv, t);
            foreach (var s in jac) StepFull(s, t);
            step++;
        }

        void View(ComputeBuffer b, RenderTexture rt, int mode, float scale)
        {
            view.SetBuffer(kView, "_view_source", b); view.SetTexture(kView, "_view_out", rt); view.SetTexture(kView, "_view_obstacle", obstacle);
            view.SetInt("_view_res", rt.width); view.SetInt("_view_mode", mode); view.SetFloat("_view_scale", scale);
            view.SetInt("i_Resolution", Grid); view.SetFloat("i_grid_scale", 1f); view.SetFloat("i_timeStep", 1f);
            view.Dispatch(kView, (rt.width + 15) / 16, (rt.height + 15) / 16, 1);
        }

        void Show(int n, Texture p0, Texture p1, Texture p2, float fade, string tl, string l0, string l1, string l2 = "")
        {
            compose.SetTexture("_Stage", srcRT); compose.SetTexture("_P0", p0); compose.SetTexture("_P1", p1); compose.SetTexture("_P2", p2 != null ? p2 : p0);
            compose.SetFloat("_N", n); compose.SetFloat("_Fade", fade);
            title.text = tl;
            a2.text = n == 2 ? l0 : ""; b2.text = n == 2 ? l1 : "";
            a3.text = n == 3 ? l0 : ""; b3.text = n == 3 ? l1 : ""; c3.text = n == 3 ? l2 : "";
        }

        public const float VelScale = 1f / 3f, DivScale = 1f / 0.05f, PreScale = 1f / 0.5f;

        void LateUpdate()
        {
            float t = FrameCapturer.T;
            StepAll(t);

            const float dyeScale = 1.6f;
            View(adv.dye, dyeAdv, 0, dyeScale);
            for (int i = 0; i < 3; i++) View(jac[i].dye, dyeJ[i], 0, dyeScale);

            if (step % 15 == 0)
            {
                meanDiv[0] = adv.MeanAbsDivergence(solid);
                for (int i = 0; i < 3; i++) meanDiv[i + 1] = jac[i].MeanAbsDivergence(solid);
                Debug.Log($"[gt] t {t:F2} mean|div| adv {meanDiv[0]:F4} j5 {meanDiv[1]:F4} j20 {meanDiv[2]:F4} j60 {meanDiv[3]:F4}");
                if (step % 90 == 0) LogRanges(t);
            }

            // stage camera: dolly in at the start, back out at the end
            float inT = Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / 3f)), outT = Mathf.SmoothStep(0, 1, Mathf.Clamp01((t - 22.6f) / 3.4f));
            float dist = Mathf.Lerp(Mathf.Lerp(8.5f, 3.6f, inT), 8.5f, outT);
            float yaw = Mathf.Lerp(Mathf.Lerp(-24f, 0f, inT), 20f, outT) * Mathf.Deg2Rad;
            src.transform.position = screenCentre + new Vector3(Mathf.Sin(yaw) * dist, Mathf.Lerp(0.6f, 0.1f, inT) + 0.5f * outT, -Mathf.Cos(yaw) * dist);
            src.transform.LookAt(screenCentre + Vector3.down * 0.1f);
            src.Render();

            string head = $"2D stable fluids on the GPU: {Grid} x {Grid} grid, time step 1, viscosity {Viscosity}";
            int phase;
            if (t < 3.2f)
            {
                phase = 0;
                Show(2, dyeAdv, dyeJ[2], null, 1f - Mathf.Clamp01((t - 2.6f) / 0.6f), head, "Advection only (no projection): ink stays at the jets", "Full solver, 60 Jacobi iterations per solve");
                if (t < 2.6f) { a2.text = ""; b2.text = ""; }
            }
            else if (t < 9f)
            {
                phase = 1;
                Show(2, dyeAdv, dyeJ[2], null, 0f, head, "Advection only (no projection): ink stays at the jets", "Full: advect + diffuse + project, 60 Jacobi iterations");
            }
            else if (t < 15f)
            {
                phase = 2;
                Show(3, dyeJ[0], dyeJ[1], dyeJ[2], 0f, head + ", disc obstacle",
                     $"5 Jacobi iterations, mean |div| {meanDiv[1]:F4}", $"20 iterations, mean |div| {meanDiv[2]:F4}", $"60 iterations, mean |div| {meanDiv[3]:F4}");
            }
            else if (t < 19f)
            {
                phase = 3;
                View(jac[2].velocity, velV, 1, VelScale); View(jac[2].pressure, preV, 2, PreScale);
                Show(2, velV, preV, null, 0f, "Debug views, 60 iterations",
                     "Velocity: hue = direction, full brightness at 3 cells/step", "Pressure: red +, blue -, full colour at 0.5");
            }
            else if (t < 22.6f)
            {
                phase = 4;
                jac[0].CalculateFieldDivergence(jac[0].velocity, jac[0].divergenceCheck);
                jac[2].CalculateFieldDivergence(jac[2].velocity, jac[2].divergenceCheck);
                View(jac[0].divergenceCheck, divA, 2, DivScale); View(jac[2].divergenceCheck, divB, 2, DivScale);
                Show(2, divA, divB, null, 0f, "Divergence left after the projection, full colour at 0.05",
                     $"5 iterations, mean |div| {meanDiv[1]:F4}", $"60 iterations, mean |div| {meanDiv[3]:F4}");
            }
            else
            {
                phase = 5;
                Show(2, divA, divB, null, Mathf.Clamp01((t - 22.6f) / 0.6f), head, "", "");
            }
            if (phase != logged) { logged = phase; Debug.Log($"[gt] t {t:F2} phase {phase} '{title.text}' '{a2.text}' '{b2.text}' '{a3.text}' '{b3.text}' '{c3.text}'"); }
        }

        void LogRanges(float t)
        {
            var s = jac[2]; var v = new Vector4[Grid * Grid]; var p = new Vector4[Grid * Grid];
            s.velocity.GetData(v); s.pressure.GetData(p);
            float vmax = 0, pmax = 0;
            for (int i = 0; i < v.Length; i++) { vmax = Mathf.Max(vmax, new Vector2(v[i].x, v[i].y).magnitude); pmax = Mathf.Max(pmax, Mathf.Abs(p[i].x)); }
            Debug.Log($"[gt] t {t:F2} j60 max|v| {vmax:F3} max|p| {pmax:F3}");
        }

        void OnDestroy() { adv?.Release(); foreach (var s in jac) s?.Release(); }
    }
}
