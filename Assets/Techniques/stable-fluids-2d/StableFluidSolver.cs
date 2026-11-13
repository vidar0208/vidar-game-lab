// A 2D stable-fluids solver that dispatches the compute kernels of IRCSS/Compute-Shaders-Fluid-Dynamic- (MIT, Copyright 2020
// Shahriar Shahrabi). The method names, kernel names, buffer names and the step order follow the source's FluidSimulater and
// Simple2DFluidSimArbitaryBoundary. The source records everything into a command buffer attached to the camera with
// CameraEvent.AfterEverything, which URP does not run, and reads the mouse; here the same kernels are dispatched directly
// once per captured frame, the force and dye sources are scripted, and every instance owns its own scratch buffers.
using UnityEngine;

namespace GameTech.Posts
{
    public class StableFluidSolver
    {
        public enum FieldType { Velocity, Pressure, Dye }

        public readonly int N;                         // simulation_dimension
        public int solverIterations = 80;              // solver_iteration_num (source default 80)
        public float gridScale = 1f, timeStep = 1f;    // grid_scale, time_step (source defaults)
        public float viscosity = 0.5f;                 // Viscosity (source default 0.5)
        public float velocityDissipation = 0.999f;     // velocity_dissapation (source default)
        public float dyeDissipation = 0.997f;          // the arbitrary-boundary example's dye factor

        public ComputeBuffer velocity, dye, divergence, pressure, divergenceCheck;
        ComputeBuffer ping, pong, offVel, offPre, offDye, lutVel, lutPre, lutDye;
        readonly ComputeShader ns, solver, border, util, input;
        readonly int kAdvect, kDiv, kDivFree, kJacobi, kCopy, kClear, kNeumann, kUpdOff, kArbVel, kArbPre, kArbDye, kForce, kDye;
        bool arbitrary;

        public StableFluidSolver(int n, ComputeShader navierStokes, ComputeShader solvers, ComputeShader boundary, ComputeShader utility, ComputeShader userInput)
        {
            N = n; ns = navierStokes; solver = solvers; border = boundary; util = utility; input = userInput;
            kAdvect = ns.FindKernel("advection"); kDiv = ns.FindKernel("divergence"); kDivFree = ns.FindKernel("calculate_divergence_free");
            kJacobi = solver.FindKernel("Jacobi_Solve");
            kCopy = util.FindKernel("Copy_StructuredBuffer"); kClear = util.FindKernel("Clear_StructuredBuffer");
            kNeumann = border.FindKernel("NeuMannBoundary"); kUpdOff = border.FindKernel("UpdateArbitaryBoundaryOffset");
            kArbVel = border.FindKernel("ArbitaryBoundaryVelocity"); kArbPre = border.FindKernel("ArbitaryBoundaryPressure"); kArbDye = border.FindKernel("ArbitaryBoundaryDye");
            kForce = input.FindKernel("Add_constant_force_at"); kDye = input.FindKernel("Add_constant_dye");
            int cells = n * n;
            ComputeBuffer F4() => new ComputeBuffer(cells, 16);
            velocity = F4(); dye = F4(); divergence = F4(); pressure = F4(); divergenceCheck = F4(); ping = F4(); pong = F4();
            offVel = F4(); offPre = F4(); offDye = F4();
            lutVel = new ComputeBuffer(32, 16); lutPre = new ComputeBuffer(32, 16); lutDye = new ComputeBuffer(32, 16);
            lutVel.SetData(BoundaryLUT.Velocity); lutPre.SetData(BoundaryLUT.Pressure); lutDye.SetData(BoundaryLUT.Dye);
            foreach (var b in new[] { velocity, dye, divergence, pressure, divergenceCheck, ping, pong }) ClearBuffer(b);
        }

        public void Release()
        {
            foreach (var b in new[] { velocity, dye, divergence, pressure, divergenceCheck, ping, pong, offVel, offPre, offDye, lutVel, lutPre, lutDye }) b?.Release();
        }

        void Globals(ComputeShader cs)
        {
            cs.SetInt("i_Resolution", N); cs.SetFloat("i_timeStep", timeStep); cs.SetFloat("i_grid_scale", gridScale);
        }

        void Dispatch2D(ComputeShader cs, int k) { Globals(cs); cs.Dispatch(k, (N + 15) / 16, (N + 15) / 16, 1); }
        void Dispatch1D(ComputeShader cs, int k, int threads) { Globals(cs); cs.Dispatch(k, (threads + 255) / 256, 1, 1); }

        void Copy(ComputeBuffer src, ComputeBuffer dst)
        {
            util.SetBuffer(kCopy, "_Copy_Source", src); util.SetBuffer(kCopy, "_Copy_Target", dst);
            Dispatch1D(util, kCopy, N * N);
        }

