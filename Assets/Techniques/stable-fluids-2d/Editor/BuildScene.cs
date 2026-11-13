// 84 stable fluids: a dark stage with one square screen that shows the 60-iteration simulation; FluidDirector runs the
// solvers, renders the stage camera into a texture and a display camera shows the panels or the stage.
using UnityEditor;
using UnityEngine;

namespace GameTech.Posts
{
    public static class FluidScene
    {
        const string Dir = "Assets/Techniques/stable-fluids-2d/";
        const string Lit = "Universal Render Pipeline/Lit";

        static GameObject Prim(PrimitiveType type, string name, Material m, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = m;
            go.transform.position = pos; go.transform.localScale = scale;
            return go;
        }

        public static void Build()
        {
            SceneKit.Ambient(new Color(0.05f, 0.055f, 0.07f), new Color(0.03f, 0.03f, 0.035f), new Color(0.01f, 0.01f, 0.01f));
            SceneKit.Shadows(20f);
            var key = SceneKit.Sun("Key", new Vector3(35, 30, 0), new Color(0.75f, 0.8f, 1f), 0.35f, true);
            RenderSettings.sun = key;
            RenderSettings.skybox = null;

            var floor = SceneKit.Mat(Dir, "Floor", Lit, new Color(0.035f, 0.036f, 0.04f), 0f, 0.82f);
            Prim(PrimitiveType.Plane, "Floor", floor, Vector3.zero, new Vector3(4, 1, 4));
            var wall = SceneKit.Mat(Dir, "Wall", Lit, new Color(0.02f, 0.021f, 0.025f), 0f, 0.3f);
            Prim(PrimitiveType.Cube, "Back wall", wall, new Vector3(0, 4, 3.5f), new Vector3(30, 8, 0.2f));

            var frame = SceneKit.Mat(Dir, "Frame", Lit, new Color(0.08f, 0.08f, 0.09f), 0.6f, 0.7f);
            Prim(PrimitiveType.Cube, "Screen frame", frame, new Vector3(0, 2.0f, 0.06f), new Vector3(3.24f, 3.24f, 0.1f));
            Prim(PrimitiveType.Cylinder, "Stand", frame, new Vector3(0, 0.2f, 0.25f), new Vector3(0.12f, 0.2f, 0.12f));
            Prim(PrimitiveType.Cube, "Stand base", frame, new Vector3(0, 0.02f, 0.25f), new Vector3(1.6f, 0.04f, 0.7f));

            var screen = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(screen, Dir + "Screen.mat");
            var quad = Prim(PrimitiveType.Quad, "Screen", screen, new Vector3(0, 2.0f, 0f), new Vector3(3f, 3f, 1f));
            quad.transform.rotation = Quaternion.identity;   // quad faces -Z, toward the camera

            foreach (var x in new[] { -3.2f, 3.2f })
            {
                var l = new GameObject("Rim light").AddComponent<Light>();
                l.type = LightType.Point; l.color = new Color(0.55f, 0.65f, 1f); l.intensity = 2.5f; l.range = 6f;
                l.transform.position = new Vector3(x, 3.6f, 1.2f);
            }

            // obstacle mask in grid cells: a disc, white = solid (the source's boundary texture convention)
            int n = FluidDirector.Grid;
            var ob = new Texture2D(n, n, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Obstacle" };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float dist = new Vector2(x - 128f, y - 118f).magnitude;
                px[y * n + x] = dist < 24f ? Color.white : Color.black;
            }
            ob.SetPixels(px); ob.Apply(false, false);
            AssetDatabase.CreateAsset(ob, Dir + "Obstacle.asset");

            var srcCam = SceneKit.Cam("Stage camera", new Rect(0, 0, 1, 1), 40, new Color(0.004f, 0.004f, 0.006f), -10);
            srcCam.clearFlags = CameraClearFlags.SolidColor; srcCam.cullingMask = ~(1 << 8); srcCam.farClipPlane = 100f;
            SceneKit.Grade(Dir, 0.35f, 0f, 10f, 5f, 0.25f, 0f);
            var display = SceneKit.Cam("Display camera", new Rect(0, 0, 1, 1), 40, Color.black, 10, false);
            display.cullingMask = 1 << 8; display.transform.position = new Vector3(0, -50, 0); display.tag = "MainCamera";

            var compose = new Material(Shader.Find("Hidden/GT/FluidCompose")); AssetDatabase.CreateAsset(compose, Dir + "Compose.mat");
            var dq = GameObject.CreatePrimitive(PrimitiveType.Quad);
            dq.name = "Display quad"; Object.DestroyImmediate(dq.GetComponent<Collider>()); dq.layer = 8;
            var qm = Object.Instantiate(dq.GetComponent<MeshFilter>().sharedMesh); qm.name = "DisplayQuad"; qm.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
            AssetDatabase.CreateAsset(qm, Dir + "DisplayQuad.asset");
            dq.GetComponent<MeshFilter>().sharedMesh = qm; dq.GetComponent<Renderer>().sharedMaterial = compose;
            dq.transform.position = display.transform.position + Vector3.forward;

            ComputeShader CS(string f) => AssetDatabase.LoadAssetAtPath<ComputeShader>(Dir + f + ".compute");
            var d = new GameObject("Director").AddComponent<FluidDirector>();
            d.src = srcCam; d.compose = compose; d.screen = screen; d.obstacle = ob;
            d.navierStokes = CS("Navier_Stokes"); d.solvers = CS("Solvers"); d.boundary = CS("Boundary_Handling");
            d.utility = CS("StructuredBufferUtility"); d.userInput = CS("UserInput"); d.view = CS("FluidView");
            d.title = LabelSpec.Create("", 0.02f, 0.935f, 30);
            d.a2 = LabelSpec.Create("", 100f / 1920f, 0.06f, 30);
            d.b2 = LabelSpec.Create("", 990f / 1920f, 0.06f, 30);
            d.a3 = LabelSpec.Create("", 30f / 1920f, 0.13f, 28);
            d.b3 = LabelSpec.Create("", 665f / 1920f, 0.13f, 28);
            d.c3 = LabelSpec.Create("", 1300f / 1920f, 0.13f, 28);
        }
    }
}
