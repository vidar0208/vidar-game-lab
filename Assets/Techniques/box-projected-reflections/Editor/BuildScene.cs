// Builds the box-projection room from code: an 8 x 3.2 x 6 m room with panelled walls, a window with mullions on the
// left wall (low sun comes through it), two ceiling light strips, a dark baseboard, an orange panel on the back wall,
// a teal band on the right wall, a polished tile floor and three metal props near the back wall. One reflection probe
// at the room centre, box = the room, is baked here (props excluded) and handed to the BoxReflect materials.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    public static class BoxScene
    {
        const string Dir = "Assets/Techniques/box-projected-reflections/";
        const int PropLayer = 11;

        // axis-aligned box mesh whose UVs are in metres on every face, so a 1 m texture tiles at real scale
        static Mesh BoxMesh(Vector3 size, Vector3 centre, string name)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            Vector3 h = size * 0.5f;
            Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var d in dirs)
            {
                Vector3 a = d.x != 0 ? Vector3.forward : Vector3.right, b = d.y != 0 ? Vector3.forward : Vector3.up;
                if (d.x != 0) b = Vector3.up;
                if (Vector3.Dot(Vector3.Cross(a, b), d) > 0) a = -a;   // clockwise seen from outside (Unity front face)
                int s = v.Count;
                for (int i = 0; i < 4; i++)
                {
                    float sa = (i & 1) == 0 ? -1 : 1, sb = (i & 2) == 0 ? -1 : 1;
                    var p = Vector3.Scale(d, h) + Vector3.Scale(a * sa, h) + Vector3.Scale(b * sb, h);
                    v.Add(p); n.Add(d);
                    var wp = p + centre; uv.Add(new Vector2(Vector3.Dot(wp, a), Vector3.Dot(wp, b)));   // world metres
                }
                tri.AddRange(new[] { s, s + 2, s + 1, s + 1, s + 2, s + 3 });
            }
            var m = new Mesh { name = name }; m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateBounds();
            return m;
        }

        static GameObject Box(string name, Vector3 min, Vector3 max, Material mat, List<Mesh> meshes, int layer = 0)
        {
            var m = BoxMesh(max - min, (min + max) * 0.5f, name); meshes.Add(m);
            var go = SceneKit.Part(name, m, mat, (min + max) * 0.5f, Quaternion.identity); go.layer = layer; return go;
        }

        static float Smooth(float a, float b, float x) { x = Mathf.Clamp01((x - a) / (b - a)); return x * x * (3 - 2 * x); }

        static Texture2D Tex(string name, int n, System.Func<float, float, Color> f)
        {
            var px = new Color[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) px[y * n + x] = f((x + 0.5f) / n, (y + 0.5f) / n);
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true); t.SetPixels(px); t.Apply(true);
            t.wrapMode = TextureWrapMode.Repeat; t.filterMode = FilterMode.Trilinear; t.anisoLevel = 8;
            AssetDatabase.CreateAsset(t, Dir + name + ".asset"); return t;
        }

        static Material Lit(string name, Color c, float smooth, Texture2D tex = null, Vector2? tile = null)
        {
            var m = SceneKit.Mat(Dir, name, "Universal Render Pipeline/Lit", c, 0, smooth);
            if (tex != null) { m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", tile ?? Vector2.one); }
            return m;
        }

        static Material Emissive(string name, Color c, float intensity)
        {
            var m = SceneKit.Mat(Dir, name, "Universal Render Pipeline/Lit", c, 0, 0.2f);
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * intensity); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            return m;
        }

        static Material Refl(string name, Color c, float metallic, float smooth, Texture2D tex = null, Vector2? tile = null)
        {
            var m = new Material(Shader.Find("GameTech/BoxReflect"));
            m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", smooth);
            if (tex != null) { m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", tile ?? Vector2.one); }
            AssetDatabase.CreateAsset(m, Dir + name + ".mat"); return m;
        }

        public static void Build()
        {
            var meshes = new List<Mesh>();
            RenderSettings.skybox = null; RenderSettings.fog = false;
            SceneKit.Ambient(new Color(0.46f, 0.48f, 0.54f), new Color(0.4f, 0.37f, 0.33f), new Color(0.2f, 0.17f, 0.14f));
            // the ambient probe is not regenerated in a batch build, so set it directly: warm bounce from below, cool from above
            var sh = new SphericalHarmonicsL2(); sh.AddAmbientLight(new Color(0.36f, 0.34f, 0.32f));
            sh.AddDirectionalLight(Vector3.up, new Color(0.12f, 0.13f, 0.16f), 1f); sh.AddDirectionalLight(Vector3.down, new Color(0.14f, 0.1f, 0.07f), 1f);
            RenderSettings.ambientProbe = sh; RenderSettings.ambientIntensity = 1f;
            SceneKit.Shadows(20f);

            // room interior: x -4..4, y 0..3.2, z -3..3
            Vector3 rMin = new Vector3(-4f, 0f, -3f), rMax = new Vector3(4f, 3.2f, 3f); const float T = 0.25f;

            // textures: wall panels (1 m with dark seams), floor tiles (0.6 m), ceiling
            var wallT = Tex("WallPanel", 256, (u, v) =>
            {
                float seam = Mathf.Min(Mathf.Min(u, 1 - u), Mathf.Min(v, 1 - v));
                float k = Smooth(0.002f, 0.006f, seam);
                return Color.Lerp(new Color(0.3f, 0.29f, 0.28f), new Color(0.72f, 0.68f, 0.62f), k);
            });
            var floorT = Tex("FloorTile", 256, (u, v) =>
            {
                float seam = Mathf.Min(Mathf.Min(u, 1 - u), Mathf.Min(v, 1 - v));
                float k = Smooth(0.006f, 0.016f, seam);
                return Color.Lerp(new Color(0.42f, 0.4f, 0.37f), new Color(0.2f, 0.2f, 0.22f), k);
            });
            var wallM = Lit("Wall", Color.white, 0.25f, wallT, Vector2.one);
            var ceilM = Lit("Ceiling", new Color(0.55f, 0.55f, 0.56f), 0.1f);
            var baseM = Lit("Baseboard", new Color(0.03f, 0.03f, 0.035f), 0.4f);
            var orangeM = Lit("OrangePanel", new Color(0.95f, 0.36f, 0.08f), 0.3f);
            var tealM = Lit("TealBand", new Color(0.04f, 0.32f, 0.34f), 0.3f);
            var frameM = Lit("WindowFrame", new Color(0.05f, 0.05f, 0.055f), 0.4f);
            var stripM = Emissive("LightStrip", new Color(1f, 0.93f, 0.82f), 10f);
            var floorM = Refl("Floor", Color.white, 0f, 0.9f, floorT, new Vector2(1f / 0.6f, 1f / 0.6f));

            // floor and ceiling
            Box("Floor", new Vector3(rMin.x - T, -T, rMin.z - T), new Vector3(rMax.x + T, 0f, rMax.z + T), floorM, meshes);
            Box("Ceiling", new Vector3(rMin.x - T, rMax.y, rMin.z - T), new Vector3(rMax.x + T, rMax.y + T, rMax.z + T), ceilM, meshes);
            // back (z+), front (z-), right (x+)
            Box("Wall back", new Vector3(rMin.x - T, 0, rMax.z), new Vector3(rMax.x + T, rMax.y, rMax.z + T), wallM, meshes);
            Box("Wall front", new Vector3(rMin.x - T, 0, rMin.z - T), new Vector3(rMax.x + T, rMax.y, rMin.z), wallM, meshes);
            Box("Wall right", new Vector3(rMax.x, 0, rMin.z), new Vector3(rMax.x + T, rMax.y, rMax.z), wallM, meshes);
            // left wall (x-) with a window: z -1.4..1.0, y 0.7..2.5
            float wz0 = -1.4f, wz1 = 1.0f, wy0 = 0.7f, wy1 = 2.5f; float lx0 = rMin.x - T, lx1 = rMin.x;
            Box("Wall left a", new Vector3(lx0, 0, rMin.z), new Vector3(lx1, rMax.y, wz0), wallM, meshes);
            Box("Wall left b", new Vector3(lx0, 0, wz1), new Vector3(lx1, rMax.y, rMax.z), wallM, meshes);
            Box("Wall left sill", new Vector3(lx0, 0, wz0), new Vector3(lx1, wy0, wz1), wallM, meshes);
            Box("Wall left head", new Vector3(lx0, wy1, wz0), new Vector3(lx1, rMax.y, wz1), wallM, meshes);
            Box("Mullion v", new Vector3(lx0 + 0.08f, wy0, -0.24f), new Vector3(lx1 - 0.08f, wy1, -0.16f), frameM, meshes);
            Box("Mullion h", new Vector3(lx0 + 0.08f, 1.56f, wz0), new Vector3(lx1 - 0.08f, 1.64f, wz1), frameM, meshes);

            // baseboard all round, orange panel on the back wall, teal band on the right wall, two ceiling light strips
            Box("Base back", new Vector3(rMin.x, 0, rMax.z - 0.03f), new Vector3(rMax.x, 0.12f, rMax.z), baseM, meshes);
            Box("Base front", new Vector3(rMin.x, 0, rMin.z), new Vector3(rMax.x, 0.12f, rMin.z + 0.03f), baseM, meshes);
            Box("Base right", new Vector3(rMax.x - 0.03f, 0, rMin.z), new Vector3(rMax.x, 0.12f, rMax.z), baseM, meshes);
            Box("Base left a", new Vector3(rMin.x, 0, rMin.z), new Vector3(rMin.x + 0.03f, 0.12f, wz0), baseM, meshes);
            Box("Base left b", new Vector3(rMin.x, 0, wz1), new Vector3(rMin.x + 0.03f, 0.12f, rMax.z), baseM, meshes);
            Box("Orange panel", new Vector3(-1.3f, 0.9f, rMax.z - 0.04f), new Vector3(0.5f, 2.0f, rMax.z), orangeM, meshes);
            Box("Teal band", new Vector3(rMax.x - 0.04f, 0.12f, rMin.z), new Vector3(rMax.x, 0.95f, rMax.z), tealM, meshes);
            Box("Strip L", new Vector3(-1.6f, rMax.y - 0.05f, -2.4f), new Vector3(-1.45f, rMax.y, 2.4f), stripM, meshes);
            Box("Strip R", new Vector3(1.45f, rMax.y - 0.05f, -2.4f), new Vector3(1.6f, rMax.y, 2.4f), stripM, meshes);
            Box("Strip back", new Vector3(rMax.x - 0.9f, 0.12f, rMax.z - 0.05f), new Vector3(rMax.x - 0.84f, rMax.y, rMax.z), Emissive("LightStripWarm", new Color(1f, 0.6f, 0.3f), 5f), meshes);

            // sun through the window, sky behind it
            var sun = SceneKit.Sun("Sun", new Vector3(32f, 75f, 0f), new Color(1f, 0.86f, 0.68f), 4f, true);
            sun.shadowBias = 0.02f; sun.shadowNormalBias = 0.2f;
            var skyM = new Material(Shader.Find("GameTech/BoxSky")); skyM.SetFloat("_Exposure", 1.6f); AssetDatabase.CreateAsset(skyM, Dir + "Sky.mat");
            var sky = GameObject.CreatePrimitive(PrimitiveType.Sphere); sky.name = "Sky"; Object.DestroyImmediate(sky.GetComponent<Collider>());
            sky.transform.localScale = Vector3.one * 60f;
            var smr = sky.GetComponent<MeshRenderer>(); smr.sharedMaterial = skyM; smr.shadowCastingMode = ShadowCastingMode.Off; smr.receiveShadows = false;

            // props near the back wall (excluded from the probe)
            var chrome = Refl("Chrome", new Color(0.95f, 0.95f, 0.96f), 1f, 0.96f);
            var gold = Refl("Gold", new Color(1f, 0.76f, 0.42f), 1f, 0.9f);
            var steel = Refl("Steel", new Color(0.8f, 0.82f, 0.85f), 1f, 0.93f);
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere); sphere.name = "Chrome sphere"; Object.DestroyImmediate(sphere.GetComponent<Collider>());
            sphere.transform.position = new Vector3(1.0f, 0.45f, 2.4f); sphere.transform.localScale = Vector3.one * 0.9f; sphere.GetComponent<MeshRenderer>().sharedMaterial = chrome; sphere.layer = PropLayer;
            var cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder); cyl.name = "Gold column"; Object.DestroyImmediate(cyl.GetComponent<Collider>());
            cyl.transform.position = new Vector3(-1.9f, 0.6f, 2.55f); cyl.transform.localScale = new Vector3(0.56f, 0.6f, 0.56f); cyl.GetComponent<MeshRenderer>().sharedMaterial = gold; cyl.layer = PropLayer;
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.name = "Steel block"; Object.DestroyImmediate(cube.GetComponent<Collider>());
            cube.transform.SetPositionAndRotation(new Vector3(2.85f, 0.35f, 2.35f), Quaternion.Euler(0, 28f, 0)); cube.transform.localScale = new Vector3(0.7f, 0.7f, 0.7f); cube.GetComponent<MeshRenderer>().sharedMaterial = steel; cube.layer = PropLayer;

            foreach (var m in meshes) AssetDatabase.CreateAsset(m, Dir + "Mesh_" + m.name.Replace(" ", "_") + ".asset");

            // one probe at the room centre, box = the room; bake it without the props
            var probePos = new Vector3(0f, 1.6f, 0f);
            var probe = new GameObject("Reflection probe").AddComponent<ReflectionProbe>();
            probe.transform.position = probePos; probe.size = rMax - rMin; probe.center = (rMin + rMax) * 0.5f - probePos;
            probe.boxProjection = true; probe.resolution = 256; probe.hdr = true;
            probe.cullingMask = ~((1 << PropLayer) | (1 << 10)); probe.nearClipPlane = 0.05f; probe.farClipPlane = 80f;
            // realtime probe rendered once by the director at start (during the capture warm-up), props excluded
            probe.mode = ReflectionProbeMode.Realtime; probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing; probe.importance = 1;

            SceneKit.Grade(Dir, 0.6f, 0.15f, 14f, 6f, 0.28f, 0.06f);

            var dir = new GameObject("Director").AddComponent<BoxDirector>();
            dir.probe = probe; dir.reflMats = new[] { floorM, chrome, gold, steel }; dir.probePos = probePos; dir.boxMin = rMin; dir.boxMax = rMax; dir.floorSmooth = 0.9f;
            dir.camA = SceneKit.Cam("Cam A", new Rect(0, 0, 1, 1), 55f, Color.black, -5, false);
            dir.camB = SceneKit.Cam("Cam B", new Rect(0, 0, 1, 1), 55f, Color.black, -6, false);
            foreach (var c in new[] { dir.camA, dir.camB }) { c.nearClipPlane = 0.03f; c.farClipPlane = 80f; c.cullingMask = ~(1 << 10); }

            var disp = new Material(Shader.Find("GameTech/BoxComposite")); AssetDatabase.CreateAsset(disp, Dir + "Display.mat"); dir.display = disp;
            var dcam = SceneKit.Cam("Display cam", new Rect(0, 0, 1, 1), 60f, Color.black, 0, true);
            dcam.orthographic = true; dcam.orthographicSize = 0.5f; dcam.cullingMask = 1 << 10; dcam.transform.position = new Vector3(500f, 0f, -1f);
            dcam.nearClipPlane = 0.01f; dcam.farClipPlane = 5f;
            var dq = GameObject.CreatePrimitive(PrimitiveType.Quad); dq.name = "Display quad"; dq.layer = 10; Object.DestroyImmediate(dq.GetComponent<Collider>());
            dq.transform.position = new Vector3(500f, 0f, 0f); dq.transform.localScale = new Vector3(16f / 9f, 1f, 1f);
            var dmr = dq.GetComponent<MeshRenderer>(); dmr.sharedMaterial = disp; dmr.shadowCastingMode = ShadowCastingMode.Off;

            dir.title = LabelSpec.Create("", 0.012f, 0.93f, 40);
            dir.l1 = LabelSpec.Create("", 0.012f, 0.058f, 34); dir.l2 = LabelSpec.Create("", 0.012f, 0.018f, 30);
            dir.m1 = LabelSpec.Create("", 0.512f, 0.058f, 34); dir.m2 = LabelSpec.Create("", 0.512f, 0.018f, 30);
            AssetDatabase.SaveAssets();
            Debug.Log($"[gt] built: reflect shader {floorM.shader.isSupported}, composite {disp.shader.isSupported}");
        }
    }
}
