using System.Collections.Generic;
using UnityEngine;

namespace FluidSim3DProject
{
    // Port of the source's SmokeFluidSim: same buffers (ComputeBuffers, one float or float3 per cell), same fixed
    // time step (0.1) and the same order per step: advect temperature, advect density, advect velocity, buoyancy,
    // impulses, vorticity confinement, divergence, Jacobi pressure, projection.
    // GameTech changes: obstacles are recomputed every step (static boxes + moving capsules, with an obstacle
    // velocity buffer that divergence and projection read); a switch to run only the impulse (simulation "off");
    // a pre-roll; rendering through a 3D texture (density + top light) for a URP ray-march material.
    public class SmokeSim : MonoBehaviour
    {
        const int READ = 0, WRITE = 1, PHI_N_HAT = 0, PHI_N_1_HAT = 1;
        public enum ADVECTION { NORMAL = 1, BFECC = 2, MACCORMACK = 3 };
        const int NUM_THREADS = 8;
        const float TIME_STEP = 0.1f;

        public ADVECTION m_advectionType = ADVECTION.MACCORMACK;
        public int m_width = 128, m_height = 128, m_depth = 128;
        public int m_iterations = 10;
        public float m_vorticityStrength = 1.0f;
        public float m_densityAmount = 1.0f;
        public float m_densityDissipation = 0.999f;
        public float m_densityBuoyancy = 1.0f;
        public float m_densityWeight = 0.0125f;
        public float m_temperatureAmount = 10.0f;
        public float m_temperatureDissipation = 0.995f;
        public float m_velocityDissipation = 0.995f;
        public float m_inputRadius = 0.04f;
        public Vector4 m_inputPos = new Vector4(0.5f, 0.1f, 0.5f, 0.0f);
        float m_ambientTemperature = 0.0f;

        public ComputeShader m_applyImpulse, m_applyAdvect, m_computeVorticity;
        public ComputeShader m_computeDivergence, m_computeJacobi, m_computeProjection;
        public ComputeShader m_computeConfinement, m_computeObstacles, m_applyBuoyancy;
        public ComputeShader m_light;

        // GameTech
        public bool simulate = true;                // false: only the impulse runs (no advection, no forces)
        public bool useObstacles = true;            // false: only the domain border is solid
        public int preRollSteps = 0;
        public float lightAbsorb = 30f;
        public Material material;
        public List<Bounds> boxes = new List<Bounds>();
        public System.Func<List<Vector4>> capsules;  // per capsule 4 entries: A(xyz, r), B, velocity A, velocity B (world, m/s)
        public float framesPerSecond = 30f;
        public RenderTexture Volume { get; private set; }
        public int StepsDone { get; private set; }

        Vector4 m_size;
        ComputeBuffer[] m_density, m_velocity, m_pressure, m_temperature, m_phi;
        ComputeBuffer m_temp3f, m_obstacles, m_obstacleVelocity;
        Vector3 domainMin, domainSize;

        void Start()
        {
            m_width = Mathf.ClosestPowerOfTwo(m_width); m_height = Mathf.ClosestPowerOfTwo(m_height); m_depth = Mathf.ClosestPowerOfTwo(m_depth);
            m_size = new Vector4(m_width, m_height, m_depth, 0.0f);
            int SIZE = m_width * m_height * m_depth;
            m_density = new[] { new ComputeBuffer(SIZE, sizeof(float)), new ComputeBuffer(SIZE, sizeof(float)) };
            m_temperature = new[] { new ComputeBuffer(SIZE, sizeof(float)), new ComputeBuffer(SIZE, sizeof(float)) };
            m_phi = new[] { new ComputeBuffer(SIZE, sizeof(float)), new ComputeBuffer(SIZE, sizeof(float)) };
            m_velocity = new[] { new ComputeBuffer(SIZE, sizeof(float) * 3), new ComputeBuffer(SIZE, sizeof(float) * 3) };
            m_pressure = new[] { new ComputeBuffer(SIZE, sizeof(float)), new ComputeBuffer(SIZE, sizeof(float)) };
            m_obstacles = new ComputeBuffer(SIZE, sizeof(float));
            m_obstacleVelocity = new ComputeBuffer(SIZE, sizeof(float) * 3);
            m_temp3f = new ComputeBuffer(SIZE, sizeof(float) * 3);
            // GameTech: buffers start at zero
            var z1 = new float[SIZE]; var z3 = new Vector3[SIZE];
            foreach (var b in new[] { m_density[0], m_density[1], m_temperature[0], m_temperature[1], m_phi[0], m_phi[1], m_pressure[0], m_pressure[1], m_obstacles }) b.SetData(z1);
            foreach (var b in new[] { m_velocity[0], m_velocity[1], m_temp3f, m_obstacleVelocity }) b.SetData(z3);

            domainMin = transform.position - transform.localScale * 0.5f; domainSize = transform.localScale;
            Volume = new RenderTexture(m_width, m_height, 0, RenderTextureFormat.RGHalf, RenderTextureReadWrite.Linear)
            { dimension = UnityEngine.Rendering.TextureDimension.Tex3D, volumeDepth = m_depth, enableRandomWrite = true, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = name + " volume" };
            Volume.Create();
            material = new Material(material) { name = material.name + " (" + name + ")" };
            GetComponent<Renderer>().sharedMaterial = material;
            material.SetTexture("_Volume", Volume);
            material.SetVector("_BoxMin", domainMin); material.SetVector("_BoxMax", domainMin + domainSize);
            for (int i = 0; i < preRollSteps; i++) Step();
        }