        public void ClearBuffer(ComputeBuffer b)
        {
            util.SetVector("_Clear_Value_StructuredBuffer", Vector4.zero); util.SetBuffer(kClear, "_Clear_Target_StructuredBuffer", b);
            Dispatch1D(util, kClear, N * N);
        }

        // ---- user input (scripted instead of the mouse)
        public void AddConstantForceSource(Vector2 position, Vector2 force, float radius, float falloff)
        {
            input.SetBuffer(kForce, "_user_applied_force_buffer", velocity);
            input.SetVector("_constant_force_source_direction", force); input.SetVector("_constant_force_source_position", position);
            input.SetFloat("_constant_force_radius", radius); input.SetFloat("_constant_force_falloff", falloff);
            Dispatch2D(input, kForce);
        }

        public void AddConstantDyeSource(Vector2 position, Color colour, float radius, float falloff)
        {
            input.SetBuffer(kDye, "_dye_buffer", dye);
            input.SetVector("_dye_color", colour); input.SetVector("_constant_dye_source_position", position);
            input.SetFloat("_constant_dye_radius", radius); input.SetFloat("_constant_dye_falloff", falloff);
            Dispatch2D(input, kDye);
        }

        // ---- boundaries
        public void HandleCornerBoundaries(ComputeBuffer field, FieldType type)
        {
            float scale = type == FieldType.Velocity ? -1f : type == FieldType.Pressure ? 1f : 0f;
            border.SetFloat("_neumaboundary_scale", scale); border.SetBuffer(kNeumann, "_neumaboundary_field_to_contain", field);
            Dispatch1D(border, kNeumann, N * 4);
        }

        public void UpdateArbitaryBoundaryOffsets(Texture2D boundaryTexture)
        {
            arbitrary = true;
            border.SetTexture(kUpdOff, "_arbitary_boundaries_texture", boundaryTexture);
            border.SetFloat("_arbitary_boundaries_texel_size", 1f / boundaryTexture.width);
            foreach (var (lut, off) in new[] { (lutVel, offVel), (lutPre, offPre), (lutDye, offDye) })
            {
                border.SetBuffer(kUpdOff, "_arbitaryBoundaryCardinalDirectionsLUT", lut);
                border.SetBuffer(kUpdOff, "_perCellArbitaryBoundryOffsets", off);
                Dispatch2D(border, kUpdOff);
            }
        }

        public void HandleArbitaryBoundary(ComputeBuffer field, FieldType type)
        {
            if (!arbitrary) return;
            int k; string inName, offName, outName; ComputeBuffer off;
            if (type == FieldType.Velocity) { k = kArbVel; inName = "_velocity_buffer"; offName = "_perCellArbitaryBoundryOffsetsVellocity"; outName = "_new_handleded_velocity"; off = offVel; }
            else if (type == FieldType.Pressure) { k = kArbPre; inName = "_pressure_buffer"; offName = "_perCellArbitaryBoundryOffsetsPressure"; outName = "_new_handleded_pressure"; off = offPre; }
            else { k = kArbDye; inName = "_dye_buffer"; offName = "_perCellArbitaryBoundryOffsetsDye"; outName = "_new_handleded_dye"; off = offDye; }
            border.SetBuffer(k, inName, field); border.SetBuffer(k, offName, off); border.SetBuffer(k, outName, ping);
            Dispatch2D(border, k);
            Copy(ping, field);
            ClearBuffer(ping);
        }

        // ---- implicit diffusion with Jacobi iterations: (1 + 4a) x - a * (sum of 4 neighbours) = b, a = viscosity * dt
        // Change: the source binds the latest iterate as both _b_buffer and _updated_x_buffer, so every iteration is another
        // diffusion step and the blur grows with the iteration count. Here b stays the field from before the solve.
        public void Diffuse(ComputeBuffer field)
        {
            if (viscosity <= 0) return;
            float centerFactor = 1f / (viscosity * timeStep);
            float rDiagonal = (viscosity * timeStep) / (1f + 4f * (viscosity * timeStep));
            solver.SetFloat("_centerFactor", centerFactor); solver.SetFloat("_rDiagonal", rDiagonal);
            ComputeBuffer x = field;
            for (int i = 0; i < solverIterations; i++)
            {
                var r = (i % 2 == 0) ? ping : pong;
                solver.SetBuffer(kJacobi, "_b_buffer", field); solver.SetBuffer(kJacobi, "_updated_x_buffer", x); solver.SetBuffer(kJacobi, "_results", r);
                solver.SetInt("_current_iteration", i);
                Dispatch2D(solver, kJacobi);
                x = r;
            }
            if (x != field) Copy(x, field);
            ClearBuffer(ping); ClearBuffer(pong);
        }

