// Drives the FLIP post: three simulations share one tank and one start (the same block of water against the left
// wall, a box pillar in the middle). A: FLIP (flipness 0.96, the source scene's value), cell 0.2. B: the same with
// flipness 0 (pure PIC). C: FLIP on a grid with cells twice as large. Each is drawn on its own layer and each camera
// sees one. A simulation restarts when a phase that shows it begins, so each comparison starts from the same frame.
using UnityEngine;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class FLIPDirector : MonoBehaviour
    {
        public Camera camA, camB;
        public FLIPFluid flip, pic, low;
        public LabelSpec title, l1, l2, m1, m2;
        public Vector3 tankCentre;
        int phase = -1; float nextLog = 0f, phaseStart = 0f;

        static void Look(Camera c, Vector3 pos, Vector3 target) { c.transform.position = pos; c.transform.LookAt(target); }
        static Vector3 Orbit(Vector3 target, float yawDeg, float dist, float height)
        {
            float a = yawDeg * Mathf.Deg2Rad;
            return target + new Vector3(Mathf.Sin(a) * dist, height, -Mathf.Cos(a) * dist);
        }

        void Show(Camera c, bool on, Rect r, float fov, FLIPFluid sim)
        {
            c.enabled = on;
            if (!on) return;
            c.rect = r; c.fieldOfView = fov; c.aspect = r.width * 1920f / (r.height * 1080f);
            c.cullingMask = ~((1 << 20) | (1 << 21) | (1 << 22)) | (1 << sim.layer);
        }

        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }

        static string Grid(FLIPFluid f) => $"{f.GridSize.x}x{f.GridSize.y}x{f.GridSize.z}";

        void Update()
        {
            float t = FrameCapturer.T;
            int p = t < 4f ? 0 : t < 12f ? 1 : t < 19f ? 2 : t < 23f ? 3 : 4;
            if (p != phase)
            {
                phase = p; phaseStart = t; nextLog = t;
                if (p == 1) { flip.Respawn(); pic.Respawn(); }
                if (p == 2) { flip.Respawn(); low.Respawn(); }
                if (p == 3) flip.Respawn();
                Debug.Log($"[gt] t {t:F2} phase {p}");
            }
            if (p == 0 && t <= 0f) flip.Respawn();   // hold the start during warm-up
            flip.running = true;
            pic.running = p == 1;
            low.running = p == 2;
        }

        void LateUpdate()
        {
            float t = FrameCapturer.T;
            var full = new Rect(0, 0, 1, 1);
            var h0 = new Rect(0, 0, 0.4985f, 1); var h1 = new Rect(0.5015f, 0, 0.4985f, 1);
            var c0 = tankCentre;
            string n = $"{flip.NumParticles:N0} particles";
            if (phase == 0)
            {
                Show(camA, true, full, 34f, flip); Show(camB, false, full, 0, flip);
                Look(camA, Orbit(c0, Mathf.Lerp(-34f, -22f, t / 4f), 6.4f, 1.9f), c0 + Vector3.down * 0.35f);
                Labels("FLIP fluid: particles + grid", $"{n} carry the velocity; pressure is solved on a {Grid(flip)} grid, then velocity goes back to the particles", "", "", "");
            }
            else if (phase == 1)
            {
                Show(camA, true, h0, 44f, pic); Show(camB, true, h1, 44f, flip);
                float u = Mathf.SmoothStep(0f, 1f, (t - 4f) / 8f);
                foreach (var c in new[] { camA, camB }) Look(c, Orbit(c0, Mathf.Lerp(-26f, -10f, u), 5.6f, 1.9f), c0 + Vector3.down * 0.35f);
                Labels("PIC vs FLIP: same start, same grid",
                    "PIC, flipness 0", "particles take the grid velocity: smooth, damped",
                    $"FLIP, flipness {flip.flipness}", "particles keep their own velocity + the grid change");
            }
            else if (phase == 2)
            {
                Show(camA, true, h0, 44f, low); Show(camB, true, h1, 44f, flip);
                float u = Mathf.SmoothStep(0f, 1f, (t - 12f) / 7f);
                foreach (var c in new[] { camA, camB }) Look(c, Orbit(c0, Mathf.Lerp(-6f, -24f, u), 5.6f, 2.0f), c0 + Vector3.down * 0.35f);
                Labels($"Grid resolution (FLIP {flip.flipness})",
                    $"grid {Grid(low)}, {low.NumParticles:N0} particles", $"cell {low.gridSpacing}",
                    $"grid {Grid(flip)}, {flip.NumParticles:N0} particles", $"cell {flip.gridSpacing}");
            }
            else if (phase == 3)
            {
                Show(camA, true, full, 32f, flip); Show(camB, false, full, 0, flip);
                float u = Mathf.SmoothStep(0f, 1f, (t - 19f) / 4f);
                var f = c0 + new Vector3(0.35f, -0.35f, 0f);
                Look(camA, Orbit(f, Mathf.Lerp(-44f, -30f, u), 4.4f, 1.3f), f);
                Labels("Free surface and splashes", $"FLIP {flip.flipness}, {n}, density projection on", "", "", "");
            }
            else
            {
                Show(camA, true, full, 34f, flip); Show(camB, false, full, 0, flip);
                float u = Mathf.SmoothStep(0f, 1f, (t - 23f) / 3f);
                Look(camA, Orbit(c0, Mathf.Lerp(-14f, -4f, u), 6.4f, 2.1f), c0 + Vector3.down * 0.35f);
                Labels("FLIP fluid", "Unity 6.3 URP, ported from abecombe's FLIP-Fluid-for-Unity (HDRP); spheres drawn by my URP shader", "", "", "");
            }
            if (phase == 0 || phase == 3 || phase == 4) l1.anchor = new Vector2(0.012f, 0.03f);
            else l1.anchor = new Vector2(0.012f, 0.058f);

            if (t >= nextLog)
            {
                nextLog = t + 1f;
                float pt = t - phaseStart;
                Debug.Log($"[gt] t {t:F2} phase {phase} +{pt:F1}s | FLIP: {flip.Stats()}");
                if (phase == 1) Debug.Log($"[gt] t {t:F2} phase 1 +{pt:F1}s | PIC: {pic.Stats()}");
                if (phase == 2) Debug.Log($"[gt] t {t:F2} phase 2 +{pt:F1}s | LOW: {low.Stats()}");
                if (t < 0.5f) Debug.Log($"[gt] grids: high {Grid(flip)} {flip.NumParticles} particles {flip.GpuBytes / 1048576f:F1} MiB, low {Grid(low)} {low.NumParticles} particles {low.GpuBytes / 1048576f:F1} MiB");
            }
        }
    }
}
