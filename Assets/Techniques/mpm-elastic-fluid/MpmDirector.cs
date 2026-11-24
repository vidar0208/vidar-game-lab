// Scenes for the MLS-MPM port (MpmSim.cs, from nialltl/incremental_mpm, MIT).
// My code: the blocks and their start velocities, the dam break, particles-per-cell variants, the ball renderer,
// walls, timing, cameras and labels.
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class MpmDirector : MonoBehaviour
    {
        public Material dotMat, wallMat;
        public Camera camFull, camL, camR, camBack;
        public LabelSpec title, l1, l2, m1, m2;
        const float CellW = 0.1f;
        static readonly Color[] Palette = { new Color(1f, 0.3f, 0.55f), new Color(1f, 0.55f, 0.15f), new Color(1f, 0.85f, 0.25f), new Color(0.2f, 0.62f, 1f) };

        class View
        {
            public MpmSim sim; public Vector3 origin; public ComputeBuffer buf; public Vector4[] data; public Material mat;
            public double msSum; public int steps; public float Ms => steps == 0 ? 0 : (float)(msSum / steps);
            public int N => sim.ps.Length;
        }
        View hero, elas, fluid, low, high;
        int phaseShown = -1;

        static void Block(List<(float2, float2, float)> pts, float x0, float y0, float w, float h, float spacing, float2 v, float colour)
        {
            for (float i = x0 + spacing * 0.5f; i < x0 + w; i += spacing)
                for (float j = y0 + spacing * 0.5f; j < y0 + h; j += spacing) pts.Add((math.float2(i, j), v, colour));
        }

        View Make(MpmSim.Model model, int rx, int ry, List<(float2, float2, float)> pts, float spacing, Vector3 origin)
        {
            var v = new View { origin = origin };
            v.sim = new MpmSim(model, rx, ry, pts, 4f * spacing * spacing); // 4 kg per cell, as the repo's 4 particles of mass 1
            v.buf = new ComputeBuffer(v.N, 16); v.data = new Vector4[v.N];
            v.mat = new Material(dotMat);
            v.mat.SetFloat("_Size", 0.72f * spacing); v.mat.SetFloat("_Cell", CellW); v.mat.SetVector("_Origin", origin);
            var pal = new Vector4[8]; for (int i = 0; i < Palette.Length; i++) pal[i] = Palette[i]; v.mat.SetVectorArray("_Palette", pal);
            v.mat.SetBuffer("_P", v.buf);
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 }; mesh.vertices = new Vector3[v.N * 6];
            var idx = new int[v.N * 6]; for (int i = 0; i < idx.Length; i++) idx[i] = i;
            mesh.SetIndices(idx, MeshTopology.Triangles, 0, false);
            mesh.bounds = new Bounds(origin + new Vector3(rx, ry, 0) * CellW * 0.5f, new Vector3(rx, ry, 4) * CellW);
            var go = new GameObject($"MPM {model} {v.N}"); go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = v.mat; r.shadowCastingMode = ShadowCastingMode.Off;
            // walls: the grid's 2-cell boundary band
            Wall(origin, new Vector2(0, 0), new Vector2(rx, 2)); Wall(origin, new Vector2(0, 0), new Vector2(2, ry)); Wall(origin, new Vector2(rx - 2, 0), new Vector2(2, ry));
            Upload(v);
            return v;
        }

        void Wall(Vector3 origin, Vector2 min, Vector2 size)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(g.GetComponent<Collider>());
            g.transform.position = origin + new Vector3(min.x + size.x * 0.5f, min.y + size.y * 0.5f, 0.05f) * CellW + new Vector3(0, 0, 0.05f);
            g.transform.localScale = new Vector3(size.x, size.y, 1) * CellW; g.GetComponent<Renderer>().sharedMaterial = wallMat;
        }

        static void Upload(View v)
        {
            var ps = v.sim.ps;
            for (int i = 0; i < ps.Length; i++) { var p = ps[i]; v.data[i] = new Vector4(p.x.x, p.x.y, math.length(p.v), p.colour); }
            v.buf.SetData(v.data);
        }

        static void Run(View v)
        {
            for (int s = 0; s < v.sim.StepsPerFrame; s++)
            {
                var sw = Stopwatch.StartNew(); v.sim.Step(); sw.Stop();
                v.msSum += sw.Elapsed.TotalMilliseconds; v.steps++;
            }
            Upload(v);
        }

        void Start()
        {
            var pts = new List<(float2, float2, float)>();
            Block(pts, 24, 34, 28, 28, 0.5f, math.float2(0.25f, 0f), 0);
            Block(pts, 98, 48, 24, 36, 0.5f, math.float2(0f, 0f), 1);
            Block(pts, 160, 30, 30, 22, 0.5f, math.float2(-0.25f, 0.15f), 2);
            hero = Make(MpmSim.Model.NeoHookean, 224, 128, pts, 0.5f, new Vector3(0, 0, 0));
            pts = new List<(float2, float2, float)>(); Block(pts, 14, 16, 32, 32, 0.5f, math.float2(0.3f, 0f), 0);
            elas = Make(MpmSim.Model.NeoHookean, 112, 128, pts, 0.5f, new Vector3(100, 0, 0));
            pts = new List<(float2, float2, float)>(); Block(pts, 14, 16, 32, 32, 0.5f, math.float2(0.3f, 0f), 3);
            fluid = Make(MpmSim.Model.Fluid, 112, 128, pts, 0.5f, new Vector3(200, 0, 0));
            pts = new List<(float2, float2, float)>(); Block(pts, 4, 4, 32, 72, 1f, math.float2(0f, 0f), 3);
            low = Make(MpmSim.Model.Fluid, 112, 128, pts, 1f, new Vector3(300, 0, 0));
            pts = new List<(float2, float2, float)>(); Block(pts, 4, 4, 32, 72, 0.25f, math.float2(0f, 0f), 3);
            high = Make(MpmSim.Model.Fluid, 112, 128, pts, 0.25f, new Vector3(400, 0, 0));
            Debug.Log($"[gt] particles: hero {hero.N}, elastic {elas.N}, fluid {fluid.N}, dam low {low.N}, dam high {high.N}");
            foreach (var c in new[] { camFull, camL, camR }) { c.orthographic = true; c.orthographicSize = 7.2f; }
            Update();
        }

        void Look(Camera c, View v) { c.enabled = true; c.transform.SetPositionAndRotation(v.origin + new Vector3(v.sim.resX, v.sim.resY, 0) * CellW * 0.5f + new Vector3(0, -0.8f, -10f), Quaternion.identity); }

        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }

        void Update()
        {
            if (hero == null) return;
            float t = FrameCapturer.T;
            camFull.enabled = camL.enabled = camR.enabled = camBack.enabled = false;
            int p = t < 6f ? 0 : t < 14f ? 1 : t < 21f ? 2 : 3;
            if (p == 0 || p == 3)
            {
                if (t > 0) Run(hero); Look(camFull, hero);
                if (p == 0) Labels("Material point method: particles carry the material, a grid does the physics",
                    $"{hero.N:N0} particles, Neo-Hookean elastic, 224 x 128 grid cells",
                    "every step: particles to grid, grid update with gravity and walls, grid to particles", "", "");
                else Labels("2D MLS-MPM ported from nialltl/incremental_mpm (MIT), Burst jobs on the CPU",
                    $"elastic, {hero.N:N0} particles: {hero.Ms:F2} ms per step, 10 steps per frame",
                    "Apple M4 Mac mini, Unity 6.3 Editor; the scatter to the grid runs on one thread, as in the repo", "", "");
            }
            else if (p == 1)
            {
                camBack.enabled = true; Run(elas); Run(fluid); Look(camL, elas); Look(camR, fluid);
                Labels("Same block, same throw, 4,096 particles: only the stress model differs",
                    "Neo-Hookean elastic: stress from the deformation gradient", $"{elas.Ms:F2} ms per step",
                    "Newtonian fluid: pressure from density, plus viscosity", $"{fluid.Ms:F2} ms per step");
            }
            else
            {
                camBack.enabled = true; Run(low); Run(high); Look(camL, low); Look(camR, high);
                Labels("Same fluid, same dam break, same 112 x 128 grid",
                    $"1 particle per cell: {low.N:N0} particles", $"{low.Ms:F2} ms per step",
                    $"16 particles per cell: {high.N:N0} particles", $"{high.Ms:F2} ms per step");
            }
            if (p != phaseShown) { phaseShown = p; Debug.Log($"[gt] t {t:F2} phase {p} '{title.text}' | {l1.text} | {l2.text} | {m1.text} | {m2.text}"); }
            if (Time.frameCount % 60 == 0) Debug.Log($"[gt] t {t:F2} ms/step hero {hero.Ms:F3} elas {elas.Ms:F3} fluid {fluid.Ms:F3} low {low.Ms:F3} high {high.Ms:F3}");
        }

        void OnDestroy()
        {
            foreach (var v in new[] { hero, elas, fluid, low, high }) if (v != null) { v.sim.Dispose(); v.buf.Release(); }
        }
    }
}
