// Builds the VSM courtyard: floor, back wall, a pergola with a slatted roof, a lattice screen in front of the wall,
// the stand-in mannequin, the low sun, the ShadowVSM (URP) component with the source's defaults, two cameras, labels.
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    public static class VSMScene
    {
        const string Dir = "Assets/Techniques/variance-shadow-maps/";
        const string Shd = "GameTech/VSMLit";

        static Material M(string name, Color c, float smooth)
        {
            var m = new Material(Shader.Find(Shd)); m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth);
            AssetDatabase.CreateAsset(m, Dir + name + ".mat");
            return m;
        }

        static GameObject Box(string name, Material m, Vector3 pos, Vector3 size, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name; Object.DestroyImmediate(go.GetComponent<Collider>());
            if (parent) go.transform.SetParent(parent, false);
            go.transform.position = pos; go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            return go;
        }

        public static void Build()
        {
            var sun = SceneKit.Sun("Sun", new Vector3(50f, 35f, 0f), new Color(1.0f, 0.88f, 0.74f), 2.3f, false);
            RenderSettings.sun = sun;

            var floorM = M("Floor", new Color(0.60f, 0.54f, 0.46f), 0.2f);
            var wallM = M("Wall", new Color(0.80f, 0.73f, 0.64f), 0.1f);
            var woodM = M("Wood", new Color(0.40f, 0.26f, 0.16f), 0.3f);
            var latticeM = M("Lattice", new Color(0.28f, 0.37f, 0.33f), 0.3f);
            var stoneM = M("Stone", new Color(0.52f, 0.50f, 0.47f), 0.2f);

            Box("Floor", floorM, new Vector3(0, -0.05f, 0), new Vector3(16f, 0.1f, 12f));
            Box("Back wall", wallM, new Vector3(0, 1.7f, 3.1f), new Vector3(16f, 3.4f, 0.2f));
            Box("Wall cap", stoneM, new Vector3(0, 3.45f, 3.05f), new Vector3(16f, 0.1f, 0.34f));

            // pergola: four posts, two beams, slats along z on top
            var pg = new GameObject("Pergola").transform;
            foreach (var x in new[] { -2.6f, 1.6f })
                foreach (var z in new[] { -0.4f, 2.4f })
                    Box("Post", woodM, new Vector3(x, 1.25f, z), new Vector3(0.16f, 2.5f, 0.16f), pg);
            foreach (var z in new[] { -0.4f, 2.4f }) Box("Beam", woodM, new Vector3(-0.5f, 2.56f, z), new Vector3(4.7f, 0.18f, 0.12f), pg);
            for (float x = -2.85f; x <= 1.91f; x += 0.45f) Box("Slat", woodM, new Vector3(x, 2.72f, 1.0f), new Vector3(0.16f, 0.14f, 3.5f), pg);

            // lattice screen in front of the back wall
            var lt = new GameObject("Lattice").transform;
            Box("Frame L", woodM, new Vector3(-5.45f, 1.2f, 2.55f), new Vector3(0.08f, 2.4f, 0.08f), lt);
            Box("Frame R", woodM, new Vector3(-2.35f, 1.2f, 2.55f), new Vector3(0.08f, 2.4f, 0.08f), lt);
            for (float x = -5.2f; x <= -2.59f; x += 0.25f) Box("Lattice v", latticeM, new Vector3(x, 1.25f, 2.55f), new Vector3(0.035f, 2.0f, 0.035f), lt);
            for (float y = 0.25f; y <= 2.26f; y += 0.25f) Box("Lattice h", latticeM, new Vector3(-3.9f, y, 2.55f), new Vector3(3.1f, 0.035f, 0.035f), lt);

            // a bench and planters for scale
            Box("Bench", woodM, new Vector3(3.6f, 0.42f, 2.4f), new Vector3(1.6f, 0.08f, 0.42f));
            Box("Bench leg 1", stoneM, new Vector3(3.0f, 0.19f, 2.4f), new Vector3(0.12f, 0.38f, 0.36f));
            Box("Bench leg 2", stoneM, new Vector3(4.2f, 0.19f, 2.4f), new Vector3(0.12f, 0.38f, 0.36f));
            Box("Planter", stoneM, new Vector3(-6.2f, 0.35f, 1.9f), new Vector3(0.7f, 0.7f, 0.7f));

            var man = Mannequin.Build(Shd, Dir, (m, part) =>
            {
                m.SetColor("_BaseColor", part == "skin" ? new Color(0.80f, 0.66f, 0.56f) : new Color(0.36f, 0.45f, 0.62f));
                m.SetFloat("_Smoothness", 0.35f);
            });
            foreach (var smr in man.GetComponentsInChildren<SkinnedMeshRenderer>()) { smr.shadowCastingMode = ShadowCastingMode.Off; smr.updateWhenOffscreen = true; }
            man.transform.position = new Vector3(VSMDirector.X0, 0, VSMDirector.Z);

            // ShadowVSM (URP): source defaults except the depth range (scene is small) and a fixed box position
            var vsm = new GameObject("ShadowVSM").AddComponent<ShadowVSMURP>();
            vsm.depthShader = Shader.Find("Hidden/GameTech/VSMDepth");
            vsm.blurShader = Shader.Find("Hidden/ShadowVSM/BlurShader");
            if (vsm.depthShader == null || vsm.blurShader == null) Debug.LogError("[gt] VSM shaders not found");
            vsm.resolution = 512; vsm.numCascades = 6; vsm.firstCascadeLevelSize = 8f; vsm.depthOfShadowRange = 20f;
            vsm.sun = sun; vsm.shadowCamPosition = new Vector3(-0.5f, 1f, 1f);
            vsm.casters = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);

            var bg = new Color(0.56f, 0.66f, 0.82f);
            var clear = SceneKit.Cam("Clear", new Rect(0, 0, 1, 1), 30, Color.black, -10, false); clear.cullingMask = 0;
            var camA = SceneKit.Cam("Cam A", new Rect(0, 0, 1, 1), 38, bg, 0);
            var camB = SceneKit.Cam("Cam B", new Rect(0.5f, 0, 0.5f, 1), 38, bg, 1);
            camA.tag = "MainCamera";
            foreach (var c in new[] { camA, camB }) c.transform.position = new Vector3(0, 1.9f, -6.5f);
            SceneKit.Grade(Dir, 0.25f, 0f, 14f, 4f, 0.25f, 0.12f);

            var title = LabelSpec.Create("", 0.012f, 0.935f, 46);
            var l1 = LabelSpec.Create("", 0.012f, 0.03f, 30);
            var l2 = LabelSpec.Create("", 0.012f, 0.018f, 26);
            var m1 = LabelSpec.Create("", 0.512f, 0.058f, 30);
            var m2 = LabelSpec.Create("", 0.512f, 0.018f, 26);

            var d = new GameObject("Director").AddComponent<VSMDirector>();
            d.camA = camA; d.camB = camB; d.character = man.transform; d.vsm = vsm;
            d.title = title; d.l1 = l1; d.l2 = l2; d.m1 = m1; d.m2 = m2;
            Debug.Log("[gt] VSM scene built: " + vsm.casters.Length + " caster renderers");
        }
    }
}
