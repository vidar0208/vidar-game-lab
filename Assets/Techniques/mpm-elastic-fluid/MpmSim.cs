// 2D MLS-MPM, ported from nialltl/incremental_mpm (MIT): the Neo-Hookean elastic scene
// (MLS_MPM_NeoHookean_Multithreaded.cs) and the Newtonian fluid scene (MLS_MPM_Fluid_Multithreaded.cs).
// The jobs (clear grid, P2G, grid update, G2P), the constants (Lame parameters 10 / 20, fluid rest density 4,
// viscosity 0.1, EOS stiffness 10, power 4, gravity -0.3, dt 0.1 / 0.2) and the boundary handling are the
// repository's. My changes: one class for both models, a rectangular grid (resX x resY) instead of 64 x 64,
// several blocks with start velocities and colours, particle mass scaled with particles per cell so the
// density stays the same, no stackalloc (no unsafe code), no mouse interaction, and timing.
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace GameTech.Posts
{
    public class MpmSim : System.IDisposable
    {
        public enum Model { NeoHookean, Fluid }
        public struct Particle { public float2 x, v; public float2x2 C; public float mass, volume_0; public float colour; }
        public struct Cell { public float2 v; public float mass, padding; }

        public readonly Model model; public readonly int resX, resY;
        public NativeArray<Particle> ps; public NativeArray<float2x2> Fs; public NativeArray<Cell> grid;
        public float Dt => model == Model.NeoHookean ? 0.1f : 0.2f;
        public int StepsPerFrame => model == Model.NeoHookean ? 10 : 5; // repo: 1 / dt steps per Update
        const float gravity = -0.3f;

        public MpmSim(Model model, int resX, int resY, System.Collections.Generic.List<(float2 x, float2 v, float colour)> pts, float mass)
        {
            this.model = model; this.resX = resX; this.resY = resY;
            int n = pts.Count;
            ps = new NativeArray<Particle>(n, Allocator.Persistent); Fs = new NativeArray<float2x2>(n, Allocator.Persistent);
            grid = new NativeArray<Cell>(resX * resY, Allocator.Persistent);
            for (int i = 0; i < n; i++) { ps[i] = new Particle { x = pts[i].x, v = pts[i].v, mass = mass, colour = pts[i].colour }; Fs[i] = float2x2.identity; }
            if (model == Model.NeoHookean)
            {
                // precompute particle volumes (repo: MPM course eq. 152)
                new P2GElastic { ps = ps, Fs = Fs, grid = grid, n = n, resY = resY, dt = Dt }.Run();
                for (int i = 0; i < n; i++)
                {
                    var p = ps[i]; float2 ci = math.floor(p.x), d = (p.x - ci) - 0.5f;
                    float2 w0 = 0.5f * math.pow(0.5f - d, 2), w1 = 0.75f - math.pow(d, 2), w2 = 0.5f * math.pow(0.5f + d, 2);
                    float density = 0;
                    for (int gx = 0; gx < 3; gx++) for (int gy = 0; gy < 3; gy++)
                        density += grid[((int)ci.x + gx - 1) * resY + (int)ci.y + gy - 1].mass * W(gx, w0, w1, w2).x * W(gy, w0, w1, w2).y;
                    p.volume_0 = p.mass / density; ps[i] = p;
                }
            }
        }

        public static float2 W(int g, float2 w0, float2 w1, float2 w2) => g == 0 ? w0 : g == 1 ? w1 : w2;

        public void Step()
        {
            var h = new ClearGrid { grid = grid }.Schedule(grid.Length, 128);
            if (model == Model.NeoHookean) h = new P2GElastic { ps = ps, Fs = Fs, grid = grid, n = ps.Length, resY = resY, dt = Dt }.Schedule(h);
            else
            {
                h = new P2GFluid1 { ps = ps, grid = grid, n = ps.Length, resY = resY }.Schedule(h);
                h = new P2GFluid2 { ps = ps, grid = grid, n = ps.Length, resY = resY, dt = Dt }.Schedule(h);
            }
            h = new UpdateGrid { grid = grid, resX = resX, resY = resY, dt = Dt }.Schedule(grid.Length, 128, h);
            h = new G2P { ps = ps, Fs = Fs, grid = grid, resX = resX, resY = resY, dt = Dt, fluid = model == Model.Fluid }.Schedule(ps.Length, 64, h);
            h.Complete();
        }

        [BurstCompile] struct ClearGrid : IJobParallelFor
        {
            public NativeArray<Cell> grid;
            public void Execute(int i) { grid[i] = default; }
        }

        // Neo-Hookean P2G (repo: Job_P2G in the NeoHookean scene)
        [BurstCompile] struct P2GElastic : IJob
        {
            public NativeArray<Cell> grid; [ReadOnly] public NativeArray<Particle> ps; [ReadOnly] public NativeArray<float2x2> Fs;
            public int n, resY; public float dt;
            const float elastic_lambda = 10f, elastic_mu = 20f;
            public void Execute()
            {
                for (int i = 0; i < n; i++)
                {
                    var p = ps[i]; var F = Fs[i]; var J = math.determinant(F);
                    var volume = p.volume_0 * J;
                    var F_T = math.transpose(F); var F_inv_T = math.inverse(F_T);
                    var P = elastic_mu * (F - F_inv_T) + elastic_lambda * math.log(J) * F_inv_T;
                    var stress = (1.0f / J) * math.mul(P, F_T);
                    var eq_16_term_0 = -volume * 4 * stress * dt;
                    uint2 ci = (uint2)p.x; float2 d = (p.x - ci) - 0.5f;
                    float2 w0 = 0.5f * math.pow(0.5f - d, 2), w1 = 0.75f - math.pow(d, 2), w2 = 0.5f * math.pow(0.5f + d, 2);
                    for (int gx = 0; gx < 3; gx++) for (int gy = 0; gy < 3; gy++)
                    {
                        float weight = W(gx, w0, w1, w2).x * W(gy, w0, w1, w2).y;
                        uint2 cx = math.uint2(ci.x + (uint)gx - 1, ci.y + (uint)gy - 1);
                        float2 dist = (cx - p.x) + 0.5f; float2 Q = math.mul(p.C, dist);
                        int idx = (int)cx.x * resY + (int)cx.y; var cell = grid[idx];
                        float wm = weight * p.mass; cell.mass += wm; cell.v += wm * (p.v + Q);
                        cell.v += math.mul(eq_16_term_0 * weight, dist);
                        grid[idx] = cell;
                    }
                }
            }
        }

        // fluid P2G, first pass: mass and momentum (repo: Job_P2G_1)
        [BurstCompile] struct P2GFluid1 : IJob
        {
            public NativeArray<Cell> grid; [ReadOnly] public NativeArray<Particle> ps; public int n, resY;
            public void Execute()
            {
                for (int i = 0; i < n; i++)
                {
                    var p = ps[i]; uint2 ci = (uint2)p.x; float2 d = (p.x - ci) - 0.5f;
                    float2 w0 = 0.5f * math.pow(0.5f - d, 2), w1 = 0.75f - math.pow(d, 2), w2 = 0.5f * math.pow(0.5f + d, 2);
                    for (int gx = 0; gx < 3; gx++) for (int gy = 0; gy < 3; gy++)
                    {
                        float weight = W(gx, w0, w1, w2).x * W(gy, w0, w1, w2).y;
                        uint2 cx = math.uint2(ci.x + (uint)gx - 1, ci.y + (uint)gy - 1);
                        float2 dist = (cx - p.x) + 0.5f; float2 Q = math.mul(p.C, dist);
                        int idx = (int)cx.x * resY + (int)cx.y; var cell = grid[idx];
                        float mc = weight * p.mass; cell.mass += mc; cell.v += mc * (p.v + Q); grid[idx] = cell;
                    }
                }
            }
        }

        // fluid P2G, second pass: density from the grid, pressure from the equation of state, viscosity (repo: Job_P2G_2)
        [BurstCompile] struct P2GFluid2 : IJob
        {
            public NativeArray<Cell> grid; [ReadOnly] public NativeArray<Particle> ps; public int n, resY; public float dt;
            const float rest_density = 4f, dynamic_viscosity = 0.1f, eos_stiffness = 10f, eos_power = 4;
            public void Execute()
            {
                for (int i = 0; i < n; i++)
                {
                    var p = ps[i]; uint2 ci = (uint2)p.x; float2 d = (p.x - ci) - 0.5f;
                    float2 w0 = 0.5f * math.pow(0.5f - d, 2), w1 = 0.75f - math.pow(d, 2), w2 = 0.5f * math.pow(0.5f + d, 2);
                    float density = 0;
                    for (int gx = 0; gx < 3; gx++) for (int gy = 0; gy < 3; gy++)
                        density += grid[(int)(ci.x + (uint)gx - 1) * resY + (int)(ci.y + (uint)gy - 1)].mass * W(gx, w0, w1, w2).x * W(gy, w0, w1, w2).y;
                    float volume = p.mass / density;
                    float pressure = math.max(-0.1f, eos_stiffness * (math.pow(density / rest_density, eos_power) - 1));
                    float2x2 stress = math.float2x2(-pressure, 0, 0, -pressure);
                    float2x2 strain = p.C;
                    float trace = strain.c1.x + strain.c0.y;
                    strain.c0.y = strain.c1.x = trace;
                    stress += dynamic_viscosity * strain;
                    var eq_16_term_0 = -volume * 4 * stress * dt;
                    for (int gx = 0; gx < 3; gx++) for (int gy = 0; gy < 3; gy++)
                    {
                        float weight = W(gx, w0, w1, w2).x * W(gy, w0, w1, w2).y;
                        uint2 cx = math.uint2(ci.x + (uint)gx - 1, ci.y + (uint)gy - 1);
                        float2 dist = (cx - p.x) + 0.5f;
                        int idx = (int)cx.x * resY + (int)cx.y; var cell = grid[idx];
                        cell.v += math.mul(eq_16_term_0 * weight, dist); grid[idx] = cell;
                    }
                }
            }
        }

        [BurstCompile] struct UpdateGrid : IJobParallelFor
        {
            public NativeArray<Cell> grid; public int resX, resY; public float dt;
            public void Execute(int i)
            {
                var cell = grid[i];
                if (cell.mass > 0)
                {
                    cell.v /= cell.mass; cell.v += dt * math.float2(0, gravity);
                    int x = i / resY, y = i % resY;
                    if (x < 2 || x > resX - 3) cell.v.x = 0;
                    if (y < 2 || y > resY - 3) cell.v.y = 0;
                    grid[i] = cell;
                }
            }
        }

        [BurstCompile] struct G2P : IJobParallelFor
        {
            public NativeArray<Particle> ps; public NativeArray<float2x2> Fs; [ReadOnly] public NativeArray<Cell> grid;
            public int resX, resY; public float dt; public bool fluid;
            public void Execute(int i)
            {
                var p = ps[i]; p.v = 0;
                uint2 ci = (uint2)p.x; float2 d = (p.x - ci) - 0.5f;
                float2 w0 = 0.5f * math.pow(0.5f - d, 2), w1 = 0.75f - math.pow(d, 2), w2 = 0.5f * math.pow(0.5f + d, 2);
                float2x2 B = 0;
                for (int gx = 0; gx < 3; gx++) for (int gy = 0; gy < 3; gy++)
                {
                    float weight = W(gx, w0, w1, w2).x * W(gy, w0, w1, w2).y;
                    uint2 cx = math.uint2(ci.x + (uint)gx - 1, ci.y + (uint)gy - 1);
                    float2 dist = (cx - p.x) + 0.5f;
                    float2 wv = grid[(int)cx.x * resY + (int)cx.y].v * weight;
                    B += math.float2x2(wv * dist.x, wv * dist.y); p.v += wv;
                }
                p.C = B * 4;
                p.x += p.v * dt;
                p.x = math.clamp(p.x, 1, math.float2(resX - 2, resY - 2));
                if (fluid)
                {
                    // repo fluid scene: push velocities back from the walls before they reach them
                    float2 x_n = p.x + p.v; const float wall_min = 3; float2 wall_max = math.float2(resX - 4, resY - 4);
                    if (x_n.x < wall_min) p.v.x += wall_min - x_n.x;
                    if (x_n.x > wall_max.x) p.v.x += wall_max.x - x_n.x;
                    if (x_n.y < wall_min) p.v.y += wall_min - x_n.y;
                    if (x_n.y > wall_max.y) p.v.y += wall_max.y - x_n.y;
                }
                else Fs[i] = math.mul(float2x2.identity + dt * p.C, Fs[i]); // deformation gradient update (MPM course eq. 181)
                ps[i] = p;
            }
        }

        public void Dispose() { ps.Dispose(); Fs.Dispose(); grid.Dispose(); }
    }
}
