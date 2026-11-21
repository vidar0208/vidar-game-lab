// Port of Gornhoth's ComputeShaderParticleManager (MIT, licence in LICENSE.txt next to this file): same parameters,
// same particle spawn (a cube of particles 0.5 apart in the top corner, moving down at 50), same buffers and the same six
// kernels in the same order. Changes: the grid cell is 2 x radius instead of 4 x radius (the smallest size the
// 8-cell neighbour search allows) so far fewer particles share a cell; several solver steps run per frame; each
// instance gets its own copy of the compute shader so several simulations can run side by side; Respawn() can restart
// it; rendering is a URP shader that reads the particle and velocity buffers; a sphOn switch (in the compute shader)
// turns the pressure and viscosity forces off for the comparison; a few read-back counters for the on-screen numbers.
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

namespace GameTech.Posts
{
    public class SPHFluid : MonoBehaviour
    {
        [Header("Particle properties")]
        public float radius = 1f;
        public float mass = 1f;
        public float gasConstant = 2000.0f;
        public float restDensity = 2f;
        public float viscosityCoefficient = 0.25f;
        public Vector3 g = new Vector3(0, -9.81f, 0);
        public float damping = -0.5f;
        public float dt = 0.0008f;

        [Header("Simulation space properties")]
        public int numberOfParticles = 24000;
        public int dimensions = 30;
        public int maximumParticlesPerCell = 128;

        [Header("GameTech")]
        public ComputeShader computeShader;
        public bool sphForces = true;
        public int stepsPerFrame = 24;
        public bool running;
        public int seed = 1;
        public float startSpeed = 50f;   // source: every particle starts moving down at 50
        public Mesh particleMesh;
        public Material material;
        public int layer;
        public Vector3 origin;
        public float worldScale = 0.1f;
        public float particleSize = 0.055f;

        public int ElapsedSimulationSteps { get; private set; }
        public float CellSize => radius * 2;

        [StructLayout(LayoutKind.Sequential, Size = 28)]
        struct Particle { public Vector3 Position; public Vector4 Color; }

        ComputeShader cs;
        int clearHashGridKernel, recalculateHashGridKernel, buildNeighbourListKernel, computeDensityPressureKernel, computeForcesKernel, integrateKernel;
        ComputeBuffer particlesBuffer, neighbourListBuffer, neighbourTrackerBuffer, hashGridBuffer, hashGridTrackerBuffer, densitiesBuffer, pressuresBuffer, velocitiesBuffer, forcesBuffer;
        MaterialPropertyBlock mpb;
        bool ready;

        public long GpuBytes => (long)numberOfParticles * (28 + 4 + 4 + 4 + 12 + 12 + 4L * maximumParticlesPerCell * 8) + (long)dimensions * dimensions * dimensions * 4 * (maximumParticlesPerCell + 1);

        void Awake() { Init(); }

