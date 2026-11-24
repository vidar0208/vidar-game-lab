// Runs the Gray-Scott grids and the video timeline. Each grid is shown on its own slab with the RDSurface material;
// cameras with viewport rects make the split screens. test = true shows six feed/kill sets side by side (tuning only).
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class RDDirector : MonoBehaviour
    {
        public ComputeShader cs;
        public Material surface;
        public Camera[] cams;        // 0 full, 1-2 halves, 3-5 thirds, 6-11 test grid, last = divider background
        public LabelSpec title, l1, l2, m1, m2, n1, n2;
        public bool test;
        public int size = 1024, stepsPerFrame = 24;
        public float pop = 0.4f; public uint seed = 1459;
        public Vector2[] sets;       // (feed, kill) for the three-panel comparison or the test grid
        public string[] setNames;

        RDSim hero, diff, gs; RDSim[] panel; Material[] mats; Transform[] slabs; double msPerStep; int phase = -1;

        Transform Slab(int i, RDSim s)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(go.GetComponent<Collider>());
            go.name = "Slab " + i; go.transform.position = new Vector3(i * 40f, 0, 0);
            go.transform.rotation = Quaternion.Euler(90, 0, 0); go.transform.localScale = new Vector3(10, 10, 1);
            var m = new Material(surface); m.SetTexture("_RD", s.a); mats[i] = m;
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off;
            return go.transform;
        }

        RDSim Make(float f, float k, bool react)
        {
            var s = new RDSim(cs, size, size) { feed = f, kill = k, react = react, pop = pop };
            s.Init(seed); return s;
        }

        void Start()
        {
            // timing: 400 steps on one grid, GPU waited with a readback
            var t0 = Make(0.0547f, 0.0632f, true);
            t0.Step(50); AsyncGPUReadback.Request(t0.a).WaitForCompletion();
            var sw = Stopwatch.StartNew(); t0.Step(400); AsyncGPUReadback.Request(t0.a).WaitForCompletion(); sw.Stop();
            msPerStep = sw.Elapsed.TotalMilliseconds / 400.0;
            t0.a.Release(); t0.b.Release();
            Debug.Log($"[gt] {size}x{size}: {msPerStep:F3} ms per step");

            int n = test ? sets.Length : 6;
            mats = new Material[n]; slabs = new Transform[n]; panel = new RDSim[test ? n : 3];
            if (test)
            {
                for (int i = 0; i < n; i++) { panel[i] = Make(sets[i].x, sets[i].y, true); slabs[i] = Slab(i, panel[i]); }
            }
            else
            {
                hero = Make(heroSet.x, heroSet.y, true); slabs[0] = Slab(0, hero);
                hero.Step(preHero);
                diff = Make(0.0547f, 0.0632f, false); slabs[1] = Slab(1, diff);
                gs = Make(0.0547f, 0.0632f, true); slabs[2] = Slab(2, gs);
                for (int i = 0; i < 3; i++) { panel[i] = Make(sets[i].x, sets[i].y, true); slabs[3 + i] = Slab(3 + i, panel[i]); }
            }
            Update();
        }
        public int preHero = 2500; public Vector2 heroSet = new Vector2(0.078f, 0.061f);

        void Light(float t)
        {
            float yaw = 210f + t * 9f;
            var d = Quaternion.Euler(-28f, yaw, 0) * Vector3.forward;   // direction toward the light
            Shader.SetGlobalVector("_L1Dir", d);
            Shader.SetGlobalVector("_L1Col", new Color(1f, 0.92f, 0.82f) * 4.2f);
            Shader.SetGlobalVector("_L2Dir", Quaternion.Euler(-55f, yaw + 150f, 0) * Vector3.forward);
            Shader.SetGlobalVector("_L2Col", new Color(0.35f, 0.55f, 1f) * 1.1f);
            Shader.SetGlobalVector("_SkyTop", new Color(0.55f, 0.65f, 0.85f));
            Shader.SetGlobalVector("_SkyBottom", new Color(0.06f, 0.05f, 0.05f));
        }

        void Look(Camera c, int slab, float yaw, float pitch, float dist, float fov)
        {
            c.enabled = true; c.fieldOfView = fov;
            var rot = Quaternion.Euler(pitch, yaw, 0);
            c.transform.SetPositionAndRotation(slabs[slab].position - rot * Vector3.forward * dist, rot);
        }

        void Labels(string t, string a1, string a2, string b1, string b2, string c1, string c2)
        { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; n1.text = c1; n2.text = c2; }

        void Bind() { for (int i = 0; i < mats.Length; i++) if (mats[i] != null) mats[i].SetTexture("_RD", Sim(i).a); }
        RDSim Sim(int i) => test ? panel[i] : i == 0 ? hero : i == 1 ? diff : i == 2 ? gs : panel[i - 3];

        void Update()
        {
            if (mats == null) return;
            float t = FrameCapturer.T;
            Light(t);
            foreach (var c in cams) c.enabled = false;
            if (test)
            {
                foreach (var s in panel) s.Step(stepsPerFrame);
                for (int i = 0; i < panel.Length; i++) Look(cams[6 + i], i, 0, 70f, 15f, 40f);
                Labels($"steps {panel[0].steps}", $"{setNames[0]} {setNames[1]} {setNames[2]}", $"{setNames[3]} {setNames[4]} {setNames[5]}", "", "", "", "");
                cams[cams.Length - 1].enabled = true;
                Bind(); return;
            }
            int p = t < 3f ? 0 : t < 11f ? 1 : t < 21f ? 2 : 3;
            if (p == 0 || p == 3)
            {
                hero.Step(8);
                Look(cams[0], 0, -8f + t * 1.2f, 55f, 7.2f, 42f);
                if (p == 0) Labels("Reaction-diffusion (Gray-Scott) on the GPU", $"{size} x {size} grid, feed {heroSet.x:0.000}, kill {heroSet.y:0.000}", "two chemicals: both spread out, one feeds on the other", "", "", "", "");
                else Labels("Reaction-diffusion (Gray-Scott)", "Unity 6.3 URP compute shader, after keijiro/RDSystem (public domain)",
                    $"{size}x{size}: {msPerStep:F2} ms per step on an Apple M4 Mac mini (Metal)", "", "", "", "");
            }
            else if (p == 1)
            {
                if (t >= 3.2f) { diff.Step(stepsPerFrame); gs.Step(stepsPerFrame); }
                cams[cams.Length - 1].enabled = true;
                Look(cams[1], 1, t * 1.5f, 62f, 10.5f, 40f); Look(cams[2], 2, t * 1.5f, 62f, 10.5f, 40f);
                m1.anchor.x = m2.anchor.x = 0.512f;
                Labels($"Same start, {gs.steps:N0} steps", "Diffusion only", "reaction, feed and kill turned off", "Gray-Scott (the source's feed 0.0547, kill 0.0632)", "reaction u + 2v -> 3v, feed adds u, kill removes v", "", "");
            }
            else
            {
                if (t >= 11.2f) foreach (var s in panel) s.Step(stepsPerFrame);
                cams[cams.Length - 1].enabled = true;
                for (int i = 0; i < 3; i++) Look(cams[3 + i], 3 + i, t * 1.5f, 62f, 14f, 40f);
                m1.anchor.x = m2.anchor.x = 0.346f;
                Labels($"Same equation, 3 feed / kill pairs, {panel[0].steps:N0} steps",
                    setNames[0], $"feed {sets[0].x:0.0000}, kill {sets[0].y:0.0000}",
                    setNames[1], $"feed {sets[1].x:0.0000}, kill {sets[1].y:0.0000}",
                    setNames[2], $"feed {sets[2].x:0.0000}, kill {sets[2].y:0.0000}");
            }
            Bind();
            if (p != phase) { phase = p; Debug.Log($"[gt] t {t:F2} phase {p} '{title.text}'"); }
        }
    }
}