        void Swap(ComputeBuffer[] buffer) { var tmp = buffer[READ]; buffer[READ] = buffer[WRITE]; buffer[WRITE] = tmp; }
        void Dispatch(ComputeShader cs, int k) { cs.Dispatch(k, (int)m_size.x / NUM_THREADS, (int)m_size.y / NUM_THREADS, (int)m_size.z / NUM_THREADS); }

        readonly List<Vector4> lo = new List<Vector4>(), hi = new List<Vector4>(), ca = new List<Vector4>(), cb = new List<Vector4>(), va = new List<Vector4>(), vb = new List<Vector4>();
        void ComputeObstacles()
        {
            var cs = m_computeObstacles;
            cs.SetVector("_Size", m_size);
            cs.SetVector("_DomainMin", domainMin); cs.SetVector("_DomainSize", domainSize);
            lo.Clear(); hi.Clear(); ca.Clear(); cb.Clear(); va.Clear(); vb.Clear();
            if (useObstacles)
            {
                foreach (var b in boxes) { lo.Add(b.min); hi.Add(b.max); }
                var caps = capsules?.Invoke();
                if (caps != null) for (int i = 0; i + 3 < caps.Count; i += 4) { ca.Add(caps[i]); cb.Add(caps[i + 1]); va.Add(caps[i + 2]); vb.Add(caps[i + 3]); }
            }
            cs.SetInt("_BoxCount", lo.Count); cs.SetInt("_CapCount", ca.Count);
            void Arr(string n, List<Vector4> l, int len) { var a = new Vector4[len]; for (int i = 0; i < l.Count && i < len; i++) a[i] = l[i]; cs.SetVectorArray(n, a); }
            Arr("_BoxLo", lo, 8); Arr("_BoxHi", hi, 8); Arr("_CapA", ca, 24); Arr("_CapB", cb, 24); Arr("_CapVA", va, 24); Arr("_CapVB", vb, 24);
            // world m/s -> cells per unit of simulation time (one step of TIME_STEP per frame)
            cs.SetFloat("_VelocityScale", (m_width - 1) / domainSize.x / (TIME_STEP * framesPerSecond));
            cs.SetBuffer(0, "_Write", m_obstacles);
            cs.SetBuffer(0, "_WriteVelocity", m_obstacleVelocity);
            Dispatch(cs, 0);
        }

        void ApplyImpulse(float dt, float amount, ComputeBuffer[] buffer)
        {
            m_applyImpulse.SetVector("_Size", m_size);
            m_applyImpulse.SetFloat("_Radius", m_inputRadius);
            m_applyImpulse.SetFloat("_Amount", amount);
            m_applyImpulse.SetFloat("_DeltaTime", dt);
            m_applyImpulse.SetVector("_Pos", m_inputPos);
            m_applyImpulse.SetBuffer(0, "_Read", buffer[READ]);
            m_applyImpulse.SetBuffer(0, "_Write", buffer[WRITE]);
            Dispatch(m_applyImpulse, 0);
            Swap(buffer);
        }