        void Init()
        {
            if (ready) return;
            cs = Instantiate(computeShader);
            clearHashGridKernel = cs.FindKernel("ClearHashGrid");
            recalculateHashGridKernel = cs.FindKernel("RecalculateHashGrid");
            buildNeighbourListKernel = cs.FindKernel("BuildNeighbourList");
            computeDensityPressureKernel = cs.FindKernel("ComputeDensityPressure");
            computeForcesKernel = cs.FindKernel("ComputeForces");
            integrateKernel = cs.FindKernel("Integrate");

            float radius2 = radius * radius, radius3 = radius2 * radius, radius4 = radius3 * radius, radius5 = radius4 * radius;
            cs.SetFloat("CellSize", CellSize);
            cs.SetInt("Dimensions", dimensions);
            cs.SetInt("maximumParticlesPerCell", maximumParticlesPerCell);
            cs.SetFloat("radius", radius); cs.SetFloat("radius2", radius2); cs.SetFloat("radius3", radius3);
            cs.SetFloat("radius4", radius4); cs.SetFloat("radius5", radius5);
            cs.SetFloat("mass", mass); cs.SetFloat("mass2", mass * mass);
            cs.SetFloat("gasConstant", gasConstant); cs.SetFloat("restDensity", restDensity);
            cs.SetFloat("viscosityCoefficient", viscosityCoefficient);
            cs.SetFloat("damping", damping); cs.SetFloat("dt", dt);
            cs.SetVector("g", g);
            cs.SetFloat("epsilon", Mathf.Epsilon); cs.SetFloat("pi", Mathf.PI);
            cs.SetInt("sphOn", sphForces ? 1 : 0);

            int n = numberOfParticles, cells = dimensions * dimensions * dimensions;
            particlesBuffer = new ComputeBuffer(n, 28);
            neighbourListBuffer = new ComputeBuffer(n * maximumParticlesPerCell * 8, sizeof(int));
            neighbourTrackerBuffer = new ComputeBuffer(n, sizeof(int));
            hashGridBuffer = new ComputeBuffer(cells * maximumParticlesPerCell, sizeof(uint));
            hashGridTrackerBuffer = new ComputeBuffer(cells, sizeof(uint));
            densitiesBuffer = new ComputeBuffer(n, sizeof(float));
            pressuresBuffer = new ComputeBuffer(n, sizeof(float));
            velocitiesBuffer = new ComputeBuffer(n, sizeof(float) * 3);
            forcesBuffer = new ComputeBuffer(n, sizeof(float) * 3);

            cs.SetBuffer(clearHashGridKernel, "_hashGridTracker", hashGridTrackerBuffer);
            cs.SetBuffer(recalculateHashGridKernel, "_particles", particlesBuffer);
            cs.SetBuffer(recalculateHashGridKernel, "_hashGrid", hashGridBuffer);
            cs.SetBuffer(recalculateHashGridKernel, "_hashGridTracker", hashGridTrackerBuffer);
            cs.SetBuffer(buildNeighbourListKernel, "_particles", particlesBuffer);
            cs.SetBuffer(buildNeighbourListKernel, "_hashGrid", hashGridBuffer);
            cs.SetBuffer(buildNeighbourListKernel, "_hashGridTracker", hashGridTrackerBuffer);
            cs.SetBuffer(buildNeighbourListKernel, "_neighbourList", neighbourListBuffer);
            cs.SetBuffer(buildNeighbourListKernel, "_neighbourTracker", neighbourTrackerBuffer);
            cs.SetBuffer(computeDensityPressureKernel, "_neighbourTracker", neighbourTrackerBuffer);
            cs.SetBuffer(computeDensityPressureKernel, "_neighbourList", neighbourListBuffer);
            cs.SetBuffer(computeDensityPressureKernel, "_particles", particlesBuffer);
            cs.SetBuffer(computeDensityPressureKernel, "_densities", densitiesBuffer);
            cs.SetBuffer(computeDensityPressureKernel, "_pressures", pressuresBuffer);
            cs.SetBuffer(computeForcesKernel, "_neighbourTracker", neighbourTrackerBuffer);
            cs.SetBuffer(computeForcesKernel, "_neighbourList", neighbourListBuffer);
            cs.SetBuffer(computeForcesKernel, "_particles", particlesBuffer);
            cs.SetBuffer(computeForcesKernel, "_densities", densitiesBuffer);
            cs.SetBuffer(computeForcesKernel, "_pressures", pressuresBuffer);
            cs.SetBuffer(computeForcesKernel, "_velocities", velocitiesBuffer);
            cs.SetBuffer(computeForcesKernel, "_forces", forcesBuffer);
            cs.SetBuffer(integrateKernel, "_particles", particlesBuffer);
            cs.SetBuffer(integrateKernel, "_forces", forcesBuffer);
            cs.SetBuffer(integrateKernel, "_velocities", velocitiesBuffer);

            mpb = new MaterialPropertyBlock();
            mpb.SetBuffer("_Particles", particlesBuffer);
            mpb.SetBuffer("_Velocities", velocitiesBuffer);
            ready = true;
            Respawn();
        }

