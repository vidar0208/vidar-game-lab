// Two disc galaxies on a close pass, run as several independent simulations for the comparisons.
// Each galaxy = one heavy centre (mass 1) + 16,383 stars (total star mass 0.5), G = 1. Stars start on circular
// orbits around the mass inside their radius. Initial conditions, colours and rendering are my own; the force
// and integration kernel follows the source (see IntegrateBodies.compute).
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace GameTech.Posts
{
    public class NBody
    {
        public ComputeBuffer[] pos = new ComputeBuffer[2], vel = new ComputeBuffer[2];
        public float soft2 = 0.1f; public bool centreOnly; public int steps;
        readonly ComputeShader cs; readonly int n, kFull, kCentre;
        public NBody(ComputeShader cs, Vector4[] p, Vector4[] v)
        {
            this.cs = cs; n = p.Length; kFull = cs.FindKernel("CSMain"); kCentre = cs.FindKernel("CentreOnly");
            for (int i = 0; i < 2; i++) { pos[i] = new ComputeBuffer(n, 16); vel[i] = new ComputeBuffer(n, 16); pos[i].SetData(p); vel[i].SetData(v); }
        }
        public void Step(int count, float dt)
        {
            int k = centreOnly ? kCentre : kFull;
            cs.SetFloat("_DeltaTime", dt); cs.SetFloat("_Damping", 1f); cs.SetFloat("_SofteningSquared", soft2);
            cs.SetInt("_NumBodies", n); cs.SetVector("_GroupDim", new Vector4(n / 64, 1, 1, 0));
            cs.SetInt("_CoreA", 0); cs.SetInt("_CoreB", n / 2);
            for (int s = 0; s < count; s++)
            {
                cs.SetBuffer(k, "_ReadPos", pos[0]); cs.SetBuffer(k, "_WritePos", pos[1]);
                cs.SetBuffer(k, "_ReadVel", vel[0]); cs.SetBuffer(k, "_WriteVel", vel[1]);
                cs.Dispatch(k, n / 64, 1, 1);
                (pos[0], pos[1]) = (pos[1], pos[0]); (vel[0], vel[1]) = (vel[1], vel[0]);
            }
            steps += count;
        }
    }

    [DefaultExecutionOrder(-100)]
    public class GalaxyDirector : MonoBehaviour
    {
        public ComputeShader cs;
        public Material starMat;
        public Camera camFull, camL, camR, camBack;
        public LabelSpec title, l1, l2, m1, m2;
        public int perGalaxy = 16384;
        public float dt = 0.01f; public int stepsPerFrame = 16, preHero = 3000, preSoft = 1000;
        public float softSmall = 0.0004f, softSource = 0.1f;

        NBody hero, centre, full, small, big; Material mHero, mA, mB; double msFull, msCentre; int phase = -1; int N;

        static Vector3 Rand3(System.Random r) => new Vector3((float)r.NextDouble() * 2 - 1, (float)r.NextDouble() * 2 - 1, (float)r.NextDouble() * 2 - 1);

        void Galaxy(Vector4[] p, Vector4[] v, int start, Vector3 c, Vector3 cv, Vector3 normal, System.Random rnd)
        {
            int stars = perGalaxy - 1; float mStar = 0.5f / stars;
            p[start] = new Vector4(c.x, c.y, c.z, 1f); v[start] = cv;
            var radii = new float[stars];
            for (int i = 0; i < stars; i++) radii[i] = Mathf.Min(0.5f + (float)(-System.Math.Log(1 - rnd.NextDouble())) * 1.1f, 5f);
            System.Array.Sort(radii);
            normal.Normalize();
            var t1 = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var t2 = Vector3.Cross(normal, t1);
            for (int i = 0; i < stars; i++)
            {
                float r = radii[i], a = (float)rnd.NextDouble() * Mathf.PI * 2;
                var dir = t1 * Mathf.Cos(a) + t2 * Mathf.Sin(a);
                var off = dir * r + normal * ((float)rnd.NextDouble() - 0.5f) * 0.12f;
                float menc = 1f + mStar * i;
                float d2 = r * r + softSource;
                float speed = Mathf.Sqrt(menc * r * r / Mathf.Pow(d2, 1.5f));
                var vel = Vector3.Cross(normal, dir) * speed + Rand3(rnd) * 0.02f;
                var q = c + off; var w = cv + vel;
                p[start + 1 + i] = new Vector4(q.x, q.y, q.z, mStar); v[start + 1 + i] = new Vector4(w.x, w.y, w.z, 0);
            }
        }

        NBody Make(Vector4[] p, Vector4[] v, float s2, bool centreOnly) => new NBody(cs, p, v) { soft2 = s2, centreOnly = centreOnly };

        double Time1(NBody b, int count)
        {
            b.Step(5, dt); AsyncGPUReadback.Request(b.pos[0]).WaitForCompletion();
            var sw = Stopwatch.StartNew(); b.Step(count, dt); AsyncGPUReadback.Request(b.pos[0]).WaitForCompletion(); sw.Stop();
            return sw.Elapsed.TotalMilliseconds / count;
        }

        void Start()
        {
            N = perGalaxy * 2;
            var p = new Vector4[N]; var v = new Vector4[N]; var rnd = new System.Random(7);
            Galaxy(p, v, 0, new Vector3(-7f, 0, 2.5f), new Vector3(0.25f, 0, 0), Quaternion.Euler(25, 0, 0) * Vector3.up, rnd);
            Galaxy(p, v, perGalaxy, new Vector3(7f, 0, -2.5f), new Vector3(-0.25f, 0, 0), Quaternion.Euler(-15, 0, 35) * Vector3.up, rnd);

            var tf = Make(p, v, softSource, false); msFull = Time1(tf, 40);
            var tc = Make(p, v, softSource, true); msCentre = Time1(tc, 200);
            Debug.Log($"[gt] {N} bodies: full {msFull:F2} ms per step, centre-only {msCentre:F3} ms per step");

            hero = Make(p, v, softSource, false); hero.Step(preHero, dt);
            centre = Make(p, v, softSource, true); full = Make(p, v, softSource, false);
            small = Make(p, v, softSmall, false); big = Make(p, v, softSource, false);
            small.Step(preSoft, dt); big.Step(preSoft, dt);
            mHero = new Material(starMat); mA = new Material(starMat); mB = new Material(starMat);
            foreach (var m in new[] { mHero, mA, mB })
            {
                m.SetFloat("_Size", 0.045f); m.SetFloat("_Gain", 0.55f); m.SetFloat("_MaxMass", 0.5f);
                m.SetColor("_ColA", new Color(0.45f, 0.65f, 1f)); m.SetColor("_ColB", new Color(1f, 0.6f, 0.28f));
            }
            Debug.Log($"[gt] star shader supported {starMat.shader.isSupported}");
            Renderer("Stars full", mHero, 13); Renderer("Stars left", mA, 11); Renderer("Stars right", mB, 12);
            camFull.cullingMask = 1 << 13; camL.cullingMask = 1 << 11; camR.cullingMask = 1 << 12;
            Update();
        }

        // One mesh with 6 vertices per star; the shader reads the star index from the vertex id. Each view has its own
        // renderer on its own layer, so each camera only sees its simulation.
        MeshRenderer Renderer(string name, Material m, int layer)
        {
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = new Vector3[N * 6];
            var idx = new int[N * 6]; for (int i = 0; i < idx.Length; i++) idx[i] = i;
            mesh.SetIndices(idx, MeshTopology.Triangles, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
            var go = new GameObject(name); go.layer = layer;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            return r;
        }

        void Draw(Material m, NBody b, Camera c)
        {
            m.SetBuffer("_Pos", b.pos[0]); m.SetBuffer("_Vel", b.vel[0]); m.SetFloat("_Half", N / 2);
        }

        void Orbit(Camera c, float yaw, float pitch, float dist)
        {
            c.enabled = true;
            var rot = Quaternion.Euler(pitch, yaw, 0);
            c.transform.SetPositionAndRotation(-(rot * Vector3.forward) * dist, rot);
        }

        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }

        void Update()
        {
            if (hero == null) return;
            float t = FrameCapturer.T;
            camFull.enabled = camL.enabled = camR.enabled = camBack.enabled = false;
            int p = t < 3f ? 0 : t < 12f ? 1 : t < 21f ? 2 : 3;
            float yaw = 10f + t * 1.5f;
            string stars = $"{N - 2:N0} stars";
            if (p == 0 || p == 3)
            {
                hero.Step(stepsPerFrame / 2, dt);
                Orbit(camFull, yaw, 52f, 36f); Draw(mHero, hero, camFull);
                if (p == 0) Labels("Two galaxies colliding: full N-body on the GPU", $"{stars}, every star pulls every other star", "tidal tails come from that pull", "", "");
                else Labels("Full N-body gravity", "Unity 6.3 URP compute shader, after Scrawk/GPU-GEMS-NBody-Simulation (MIT)",
                    $"{N:N0} bodies: {msFull:F1} ms per step on an Apple M4 Mac mini (Metal)", "", "");
            }
            else if (p == 1)
            {
                if (t >= 3.2f) { centre.Step(stepsPerFrame, dt); full.Step(stepsPerFrame, dt); }
                camBack.enabled = true;
                Orbit(camL, yaw, 52f, 40f); Orbit(camR, yaw, 52f, 40f);
                Draw(mA, centre, camL); Draw(mB, full, camR);
                Labels($"Same start, same {stars}, t = {full.steps * dt:F1}",
                    "Each star pulled only by its galaxy's centre", "the two centres pull each other",
                    "Full N-body", "every star pulls every other star");
            }
            else
            {
                if (t >= 12.2f) { small.Step(stepsPerFrame, dt); big.Step(stepsPerFrame, dt); }
                camBack.enabled = true;
                Orbit(camL, yaw, 52f, 40f); Orbit(camR, yaw, 52f, 40f);
                Draw(mA, small, camL); Draw(mB, big, camR);
                Labels($"Softening: how close pulls are allowed to get, t = {big.steps * dt:F1}",
                    $"Small softening (eps^2 = {softSmall})", "close passes kick stars: discs puff up into haze",
                    $"Source value (eps^2 = {softSource})", "discs and tidal tails stay sharp");
            }
            if (p != phase) { phase = p; Debug.Log($"[gt] t {t:F2} phase {p} '{title.text}'"); }
        }
    }
}
