// 95 Casual PRT: a closed room built from code, red left wall, green back wall, white walls and ceiling, a wide window
// in the right wall, a white sphere, a white block and a blue cube. One directional sun is the only light; there is
// no ambient light and nothing is baked. Every part has a mesh collider so the probes can capture their surfels.
// Built from code; no external assets.
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GameTech.Posts
{
    public static class PRTScene
    {
        const string Dir = "Assets/Techniques/casual-prt/";
        const string Lit = "Universal Render Pipeline/Lit";

        static Mesh Save(MeshKit k, string name, float smooth = 35f)
        {
            var m = k.Build(name, smooth);
            AssetDatabase.CreateAsset(m, Dir + name + ".asset"); return m;
        }

        static GameObject Solid(string name, Mesh mesh, Material m, Vector3 p, Quaternion r)
        {
            var go = SceneKit.Part(name, mesh, m, p, r);
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        public static void Build()
        {
            RenderSettings.skybox = null; RenderSettings.fog = false;
            var dir = new GameObject("Director").AddComponent<PRTDirector>();

            var white = SceneKit.Mat(Dir, "White", Lit, new Color(0.78f, 0.77f, 0.74f), 0f, 0.15f);
            var floorM = SceneKit.Mat(Dir, "Floor", Lit, new Color(0.62f, 0.58f, 0.52f), 0f, 0.2f);
            var red = SceneKit.Mat(Dir, "Red", Lit, new Color(0.8f, 0.1f, 0.07f), 0f, 0.1f);
            var green = SceneKit.Mat(Dir, "Green", Lit, new Color(0.12f, 0.62f, 0.16f), 0f, 0.1f);
            var blue = SceneKit.Mat(Dir, "Blue", Lit, new Color(0.08f, 0.2f, 0.85f), 0f, 0.2f);
            var outside = SceneKit.Mat(Dir, "Outside", Lit, new Color(0.45f, 0.42f, 0.36f), 0f, 0.1f);

            // room: inner faces at x = +-4, z = +-4, floor 0, ceiling 3.3; walls 0.3 m thick
            var fk = new MeshKit(); fk.Box(new Vector3(0, -0.15f, 0), new Vector3(8.6f, 0.3f, 8.6f));
            Solid("Floor", Save(fk, "Floor"), floorM, Vector3.zero, Quaternion.identity);
            var ck = new MeshKit(); ck.Box(new Vector3(0, 3.45f, 0), new Vector3(8.6f, 0.3f, 8.6f));
            Solid("Ceiling", Save(ck, "Ceiling"), white, Vector3.zero, Quaternion.identity);
            var lk = new MeshKit(); lk.Box(new Vector3(-4.15f, 1.65f, 0), new Vector3(0.3f, 3.3f, 8.6f));
            Solid("Red wall", Save(lk, "RedWall"), red, Vector3.zero, Quaternion.identity);
            var bk = new MeshKit(); bk.Box(new Vector3(0, 1.65f, 4.15f), new Vector3(8.0f, 3.3f, 0.3f));
            Solid("Green wall", Save(bk, "GreenWall"), green, Vector3.zero, Quaternion.identity);
            var wk = new MeshKit();
            wk.Box(new Vector3(0, 1.65f, -4.15f), new Vector3(8.0f, 3.3f, 0.3f));                  // front wall
            // right wall with a window from z -2.2 to 2.2, y 0.5 to 2.8
            wk.Box(new Vector3(4.15f, 1.65f, -3.2f), new Vector3(0.3f, 3.3f, 2.0f));
            wk.Box(new Vector3(4.15f, 1.65f, 3.2f), new Vector3(0.3f, 3.3f, 2.0f));
            wk.Box(new Vector3(4.15f, 0.25f, 0f), new Vector3(0.3f, 0.5f, 4.4f));
            wk.Box(new Vector3(4.15f, 3.05f, 0f), new Vector3(0.3f, 0.5f, 4.4f));
            Solid("White walls", Save(wk, "WhiteWalls"), white, Vector3.zero, Quaternion.identity);
            var ok = new MeshKit(); ok.Box(new Vector3(14f, -0.15f, 0), new Vector3(19.4f, 0.3f, 30f));
            Solid("Outside ground", Save(ok, "OutsideGround"), outside, Vector3.zero, Quaternion.identity);

            // props
            var prof = new Vector2[34];
            for (int i = 0; i <= 32; i++) { float a = Mathf.PI * i / 32f; prof[i] = new Vector2(-Mathf.Cos(a), Mathf.Sin(a)); }
            prof[33] = new Vector2(1, 0);
            var sk = new MeshKit(); sk.Lathe(prof, 48); var sphere = Save(sk, "Sphere", 60f);
            var ball = Solid("White sphere", sphere, white, new Vector3(-1.7f, 0.65f, 1.0f), Quaternion.identity);
            ball.transform.localScale = Vector3.one * 0.65f;
            var blk = new MeshKit(); blk.Box(new Vector3(0, 0.8f, 0), new Vector3(1.0f, 1.6f, 1.0f));
            Solid("White block", Save(blk, "Block", 20f), white, new Vector3(0.6f, 0f, 2.5f), Quaternion.Euler(0, 25f, 0));
            var cub = new MeshKit(); cub.Box(new Vector3(0, 0.45f, 0), new Vector3(0.9f, 0.9f, 0.9f));
            Solid("Blue cube", Save(cub, "Cube", 20f), blue, new Vector3(-2.9f, 0f, -1.3f), Quaternion.Euler(0, -20f, 0));

            dir.sun = SceneKit.Sun("Sun", new Vector3(16f, -40f, 0f), new Color(1f, 0.93f, 0.82f), 4f, true);
            dir.sun.shadowStrength = 1f; dir.sun.shadowBias = 0.02f; dir.sun.shadowNormalBias = 0.3f;
            SceneKit.Shadows(30f);

            var c = SceneKit.Cam("Band camera", new Rect(0, 0, 1, 1), 62, Color.black, -10, false);
            c.nearClipPlane = 0.05f; c.farClipPlane = 60f;
            c.GetUniversalAdditionalCameraData().antialiasing = AntialiasingMode.None;
            dir.cam = c;

            dir.relight = AssetDatabase.LoadAssetAtPath<ComputeShader>(Dir + "Shaders/SurfelReLightCS.compute");
            dir.posMat = new Material(Shader.Find("CasualPRT/GbufferWorldPos")); AssetDatabase.CreateAsset(dir.posMat, Dir + "GbufferWorldPos.mat");
            dir.nrmMat = new Material(Shader.Find("CasualPRT/GbufferNormal")); AssetDatabase.CreateAsset(dir.nrmMat, Dir + "GbufferNormal.mat");
            dir.composite = new Material(Shader.Find("Hidden/GT/PRTComposite")); AssetDatabase.CreateAsset(dir.composite, Dir + "PRTComposite.mat");

            var display = SceneKit.Cam("Display camera", new Rect(0, 0, 1, 1), 40, Color.black, 10, false);
            display.cullingMask = 1 << 8; display.transform.position = new Vector3(0, -50, 0); display.tag = "MainCamera";
            var compose = new Material(Shader.Find("Hidden/GT/PRTCompose")); AssetDatabase.CreateAsset(compose, Dir + "Compose.mat");
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Display quad"; Object.DestroyImmediate(quad.GetComponent<Collider>()); quad.layer = 8;
            var qm = Object.Instantiate(quad.GetComponent<MeshFilter>().sharedMesh); qm.name = "DisplayQuad"; qm.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
            AssetDatabase.CreateAsset(qm, Dir + "DisplayQuad.asset");
            quad.GetComponent<MeshFilter>().sharedMesh = qm; quad.GetComponent<Renderer>().sharedMaterial = compose;
            quad.transform.position = display.transform.position + Vector3.forward;
            c.cullingMask = ~(1 << 8);

            dir.compose = compose;
            dir.title = LabelSpec.Create("", 0.012f, 0.935f, 30);
            dir.l1 = new LabelSpec[3]; dir.l2 = new LabelSpec[3];
            for (int i = 0; i < 3; i++) { dir.l1[i] = LabelSpec.Create("", 0.012f, 0.058f, 28); dir.l2[i] = LabelSpec.Create("", 0.012f, 0.018f, 22); }
            AssetDatabase.SaveAssets();
            Debug.Log($"[gt] prt scene: compute {(dir.relight != null)}, pos {(Shader.Find("CasualPRT/GbufferWorldPos") != null)}, composite {(Shader.Find("Hidden/GT/PRTComposite") != null)}");
        }
    }
}
