// 92 specular probes: a dark room lit only by baked lights (four pendant / sconce point lights, no realtime light),
// a glossy floor and glossy props (chrome ball, ceramic vase, copper jug, black ceramic sphere). The builder bakes the
// lightmap, then bakes four versions of one box-projected reflection probe: plain, and with the source's bright
// spheres drawn at the lights at three brightness / size settings. Built from code; no external assets.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    public static class SpecProbeScene
    {
        const string Dir = "Assets/Techniques/specular-probes/";
        const string Lit = "Universal Render Pipeline/Lit";
        const StaticEditorFlags Static = StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic;

        static Mesh Save(MeshKit k, string name, float smooth = 35f)
        {
            var m = k.Build(name, smooth);
            Unwrapping.GenerateSecondaryUVSet(m);   // lightmap UVs
            AssetDatabase.CreateAsset(m, Dir + name + ".asset"); return m;
        }

        static GameObject Part(string name, Mesh mesh, Material m, Vector3 pos, Quaternion rot, bool stat = true)
        {
            var go = SceneKit.Part(name, mesh, m, pos, rot);
            if (stat) GameObjectUtility.SetStaticEditorFlags(go, Static);
            return go;
        }

        static Material Emissive(string name, Color c, float hdr)
        {
            var m = new Material(Shader.Find(Lit));
            m.SetColor("_BaseColor", Color.black);
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * hdr);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            AssetDatabase.CreateAsset(m, Dir + name + ".mat");
            return m;
        }

        static float lastMax;

        static Texture BakeProbe(ReflectionProbe probe, SpecularProbeRenderer spr, string file, float radius, float mult, bool spheres)
        {
            string path = Dir + file;
            foreach (var l in Object.FindObjectsByType<SpecularProbeLight>(FindObjectsSortMode.None)) { l.radius = radius; l.intensityMultiplier = mult; }
            bool ok = spheres ? spr.RenderTo(path) : Lightmapping.BakeReflectionProbe(probe, path);
            AssetDatabase.ImportAsset(path);
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti != null) { ti.isReadable = true; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.SaveAndReimport(); }
            var tex = AssetDatabase.LoadAssetAtPath<Texture>(path);
            // measure what is in the baked texture: brightest texel and texels above 4 (linear)
            float max = 0; int hot = 0;
            if (tex is Cubemap cm)
                for (int f = 0; f < 6; f++)
                    foreach (var p in cm.GetPixels((CubemapFace)f))
                    {
                        float v = Mathf.Max(p.r, Mathf.Max(p.g, p.b));
                        if (v > max) max = v;
                        if (v > 4f) hot++;
                    }
            lastMax = max;
            Debug.Log($"[gt] probe {file}: ok {ok}, {(tex != null ? tex.GetType().Name + " " + tex.width : "null")}, spheres {spheres} radius {radius} mult {mult}, max texel {max:F2}, texels > 4: {hot}");
            return tex;
        }

        public static void Build()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.01f, 0.01f, 0.012f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom; RenderSettings.customReflectionTexture = null;
            RenderSettings.fog = false;

            var floorM = SceneKit.Mat(Dir, "Floor", Lit, new Color(0.1f, 0.1f, 0.11f), 0f, 0.88f);
            var wallM = SceneKit.Mat(Dir, "Wall", Lit, new Color(0.36f, 0.33f, 0.3f), 0f, 0.25f);
            var ceilM = SceneKit.Mat(Dir, "Ceiling", Lit, new Color(0.3f, 0.3f, 0.3f), 0f, 0.1f);
            var trimM = SceneKit.Mat(Dir, "Trim", Lit, new Color(0.42f, 0.37f, 0.32f), 0f, 0.55f);
            var plinthM = SceneKit.Mat(Dir, "Plinth", Lit, new Color(0.45f, 0.43f, 0.4f), 0f, 0.6f);
            var chromeM = SceneKit.Mat(Dir, "Chrome", Lit, new Color(0.9f, 0.9f, 0.92f), 1f, 0.97f);
            var ceramicM = SceneKit.Mat(Dir, "Ceramic", Lit, new Color(0.85f, 0.84f, 0.8f), 0f, 0.92f);
            var copperM = SceneKit.Mat(Dir, "Copper", Lit, new Color(0.95f, 0.62f, 0.45f), 1f, 0.8f);
            var blackM = SceneKit.Mat(Dir, "BlackCeramic", Lit, new Color(0.03f, 0.03f, 0.035f), 0f, 0.93f);
            var shadeM = SceneKit.Mat(Dir, "Shade", Lit, new Color(0.04f, 0.04f, 0.045f), 1f, 0.5f);
            var cordM = SceneKit.Mat(Dir, "Cord", Lit, new Color(0.02f, 0.02f, 0.02f), 0f, 0.3f);

            // room 8 x 3.2 x 7.5 m
            var fk = new MeshKit(); fk.Box(new Vector3(0, -0.05f, -0.25f), new Vector3(8f, 0.1f, 7.5f));
            Part("Floor", Save(fk, "Floor"), floorM, Vector3.zero, Quaternion.identity);
            var wk = new MeshKit();
            wk.Quad(new Vector3(-4, 0, 3.5f), new Vector3(4, 0, 3.5f), new Vector3(4, 3.2f, 3.5f), new Vector3(-4, 3.2f, 3.5f), Vector3.back);
            wk.Quad(new Vector3(-4, 0, -4f), new Vector3(4, 0, -4f), new Vector3(4, 3.2f, -4f), new Vector3(-4, 3.2f, -4f), Vector3.forward);
            wk.Quad(new Vector3(-4, 0, -4f), new Vector3(-4, 0, 3.5f), new Vector3(-4, 3.2f, 3.5f), new Vector3(-4, 3.2f, -4f), Vector3.right);
            wk.Quad(new Vector3(4, 0, -4f), new Vector3(4, 0, 3.5f), new Vector3(4, 3.2f, 3.5f), new Vector3(4, 3.2f, -4f), Vector3.left);
            Part("Walls", Save(wk, "Walls"), wallM, Vector3.zero, Quaternion.identity);
            var ck = new MeshKit(); ck.Quad(new Vector3(-4, 3.2f, -4f), new Vector3(4, 3.2f, -4f), new Vector3(4, 3.2f, 3.5f), new Vector3(-4, 3.2f, 3.5f), Vector3.down);
            Part("Ceiling", Save(ck, "Ceiling"), ceilM, Vector3.zero, Quaternion.identity);

            // trim: chair rail and skirting on three walls, framed panels on the back wall
            var tk = new MeshKit();
            foreach (float y in new[] { 0.06f, 1.0f })
            {
                float hgt = y < 0.5f ? 0.12f : 0.06f;
                tk.Box(new Vector3(0, y, 3.47f), new Vector3(8f, hgt, 0.06f));
                tk.Box(new Vector3(-3.97f, y, -0.25f), new Vector3(0.06f, hgt, 7.5f));
                tk.Box(new Vector3(3.97f, y, -0.25f), new Vector3(0.06f, hgt, 7.5f));
            }
            foreach (float px in new[] { -2.4f, 0f, 2.4f })
            {
                float w = 1.6f, h = 1.6f, cy = 2.05f, b = 0.06f;
                tk.Box(new Vector3(px, cy + h / 2, 3.47f), new Vector3(w, b, 0.05f));
                tk.Box(new Vector3(px, cy - h / 2, 3.47f), new Vector3(w, b, 0.05f));
                tk.Box(new Vector3(px - w / 2, cy, 3.47f), new Vector3(b, h, 0.05f));
                tk.Box(new Vector3(px + w / 2, cy, 3.47f), new Vector3(b, h, 0.05f));
            }
            Part("Trim", Save(tk, "Trim"), trimM, Vector3.zero, Quaternion.identity);

            // plinth and props
            var pk = new MeshKit(); pk.Box(new Vector3(0, 0.15f, 0.6f), new Vector3(2.6f, 0.3f, 1.2f));
            Part("Plinth", Save(pk, "Plinth"), plinthM, Vector3.zero, Quaternion.identity);
            var sphere = new MeshKit();
            var prof = new Vector2[34];
            for (int i = 0; i <= 32; i++) { float a = Mathf.PI * i / 32f; prof[i] = new Vector2(-Mathf.Cos(a), Mathf.Sin(a)); }
            prof[33] = new Vector2(1, 0);
            sphere.Lathe(prof, 64);
            var sphereMesh = Save(sphere, "Sphere", 60f);
            var ball = Part("Chrome ball", sphereMesh, chromeM, new Vector3(-0.7f, 0.52f, 0.55f), Quaternion.identity); ball.transform.localScale = Vector3.one * 0.22f;
            var bs = Part("Black sphere", sphereMesh, blackM, new Vector3(-2.1f, 0.32f, -0.4f), Quaternion.identity); bs.transform.localScale = Vector3.one * 0.32f;
            var vk = new MeshKit();
            vk.Lathe(new[] { new Vector2(0, 0), new Vector2(0, 0.1f), new Vector2(0.03f, 0.11f), new Vector2(0.15f, 0.17f), new Vector2(0.3f, 0.2f),
                             new Vector2(0.45f, 0.16f), new Vector2(0.58f, 0.08f), new Vector2(0.66f, 0.065f), new Vector2(0.72f, 0.08f), new Vector2(0.74f, 0.08f), new Vector2(0.74f, 0) }, 72);
            Part("Ceramic vase", Save(vk, "Vase", 40f), ceramicM, new Vector3(0.1f, 0.3f, 0.75f), Quaternion.identity);
            var jk = new MeshKit();
            jk.Lathe(new[] { new Vector2(0, 0), new Vector2(0, 0.12f), new Vector2(0.02f, 0.13f), new Vector2(0.12f, 0.17f), new Vector2(0.24f, 0.17f),
                             new Vector2(0.34f, 0.12f), new Vector2(0.4f, 0.1f), new Vector2(0.46f, 0.12f), new Vector2(0.48f, 0.12f), new Vector2(0.48f, 0) }, 72);
            Part("Copper jug", Save(jk, "Jug", 40f), copperM, new Vector3(0.8f, 0.3f, 0.45f), Quaternion.identity);

            // baked lights: three warm pendants and one cool wall sconce, each with the source's SpecularProbeLight
            var shadeK = new MeshKit();
            shadeK.Lathe(new[] { new Vector2(0, 0), new Vector2(0, 0.17f), new Vector2(0.025f, 0.17f), new Vector2(0.13f, 0.03f), new Vector2(0.15f, 0.02f), new Vector2(0.15f, 0) }, 48);
            var shadeMesh = Save(shadeK, "Shade");
            var bulbK = new MeshKit(); bulbK.Lathe(prof, 24); var bulbMesh = Save(bulbK, "Bulb", 60f);
            var warm = new Color(1f, 0.78f, 0.55f); var cool = new Color(0.62f, 0.76f, 1f);
            var bulbWarm = Emissive("BulbWarm", warm, 2f); var bulbCool = Emissive("BulbCool", cool, 2f);
            var lights = new (Vector3 p, Color c, float i, bool pendant)[]
            {
                (new Vector3(-1.9f, 2.25f, 1.2f), warm, 10f, true),
                (new Vector3(0.5f, 2.45f, -0.4f), warm, 10f, true),
                (new Vector3(2.3f, 2.15f, 1.7f), warm, 10f, true),
                (new Vector3(3.8f, 1.6f, -1.2f), cool, 6f, false),
            };
            int n = 0;
            foreach (var L in lights)
            {
                var go = new GameObject("Light " + (++n)); go.transform.position = L.p;
                var l = go.AddComponent<Light>(); l.type = LightType.Point; l.color = L.c; l.intensity = L.i; l.range = 7f;
                l.lightmapBakeType = LightmapBakeType.Baked; l.shadows = LightShadows.Soft; l.shadowRadius = 0.04f;
                go.AddComponent<SpecularProbeLight>();
                Part("Bulb " + n, bulbMesh, L.pendant ? bulbWarm : bulbCool, L.p, Quaternion.identity, false).transform.localScale = Vector3.one * 0.035f;
                if (L.pendant)
                {
                    Part("Shade " + n, shadeMesh, shadeM, L.p + Vector3.up * 0.015f, Quaternion.identity);
                    var cord = new MeshKit(); cord.Box(new Vector3(0, (3.2f - L.p.y) / 2f + 0.1f, 0), new Vector3(0.012f, 3.2f - L.p.y - 0.12f, 0.012f));
                    Part("Cord " + n, Save(cord, "Cord" + n), cordM, L.p, Quaternion.identity);
                }
                else
                {
                    var bk = new MeshKit(); bk.Box(new Vector3(0.12f, 0, 0), new Vector3(0.06f, 0.24f, 0.16f));
                    Part("Sconce plate", Save(bk, "SconcePlate"), shadeM, L.p, Quaternion.identity);
                }
            }

            // reflection probe covering the room, box projected
            var pgo = new GameObject("Reflection probe"); pgo.transform.position = new Vector3(0, 1.4f, -0.25f);
            var probe = pgo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked; probe.size = new Vector3(8f, 3.2f, 7.5f); probe.center = new Vector3(0, 0.2f, 0);
            probe.boxProjection = true; probe.resolution = 256; probe.hdr = true; probe.clearFlags = ReflectionProbeClearFlags.SolidColor; probe.backgroundColor = Color.black;

            var srcCam = SceneKit.Cam("Scene camera", new Rect(0, 0, 1, 1), 42, Color.black, -10);
            srcCam.clearFlags = CameraClearFlags.SolidColor; srcCam.cullingMask = ~(1 << 8); srcCam.farClipPlane = 40f; srcCam.nearClipPlane = 0.05f;
            SceneKit.Grade(Dir, 0.45f, 0.3f, 12f, 5f, 0.3f, 0f);
            var display = SceneKit.Cam("Display camera", new Rect(0, 0, 1, 1), 40, Color.black, 10, false);
            display.cullingMask = 1 << 8; display.transform.position = new Vector3(0, -50, 0); display.tag = "MainCamera";
            var compose = new Material(Shader.Find("Hidden/GT/SpecCompose")); AssetDatabase.CreateAsset(compose, Dir + "Compose.mat");
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Display quad"; Object.DestroyImmediate(quad.GetComponent<Collider>()); quad.layer = 8;
            var qm = Object.Instantiate(quad.GetComponent<MeshFilter>().sharedMesh); qm.name = "DisplayQuad"; qm.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
            AssetDatabase.CreateAsset(qm, Dir + "DisplayQuad.asset");
            quad.GetComponent<MeshFilter>().sharedMesh = qm; quad.GetComponent<Renderer>().sharedMaterial = compose;
            quad.transform.position = display.transform.position + Vector3.forward;

            // lightmap bake (needs a saved scene)
            var ls = new LightingSettings
            {
                name = "Lighting", bakedGI = true, realtimeGI = false, lightmapper = LightingSettings.Lightmapper.ProgressiveGPU,
                lightmapResolution = 20, lightmapMaxSize = 1024, directSampleCount = 32, indirectSampleCount = 256, environmentSampleCount = 64,
                maxBounces = 2, lightmapCompression = LightmapCompression.NormalQuality, directionalityMode = LightmapsMode.CombinedDirectional,
                ao = false, filteringMode = LightingSettings.FilterMode.Auto
            };
            AssetDatabase.CreateAsset(ls, Dir + "Lighting.lighting");
            Lightmapping.lightingSettings = ls;
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), Dir + "Main.unity");
            var t0 = System.DateTime.Now;
            bool baked = Lightmapping.Bake();
            Debug.Log($"[gt] lightmap bake {baked} in {(System.DateTime.Now - t0).TotalSeconds:F1} s, {LightmapSettings.lightmaps.Length} lightmaps");

            // probe versions: plain, tuned, too dim, too big
            var spr = pgo.AddComponent<SpecularProbeRenderer>();
            spr.radius = 100;
            var dir = new GameObject("Director").AddComponent<SpecProbeDirector>();
            const float tunedR = 0.06f, tunedM = 1f, dimM = 0.04f, bigR = 0.35f;
            dir.plain = BakeProbe(probe, spr, "Probe_plain.exr", tunedR, tunedM, false); float plainMax = lastMax;
            dir.specular = BakeProbe(probe, spr, "Probe_specular.exr", tunedR, tunedM, true); float specMax = lastMax;
            dir.dim = BakeProbe(probe, spr, "Probe_dim.exr", tunedR, dimM, true); float dimMax = lastMax;
            dir.big = BakeProbe(probe, spr, "Probe_big.exr", bigR, tunedM, true);
            foreach (var l in Object.FindObjectsByType<SpecularProbeLight>(FindObjectsSortMode.None)) { l.radius = tunedR; l.intensityMultiplier = tunedM; }
            probe.mode = ReflectionProbeMode.Custom; probe.customBakedTexture = dir.plain; probe.name = "Probe plain";
            var list = new System.Collections.Generic.List<ReflectionProbe> { probe };
            foreach (var (nm, tx) in new[] { ("specular", dir.specular), ("dim", dir.dim), ("big", dir.big) })
            {
                var g2 = new GameObject("Probe " + nm); g2.transform.position = pgo.transform.position;
                var p2 = g2.AddComponent<ReflectionProbe>();
                p2.mode = ReflectionProbeMode.Custom; p2.customBakedTexture = tx; p2.size = probe.size; p2.center = probe.center;
                p2.boxProjection = true; p2.resolution = probe.resolution; p2.hdr = true; p2.importance = probe.importance;
                list.Add(p2);
            }
            dir.probes = list.ToArray();

            dir.tuned = $"spheres r {tunedR} m, colour = light x intensity x {tunedM} x 5, 256 px cubemap";
            dir.dimText = $"multiplier {dimM}: peak texel {dimMax:F1} (plain {plainMax:F1})";
            dir.tunedShort = $"r {tunedR} m, multiplier {tunedM}: peak texel {specMax:F0}";
            Debug.Log($"[gt] maxima plain {plainMax} spec {specMax} dim {dimMax}");
            dir.bigText = $"r {bigR} m, multiplier {tunedM}: a broad glowing blob";
            dir.src = srcCam; dir.compose = compose; dir.probe = probe;
            dir.title = LabelSpec.Create("", 0.012f, 0.935f, 30);
            dir.l1 = new LabelSpec[3]; dir.l2 = new LabelSpec[3];
            for (int i = 0; i < 3; i++) { dir.l1[i] = LabelSpec.Create("", 0.012f, 0.058f, 28); dir.l2[i] = LabelSpec.Create("", 0.012f, 0.018f, 22); }
        }
    }
}
