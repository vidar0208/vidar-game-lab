// Drives the SPH post: three simulations share one tank (same spawn, same seed). A has the SPH forces off (gravity
// only), B is the source's scene settings, C is B with the source script's default viscosity. Each is drawn on its
// own layer and each camera sees one of them. A simulation restarts when a phase that shows it begins, so every
// comparison starts from the same falling block.
using UnityEngine;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class SPHDirector : MonoBehaviour
    {
        public Camera camA, camB;
        public SPHFluid simOff, simOn, simVisc;
        public LabelSpec title, l1, l2, m1, m2;
        public Vector3 tankCentre;
        int phase = -1; float nextLog = 0f;

        static void Look(Camera c, Vector3 pos, Vector3 target) { c.transform.position = pos; c.transform.LookAt(target); }
        static Vector3 Orbit(Vector3 target, float yawDeg, float dist, float height)
        {
            float a = yawDeg * Mathf.Deg2Rad;
            return target + new Vector3(Mathf.Sin(a) * dist, height, -Mathf.Cos(a) * dist);
        }

        void Show(Camera c, bool on, Rect r, float fov, SPHFluid sim)
        {
            c.enabled = on;
            if (!on) return;
            c.rect = r; c.fieldOfView = fov; c.aspect = r.width * 1920f / (r.height * 1080f);
            c.cullingMask = ~((1 << 20) | (1 << 21) | (1 << 22)) | (1 << sim.layer);
        }

        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }

        void Update()
        {
            float t = FrameCapturer.T;
            int p = t < 4f ? 0 : t < 11f ? 1 : t < 18f ? 2 : t < 22f ? 3 : 4;
            if (p != phase)
            {
                phase = p;
                if (p == 1) { simOff.Respawn(); simOn.Respawn(); }
                if (p == 2) { simOn.Respawn(); simVisc.Respawn(); }
                if (p == 3) simOn.Respawn();
            }
            if (p == 0 && t <= 0f) simOn.Respawn();   // hold the start during warm-up
            simOn.running = true;
            simOff.running = p == 1;
            simVisc.running = p == 2;
        }

        void LateUpdate()
        {
            float t = FrameCapturer.T;
            var full = new Rect(0, 0, 1, 1);
            var h0 = new Rect(0, 0, 0.4985f, 1); var h1 = new Rect(0.5015f, 0, 0.4985f, 1);
            var c0 = tankCentre;
            string n = $"{simOn.numberOfParticles:N0} particles";
            if (phase == 0)
            {
                Show(camA, true, full, 34f, simOn); Show(camB, false, full, 0, simOn);
                Look(camA, Orbit(c0, Mathf.Lerp(-38f, -24f, t / 4f), 5.6f, 1.6f), c0 + Vector3.down * 0.4f);
                Labels("SPH fluid on the GPU", $"{n}, density, pressure and viscosity from neighbours in a uniform grid", "", "", "");
            }
            else if (phase == 1)
            {
                Show(camA, true, h0, 40f, simOff); Show(camB, true, h1, 40f, simOn);
                float u = Mathf.SmoothStep(0f, 1f, (t - 4f) / 7f);
                foreach (var c in new[] { camA, camB }) Look(c, Orbit(c0, Mathf.Lerp(-30f, -12f, u), 5.2f, 1.5f), c0 + Vector3.down * 0.5f);
                Labels("SPH forces off vs on", "Off: gravity only", "no pressure: the block lands flat; red = grid cell over its particle limit", "On: pressure + viscosity", $"{n}, rest density {simOn.restDensity}, viscosity {simOn.viscosityCoefficient}");
            }
            else if (phase == 2)
            {
                Show(camA, true, h0, 40f, simOn); Show(camB, true, h1, 40f, simVisc);
                float u = Mathf.SmoothStep(0f, 1f, (t - 11f) / 7f);
                foreach (var c in new[] { camA, camB }) Look(c, Orbit(c0, Mathf.Lerp(-8f, -26f, u), 5.2f, 1.7f), c0 + Vector3.down * 0.5f);
                Labels("Viscosity", $"viscosity {simOn.viscosityCoefficient}", "the source scene's value", $"viscosity {simVisc.viscosityCoefficient}", "the source script's default");
            }
            else if (phase == 3)
            {
                Show(camA, true, full, 30f, simOn); Show(camB, false, full, 0, simOn);
                float u = (t - 18f) / 4f;
                var corner = new Vector3(0.35f, 0.35f, 0.35f);
                Look(camA, Orbit(corner, Mathf.Lerp(-38f, -22f, u), 4.2f, 0.9f), corner);
                Labels("Close-up: the block hits the floor", $"{n}, {simOn.stepsPerFrame} solver steps per video frame", "", "", "");
            }
            else
            {
                Show(camA, true, full, 34f, simOn); Show(camB, false, full, 0, simOn);
                float u = Mathf.SmoothStep(0f, 1f, (t - 22f) / 4f);
                Look(camA, Orbit(c0, Mathf.Lerp(-20f, -4f, u), Mathf.Lerp(5.0f, 6.0f, u), 1.8f), c0 + Vector3.down * 0.4f);
                Labels("SPH fluid on the GPU", "Unity 6.3 URP, ported from Gornhoth's compute shader SPH", "", "", "");
            }
            if (phase == 0 || phase >= 3) l1.anchor = new Vector2(0.012f, 0.03f);
            else { l1.anchor = new Vector2(0.012f, 0.058f); l2.anchor = new Vector2(0.012f, 0.018f); }
            m1.anchor = new Vector2(0.512f, 0.058f); m2.anchor = new Vector2(0.512f, 0.018f);
            if (t >= nextLog)
            {
                nextLog = t + 2f;
                simOn.Stats(out var maxCell, out var over, out var maxH);
                string extra = "";
                if (simOff.running) { simOff.Stats(out var mc, out var ov, out var mh); extra += $" | off: max per cell {mc}, red {ov}, top {mh:F1}"; }
                if (simVisc.running) { simVisc.Stats(out var mc, out var ov, out var mh); extra += $" | visc: max per cell {mc}, red {ov}, top {mh:F1}"; }
                Debug.Log($"[gt] t {t:F2} phase {phase} '{title.text}' | on: steps {simOn.ElapsedSimulationSteps}, max per cell {maxCell} (limit {simOn.maximumParticlesPerCell}), red {over}, top {maxH:F1}, GPU MB {simOn.GpuBytes / 1048576f:F0}{extra}");
            }
        }
    }
}
