// Drives the 3D fluid post: the stand-in mannequin walks through the smoke plume, its body is turned into moving
// capsule obstacles every frame, the cameras move, and each camera only sees the simulation it is meant to show.
// Four copies of the solver run side by side with the same emitter: full (slab + figure obstacles, vorticity 1),
// no obstacles, vorticity 0, and solver off (the emitter only adds density).
using System.Collections.Generic;
using FluidSim3DProject;
using UnityEngine;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class FluidDirector : MonoBehaviour
    {
        public Camera camA, camB;
        public Transform character, slab;
        public SmokeSim full, noObstacles, noVorticity, off;   // layers 20, 21, 22, 23
        public LabelSpec title, l1, l2, m1, m2;

        readonly Dictionary<Transform, Quaternion> bind = new Dictionary<Transform, Quaternion>();
        readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();

        // the mannequin's capsules (same table as Mannequin.Build): bone, a, b, radius in the bind pose
        struct Cap { public Transform bone; public Vector3 a, b; public float r; }
        readonly List<Cap> caps = new List<Cap>();
        readonly List<Vector4> capData = new List<Vector4>();
        Vector3[] prevA, prevB;
        int logged = -1; float nextLog = 0f, lastT = -1f;

        void AddCap(string bone, Vector3 a, Vector3 b, float r)
        {
            var t = bones[bone];
            caps.Add(new Cap { bone = t, a = t.InverseTransformPoint(character.TransformPoint(a)), b = t.InverseTransformPoint(character.TransformPoint(b)), r = r });
        }

        void Awake()
        {
            foreach (var t in character.GetComponentsInChildren<Transform>(true)) { bones[t.name] = t; if (t != character) bind[t] = t.localRotation; }
            AddCap("Hips", new Vector3(-0.05f, 0.75f, 0), new Vector3(0.05f, 0.75f, 0), 0.085f);
            AddCap("Spine", new Vector3(0, 0.80f, 0), new Vector3(0, 0.94f, 0), 0.08f);
            AddCap("Chest", new Vector3(0, 0.98f, 0), new Vector3(0, 1.07f, 0), 0.105f);
            AddCap("Chest", new Vector3(-0.12f, 1.09f, 0), new Vector3(0.12f, 1.09f, 0), 0.055f);
            AddCap("Neck", new Vector3(0, 1.11f, 0), new Vector3(0, 1.17f, 0), 0.035f);
            AddCap("Head", new Vector3(0, 1.265f, 0.01f), new Vector3(0, 1.275f, 0.01f), 0.11f);
            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? -1 : 1; string n = side == 0 ? "_L" : "_R";
                AddCap("Upperarm" + n, new Vector3(0.15f * sx, 1.10f, 0), new Vector3(0.36f * sx, 1.10f, 0), 0.038f);
                AddCap("Lowerarm" + n, new Vector3(0.38f * sx, 1.10f, 0), new Vector3(0.56f * sx, 1.10f, 0), 0.032f);
                AddCap("Hand" + n, new Vector3(0.61f * sx, 1.10f, 0), new Vector3(0.63f * sx, 1.10f, 0), 0.04f);
                AddCap("Upperleg" + n, new Vector3(0.085f * sx, 0.69f, 0), new Vector3(0.085f * sx, 0.41f, 0), 0.062f);
                AddCap("Lowerleg" + n, new Vector3(0.085f * sx, 0.38f, 0), new Vector3(0.085f * sx, 0.10f, 0), 0.046f);
                AddCap("Foot" + n, new Vector3(0.085f * sx, 0.04f, -0.03f), new Vector3(0.085f * sx, 0.04f, 0.10f), 0.04f);
            }
            prevA = new Vector3[caps.Count]; prevB = new Vector3[caps.Count];
            var box = new Bounds(slab.position, slab.localScale);
            foreach (var s in new[] { full, noObstacles, noVorticity, off }) { s.boxes = new List<Bounds> { box }; s.capsules = () => capData; }
            noObstacles.useObstacles = false;
            noVorticity.m_vorticityStrength = 0f;
            off.simulate = false;
            Walk(0f); UpdateCaps(0f);
            Debug.Log($"[gt] capsules {caps.Count}, slab box {box.min} .. {box.max}");
        }

        void Turn(string bone, float deg, Vector3 axis)
        {
            if (!bones.TryGetValue(bone, out var b)) return;
            b.rotation = Quaternion.AngleAxis(deg, character.TransformDirection(axis)) * b.rotation;
        }

        // walk along x through the plume (x -0.95 .. 0.95 at z 0.05), turning at the ends; simple gait
        void Walk(float t)
        {
            const float speed = 0.5f, x0 = -0.95f, x1 = 0.95f, len = x1 - x0;
            float s = Mathf.Repeat(t * speed + 0.25f, 2 * len);
            bool back = s > len;
            float x = back ? x1 - (s - len) : x0 + s;
            character.position = new Vector3(x, 0f, 0.05f);
            character.rotation = Quaternion.Euler(0, back ? -90f : 90f, 0);
            foreach (var kv in bind) kv.Key.localRotation = kv.Value;
            float ph = t * speed / 0.62f * Mathf.PI;
            float sw = Mathf.Sin(ph);
            Turn("Upperarm_L", 72f, Vector3.forward); Turn("Upperarm_R", -72f, Vector3.forward);
            Turn("Upperarm_L", 22f * sw, Vector3.right); Turn("Upperarm_R", -22f * sw, Vector3.right);
            Turn("Lowerarm_L", -12f, Vector3.right); Turn("Lowerarm_R", -12f, Vector3.right);
            Turn("Upperleg_L", -26f * sw, Vector3.right); Turn("Upperleg_R", 26f * sw, Vector3.right);
            Turn("Lowerleg_L", 30f * Mathf.Max(0f, -Mathf.Cos(ph)), Vector3.right); Turn("Lowerleg_R", 30f * Mathf.Max(0f, Mathf.Cos(ph)), Vector3.right);
            Turn("Head", 4f * Mathf.Sin(t * 0.7f), Vector3.up);
        }

        // capsule end points in world space and their velocities (m/s, from the previous frame)
        void UpdateCaps(float dt)
        {
            capData.Clear();
            for (int i = 0; i < caps.Count; i++)
            {
                var c = caps[i];
                Vector3 a = c.bone.TransformPoint(c.a), b = c.bone.TransformPoint(c.b);
                Vector3 va = dt > 0 ? (a - prevA[i]) / dt : Vector3.zero, vb = dt > 0 ? (b - prevB[i]) / dt : Vector3.zero;
                prevA[i] = a; prevB[i] = b;
                capData.Add(new Vector4(a.x, a.y, a.z, c.r)); capData.Add(b); capData.Add(va); capData.Add(vb);
            }
        }

        const int SimLayers = 0xF << 20;
        static void Look(Camera c, Vector3 pos, Vector3 target) { c.transform.position = pos; c.transform.LookAt(target); }

        void Show(Camera c, bool on, Rect r, float fov, SmokeSim sim)
        {
            c.enabled = on;
            if (!on) return;
            c.rect = r; c.fieldOfView = fov; c.aspect = r.width * 1920f / (r.height * 1080f);
            c.cullingMask = ~SimLayers | (1 << sim.gameObject.layer);
        }

        void Labels(string t, string a1, string a2, string b1, string b2)
        {
            title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2;
        }

        static Vector3 Orbit(float deg, float dist, float y) => new Vector3(Mathf.Sin(deg * Mathf.Deg2Rad) * dist, y, -Mathf.Cos(deg * Mathf.Deg2Rad) * dist);

        void Update()
        {
            float t = FrameCapturer.T;
            float dt = lastT < 0 ? 0f : t - lastT; lastT = t;
            Walk(t);
            UpdateCaps(dt > 0 ? dt : 0f);
            var full01 = new Rect(0, 0, 1, 1);
            var h0 = new Rect(0, 0, 0.4985f, 1); var h1 = new Rect(0.5015f, 0, 0.4985f, 1);
            var centre = new Vector3(0f, 1.15f, 0f);
            int phase;
            string grid = $"{full.m_width}³ cells, {full.m_iterations} Jacobi iterations per step";
            if (t < 4f)
            {
                phase = 0;
                Show(camA, true, full01, 42f, full); Show(camB, false, full01, 0, full);
                Look(camA, Orbit(Mathf.Lerp(-28f, -8f, t / 4f), 4.4f, 1.5f), centre);
                Labels("3D smoke solver on the GPU", "Grid fluid in compute shaders: a walking figure and a slab are obstacles", "", "", "");
            }
            else if (t < 10f)
            {
                phase = 1;
                Show(camA, true, h0, 52f, off); Show(camB, true, h1, 52f, full);
                float u = Mathf.SmoothStep(0f, 1f, (t - 4f) / 6f);
                foreach (var c in new[] { camA, camB }) Look(c, Orbit(Mathf.Lerp(-12f, 8f, u), 3.7f, 1.35f), centre);
                Labels("Solver off vs on",
                    "Off", "the emitter adds smoke, nothing moves it",
                    "On", grid);
            }
            else if (t < 16f)
            {
                phase = 2;
                Show(camA, true, h0, 52f, noObstacles); Show(camB, true, h1, 52f, full);
                float u = Mathf.SmoothStep(0f, 1f, (t - 10f) / 6f);
                foreach (var c in new[] { camA, camB }) Look(c, Orbit(Mathf.Lerp(20f, 34f, u), 3.6f, 1.25f), centre);
                Labels("Obstacles",
                    "No obstacles", "smoke passes through the slab and the figure",
                    "Obstacles", $"slab + {caps.Count} moving capsules from the figure's bones");
            }
            else if (t < 22f)
            {
                phase = 3;
                Show(camA, true, h0, 52f, noVorticity); Show(camB, true, h1, 52f, full);
                float u = Mathf.SmoothStep(0f, 1f, (t - 16f) / 6f);
                foreach (var c in new[] { camA, camB }) Look(c, Orbit(Mathf.Lerp(-34f, -20f, u), 3.6f, 1.45f), centre + new Vector3(0, 0.15f, 0));
                Labels("Vorticity confinement",
                    "Strength 0", "small swirls are smoothed away",
                    "Strength 1", "source default: swirls are fed back in");
            }
            else
            {
                phase = 4;
                Show(camA, true, full01, 40f, full); Show(camB, false, full01, 0, full);
                float u = Mathf.SmoothStep(0f, 1f, (t - 22f) / 4f);
                Look(camA, Orbit(Mathf.Lerp(12f, 30f, u), Mathf.Lerp(4.6f, 4.2f, u), 1.4f), centre);
                Labels("3D smoke solver on the GPU", "Unity 6.3 URP, ported from Scrawk's GPU-GEMS-3D-Fluid-Simulation", "", "", "");
            }
            if (phase == 0 || phase == 4) l1.anchor = new Vector2(0.012f, 0.03f);
            else { l1.anchor = new Vector2(0.012f, 0.058f); l2.anchor = new Vector2(0.012f, 0.018f); }
            m1.anchor = new Vector2(0.512f, 0.058f); m2.anchor = new Vector2(0.512f, 0.018f);
            if (phase != logged) { logged = phase; Debug.Log($"[gt] t {t:F2} phase {phase} '{title.text}' | '{l1.text}' '{l2.text}' | '{m1.text}' '{m2.text}'"); }
            if (t >= nextLog)
            {
                nextLog = t + 2f;
                Debug.Log($"[gt] t {t:F2} steps {full.StepsDone} density full {full.TotalDensity():F0} noObst {noObstacles.TotalDensity():F0} noVort {noVorticity.TotalDensity():F0} off {off.TotalDensity():F0} | figure x {character.position.x:F2} | grid {full.m_width}x{full.m_height}x{full.m_depth} it {full.m_iterations} vort {full.m_vorticityStrength}/{noVorticity.m_vorticityStrength}");
            }
        }
    }
}
