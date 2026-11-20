// Builds the 3D fluid scene from code: a dark studio floor, a 2.4 m cube domain (thin frame on its edges), a
// slab hanging inside it, the stand-in mannequin walking through the plume, and four copies of the smoke solver
// (same emitter, each on its own layer) so split screens compare settings on the same frame.
using FluidSim3DProject;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GameTech.Posts
{
    public static class FluidScene
    {
        const string Dir = "Assets/Techniques/gpu-3d-smoke/";
        const string Lit = "Universal Render Pipeline/Lit";

        static GameObject Box(string name, Material m, Vector3 pos, Vector3 size, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        static T Load<T>(string p) where T : Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(Dir + p);
            if (a == null) Debug.LogError("[gt] missing asset " + p);
            return a;
        }

        static SmokeSim Sim(string name, int layer, Material vol, Vector3 centre, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.layer = layer;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.position = centre; go.transform.localScale = Vector3.one * size;
            var r = go.GetComponent<Renderer>(); r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.sharedMaterial = vol;
            var s = go.AddComponent<SmokeSim>();
            s.material = vol;
            s.m_applyImpulse = Load<ComputeShader>("FluidSim3D/ApplyImpulse.compute");
            s.m_applyAdvect = Load<ComputeShader>("FluidSim3D/ApplyAdvection.compute");
            s.m_computeVorticity = Load<ComputeShader>("FluidSim3D/ComputeVorticity.compute");
            s.m_computeDivergence = Load<ComputeShader>("FluidSim3D/ComputeDivergence.compute");
            s.m_computeJacobi = Load<ComputeShader>("FluidSim3D/ComputeJacobi.compute");
            s.m_computeProjection = Load<ComputeShader>("FluidSim3D/ComputeProjection.compute");
            s.m_computeConfinement = Load<ComputeShader>("FluidSim3D/ComputeConfinement.compute");
            s.m_computeObstacles = Load<ComputeShader>("FluidSim3D/ComputeObstacles.compute");
            s.m_applyBuoyancy = Load<ComputeShader>("FluidSim3D/ApplyBuoyancy.compute");
            s.m_light = Load<ComputeShader>("Shaders/SmokeLight.compute");
            s.m_width = s.m_height = s.m_depth = 128;
            s.m_iterations = 10;
            s.m_inputPos = new Vector4(0.5f, 0.05f, 0.46f, 0f);
            s.preRollSteps = 150;
            s.m_densityDissipation = 0.995f;   // source 0.999: the closed box fills up within the clip
            s.lightAbsorb = 15f;
            s.framesPerSecond = 30f;
            return s;
        }

        public static void Build()
        {
            RenderSettings.skybox = null; RenderSettings.fog = false;
            SceneKit.Ambient(new Color(0.10f, 0.11f, 0.13f), new Color(0.06f, 0.06f, 0.07f), new Color(0.03f, 0.03f, 0.03f));
            SceneKit.Shadows(20f);
            var dir = new GameObject("Director").AddComponent<FluidDirector>();

            var floorM = SceneKit.Mat(Dir, "Floor", Lit, new Color(0.10f, 0.10f, 0.11f), 0f, 0.45f);
            var frameM = SceneKit.Mat(Dir, "Frame", Lit, new Color(0.35f, 0.36f, 0.38f), 0.9f, 0.55f);
            var slabM = SceneKit.Mat(Dir, "Slab", Lit, new Color(0.55f, 0.32f, 0.18f), 0f, 0.35f);
            Box("Floor", floorM, new Vector3(0, -0.05f, 0), new Vector3(14f, 0.1f, 14f));

            // domain: 2.4 m cube standing on the floor; thin frame just outside its edges
            const float S = 2.4f; var c = new Vector3(0, S / 2, 0); float h = S / 2 + 0.02f, w = 0.025f;
            var frame = new GameObject("Frame").transform;
            for (int a = 0; a < 3; a++)
                for (int i = 0; i < 4; i++)
                {
                    float p = (i & 1) == 0 ? -h : h, q = (i & 2) == 0 ? -h : h;
                    Vector3 pos = a == 0 ? new Vector3(0, p, q) : a == 1 ? new Vector3(p, 0, q) : new Vector3(p, q, 0);
                    Vector3 size = a == 0 ? new Vector3(S + 2 * w + 0.04f, w, w) : a == 1 ? new Vector3(w, S + 2 * w + 0.04f, w) : new Vector3(w, w, S + 2 * w + 0.04f);
                    Box("Edge " + a + i, frameM, c + pos, size, frame);
                }

            // slab inside the domain (also the static obstacle box), on two thin hangers from the top
            var slab = Box("Slab", slabM, new Vector3(-0.1f, 1.65f, -0.1f), new Vector3(0.8f, 0.1f, 1.0f));
            Box("Hanger 1", frameM, new Vector3(-0.45f, 2.05f, -0.1f), new Vector3(0.02f, 0.7f, 0.02f));
            Box("Hanger 2", frameM, new Vector3(0.25f, 2.05f, -0.1f), new Vector3(0.02f, 0.7f, 0.02f));
            dir.slab = slab.transform;

            var man = Mannequin.Build(Lit, Dir, (m, part) =>
            {
                m.SetColor("_BaseColor", part == "skin" ? new Color(0.78f, 0.62f, 0.52f) : new Color(0.2f, 0.32f, 0.5f));
                m.SetFloat("_Smoothness", 0.35f);
            });
            foreach (var smr in man.GetComponentsInChildren<SkinnedMeshRenderer>()) { smr.shadowCastingMode = ShadowCastingMode.On; smr.updateWhenOffscreen = true; }
            dir.character = man.transform;

            var vol = new Material(Load<Shader>("Shaders/SmokeVolume.shader")) { name = "SmokeVolume" };
            vol.SetColor("_ShadowColor", new Color(0.22f, 0.24f, 0.29f));
            AssetDatabase.CreateAsset(vol, Dir + "SmokeVolume.mat");
            dir.full = Sim("Smoke full", 20, vol, c, S);
            dir.noObstacles = Sim("Smoke no obstacles", 21, vol, c, S);
            dir.noVorticity = Sim("Smoke no vorticity", 22, vol, c, S);
            dir.off = Sim("Smoke off", 23, vol, c, S);

            SceneKit.Sun("Sun", new Vector3(55f, -35f, 0f), new Color(1f, 0.95f, 0.88f), 1.6f, true);
            var rim = new GameObject("Rim").AddComponent<Light>(); rim.type = LightType.Spot; rim.color = new Color(0.6f, 0.75f, 1f);
            rim.intensity = 12f; rim.range = 10f; rim.spotAngle = 60f; rim.transform.position = new Vector3(0, 4.2f, 2.6f); rim.transform.LookAt(new Vector3(0, 0.5f, 0));
            SceneKit.Grade(Dir, 0.25f, 0.1f, 12f, 4f, 0.32f, 0.08f);
            AssetDatabase.SaveAssets();

            var bg = new Color(0.025f, 0.028f, 0.035f);
            dir.camA = SceneKit.Cam("Cam A", new Rect(0, 0, 1, 1), 42f, bg, 0, true);
            dir.camB = SceneKit.Cam("Cam B", new Rect(0, 0, 1, 1), 42f, bg, 1, true);
            foreach (var cam in new[] { dir.camA, dir.camB }) { cam.nearClipPlane = 0.05f; cam.farClipPlane = 60f; cam.GetComponent<UniversalAdditionalCameraData>().requiresDepthTexture = true; }
            var clear = SceneKit.Cam("Clear", new Rect(0, 0, 1, 1), 60f, Color.black, -10, false); clear.cullingMask = 0;

            dir.title = LabelSpec.Create("", 0.012f, 0.93f, 40);
            dir.l1 = LabelSpec.Create("", 0.012f, 0.058f, 34); dir.l2 = LabelSpec.Create("", 0.012f, 0.018f, 30);
            dir.m1 = LabelSpec.Create("", 0.512f, 0.058f, 34); dir.m2 = LabelSpec.Create("", 0.512f, 0.018f, 30);
            Debug.Log("[gt] built: floor, 2.4 m domain + frame, slab, mannequin, 4 smoke solvers 128^3");
        }
    }
}
