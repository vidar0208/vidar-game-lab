// 83 halftone: a bright, colourful set (spheres, an upright torus, a cone, blocks, an arch of pillars) rendered by a
// scene camera into a texture; HalftoneDirector turns it into printed dots and a display camera shows the split screen.
using UnityEditor;
using UnityEngine;

namespace GameTech.Posts
{
    public static class HalftoneScene
    {
        const string Dir = "Assets/Techniques/halftone-comic/";
        const string Lit = "Universal Render Pipeline/Lit";

        static Material Mat(string name, Color c, float smooth = 0.35f) => SceneKit.Mat(Dir, name, Lit, c, 0f, smooth);

        static GameObject Prim(PrimitiveType type, string name, Material m, Vector3 pos, Vector3 scale, float yaw = 0)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = m;
            go.transform.position = pos; go.transform.localScale = scale; go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            return go;
        }

        public static void Build()
        {
            SceneKit.Ambient(new Color(0.55f, 0.62f, 0.78f), new Color(0.50f, 0.48f, 0.45f), new Color(0.30f, 0.26f, 0.22f));
            SceneKit.Shadows(30f);
            var sun = SceneKit.Sun("Sun", new Vector3(42, -35, 0), new Color(1f, 0.95f, 0.86f), 2.2f, true);
            RenderSettings.sun = sun;
            var sky = new Material(Shader.Find("Skybox/Procedural"));
            sky.SetFloat("_SunSize", 0.03f); sky.SetFloat("_AtmosphereThickness", 1.0f); sky.SetFloat("_Exposure", 1.3f);
            sky.SetColor("_GroundColor", new Color(0.45f, 0.42f, 0.40f));
            AssetDatabase.CreateAsset(sky, Dir + "Sky.mat");
            RenderSettings.skybox = sky;

            Prim(PrimitiveType.Plane, "Floor", Mat("Floor", new Color(0.92f, 0.88f, 0.80f), 0.15f), Vector3.zero, new Vector3(4, 1, 4));

            Prim(PrimitiveType.Sphere, "Red sphere", Mat("Red", new Color(0.90f, 0.12f, 0.10f), 0.55f), new Vector3(-1.9f, 1.1f, 0.4f), Vector3.one * 2.2f);
            Prim(PrimitiveType.Sphere, "Blue sphere", Mat("Blue", new Color(0.10f, 0.35f, 0.90f), 0.55f), new Vector3(1.9f, 0.7f, 1.4f), Vector3.one * 1.4f);
            Prim(PrimitiveType.Sphere, "Green sphere", Mat("Green", new Color(0.20f, 0.75f, 0.30f), 0.5f), new Vector3(0.6f, 0.45f, -1.9f), Vector3.one * 0.9f);

            var prof = new Vector2[28];
            for (int i = 0; i < prof.Length; i++) { float a = i * Mathf.PI * 2f / prof.Length; prof[i] = new Vector2(0.3f * Mathf.Sin(a), 0.95f + 0.3f * Mathf.Cos(a)); }
            var torus = SceneKit.Part("Torus", new MeshKit().Lathe(prof, 56).Build("Torus", 60f), Mat("Yellow", new Color(0.98f, 0.80f, 0.10f), 0.5f), new Vector3(0.3f, 1.35f, 0.9f), Quaternion.Euler(70, 0, 0));

            var cone = new[] { new Vector2(0, 0), new Vector2(0, 0.8f), new Vector2(2.2f, 0.02f), new Vector2(2.2f, 0) };
            SceneKit.Part("Cone", new MeshKit().Lathe(cone, 48).Build("Cone", 50f), Mat("Magenta", new Color(0.85f, 0.15f, 0.60f), 0.4f), new Vector3(3.2f, 0, -1.2f), Quaternion.identity);

            var white = Mat("Block", new Color(0.95f, 0.94f, 0.90f), 0.2f);
            Prim(PrimitiveType.Cube, "Block", white, new Vector3(-0.8f, 0.5f, -2.6f), new Vector3(1.6f, 1.0f, 1.0f), 25f);
            Prim(PrimitiveType.Cube, "Block", Mat("Teal", new Color(0.10f, 0.65f, 0.65f), 0.3f), new Vector3(-3.4f, 0.4f, -1.6f), Vector3.one * 0.8f, -15f);

            var pillar = Mat("Pillar", new Color(0.92f, 0.90f, 0.86f), 0.3f);
            for (int i = 0; i < 7; i++)
            {
                float a = (20f + i * 22f) * Mathf.Deg2Rad;
                Prim(PrimitiveType.Cylinder, "Pillar", pillar, new Vector3(Mathf.Cos(a) * 6.5f, 1.4f, Mathf.Sin(a) * 6.5f), new Vector3(0.45f, 1.4f, 0.45f));
            }

            // linear ramp for the zoom view: input value 0 at the left edge to 1 at the right edge
            var ramp = new Texture2D(256, 1, TextureFormat.RGBAHalf, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Ramp" };
            for (int x = 0; x < 256; x++) { float v = x / 255f; ramp.SetPixel(x, 0, new Color(v, v, v, 1)); }
            ramp.Apply(false, false);
            AssetDatabase.CreateAsset(ramp, Dir + "Ramp.asset");

            // ---- cameras: the scene camera renders into a texture (the director sets that up), the display camera shows the result
            var srcCam = SceneKit.Cam("Scene camera", new Rect(0, 0, 1, 1), 42, Color.black, -10);
            srcCam.clearFlags = CameraClearFlags.Skybox; srcCam.cullingMask = ~(1 << 8); srcCam.farClipPlane = 300f;
            SceneKit.Grade(Dir, 0.2f, 0.2f, 10f, 15f, 0.15f, 0f);
            var display = SceneKit.Cam("Display camera", new Rect(0, 0, 1, 1), 40, Color.black, 10, false);
            display.cullingMask = 1 << 8; display.transform.position = new Vector3(0, -50, 0); display.tag = "MainCamera";

            var compose = new Material(Shader.Find("Hidden/GT/HalftoneCompose")); AssetDatabase.CreateAsset(compose, Dir + "Compose.mat");
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Display quad"; Object.DestroyImmediate(quad.GetComponent<Collider>()); quad.layer = 8;
            var qm = Object.Instantiate(quad.GetComponent<MeshFilter>().sharedMesh); qm.name = "DisplayQuad"; qm.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
            AssetDatabase.CreateAsset(qm, Dir + "DisplayQuad.asset");
            quad.GetComponent<MeshFilter>().sharedMesh = qm; quad.GetComponent<Renderer>().sharedMaterial = compose;
            quad.transform.position = display.transform.position + Vector3.forward;

            var ht = new Material(Shader.Find("Hidden/GT/Halftone")); AssetDatabase.CreateAsset(ht, Dir + "Halftone.mat");
            var d = new GameObject("Director").AddComponent<HalftoneDirector>();
            d.src = srcCam; d.compose = compose; d.halftone = ht; d.ramp = ramp; d.torus = torus.transform;
            d.left = LabelSpec.Create("", 0.02f, 0.05f, 32);
            d.mid3 = LabelSpec.Create("", 0.353f, 0.05f, 32);
            d.right3 = LabelSpec.Create("", 0.687f, 0.05f, 32);
            d.right2 = LabelSpec.Create("", 0.52f, 0.05f, 32);
            d.top = LabelSpec.Create("", 0.02f, 0.55f, 32);
        }
    }
}
