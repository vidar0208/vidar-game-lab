// 86 planar reflections: an open pavilion at dusk with a polished floor, light strips on a back wall, pillars and pedestals,
// plus four moving things (rolling ball, floating torus, sliding slab, glowing orb with a point light).
using UnityEditor;
using UnityEngine;

namespace GameTech.Posts
{
    public static class PlanarScene
    {
        const string Dir = "Assets/Techniques/planar-reflections/";
        const string Lit = "Universal Render Pipeline/Lit";

        static Material Mat(string name, Color c, float smooth = 0.4f) => SceneKit.Mat(Dir, name, Lit, c, 0f, smooth);

        static Material Glow(string name, Color c, float power)
        {
            var m = Mat(name, c, 0.5f);
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * power);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            return m;
        }

        static GameObject Prim(PrimitiveType type, string name, Material m, Vector3 pos, Vector3 scale, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = m;
            go.transform.position = pos; go.transform.localScale = scale; go.transform.rotation = Quaternion.Euler(euler);
            return go;
        }

        public static void Build()
        {
            SceneKit.Ambient(new Color(0.20f, 0.22f, 0.32f), new Color(0.18f, 0.16f, 0.18f), new Color(0.06f, 0.05f, 0.05f));
            SceneKit.Shadows(30f);
            var sun = SceneKit.Sun("Sun", new Vector3(18, -120, 0), new Color(1f, 0.70f, 0.48f), 1.6f, true);
            RenderSettings.sun = sun;
            var sky = new Material(Shader.Find("Skybox/Procedural"));
            sky.SetFloat("_SunSize", 0.04f); sky.SetFloat("_AtmosphereThickness", 1.4f); sky.SetFloat("_Exposure", 0.9f);
            sky.SetColor("_SkyTint", new Color(0.45f, 0.45f, 0.62f)); sky.SetColor("_GroundColor", new Color(0.12f, 0.11f, 0.12f));
            AssetDatabase.CreateAsset(sky, Dir + "Sky.mat");
            RenderSettings.skybox = sky;

            var floorMat = new Material(Shader.Find("GT/PlanarFloor")); AssetDatabase.CreateAsset(floorMat, Dir + "Floor.mat");
            Prim(PrimitiveType.Plane, "Floor", floorMat, Vector3.zero, new Vector3(3, 1, 3));

            var wall = Mat("Wall", new Color(0.10f, 0.10f, 0.12f), 0.3f);
            Prim(PrimitiveType.Cube, "Back wall", wall, new Vector3(0, 2.5f, 6f), new Vector3(16f, 5f, 0.4f));
            var warm = Glow("Strip warm", new Color(1f, 0.62f, 0.32f), 5f);
            var cool = Glow("Strip cool", new Color(0.35f, 0.65f, 1f), 5f);
            for (int i = 0; i < 7; i++)
                Prim(PrimitiveType.Cube, "Light strip", i % 2 == 0 ? warm : cool, new Vector3(-6f + i * 2f, 2.2f, 5.75f), new Vector3(0.12f, 3.6f, 0.08f));
            var stone = Mat("Stone", new Color(0.75f, 0.72f, 0.68f), 0.35f);
            foreach (var x in new[] { -5.5f, 5.5f })
                foreach (var z in new[] { -2f, 3f })
                    Prim(PrimitiveType.Cylinder, "Pillar", stone, new Vector3(x, 2f, z), new Vector3(0.6f, 2f, 0.6f));
            var marker = Mat("Marker", new Color(0.85f, 0.83f, 0.78f), 0.4f);
            for (int i = 0; i < 3; i++) // three floor blocks, one per band of the clip plane offset comparison
                Prim(PrimitiveType.Cube, "Floor block", marker, new Vector3(-3f + 3f * i, 0.25f, -1.7f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(0, 30, 0));
            Prim(PrimitiveType.Cube, "Pedestal", stone, new Vector3(-3.4f, 0.4f, 2.6f), new Vector3(0.9f, 0.8f, 0.9f));
            Prim(PrimitiveType.Sphere, "Pedestal sphere", Mat("Teal", new Color(0.10f, 0.55f, 0.55f), 0.7f), new Vector3(-3.4f, 1.2f, 2.6f), Vector3.one * 0.8f);
            Prim(PrimitiveType.Cube, "Pedestal", stone, new Vector3(3.8f, 0.3f, 3.4f), new Vector3(0.8f, 0.6f, 0.8f));
            Prim(PrimitiveType.Cube, "Pedestal block", Mat("White", new Color(0.92f, 0.91f, 0.88f), 0.5f), new Vector3(3.8f, 0.9f, 3.4f), Vector3.one * 0.6f, new Vector3(35, 45, 0));

            // moving things
            var ball = Prim(PrimitiveType.Sphere, "Ball", Mat("Red", new Color(0.85f, 0.12f, 0.08f), 0.6f), new Vector3(0, 0.45f, -0.6f), Vector3.one * 0.9f);
            var stripe = Prim(PrimitiveType.Cylinder, "Ball stripe", Mat("Cream", new Color(0.95f, 0.90f, 0.78f), 0.5f), Vector3.zero, Vector3.one);
            stripe.transform.SetParent(ball.transform, false); stripe.transform.localPosition = Vector3.zero; stripe.transform.localScale = new Vector3(1.03f, 0.1f, 1.03f); stripe.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var prof = new Vector2[28];
            for (int i = 0; i < prof.Length; i++) { float a = i * Mathf.PI * 2f / prof.Length; prof[i] = new Vector2(0.14f * Mathf.Sin(a), 0.5f + 0.14f * Mathf.Cos(a)); }
            var torusMesh = new MeshKit().Lathe(prof, 56).Build("Torus", 60f); AssetDatabase.CreateAsset(torusMesh, Dir + "Torus.asset");
            var torus = SceneKit.Part("Torus", torusMesh, Mat("Gold", new Color(0.95f, 0.70f, 0.22f), 0.75f), new Vector3(-1.6f, 1.1f, 1.4f), Quaternion.Euler(70, 0, 0));
            var slab = Prim(PrimitiveType.Cube, "Slab", Mat("Slab", new Color(0.88f, 0.86f, 0.82f), 0.45f), new Vector3(2.2f, 0.9f, 1.8f), new Vector3(0.5f, 1.8f, 0.9f));
            var orb = Prim(PrimitiveType.Sphere, "Orb", Glow("Orb", new Color(1f, 0.55f, 0.22f), 8f), new Vector3(0, 1.5f, 0.4f), Vector3.one * 0.28f);
            var pl = new GameObject("Orb light").AddComponent<Light>();
            pl.type = LightType.Point; pl.color = new Color(1f, 0.6f, 0.3f); pl.intensity = 6f; pl.range = 6f; pl.shadows = LightShadows.None;
            pl.transform.SetParent(orb.transform, false);

            var srcCam = SceneKit.Cam("Scene camera", new Rect(0, 0, 1, 1), 40, Color.black, -10);
            srcCam.clearFlags = CameraClearFlags.Skybox; srcCam.cullingMask = ~(1 << 8); srcCam.farClipPlane = 200f;
            var probe = SceneKit.Cam("Cube map probe", new Rect(0, 0, 1, 1), 90, Color.black, -20, false);
            probe.clearFlags = CameraClearFlags.Skybox; probe.cullingMask = ~(1 << 8); probe.transform.position = new Vector3(0, 0.8f, 0.6f);
            SceneKit.Grade(Dir, 0.6f, 0f, 12f, 5f, 0.25f, 0f);
            var display = SceneKit.Cam("Display camera", new Rect(0, 0, 1, 1), 40, Color.black, 10, false);
            display.cullingMask = 1 << 8; display.transform.position = new Vector3(0, -50, 0); display.tag = "MainCamera";

            var compose = new Material(Shader.Find("Hidden/GT/PlanarCompose")); AssetDatabase.CreateAsset(compose, Dir + "Compose.mat");
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Display quad"; Object.DestroyImmediate(quad.GetComponent<Collider>()); quad.layer = 8;
            var qm = Object.Instantiate(quad.GetComponent<MeshFilter>().sharedMesh); qm.name = "DisplayQuad"; qm.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
            AssetDatabase.CreateAsset(qm, Dir + "DisplayQuad.asset");
            quad.GetComponent<MeshFilter>().sharedMesh = qm; quad.GetComponent<Renderer>().sharedMaterial = compose;
            quad.transform.position = display.transform.position + Vector3.forward;

            var dir = new GameObject("Director").AddComponent<PlanarDirector>();
            dir.src = srcCam; dir.probe = probe; dir.compose = compose;
            dir.ball = ball.transform; dir.torus = torus.transform; dir.slab = slab.transform; dir.orb = orb.transform;
            dir.movers = new[] { ball, torus, slab, orb };
            dir.title = LabelSpec.Create("", 0.012f, 0.935f, 30);
            dir.l1 = new LabelSpec[3]; dir.l2 = new LabelSpec[3];
            for (int i = 0; i < 3; i++) { dir.l1[i] = LabelSpec.Create("", 0.012f, 0.058f, 28); dir.l2[i] = LabelSpec.Create("", 0.012f, 0.018f, 22); }
        }
    }
}