        void ApplyBuoyancy(float dt)
        {
            m_applyBuoyancy.SetVector("_Size", m_size);
            m_applyBuoyancy.SetVector("_Up", new Vector4(0, 1, 0, 0));
            m_applyBuoyancy.SetFloat("_Buoyancy", m_densityBuoyancy);
            m_applyBuoyancy.SetFloat("_AmbientTemperature", m_ambientTemperature);
            m_applyBuoyancy.SetFloat("_Weight", m_densityWeight);
            m_applyBuoyancy.SetFloat("_DeltaTime", dt);
            m_applyBuoyancy.SetBuffer(0, "_Write", m_velocity[WRITE]);
            m_applyBuoyancy.SetBuffer(0, "_Velocity", m_velocity[READ]);
            m_applyBuoyancy.SetBuffer(0, "_Density", m_density[READ]);
            m_applyBuoyancy.SetBuffer(0, "_Temperature", m_temperature[READ]);
            Dispatch(m_applyBuoyancy, 0);
            Swap(m_velocity);
        }

        void SetAdvect(int k, float dt, float dissipation, float decay, float forward)
        {
            m_applyAdvect.SetVector("_Size", m_size);
            m_applyAdvect.SetFloat("_DeltaTime", dt);
            m_applyAdvect.SetFloat("_Dissipate", dissipation);
            m_applyAdvect.SetFloat("_Forward", forward);
            m_applyAdvect.SetFloat("_Decay", decay);
            m_applyAdvect.SetBuffer(k, "_Velocity", m_velocity[READ]);
            m_applyAdvect.SetBuffer(k, "_Obstacles", m_obstacles);
        }

        void ApplyAdvection(float dt, float dissipation, float decay, ComputeBuffer read, ComputeBuffer write, float forward = 1.0f)
        {
            int k = (int)ADVECTION.NORMAL;
            SetAdvect(k, dt, dissipation, decay, forward);
            m_applyAdvect.SetBuffer(k, "_Read1f", read);
            m_applyAdvect.SetBuffer(k, "_Write1f", write);
            Dispatch(m_applyAdvect, k);
        }

        void ApplyAdvection(float dt, float dissipation, float decay, ComputeBuffer[] buffer, float forward = 1.0f)
        {
            ApplyAdvection(dt, dissipation, decay, buffer[READ], buffer[WRITE], forward);
            Swap(buffer);
        }

        void ApplyAdvectionMacCormack(float dt, float dissipation, float decay, ComputeBuffer[] buffer)
        {
            int k = (int)ADVECTION.MACCORMACK;
            SetAdvect(k, dt, dissipation, decay, 1.0f);
            m_applyAdvect.SetBuffer(k, "_Read1f", buffer[READ]);
            m_applyAdvect.SetBuffer(k, "_Write1f", buffer[WRITE]);
            m_applyAdvect.SetBuffer(k, "_Phi_n_1_hat", m_phi[PHI_N_1_HAT]);
            m_applyAdvect.SetBuffer(k, "_Phi_n_hat", m_phi[PHI_N_HAT]);
            Dispatch(m_applyAdvect, k);
            Swap(buffer);
        }

        void ApplyAdvectionVelocity(float dt)
        {
            SetAdvect(0, dt, m_velocityDissipation, 0.0f, 1.0f);
            m_applyAdvect.SetBuffer(0, "_Read3f", m_velocity[READ]);
            m_applyAdvect.SetBuffer(0, "_Write3f", m_velocity[WRITE]);
            Dispatch(m_applyAdvect, 0);
            Swap(m_velocity);
        }

        void ComputeVorticityConfinement(float dt)
        {
            m_computeVorticity.SetVector("_Size", m_size);
            m_computeVorticity.SetBuffer(0, "_Write", m_temp3f);
            m_computeVorticity.SetBuffer(0, "_Velocity", m_velocity[READ]);
            Dispatch(m_computeVorticity, 0);
            m_computeConfinement.SetVector("_Size", m_size);
            m_computeConfinement.SetFloat("_DeltaTime", dt);
            m_computeConfinement.SetFloat("_Epsilon", m_vorticityStrength);
            m_computeConfinement.SetBuffer(0, "_Write", m_velocity[WRITE]);
            m_computeConfinement.SetBuffer(0, "_Read", m_velocity[READ]);
            m_computeConfinement.SetBuffer(0, "_Vorticity", m_temp3f);
            Dispatch(m_computeConfinement, 0);
            Swap(m_velocity);
        }