        // ---- semi-Lagrangian advection
        public void Advect(ComputeBuffer field, ComputeBuffer vel, float dissipation)
        {
            ns.SetFloat("_dissipationFactor", dissipation);
            ns.SetBuffer(kAdvect, "_velocity_field_buffer", vel); ns.SetBuffer(kAdvect, "_field_to_advect_buffer", field); ns.SetBuffer(kAdvect, "_new_advected_field", ping);
            Dispatch2D(ns, kAdvect);
            Copy(ping, field);
            ClearBuffer(ping);
        }

        public void CalculateFieldDivergence(ComputeBuffer field, ComputeBuffer result)
        {
            ns.SetBuffer(kDiv, "_divergence_vector_field", field); ns.SetBuffer(kDiv, "_divergence_values", result);
            Dispatch2D(ns, kDiv);
        }

        // ---- pressure projection: solve laplace(p) = div(v) with Jacobi, then v -= grad(p)
        public void Project(ComputeBuffer vel)
        {
            CalculateFieldDivergence(vel, divergence);
            solver.SetFloat("_centerFactor", -1f * gridScale * gridScale); solver.SetFloat("_rDiagonal", 0.25f);
            ClearBuffer(pressure);
            bool pingAsResults = false;
            for (int i = 0; i < solverIterations; i++)
            {
                pingAsResults = !pingAsResults;
                var x = pingAsResults ? pressure : ping; var r = pingAsResults ? ping : pressure;
                HandleCornerBoundaries(x, FieldType.Pressure);
                solver.SetBuffer(kJacobi, "_b_buffer", divergence); solver.SetBuffer(kJacobi, "_updated_x_buffer", x); solver.SetBuffer(kJacobi, "_results", r);
                solver.SetInt("_current_iteration", i);
                Dispatch2D(solver, kJacobi);
            }
            if (pingAsResults) Copy(ping, pressure);
            ClearBuffer(ping);
            HandleCornerBoundaries(pressure, FieldType.Pressure);
            HandleArbitaryBoundary(pressure, FieldType.Pressure);
            ns.SetBuffer(kDivFree, "_non_zero_divergence_velocity_field", vel); ns.SetBuffer(kDivFree, "_pressure_field", pressure);
            ns.SetBuffer(kDivFree, "_pressure_gradient", pong); ns.SetBuffer(kDivFree, "_divergence_free_field", ping);
            Dispatch2D(ns, kDivFree);
            Copy(ping, vel);
            ClearBuffer(ping); ClearBuffer(pong);
        }

        // Mean |divergence| of the current velocity over fluid cells (read back to the CPU, for the labels and notes).
        public float MeanAbsDivergence(bool[] solid)
        {
            CalculateFieldDivergence(velocity, divergenceCheck);
            var data = new Vector4[N * N]; divergenceCheck.GetData(data);
            double sum = 0; int count = 0;
            for (int y = 2; y < N - 2; y++) for (int x = 2; x < N - 2; x++)
            {
                int i = y * N + x; if (solid != null && solid[i]) continue;
                sum += Mathf.Abs(data[i].x); count++;
            }
            return count > 0 ? (float)(sum / count) : 0f;
        }
    }

    // The three look-up tables of the source's ArbitaryBoundaryLUTGenerator, copied as data: index = center*16 | south*8 | west*4 | north*2 | east.
    public static class BoundaryLUT
    {
        static Vector4Int[] T(params int[] v) { var r = new Vector4Int[v.Length / 4]; for (int i = 0; i < r.Length; i++) r[i] = new Vector4Int(v[i * 4], v[i * 4 + 1], v[i * 4 + 2], v[i * 4 + 3]); return r; }
        public struct Vector4Int { public int x, y, z, w; public Vector4Int(int a, int b, int c, int d) { x = a; y = b; z = c; w = d; } }

        public static readonly Vector4Int[] Velocity = T(
            0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1,
            0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 0, 0,
            0, 0, -1, -1, -1, 0, -1, -1, 0, -1, -1, -1, -1, -1, -1, -1, 1, 0, -1, -1, 0, -1, -1, -1, 1, -1, -1, -1, 0, -1, -1, -1,
            0, 1, -1, -1, -1, 1, -1, -1, -1, 0, -1, -1, -1, 0, -1, -1, 1, 1, -1, -1, 0, 1, -1, -1, 1, 0, -1, -1, 0, 0, 0, 0);

        public static readonly Vector4Int[] Pressure = T(
            0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1,
            0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 0, 0,
            0, 0, 1, 1, -1, 0, -1, 0, 0, -1, 0, -1, -1, 0, 0, -1, 1, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, -1, 0, -1, 0, -1,
            0, 1, 0, 1, -1, 0, 0, 1, 0, 0, 0, 0, -1, 0, -1, 0, 1, 0, 0, 1, 0, 1, 0, 1, 1, 0, 1, 0, 0, 0, 0, 0);

        public static readonly Vector4Int[] Dye = T(
            0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1,
            0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    }
}
