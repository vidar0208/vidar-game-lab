// Builds the SPH scene: a cut-away tank the size of the simulation domain (two solid walls, an edge frame on the open
// sides), three SPHFluid instances with the source scene's parameters (one with forces off, one with the source
// script's default viscosity), cameras, light and labels.
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    public static class SPHScene
    {
        const string Dir = "Assets/Techniques/sph-fluid-gpu/";
        const string Lit = "Universal Render Pipeline/Lit";
        // Density of the source's own spawn lattice (0.5 apart, kernel radius 1, poly6 kernel 1.567 x (1 - r^2)^3), with the
        // particle's own term that I add in the compute shader: 1.567 x (1 + 6 x 0.4219 + 12 x 0.125 + 8 x 0.0156) = 8.08.
        // The source scene's rest density of 2 starts every particle far above rest, so the block blows apart.
        public const float RestDensity = 8.1f;

        static GameObject Box(string name, Material m, Vector3 pos, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name; Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.position = pos; go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            return go;
        }

        static SPHFluid Sim(string name, int layer, ComputeShader cs, Mesh mesh, Material mat, Vector3 origin, bool forces, float visc)
        {
            var f = new GameObject(name).AddComponent<SPHFluid>();
            f.computeShader = cs; f.particleMesh = mesh; f.material = mat; f.layer = layer; f.origin = origin;
            // source scene values, except the rest density (see RestDensity)
            f.radius = 1f; f.mass = 1f; f.gasConstant = 2000f; f.restDensity = RestDensity; f.viscosityCoefficient = visc;
            f.g = new Vector3(0, -9.81f, 0); f.damping = -0.5f; f.dt = 0.0008f;
            // mine: tank size, particle count, cell limit, steps per frame
            f.numberOfParticles = 48000; f.dimensions = 30; f.maximumParticlesPerCell = 256; f.stepsPerFrame = 36;
            f.startSpeed = 0f;   // source: 50, faster than this fluid's pressure waves, so the landing packed hundreds of particles into one grid cell
            f.worldScale = 0.1f; f.particleSize = 0.06f; f.sphForces = forces; f.seed = 7;
            return f;
        }

        public static void Build()
        {
            SceneKit.Shadows(20f);
            SceneKit.Ambient(new Color(0.40f, 0.46f, 0.56f), new Color(0.30f, 0.30f, 0.32f), new Color(0.12f, 0.11f, 0.10f));
            SceneKit.Sun("Sun", new Vector3(48f, -30f, 0f), new Color(1.0f, 0.93f, 0.84f), 1.8f, true);

            const float S = 0.1f; const int D = 30;
            float L = (D - 1) * S;                         // 2.9 m, the domain the source clamps particles to
            var origin = new Vector3(-L / 2, 0.03f, -L / 2);

            var floorM = SceneKit.Mat(Dir, "Floor", Lit, new Color(0.20f, 0.21f, 0.23f), 0, 0.35f);
            var tankM = SceneKit.Mat(Dir, "TankFloor", Lit, new Color(0.78f, 0.80f, 0.82f), 0, 0.5f);
            var wallM = SceneKit.Mat(Dir, "Wall", Lit, new Color(0.30f, 0.34f, 0.40f), 0, 0.7f);
            var frameM = SceneKit.Mat(Dir, "Frame", Lit, new Color(0.62f, 0.64f, 0.66f), 0.8f, 0.6f);
            Box("Ground", floorM, new Vector3(0, -0.1f, 0), new Vector3(30f, 0.2f, 30f));
            Box("Tank floor", tankM, new Vector3(0, -0.01f, 0), new Vector3(L + 0.12f, 0.04f, L + 0.12f));
            float H = L + 0.05f, e = 0.04f, h = L / 2 + 0.03f;
            Box("Back wall", wallM, new Vector3(0, H / 2, h + 0.02f), new Vector3(L + 0.12f, H, 0.04f));
            Box("Right wall", wallM, new Vector3(h + 0.02f, H / 2, 0), new Vector3(0.04f, H, L + 0.12f));
            // edge frame on the open sides
            Box("Edge front-left", frameM, new Vector3(-h, H / 2, -h), new Vector3(e, H, e));
            Box("Edge front-right", frameM, new Vector3(h, H / 2, -h), new Vector3(e, H, e));
            Box("Edge back-left", frameM, new Vector3(-h, H / 2, h), new Vector3(e, H, e));
            Box("Edge top-front", frameM, new Vector3(0, H, -h), new Vector3(L + 0.1f, e, e));
            Box("Edge top-left", frameM, new Vector3(-h, H, 0), new Vector3(e, e, L + 0.1f));
            Box("Edge bottom-front", frameM, new Vector3(0, 0.02f, -h), new Vector3(L + 0.1f, e, e));
            Box("Edge bottom-left", frameM, new Vector3(-h, 0.02f, 0), new Vector3(e, e, L + 0.1f));

            var cs = AssetDatabase.LoadAssetAtPath<ComputeShader>(Dir + "SPH/SPH3D.compute");
            if (cs == null) Debug.LogError("[gt] compute shader not found");
            var mesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            var pm = new Material(Shader.Find("GameTech/SPHParticle"));
            AssetDatabase.CreateAsset(pm, Dir + "Particle.mat");
            var off = Sim("SPH forces off", 20, cs, mesh, pm, origin, false, 0.25f);
            var on = Sim("SPH on", 21, cs, mesh, pm, origin, true, 0.25f);
            var visc = Sim("SPH viscosity 2.5", 22, cs, mesh, pm, origin, true, 2.5f);

            var bg = new Color(0.07f, 0.08f, 0.10f);
            var clear = SceneKit.Cam("Clear", new Rect(0, 0, 1, 1), 30, Color.black, -10, false); clear.cullingMask = 0;
            var camA = SceneKit.Cam("Cam A", new Rect(0, 0, 1, 1), 34, bg, 0);
            var camB = SceneKit.Cam("Cam B", new Rect(0.5f, 0, 0.5f, 1), 34, bg, 1);
            camA.tag = "MainCamera";
            foreach (var c in new[] { camA, camB }) c.transform.position = new Vector3(-3.4f, 2.8f, -4.4f);
            SceneKit.Grade(Dir, 0.35f, 0f, 14f, 6f, 0.3f, 0.12f);

            var title = LabelSpec.Create("", 0.012f, 0.935f, 46);
            var l1 = LabelSpec.Create("", 0.012f, 0.03f, 30);
            var l2 = LabelSpec.Create("", 0.012f, 0.018f, 26);
            var m1 = LabelSpec.Create("", 0.512f, 0.058f, 30);
            var m2 = LabelSpec.Create("", 0.512f, 0.018f, 26);

            var d = new GameObject("Director").AddComponent<SPHDirector>();
            d.camA = camA; d.camB = camB; d.simOff = off; d.simOn = on; d.simVisc = visc;
            d.title = title; d.l1 = l1; d.l2 = l2; d.m1 = m1; d.m2 = m2;
            d.tankCentre = new Vector3(0, L / 2, 0);
            Debug.Log("[gt] SPH scene built");
        }
    }
}