        void ComputeDivergence()
        {
            m_computeDivergence.SetVector("_Size", m_size);
            m_computeDivergence.SetBuffer(0, "_Write", m_temp3f);
            m_computeDivergence.SetBuffer(0, "_Velocity", m_velocity[READ]);
            m_computeDivergence.SetBuffer(0, "_Obstacles", m_obstacles);
            m_computeDivergence.SetBuffer(0, "_ObstacleVelocity", m_obstacleVelocity);
            Dispatch(m_computeDivergence, 0);
        }

        void ComputePressure()
        {
            m_computeJacobi.SetVector("_Size", m_size);
            m_computeJacobi.SetBuffer(0, "_Divergence", m_temp3f);
            m_computeJacobi.SetBuffer(0, "_Obstacles", m_obstacles);
            for (int i = 0; i < m_iterations; i++)
            {
                m_computeJacobi.SetBuffer(0, "_Write", m_pressure[WRITE]);
                m_computeJacobi.SetBuffer(0, "_Pressure", m_pressure[READ]);
                Dispatch(m_computeJacobi, 0);
                Swap(m_pressure);
            }
        }

        void ComputeProjection()
        {
            m_computeProjection.SetVector("_Size", m_size);
            m_computeProjection.SetBuffer(0, "_Obstacles", m_obstacles);
            m_computeProjection.SetBuffer(0, "_ObstacleVelocity", m_obstacleVelocity);
            m_computeProjection.SetBuffer(0, "_Pressure", m_pressure[READ]);
            m_computeProjection.SetBuffer(0, "_Velocity", m_velocity[READ]);
            m_computeProjection.SetBuffer(0, "_Write", m_velocity[WRITE]);
            Dispatch(m_computeProjection, 0);
            Swap(m_velocity);
        }

        void Step()
        {
            float dt = TIME_STEP;
            ComputeObstacles();
            if (simulate)
            {
                ApplyAdvection(dt, m_temperatureDissipation, 0.0f, m_temperature);
                if (m_advectionType == ADVECTION.MACCORMACK)
                {
                    ApplyAdvection(dt, 1.0f, 0.0f, m_density[READ], m_phi[PHI_N_1_HAT], 1.0f);
                    ApplyAdvection(dt, 1.0f, 0.0f, m_phi[PHI_N_1_HAT], m_phi[PHI_N_HAT], -1.0f);
                    ApplyAdvectionMacCormack(dt, m_densityDissipation, 0.0f, m_density);
                }
                else ApplyAdvection(dt, m_densityDissipation, 0.0f, m_density);
                ApplyAdvectionVelocity(dt);
                ApplyBuoyancy(dt);
            }
            ApplyImpulse(dt, m_densityAmount, m_density);
            ApplyImpulse(dt, m_temperatureAmount, m_temperature);
            if (simulate)
            {
                ComputeVorticityConfinement(dt);
                ComputeDivergence();
                ComputePressure();
                ComputeProjection();
            }
            StepsDone++;
        }

        void Update()
        {
            Step();
            m_light.SetVector("_Size", m_size);
            m_light.SetFloat("_LightAbsorb", lightAbsorb);
            m_light.SetBuffer(0, "_Density", m_density[READ]);
            m_light.SetBuffer(0, "_Obstacles", m_obstacles);
            m_light.SetTexture(0, "_Volume", Volume);
            m_light.Dispatch(0, m_width / 8, 1, m_depth / 8);
        }

        // total density in the grid (GameTech, for the labels and logs)
        public float TotalDensity()
        {
            var d = new float[m_width * m_height * m_depth]; m_density[READ].GetData(d);
            double s = 0; foreach (var v in d) s += v; return (float)s;
        }

        void OnDestroy()
        {
            foreach (var a in new[] { m_density, m_temperature, m_phi, m_velocity, m_pressure }) { a?[0]?.Release(); a?[1]?.Release(); }
            m_obstacles?.Release(); m_obstacleVelocity?.Release(); m_temp3f?.Release();
            if (Volume != null) Volume.Release();
        }
    }
}