        // same spawn as the source's RespawnParticles (start speed is a parameter here)
        public void Respawn()
        {
            Init();
            int n = numberOfParticles;
            var particles = new Particle[n];
            var densities = new float[n]; var pressures = new float[n];
            var velocities = new Vector3[n]; var forces = new Vector3[n];
            Random.InitState(seed);
            int ppd = Mathf.CeilToInt(Mathf.Pow(n, 1f / 3f));
            int counter = 0;
            for (int x = 0; x < ppd && counter < n; x++)
                for (int y = 0; y < ppd && counter < n; y++)
                    for (int z = 0; z < ppd && counter < n; z++)
                    {
                        var startPos = new Vector3(dimensions - 1, dimensions - 1, dimensions - 1) - new Vector3(x / 2f, y / 2f, z / 2f)
                                       - new Vector3(Random.Range(0f, 0.01f), Random.Range(0f, 0.01f), Random.Range(0f, 0.01f));
                        particles[counter] = new Particle { Position = startPos, Color = Color.white };
                        densities[counter] = -1f; velocities[counter] = Vector3.down * startSpeed;
                        counter++;
                    }
            particlesBuffer.SetData(particles);
            densitiesBuffer.SetData(densities); pressuresBuffer.SetData(pressures);
            velocitiesBuffer.SetData(velocities); forcesBuffer.SetData(forces);
            ElapsedSimulationSteps = 0;
        }

        void Update()
        {
            if (!ready) return;
            if (running)
            {
                cs.SetInt("sphOn", sphForces ? 1 : 0);
                cs.SetFloat("viscosityCoefficient", viscosityCoefficient);
                int cells = dimensions * dimensions * dimensions, n = numberOfParticles;
                for (int s = 0; s < stepsPerFrame; s++)
                {
                    cs.Dispatch(clearHashGridKernel, cells / 100, 1, 1);
                    cs.Dispatch(recalculateHashGridKernel, n / 100, 1, 1);
                    cs.Dispatch(buildNeighbourListKernel, n / 100, 1, 1);
                    cs.Dispatch(computeDensityPressureKernel, n / 100, 1, 1);
                    cs.Dispatch(computeForcesKernel, n / 100, 1, 1);
                    cs.Dispatch(integrateKernel, n / 100, 1, 1);
                    ElapsedSimulationSteps++;
                }
            }
            mpb.SetVector("_Origin", origin);
            mpb.SetFloat("_Scale", worldScale);
            mpb.SetFloat("_Size", particleSize);
            var bounds = new Bounds(origin + Vector3.one * dimensions * worldScale * 0.5f, Vector3.one * dimensions * worldScale * 1.5f);
            Graphics.DrawMeshInstancedProcedural(particleMesh, 0, material, bounds, numberOfParticles, mpb, ShadowCastingMode.Off, false, layer);
        }

        // read-back counters (synchronous, only for the log and labels)
        public void Stats(out uint maxPerCell, out int overflowed, out float maxHeight)
        {
            var tr = new uint[dimensions * dimensions * dimensions];
            hashGridTrackerBuffer.GetData(tr);
            maxPerCell = 0; foreach (var c in tr) if (c > maxPerCell) maxPerCell = c;
            var p = new Particle[numberOfParticles];
            particlesBuffer.GetData(p);
            overflowed = 0; maxHeight = 0;
            foreach (var q in p) { if (q.Color.y < 0.5f) overflowed++; if (q.Position.y > maxHeight) maxHeight = q.Position.y; }
        }

        void OnDestroy()
        {
            foreach (var b in new[] { particlesBuffer, neighbourListBuffer, neighbourTrackerBuffer, hashGridBuffer, hashGridTrackerBuffer, densitiesBuffer, pressuresBuffer, velocitiesBuffer, forcesBuffer })
                b?.Dispose();
        }
    }
}
