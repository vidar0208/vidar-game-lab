// Port of the source's FLIPSimulation (abecombe/FLIP-Fluid-for-Unity, Assets/Scripts/FLIPSimulation.cs, Apache-2.0).
// Same buffers, same compute passes in the same order (particle to grid, external force, viscosity, pressure projection,
// grid to particle with flipness, advection, density projection) and the same parameter defaults.
// Left out: HDRP / VFX Graph rendering, the RosettaUI panel, the FPS helpers and the mouse force (set to zero).
// Mine: a box domain and spawn block set from code, a fixed number of steps per video frame, the optional static box
// obstacle (see Obstacle.hlsl), Respawn(), a stats readback, and drawing one sphere per particle with
// DrawMeshInstancedProcedural on a chosen layer (URP).
using System;
using Abecombe.GPUUtil;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    public class FLIPFluid : MonoBehaviour
    {
        struct Particle { public uint ID; public float3 Position; public float3 Velocity; }

        // source parameters (FLIPSimulation fields; flipness default 0.99, the source scene uses 0.96)
        [Range(0f, 1f)] public float flipness = 0.96f;
        public Vector3 gravity = Vector3.down * 9.8f;
        public float viscosity = 0f;
        public bool quadraticKernel = false;           // source default: Linear
        public bool activeDensityProjection = true;
        public uint diffusionJacobiIteration = 15, pressureProjectionJacobiIteration = 15, densityProjectionJacobiIteration = 30;
        const int NumParticleInACell = 8;

        // mine
        public Vector3 domainSize = new Vector3(12f, 8f, 6f);
        public float gridSpacing = 0.2f;
        public Vector3 initMin = new Vector3(-6f, -4f, -3f), initMax = new Vector3(-2f, 1f, 3f);
        public bool obstacle; public Vector3 obstacleMin, obstacleMax;
        public float frameTime = 1f / 30f; public int stepsPerFrame = 2;
        public bool running = true;
        public int layer; public Mesh particleMesh; public Material material;
        public Vector3 origin; public float worldScale = 0.25f;

        float DeltaTime => frameTime / stepsPerFrame;
        public int3 GridSize => (int3)math.ceil((float3)domainSize / gridSpacing);
        public int NumGrids => GridSize.x * GridSize.y * GridSize.z;
        float3 SimulationSize => (float3)GridSize * gridSpacing;
        float3 GridMin => -SimulationSize / 2f;
        float3 GridMax => SimulationSize / 2f;
        float GridInvSpacing => 1f / gridSpacing;
        int3 InitGridMin => math.clamp((int3)math.round(((float3)initMin - GridMin) * GridInvSpacing - 0.5f), 0, GridSize - 1);
        int3 InitGridMax => math.clamp((int3)math.round(((float3)initMax - GridMin) * GridInvSpacing - 0.5f), InitGridMin, GridSize - 1);
        int3 InitGridSize => InitGridMax - InitGridMin + 1;
        public int NumParticles => InitGridSize.x * InitGridSize.y * InitGridSize.z * NumParticleInACell;
        public float ParticleRadius => gridSpacing * 0.4f;

        readonly GPUDoubleBuffer<Particle> _particleBuffer = new();
        readonly GPUBuffer<float4> _particleRenderingBuffer = new();
        readonly GPUBuffer<uint2> _gridParticleIDBuffer = new();
        readonly GPUDoubleBuffer<uint> _gridTypeBuffer = new();
        readonly GPUBuffer<float3> _gridVelocityBuffer = new();
        readonly GPUBuffer<float3> _gridOriginalVelocityBuffer = new();
        readonly GPUDoubleBuffer<float3> _gridDiffusionBuffer = new();
        readonly GPUDoubleBuffer<uint3> _gridAxisTypeBuffer = new();
        readonly GPUBuffer<float> _gridDivergenceBuffer = new();
        readonly GPUDoubleBuffer<float> _gridPressureBuffer = new();
        readonly GPUBuffer<float> _gridWeightBuffer = new();
        readonly GPUBuffer<uint> _gridUIntWeightBuffer = new();
        readonly GPUDoubleBuffer<float> _gridDensityPressureBuffer = new();
        readonly GPUBuffer<float3> _gridPositionModifyBuffer = new();
        readonly GPUBuffer<float> _gridFloatZeroBuffer = new();
        readonly GridSortHelper<Particle> _gridSortHelper = new();

        GPUComputeShader _particleInitCs, _particleToGridCs, _externalForceCs, _diffusionCs, _pressureProjectionCs,
            _gridToParticleCs, _particleAdvectionCs, _densityProjectionCs, _renderingCs;
        MaterialPropertyBlock _mpb;
        bool _inited;
        float4[] _readback;

        public long GpuBytes =>
            (long)NumParticles * (2 * 28 + 16) +
            (long)NumGrids * (8 + 2 * 4 + 12 + 12 + 2 * 12 + 2 * 12 + 4 + 2 * 4 + 4 + 4 + 2 * 4 + 12 + 4);

        void InitComputeShaders()
        {
            _particleInitCs = new GPUComputeShader("ParticleInitCS");
            _particleToGridCs = new GPUComputeShader("ParticleToGridCS");
            _externalForceCs = new GPUComputeShader("ExternalForceCS");
            _diffusionCs = new GPUComputeShader("DiffusionCS");
            _pressureProjectionCs = new GPUComputeShader("PressureProjectionCS");
            _gridToParticleCs = new GPUComputeShader("GridToParticleCS");
            _particleAdvectionCs = new GPUComputeShader("ParticleAdvectionCS");
            _densityProjectionCs = new GPUComputeShader("DensityProjectionCS");
            _renderingCs = new GPUComputeShader("RenderingCS");
        }

        public void Respawn()
        {
            if (_particleInitCs == null) InitComputeShaders();
            int n = NumParticles, g = NumGrids;
            _particleBuffer.Init(n);
            _particleRenderingBuffer.Init(n);
            _gridParticleIDBuffer.Init(g); _gridTypeBuffer.Init(g); _gridVelocityBuffer.Init(g); _gridOriginalVelocityBuffer.Init(g);
            _gridDiffusionBuffer.Init(g); _gridAxisTypeBuffer.Init(g); _gridDivergenceBuffer.Init(g); _gridPressureBuffer.Init(g);
            _gridWeightBuffer.Init(g); _gridUIntWeightBuffer.Init(g); _gridDensityPressureBuffer.Init(g);
            _gridPositionModifyBuffer.Init(g); _gridFloatZeroBuffer.Init(g);

            var cs = _particleInitCs;
            var k = cs.FindKernel("InitParticle");
            SetConstants(cs);
            cs.SetInts("_ParticleInitGridMin", InitGridMin);
            cs.SetInts("_ParticleInitGridMax", InitGridMax);
            cs.SetInts("_ParticleInitGridSize", InitGridSize);
            k.SetBuffer("_ParticleBufferWrite", _particleBuffer.Read);
            k.Dispatch(n);
            PrepareRendering();

            _mpb ??= new MaterialPropertyBlock();
            _readback = new float4[n];
            _inited = true;
        }

        void SetConstants(GPUComputeShader cs)
        {
            cs.SetFloat("_DeltaTime", DeltaTime);
            cs.SetVector("_GridMin", GridMin);
            cs.SetVector("_GridMax", GridMax);
            cs.SetInts("_GridSize", GridSize);
            cs.SetFloat("_GridSpacing", gridSpacing);
            cs.SetFloat("_GridInvSpacing", GridInvSpacing);
            if (quadraticKernel) { cs.DisableKeyword("USE_LINEAR_KERNEL"); cs.EnableKeyword("USE_QUADRATIC_KERNEL"); }
            else { cs.EnableKeyword("USE_LINEAR_KERNEL"); cs.DisableKeyword("USE_QUADRATIC_KERNEL"); }
            cs.EnableKeyword("IS_CUBE_AREA_SIMULATION"); cs.DisableKeyword("IS_SPHERE_AREA_SIMULATION");
            // mine: the box obstacle
            cs.SetVector("_GTBoxMin", new float4((float3)obstacleMin, obstacle ? 1f : 0f));
            cs.SetVector("_GTBoxMax", new float4((float3)obstacleMax, 0f));
        }

        void DispatchParticleToGrid()
        {
            _gridSortHelper.Sort(_particleBuffer, _gridParticleIDBuffer, GridMin, GridMax, GridSize, gridSpacing);
            var cs = _particleToGridCs;
            SetConstants(cs);
            var k = cs.FindKernel("SetMyGridType");
            k.SetBuffer("_GridParticleIDBufferRead", _gridParticleIDBuffer);
            k.SetBuffer("_GridTypeBufferWrite", _gridTypeBuffer.Read);
            k.Dispatch(NumGrids);
            k = cs.FindKernel("SetNeighborGridTypes");
            k.SetBuffer("_GridTypeBufferRead", _gridTypeBuffer.Read);
            k.SetBuffer("_GridTypeBufferWrite", _gridTypeBuffer.Write);
            k.Dispatch(NumGrids);
            _gridTypeBuffer.Swap();
            k = cs.FindKernel("ParticleToGrid");
            k.SetBuffer("_ParticleBufferRead", _particleBuffer.Read);
            k.SetBuffer("_GridParticleIDBufferRead", _gridParticleIDBuffer);
            k.SetBuffer("_GridVelocityBufferWrite", _gridVelocityBuffer);
            k.SetBuffer("_GridOriginalVelocityBufferWrite", _gridOriginalVelocityBuffer);
            k.SetBuffer("_GridTypeBufferRead", _gridTypeBuffer.Read);
            k.Dispatch(NumGrids);
        }

        void DispatchExternalForce()
        {
            var cs = _externalForceCs;
            var k = cs.FindKernel("AddExternalForce");
            SetConstants(cs);
            k.SetBuffer("_GridVelocityBufferRW", _gridVelocityBuffer);
            k.SetBuffer("_GridTypeBufferRead", _gridTypeBuffer.Read);
            cs.SetVector("_Gravity", (float3)gravity);
            // no mouse: zero force
            cs.SetVector("_RayOrigin", new float3(0f, 1000f, 0f));
            cs.SetVector("_RayDirection", new float3(0f, 1f, 0f));
            cs.SetVector("_MouseForceParameter", new float4(0f, 0f, 0f, 1f));
            k.Dispatch(NumGrids);
        }

        void DispatchDiffusion()
        {
            if (viscosity <= 0f) return;
            var cs = _diffusionCs;
            SetConstants(cs);
            var k = cs.FindKernel("SetMyGridAxisType");
            k.SetBuffer("_GridTypeBufferRead", _gridTypeBuffer.Read);
            k.SetBuffer("_GridAxisTypeBufferWrite", _gridAxisTypeBuffer.Read);
            k.Dispatch(NumGrids);
            k = cs.FindKernel("SetNeighborGridAxisTypes");
            k.SetBuffer("_GridAxisTypeBufferRead", _gridAxisTypeBuffer.Read);
            k.SetBuffer("_GridAxisTypeBufferWrite", _gridAxisTypeBuffer.Write);
            k.Dispatch(NumGrids);
            _gridAxisTypeBuffer.Swap();
            k = cs.FindKernel("Diffuse");
            float temp1 = viscosity * DeltaTime;
            float3 temp2 = 1f / gridSpacing / gridSpacing;
            float temp3 = 1f / (1f + 2f * (temp2.x + temp2.y + temp2.z) * temp1);
            cs.SetVector("_DiffusionParameter", new float4(temp1 * temp2 * temp3, temp3));
            k.SetBuffer("_GridTypeBufferRead", _gridTypeBuffer.Read);
            k.SetBuffer("_GridAxisTypeBufferRead", _gridAxisTypeBuffer.Read);
            k.SetBuffer("_GridVelocityBufferRead", _gridVelocityBuffer);
            for (uint i = 0; i < diffusionJacobiIteration; i++)
            {
                k.SetBuffer("_GridDiffusionBufferRead", _gridDiffusionBuffer.Read);
                k.SetBuffer("_GridDiffusionBufferWrite", _gridDiffusionBuffer.Write);
                k.Dispatch(NumGrids);
                _gridDiffusionBuffer.Swap();
            }
            k = cs.FindKernel("UpdateVelocity");
            k.SetBuffer("_GridVelocityBufferWrite", _gridVelocityBuffer);
            k.SetBuffer("_GridDiffusionBufferRead", _gridDiffusionBuffer.Read);
            k.SetBuffer("_GridTypeBufferRead", _gridTypeBuffer.Read);
            k.Dispatch(NumGrids);
        }

        void DispatchPressureProjection()
        {
            var cs = _pressureProjectionCs;
            SetConstants(cs);
            var k = cs.FindKernel("CalcDivergence");
            cs.SetVector("_DivergenceParameter", (float3)(1f / gridSpacing));
            k.SetBuffer("_GridTypeBufferRead", _gridTypeBuffer.Read);
            k.SetBuffer("_GridVelocityBufferRead", _gridVelocityBuffer);
            k.SetBuffer("_GridDivergenceBufferWrite", _gridDivergenceBuffer);
            k.Dispatch(NumGrids);
            k = cs.FindKernel("Project");
            float3 temp1 = 1f / gridSpacing / gridSpacing;
            float temp2 = 1f / (2f * (temp1.x + temp1.y + temp1.z));
            cs.SetVector("_PressureProjectionParameter1", new float4((float3)temp2 / gridSpacing / gridSpacing, -temp2));
            k.SetBuffer("_GridTypeBufferRead", _gridTypeBuffer.Read);
            k.SetBuffer("_GridDivergenceBufferRead", _gridDivergenceBuffer);
            for (uint i = 0; i < pressureProjectionJacobiIteration; i++)
            {
                k.SetBuffer("_GridPressureBufferRead", _gridPressureBuffer.Read);
                k.SetBuffer("_GridPressureBufferWrite", _gridPressureBuffer.Write);
                k.Dispatch(NumGrids);
                _gridPressureBuffer.Swap();
            }
            k = cs.FindKernel("UpdateVelocity");
            cs.SetVector("_PressureProjectionParameter2", (float3)(1f / gridSpacing));
            k.SetBuffer("_GridVelocityBufferRW", _gridVelocityBuffer);
            k.SetBuffer("_GridPressureBufferRead", _gridPressureBuffer.Read);
            k.SetBuffer("_GridTypeBufferRead", _gridTypeBuffer.Read);
            k.Dispatch(NumGrids);
        }

        void DispatchGridToParticle()
        {
            var cs = _gridToParticleCs;
            var k = cs.FindKernel("GridToParticle");
            SetConstants(cs);
            cs.SetFloat("_Flipness", math.saturate(flipness));
            k.SetBuffer("_ParticleBufferRW", _particleBuffer.Read);
            k.SetBuffer("_GridVelocityBufferRead", _gridVelocityBuffer);
            k.SetBuffer("_GridOriginalVelocityBufferRead", _gridOriginalVelocityBuffer);
            k.Dispatch(NumParticles);
        }

        void DispatchAdvection()
        {
            var cs = _particleAdvectionCs;
            var k = cs.FindKernel("Advect");
            SetConstants(cs);
            k.SetBuffer("_ParticleBufferRW", _particleBuffer.Read);
            k.SetBuffer("_GridVelocityBufferRead", _gridVelocityBuffer);
            cs.EnableKeyword("USE_RK1"); cs.DisableKeyword("USE_RK2"); cs.DisableKeyword("USE_RK3");   // source default: forward Euler
            k.Dispatch(NumParticles);
        }

        void DispatchDensityProjection()
        {
            var cs = _densityProjectionCs;
            SetConstants(cs);
            cs.SetFloat("_InvAverageWeight", 1f / NumParticleInACell);
            var k = cs.FindKernel("InitBuffer");
            k.SetBuffer("_GridTypeBufferRW", _gridTypeBuffer.Read);
            k.SetBuffer("_GridUIntWeightBufferWrite", _gridUIntWeightBuffer);
            k.Dispatch(NumGrids);
            k = cs.FindKernel("InterlockedAddWeight");
            k.SetBuffer("_ParticleBufferRead", _particleBuffer.Read);
            k.SetBuffer("_GridTypeBufferRW", _gridTypeBuffer.Read);
            k.SetBuffer("_GridUIntWeightBufferWrite", _gridUIntWeightBuffer);
            k.Dispatch(NumParticles);
            k = cs.FindKernel("SetNeighborGridTypes");
            k.SetBuffer("_GridTypeBufferRead", _gridTypeBuffer.Read);
            k.SetBuffer("_GridTypeBufferWrite", _gridTypeBuffer.Write);
            k.Dispatch(NumGrids);
            _gridTypeBuffer.Swap();
            k = cs.FindKernel("CalcGridWeight");
            k.SetBuffer("_GridTypeBufferRead", _gridTypeBuffer.Read);
            k.SetBuffer("_GridUIntWeightBufferRead", _gridUIntWeightBuffer);
            k.SetBuffer("_GridWeightBufferWrite", _gridWeightBuffer);
            cs.SetVector("_GhostWeight", new float3(36f / 64f, 6f / 64f, 1f / 64f));
            k.Dispatch(NumGrids);
            k = cs.FindKernel("Project");
            float3 temp1 = 1f / gridSpacing / gridSpacing;
            float temp2 = 1f / (2f * (temp1.x + temp1.y + temp1.z));
            cs.SetVector("_DensityProjectionParameter1", new float4((float3)temp2 / gridSpacing / gridSpacing, -temp2));
            k.SetBuffer("_GridTypeBufferRead", _gridTypeBuffer.Read);
            k.SetBuffer("_GridWeightBufferRead", _gridWeightBuffer);
            for (uint i = 0; i < densityProjectionJacobiIteration; i++)
            {
                k.SetBuffer("_GridDensityPressureBufferRead", i == 0 ? _gridFloatZeroBuffer : _gridDensityPressureBuffer.Read);
                k.SetBuffer("_GridDensityPressureBufferWrite", _gridDensityPressureBuffer.Write);
                k.Dispatch(NumGrids);
                _gridDensityPressureBuffer.Swap();
            }
            k = cs.FindKernel("CalcPositionModify");
            cs.SetVector("_DensityProjectionParameter2", (float3)(1f / gridSpacing));
            k.SetBuffer("_GridTypeBufferRead", _gridTypeBuffer.Read);
            k.SetBuffer("_GridDensityPressureBufferRead", _gridDensityPressureBuffer.Read);
            k.SetBuffer("_GridPositionModifyBufferWrite", _gridPositionModifyBuffer);
            k.Dispatch(NumGrids);
            k = cs.FindKernel("UpdatePosition");
            k.SetBuffer("_ParticleBufferRW", _particleBuffer.Read);
            k.SetBuffer("_GridPositionModifyBufferRead", _gridPositionModifyBuffer);
            k.Dispatch(NumParticles);
        }

        void PrepareRendering()
        {
            var k = _renderingCs.FindKernel("PrepareRendering");
            k.SetBuffer("_ParticleBufferRead", _particleBuffer.Read);
            k.SetBuffer("_ParticleRenderingBufferWrite", _particleRenderingBuffer);
            k.Dispatch(NumParticles);
        }

        // one video frame: stepsPerFrame source steps
        public void Step()
        {
            for (int i = 0; i < stepsPerFrame; i++)
            {
                DispatchParticleToGrid();
                DispatchExternalForce();
                DispatchDiffusion();
                DispatchPressureProjection();
                DispatchGridToParticle();
                DispatchAdvection();
                if (activeDensityProjection) DispatchDensityProjection();
            }
            PrepareRendering();
        }

        // mine: stats for the log (positions in simulation units)
        public string Stats()
        {
            _particleRenderingBuffer.Data.GetData(_readback);
            float maxV = 0, sumV = 0, sumV2 = 0, maxY = float.MinValue, sumY = 0; int fast = 0, inBox = 0, above = 0;
            float ymin = GridMin.y;
            foreach (var p in _readback)
            {
                float v = p.w; maxV = Mathf.Max(maxV, v); sumV += v; sumV2 += v * v; if (v > 4f) fast++;
                maxY = Mathf.Max(maxY, p.y); sumY += p.y;
                if (obstacle && p.x > obstacleMin.x && p.x < obstacleMax.x && p.y > obstacleMin.y && p.y < obstacleMax.y && p.z > obstacleMin.z && p.z < obstacleMax.z) inBox++;
                if (p.y - ymin > 3f) above++;
            }
            int n = _readback.Length;
            return $"mean speed {sumV / n:F3} rms {Mathf.Sqrt(sumV2 / n):F3} max {maxV:F2} | over 4 u/s {fast} | max height {maxY - ymin:F2} mean height {sumY / n - ymin:F3} | above 3 u {above} | inside obstacle {inBox}";
        }

        void Update()
        {
            if (!_inited) Respawn();
            if (running) Step();
            _mpb.SetBuffer("_ParticleRenderingBuffer", _particleRenderingBuffer.Data);
            _mpb.SetVector("_Origin", origin);
            _mpb.SetFloat("_Scale", worldScale);
            _mpb.SetFloat("_Size", 2f * ParticleRadius * worldScale);
            var bounds = new Bounds(origin, (Vector3)(float3)SimulationSize * worldScale * 1.2f);
            Graphics.DrawMeshInstancedProcedural(particleMesh, 0, material, bounds, NumParticles, _mpb, ShadowCastingMode.Off, false, layer);
        }

        void OnDisable() => Dispose();

        public void Dispose()
        {
            _particleBuffer.Dispose(); _particleRenderingBuffer.Dispose();
            _gridParticleIDBuffer.Dispose(); _gridTypeBuffer.Dispose(); _gridVelocityBuffer.Dispose(); _gridOriginalVelocityBuffer.Dispose();
            _gridDiffusionBuffer.Dispose(); _gridAxisTypeBuffer.Dispose(); _gridDivergenceBuffer.Dispose(); _gridPressureBuffer.Dispose();
            _gridWeightBuffer.Dispose(); _gridUIntWeightBuffer.Dispose(); _gridDensityPressureBuffer.Dispose();
            _gridPositionModifyBuffer.Dispose(); _gridFloatZeroBuffer.Dispose(); _gridSortHelper.Dispose();
            _inited = false;
        }
    }
}
