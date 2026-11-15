// 91 screen-space cavity: a stone hall built from code: a tiled floor with grout gaps, three steps up to a dais, four
// fluted pillars with moulded bases and capitals, a back wall with framed panels, a cloth hanging in folds from a rod,
// two turned urns and the stand-in mannequin (no character model). Every surface uses GameTech/CavityLit with a flat
// colour, so all the edge and crease shading in the "on" frames comes from the cavity pass. No external assets.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GameTech.Posts
{
    public static class CavityScene
    {
        const string Dir = "Assets/Techniques/screen-space-cavity/";
        const string Lit = "GameTech/CavityLit";

        static Mesh Save(MeshKit k, string name, float smooth = 35f)
        {
            var m = k.Build(name, smooth); AssetDatabase.CreateAsset(m, Dir + name + ".asset"); return m;
        }

        // parametric surface grid; f(u, v) gives the point, outward(u, v) the side that faces out
        static void Grid(MeshKit k, int nu, int nv, System.Func<float, float, Vector3> f, System.Func<float, float, Vector3> outward)
        {
            for (int i = 0; i < nu; i++)
                for (int j = 0; j < nv; j++)
                {
                    float u0 = i / (float)nu, u1 = (i + 1) / (float)nu, v0 = j / (float)nv, v1 = (j + 1) / (float)nv;
                    k.Quad(f(u0, v0), f(u1, v0), f(u1, v1), f(u0, v1), outward((u0 + u1) * 0.5f, (v0 + v1) * 0.5f));
                }
        }

        static Mesh Pillar(string name, float height)
        {
            var k = new MeshKit();
            k.Box(new Vector3(0, 0.06f, 0), new Vector3(0.62f, 0.12f, 0.62f));                 // plinth
            k.Lathe(new[] { new Vector2(0.12f, 0), new Vector2(0.12f, 0.28f), new Vector2(0.16f, 0.28f), new Vector2(0.18f, 0.26f),
                            new Vector2(0.2f, 0.27f), new Vector2(0.23f, 0.25f), new Vector2(0.25f, 0.22f), new Vector2(0.27f, 0.23f),
                            new Vector2(0.29f, 0.21f), new Vector2(0.3f, 0.21f), new Vector2(0.3f, 0) }, 64);   // base mouldings
            float y0 = 0.3f, y1 = height - 0.42f, R = 0.2f;
            int flutes = 20;
            System.Func<float, float> rad = th => R * (1f - 0.07f * (0.5f - 0.5f * Mathf.Cos(flutes * th)));
            Grid(k, 160, 1, (u, v) => { float th = u * Mathf.PI * 2; float rr = rad(th); return new Vector3(rr * Mathf.Cos(th), Mathf.Lerp(y0, y1, v), rr * Mathf.Sin(th)); },
                 (u, v) => { float th = u * Mathf.PI * 2; return new Vector3(Mathf.Cos(th), 0, Mathf.Sin(th)); });
            k.Lathe(new[] { new Vector2(height - 0.42f, 0), new Vector2(height - 0.42f, 0.2f), new Vector2(height - 0.38f, 0.23f),
                            new Vector2(height - 0.34f, 0.2f), new Vector2(height - 0.26f, 0.22f), new Vector2(height - 0.18f, 0.28f),
                            new Vector2(height - 0.15f, 0.28f), new Vector2(height - 0.15f, 0) }, 64);         // capital
            k.Box(new Vector3(0, height - 0.075f, 0), new Vector3(0.66f, 0.15f, 0.66f));        // abacus
            return Save(k, name);
        }

        static Mesh Urn(string name)
        {
            var k = new MeshKit();
            k.Lathe(new[] { new Vector2(0, 0), new Vector2(0, 0.14f), new Vector2(0.04f, 0.15f), new Vector2(0.06f, 0.12f), new Vector2(0.1f, 0.11f),
                            new Vector2(0.2f, 0.19f), new Vector2(0.3f, 0.245f), new Vector2(0.4f, 0.26f), new Vector2(0.5f, 0.235f),
                            new Vector2(0.56f, 0.2f), new Vector2(0.58f, 0.21f), new Vector2(0.6f, 0.2f), new Vector2(0.66f, 0.13f),
                            new Vector2(0.7f, 0.12f), new Vector2(0.74f, 0.16f), new Vector2(0.77f, 0.17f), new Vector2(0.79f, 0.12f),
                            new Vector2(0.79f, 0) }, 72);
            return Save(k, name, 30f);
        }

        static Mesh Cloth(string name)
        {
            var k = new MeshKit();
            float w = 1.1f, h = 2.1f, th = 0.012f;
            System.Func<float, float, float> z = (u, v) =>
                0.055f * (1f - 0.45f * v) * Mathf.Sin(2 * Mathf.PI * 3.5f * u + 0.6f)
                + 0.02f * Mathf.Sin(2 * Mathf.PI * (2.2f * u + 1.3f * v)) * v;
            System.Func<float, float, Vector3> front = (u, v) => new Vector3((u - 0.5f) * w * (1f - 0.08f * (1 - v)), -v * h, z(u, v));
            Grid(k, 88, 60, front, (u, v) => Vector3.back);
            Grid(k, 88, 60, (u, v) => front(u, v) + new Vector3(0, 0, th), (u, v) => Vector3.forward);
            return Save(k, name, 50f);
        }

        public static void Build()
        {
            SceneKit.Shadows(25f);
            var sun = SceneKit.Sun("Sun", new Vector3(32, -68, 0), new Color(1f, 0.9f, 0.78f), 1.7f, true);
            RenderSettings.sun = sun;
            RenderSettings.skybox = null;
            var bg = new Color(0.05f, 0.047f, 0.045f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogColor = bg;
            RenderSettings.fogStartDistance = 7f; RenderSettings.fogEndDistance = 18f;

            var stone = SceneKit.Mat(Dir, "Stone", Lit, new Color(0.62f, 0.58f, 0.52f), 0f, 0.15f);
            var tile = SceneKit.Mat(Dir, "Tile", Lit, new Color(0.5f, 0.47f, 0.43f), 0f, 0.2f);
            var grout = SceneKit.Mat(Dir, "Grout", Lit, new Color(0.3f, 0.28f, 0.26f), 0f, 0.05f);
            var wallM = SceneKit.Mat(Dir, "Wall", Lit, new Color(0.56f, 0.53f, 0.49f), 0f, 0.1f);
            var cloth = SceneKit.Mat(Dir, "Cloth", Lit, new Color(0.42f, 0.07f, 0.06f), 0f, 0.25f);
            var clay = SceneKit.Mat(Dir, "Clay", Lit, new Color(0.55f, 0.31f, 0.2f), 0f, 0.3f);
            var metal = SceneKit.Mat(Dir, "Rod", Lit, new Color(0.16f, 0.15f, 0.14f), 0f, 0.4f);

            // floor: 0.5 m tiles with 3 cm gaps over a darker grout bed
            var rnd = new System.Random(91);
            var fk = new MeshKit();
            for (int ix = -10; ix < 10; ix++)
                for (int iz = -8; iz < 4; iz++)
                {
                    float hgt = (float)rnd.NextDouble() * 0.006f;
                    fk.Box(new Vector3(ix * 0.5f + 0.25f, -0.03f + hgt, iz * 0.5f + 0.25f), new Vector3(0.47f, 0.06f, 0.47f));
                }
            SceneKit.Part("Floor tiles", Save(fk, "FloorTiles"), tile, Vector3.zero, Quaternion.identity);
            var gk = new MeshKit(); gk.Box(new Vector3(0, -0.05f, 0), new Vector3(20f, 0.04f, 16f));
            SceneKit.Part("Grout", Save(gk, "Grout"), grout, Vector3.zero, Quaternion.identity);

            // steps up to the dais, and the dais
            var sk = new MeshKit();
            sk.Box(new Vector3(0, 0.075f, 2.8f), new Vector3(6.4f, 0.15f, 2.4f));
            sk.Box(new Vector3(0, 0.225f, 3.0f), new Vector3(6.4f, 0.15f, 2.0f));
            sk.Box(new Vector3(0, 0.375f, 3.2f), new Vector3(6.4f, 0.15f, 1.6f));
            SceneKit.Part("Steps", Save(sk, "Steps"), stone, Vector3.zero, Quaternion.identity);

            // back wall with framed panels, a skirting and a cornice
            var wk = new MeshKit();
            wk.Box(new Vector3(0, 0.45f + 1.7f, 4.05f), new Vector3(6.4f, 3.4f, 0.3f));
            wk.Box(new Vector3(0, 0.55f, 3.87f), new Vector3(6.4f, 0.2f, 0.06f));
            wk.Box(new Vector3(0, 3.62f, 3.84f), new Vector3(6.4f, 0.16f, 0.12f));
            wk.Box(new Vector3(0, 3.5f, 3.86f), new Vector3(6.4f, 0.06f, 0.08f));
            foreach (float px in new[] { -2.15f, -1.05f, 1.05f, 2.15f })
                foreach (float py in new[] { 1.3f, 2.6f })
                {
                    float s = 0.92f, b = 0.1f, zf = 3.9f;
                    wk.Box(new Vector3(px, py + s / 2 - b / 2, zf - 0.03f), new Vector3(s, b, 0.06f));
                    wk.Box(new Vector3(px, py - s / 2 + b / 2, zf - 0.03f), new Vector3(s, b, 0.06f));
                    wk.Box(new Vector3(px - s / 2 + b / 2, py, zf - 0.03f), new Vector3(b, s - 2 * b, 0.06f));
                    wk.Box(new Vector3(px + s / 2 - b / 2, py, zf - 0.03f), new Vector3(b, s - 2 * b, 0.06f));
                    wk.Box(new Vector3(px, py, zf - 0.02f), new Vector3(s - 2 * b - 0.12f, s - 2 * b - 0.12f, 0.04f));
                }
            SceneKit.Part("Back wall", Save(wk, "BackWall"), wallM, Vector3.zero, Quaternion.identity);

            // pillars
            var pFloor = Pillar("PillarTall", 3.4f);
            var pDais = Pillar("PillarShort", 2.95f);
            SceneKit.Part("Pillar FL", pFloor, stone, new Vector3(-2.2f, 0, 0.9f), Quaternion.identity);
            SceneKit.Part("Pillar FR", pFloor, stone, new Vector3(2.2f, 0, 0.9f), Quaternion.identity);
            SceneKit.Part("Pillar BL", pDais, stone, new Vector3(-2.75f, 0.45f, 3.35f), Quaternion.identity);
            SceneKit.Part("Pillar BR", pDais, stone, new Vector3(2.75f, 0.45f, 3.35f), Quaternion.identity);

            // cloth on a rod in front of the wall
            SceneKit.Part("Cloth", Cloth("Cloth"), cloth, new Vector3(0, 3.22f, 3.72f), Quaternion.identity);
            var rk = new MeshKit();
            rk.Lathe(new[] { new Vector2(-0.72f, 0), new Vector2(-0.72f, 0.025f), new Vector2(0.72f, 0.025f), new Vector2(0.72f, 0) }, 24);
            rk.Lathe(new[] { new Vector2(-0.82f, 0), new Vector2(-0.8f, 0.045f), new Vector2(-0.76f, 0.05f), new Vector2(-0.72f, 0.03f), new Vector2(-0.72f, 0) }, 24);
            rk.Lathe(new[] { new Vector2(0.72f, 0), new Vector2(0.72f, 0.03f), new Vector2(0.76f, 0.05f), new Vector2(0.8f, 0.045f), new Vector2(0.82f, 0) }, 24);
            SceneKit.Part("Rod", Save(rk, "Rod"), metal, new Vector3(0, 3.24f, 3.74f), Quaternion.Euler(0, 0, 90));

            // urns
            var urn = Urn("Urn");
            var u1 = SceneKit.Part("Urn floor", urn, clay, new Vector3(-0.85f, 0, 0.75f), Quaternion.identity); u1.transform.localScale = Vector3.one * 1.25f;
            SceneKit.Part("Urn dais", urn, clay, new Vector3(1.55f, 0.45f, 3.2f), Quaternion.Euler(0, 40, 0));

            var man = Mannequin.Build(Lit, Dir, (m, part) =>
            {
                m.SetColor("_BaseColor", part == "skin" ? new Color(0.72f, 0.6f, 0.52f) : new Color(0.2f, 0.24f, 0.3f));
                m.SetFloat("_Smoothness", 0.25f);
            });
            man.transform.position = new Vector3(0.75f, 0f, 0.55f);
            man.transform.rotation = Quaternion.Euler(0, 160f, 0);

            var srcCam = SceneKit.Cam("Scene camera", new Rect(0, 0, 1, 1), 38, bg, -10);
            srcCam.clearFlags = CameraClearFlags.SolidColor; srcCam.cullingMask = ~(1 << 8); srcCam.farClipPlane = 60f; srcCam.nearClipPlane = 0.05f;
            SceneKit.Grade(Dir, 0.25f, 0f, 12f, 5f, 0.3f, 0f);
            var display = SceneKit.Cam("Display camera", new Rect(0, 0, 1, 1), 40, Color.black, 10, false);
            display.cullingMask = 1 << 8; display.transform.position = new Vector3(0, -50, 0); display.tag = "MainCamera";

            var compose = new Material(Shader.Find("Hidden/GT/CavityCompose")); AssetDatabase.CreateAsset(compose, Dir + "Compose.mat");
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Display quad"; Object.DestroyImmediate(quad.GetComponent<Collider>()); quad.layer = 8;
            var qm = Object.Instantiate(quad.GetComponent<MeshFilter>().sharedMesh); qm.name = "DisplayQuad"; qm.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
            AssetDatabase.CreateAsset(qm, Dir + "DisplayQuad.asset");
            quad.GetComponent<MeshFilter>().sharedMesh = qm; quad.GetComponent<Renderer>().sharedMaterial = compose;
            quad.transform.position = display.transform.position + Vector3.forward;

            var list = new List<Renderer>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (r.sharedMaterial != null && r.sharedMaterial.shader.name == Lit) list.Add(r);

            var dir = new GameObject("Director").AddComponent<CavityDirector>();
            dir.src = srcCam; dir.compose = compose; dir.cavityShader = Shader.Find("Hidden/GT/ScreenSpaceCavity");
            dir.renderers = list.ToArray(); dir.character = man.transform;
            dir.title = LabelSpec.Create("", 0.012f, 0.935f, 30);
            dir.l1 = new LabelSpec[3]; dir.l2 = new LabelSpec[3];
            for (int i = 0; i < 3; i++) { dir.l1[i] = LabelSpec.Create("", 0.012f, 0.058f, 28); dir.l2[i] = LabelSpec.Create("", 0.012f, 0.018f, 22); }
            Debug.Log($"[gt] cavity scene: {list.Count} renderers with {Lit}");
        }
    }
}
