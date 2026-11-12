// 71 post-processing scan: a procedural warehouse at night, the character at the centre, scan rings drawn by one full-viewport quad.
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace GameTech.Posts
{
    public static class ScanScene
    {
        const string Dir = "Assets/Techniques/post-processing-scan/";
        const string Lit = "Universal Render Pipeline/Lit";

        static Material Emissive(string name, Color c, float k)
        {
            var m = new Material(Shader.Find("GameTech/Glow"));
            m.SetColor("_BaseColor", c * k);
            AssetDatabase.CreateAsset(m, Dir + name + ".mat");
            return m;
        }

        static Vector2[] CylProfile(float r, float h) => new[] { new Vector2(0, 0), new Vector2(0, r), new Vector2(h, r), new Vector2(h, 0) };

        static Light Spot(string name, Vector3 pos, Vector3 at, Color c, float intensity, float range, float angle, bool shadows)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.type = LightType.Spot; l.color = c; l.intensity = intensity; l.range = range; l.spotAngle = angle; l.innerSpotAngle = angle * 0.55f;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            l.transform.position = pos; l.transform.LookAt(at);
            return l;
        }

        public static void Build()
        {
            var rng = new System.Random(71);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            SceneKit.Ambient(new Color(0.055f, 0.075f, 0.12f), new Color(0.04f, 0.048f, 0.07f), new Color(0.022f, 0.022f, 0.028f));
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.018f, 0.026f, 0.04f); RenderSettings.fogDensity = 0.03f;
            SceneKit.Shadows(40f);
            SceneKit.Sun("Fill", new Vector3(62, 35, 0), new Color(0.55f, 0.68f, 1f), 0.16f, false);

            var concrete = SceneKit.Mat(Dir, "Concrete", Lit, new Color(0.23f, 0.24f, 0.26f), 0f, 0.25f);
            var floorM = SceneKit.Mat(Dir, "Floor", Lit, new Color(0.17f, 0.175f, 0.185f), 0f, 0.62f);
            var steel = SceneKit.Mat(Dir, "RackSteel", Lit, new Color(0.10f, 0.22f, 0.42f), 0.5f, 0.45f);
            var pipeM = SceneKit.Mat(Dir, "Pipe", Lit, new Color(0.52f, 0.53f, 0.55f), 0.9f, 0.6f);
            var yellow = SceneKit.Mat(Dir, "Hazard", Lit, new Color(0.85f, 0.62f, 0.08f), 0f, 0.35f);
            var wood = SceneKit.Mat(Dir, "Crate", Lit, new Color(0.42f, 0.29f, 0.16f), 0f, 0.2f);
            var carton = new[] {
                SceneKit.Mat(Dir, "CartonA", Lit, new Color(0.56f, 0.42f, 0.27f), 0f, 0.15f),
                SceneKit.Mat(Dir, "CartonB", Lit, new Color(0.40f, 0.33f, 0.26f), 0f, 0.15f),
                SceneKit.Mat(Dir, "CartonC", Lit, new Color(0.26f, 0.36f, 0.30f), 0f, 0.2f) };
            var barrelM = new[] {
                SceneKit.Mat(Dir, "BarrelRed", Lit, new Color(0.55f, 0.12f, 0.08f), 0.3f, 0.5f),
                SceneKit.Mat(Dir, "BarrelBlue", Lit, new Color(0.08f, 0.25f, 0.45f), 0.3f, 0.5f) };
            var dark = SceneKit.Mat(Dir, "DarkMetal", Lit, new Color(0.07f, 0.075f, 0.085f), 0.8f, 0.5f);
            var lampGlow = Emissive("LampGlow", new Color(1f, 0.72f, 0.42f), 4f);
            var beaconGlow = Emissive("BeaconGlow", new Color(0.2f, 0.85f, 1f), 5f);

            // ---- shell: walls + ceiling (one inward box), floor slab, beams
            var shell = SceneKit.Part("Shell", new MeshKit().Box(new Vector3(0, 3.25f, -3.5f), new Vector3(16, 6.5f, 25), true).Build("Shell"), concrete, Vector3.zero, Quaternion.identity);
            shell.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            SceneKit.Part("Floor", new MeshKit().Box(new Vector3(0, -0.05f, -3.5f), new Vector3(16, 0.12f, 25)).Build("Floor"), floorM, Vector3.zero, Quaternion.identity);
            var beams = new MeshKit();
            for (float z = -14; z <= 8; z += 4) beams.Box(new Vector3(0, 6.15f, z), new Vector3(16, 0.5f, 0.3f));
            beams.Box(new Vector3(-3.2f, 5.75f, -3.5f), new Vector3(0.25f, 0.3f, 25)); beams.Box(new Vector3(3.2f, 5.75f, -3.5f), new Vector3(0.25f, 0.3f, 25));
            SceneKit.Part("Beams", beams.Build("Beams"), dark, Vector3.zero, Quaternion.identity);

            // ---- columns with hazard bands
            var cols = new MeshKit(); var bands = new MeshKit();
            foreach (float x in new[] { -3.2f, 3.2f })
                foreach (float z in new[] { -12f, -7f, -2f })
                {
                    cols.Box(new Vector3(x, 3.25f, z), new Vector3(0.5f, 6.5f, 0.5f));
                    bands.Box(new Vector3(x, 0.5f, z), new Vector3(0.54f, 1.0f, 0.54f));
                }
            SceneKit.Part("Columns", cols.Build("Columns"), concrete, Vector3.zero, Quaternion.identity);
            SceneKit.Part("Column bands", bands.Build("Bands"), yellow, Vector3.zero, Quaternion.identity);

            // ---- racks along both side walls, cartons on the shelves
            var rack = new MeshKit(); var cart = new[] { new MeshKit(), new MeshKit(), new MeshKit() };
            foreach (float x in new[] { -6.9f, 6.9f })
                for (float z = -13.5f; z <= 6.5f; z += 2.9f)
                {
                    float len = 2.5f, dep = 0.9f, hgt = 3.6f;
                    foreach (float dz in new[] { -len / 2, len / 2 }) foreach (float dx in new[] { -dep / 2, dep / 2 })
                        rack.Box(new Vector3(x + dx, hgt / 2, z + dz), new Vector3(0.07f, hgt, 0.07f));
                    for (int s = 0; s < 4; s++)
                    {
                        float y = 0.25f + s * 1.05f;
                        rack.Box(new Vector3(x, y, z), new Vector3(dep, 0.05f, len));
                        float zc = z - len / 2 + 0.2f;
                        while (zc < z + len / 2 - 0.3f)
                        {
                            float w = R(0.3f, 0.7f), h = R(0.25f, 0.75f), d = R(0.45f, 0.8f);
                            if (R(0, 1) > 0.22f) cart[rng.Next(3)].Box(new Vector3(x + R(-0.04f, 0.04f), y + 0.025f + h / 2, zc + w / 2), new Vector3(d, h, w));
                            zc += w + R(0.04f, 0.25f);
                        }
                    }
                }
            SceneKit.Part("Racks", rack.Build("Racks"), steel, Vector3.zero, Quaternion.identity);
            for (int i = 0; i < 3; i++) SceneKit.Part("Cartons " + i, cart[i].Build("Cartons" + i), carton[i], Vector3.zero, Quaternion.identity);

            // ---- crates and barrels on the floor
            var crates = new MeshKit();
            var barrel = new MeshKit().Lathe(new[] { new Vector2(0, 0), new Vector2(0, 0.29f), new Vector2(0.02f, 0.31f), new Vector2(0.06f, 0.31f), new Vector2(0.08f, 0.29f),
                new Vector2(0.42f, 0.30f), new Vector2(0.44f, 0.32f), new Vector2(0.48f, 0.32f), new Vector2(0.50f, 0.30f), new Vector2(0.82f, 0.29f), new Vector2(0.84f, 0.31f),
                new Vector2(0.88f, 0.31f), new Vector2(0.90f, 0.29f), new Vector2(0.90f, 0) }, 28).Build("Barrel");
            var clusters = new[] { new Vector3(-2.4f, 0, -4.2f), new Vector3(2.5f, 0, -5.8f), new Vector3(-1.3f, 0, -9.6f), new Vector3(4.6f, 0, 0.6f), new Vector3(-5.0f, 0, 1.6f), new Vector3(1.6f, 0, -12.5f), new Vector3(5.0f, 0, -9.5f) };
            foreach (var c in clusters)
            {
                float s0 = R(0.8f, 1.15f);
                crates.Box(c + new Vector3(0, s0 / 2, 0), new Vector3(s0, s0, s0));
                float s1 = R(0.6f, 0.9f);
                crates.Box(c + new Vector3(R(-0.1f, 0.1f), s0 + s1 / 2, R(-0.1f, 0.1f)), new Vector3(s1, s1, s1));
                float s2 = R(0.7f, 1.0f);
                crates.Box(c + new Vector3(s0 / 2 + s2 / 2 + 0.08f, s2 / 2, R(-0.2f, 0.2f)), new Vector3(s2, s2, s2));
                for (int b = 0; b < 3; b++)
                {
                    float a = R(0, 6.28f), rr = R(1.0f, 1.6f);
                    SceneKit.Part("Barrel", barrel, barrelM[rng.Next(2)], c + new Vector3(Mathf.Cos(a) * rr, 0, Mathf.Sin(a) * rr - 0.3f), Quaternion.identity);
                }
            }
            SceneKit.Part("Crates", crates.Build("Crates"), wood, Vector3.zero, Quaternion.identity);

            // ---- pipes on the back wall and along the left wall
            var hPipe = new MeshKit().Lathe(CylProfile(0.11f, 15.6f), 20).Build("PipeH");
            var zPipe = new MeshKit().Lathe(CylProfile(0.14f, 24.6f), 20).Build("PipeZ");
            var vPipe = new MeshKit().Lathe(CylProfile(0.09f, 6.4f), 20).Build("PipeV");
            foreach (float y in new[] { 4.3f, 4.75f, 5.2f }) SceneKit.Part("Pipe", hPipe, pipeM, new Vector3(-7.8f, y, -15.6f), Quaternion.Euler(0, 0, -90));
            foreach (float y in new[] { 5.0f, 5.45f }) SceneKit.Part("Pipe", zPipe, pipeM, new Vector3(-7.7f, y, -15.8f), Quaternion.Euler(90, 0, 0));
            foreach (float x in new[] { -5.5f, -5.2f, 1.2f, 5.8f }) SceneKit.Part("Pipe", vPipe, pipeM, new Vector3(x, 0, -15.75f), Quaternion.identity);

            // ---- hanging lamps and the work light
            var shade = new MeshKit().Lathe(new[] { new Vector2(0, 0.04f), new Vector2(0.02f, 0.06f), new Vector2(-0.22f, 0.36f), new Vector2(-0.24f, 0.34f) }, 28).Build("Shade");
            var bulb = new MeshKit().Lathe(CylProfile(0.2f, 0.03f), 24).Build("Bulb");
            foreach (float z in new[] { -11f, -5f, 1f })
            {
                SceneKit.Part("Lamp cord", new MeshKit().Box(new Vector3(0, 5.55f, z), new Vector3(0.03f, 0.7f, 0.03f)).Build("Cord"), dark, Vector3.zero, Quaternion.identity);
                SceneKit.Part("Lamp shade", shade, dark, new Vector3(0, 5.2f, z), Quaternion.identity);
                SceneKit.Part("Lamp bulb", bulb, lampGlow, new Vector3(0, 5.0f, z), Quaternion.identity).GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Spot("Lamp light", new Vector3(0, 4.95f, z), new Vector3(0, 0, z), new Color(1f, 0.74f, 0.48f), 120f, 11f, 104f, true);
            }
            var stand = new MeshKit();
            stand.Box(new Vector3(0, 0.9f, 0), new Vector3(0.05f, 1.8f, 0.05f)); stand.Box(new Vector3(0, 0.03f, 0), new Vector3(0.5f, 0.06f, 0.5f));
            SceneKit.Part("Work light stand", stand.Build("Stand"), dark, new Vector3(-2.3f, 0, 2.6f), Quaternion.identity);
            var panel = SceneKit.Part("Work light panel", new MeshKit().Box(Vector3.zero, new Vector3(0.5f, 0.32f, 0.06f)).Build("Panel"), lampGlow, new Vector3(-2.3f, 1.9f, 2.6f), Quaternion.LookRotation(new Vector3(2.3f, -0.9f, -2.6f)));
            panel.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Spot("Work light", new Vector3(-2.2f, 1.9f, 2.5f), new Vector3(0, 0.9f, 0), new Color(1f, 0.80f, 0.58f), 36f, 12f, 62f, true);
            Spot("Rim light", new Vector3(2.6f, 3.2f, -4.2f), new Vector3(0, 1.1f, 0), new Color(0.45f, 0.62f, 1f), 45f, 10f, 50f, false);

            // ---- character and the origin beacon
            var chr = CharacterKit.Spawn(Lit, Dir, (m, n) => { m.SetFloat("_Smoothness", 0.2f); });
            chr.transform.position = Vector3.zero; chr.transform.rotation = Quaternion.Euler(0, 12f, 0);
            var beacon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            beacon.name = "Beacon"; Object.DestroyImmediate(beacon.GetComponent<Collider>()); beacon.transform.localScale = Vector3.one * 0.15f;
            var br = beacon.GetComponent<Renderer>(); br.sharedMaterial = beaconGlow; br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var bl = new GameObject("Beacon light").AddComponent<Light>(); bl.type = LightType.Point; bl.color = new Color(0.3f, 0.85f, 1f); bl.intensity = 3.5f; bl.range = 5f;
            bl.transform.SetParent(beacon.transform, false);

            // ---- the scan quad: full viewport, never culled, hidden from the OFF camera through its layer
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Scan quad"; Object.DestroyImmediate(quad.GetComponent<Collider>()); quad.layer = 8;
            var qm = Object.Instantiate(quad.GetComponent<MeshFilter>().sharedMesh); qm.name = "ScanQuad"; qm.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
            quad.GetComponent<MeshFilter>().sharedMesh = qm;
            var sm = new Material(Shader.Find("GameTech/PostScan")); AssetDatabase.CreateAsset(sm, Dir + "Scan.mat");
            var qr = quad.GetComponent<Renderer>(); qr.sharedMaterial = sm; qr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var dr = new GameObject("Director").AddComponent<ScanDirector>();
            dr.beacon = beacon.transform;
            var bg = new Color(0.018f, 0.026f, 0.04f);
            dr.camL = SceneKit.Cam("Camera OFF", new Rect(0, 0, 0.5f, 1), 40, bg, 0); dr.camL.cullingMask = ~(1 << 8);
            dr.camR = SceneKit.Cam("Camera ON", new Rect(0.5f, 0, 0.5f, 1), 40, bg, 1);
            dr.camFull = SceneKit.Cam("Camera full", new Rect(0, 0, 1, 1), 46, bg, 2); dr.camFull.tag = "MainCamera";
            foreach (var c in new[] { dr.camL, dr.camR, dr.camFull })
            {
                var data = c.GetComponent<UniversalAdditionalCameraData>();
                data.requiresDepthTexture = true; data.requiresColorTexture = true;
            }

            SceneKit.Grade(Dir, 0.55f, 0.95f, 10f, 8f, 0.32f, 0.2f);

            LabelSpec.Create("OFF: no scan", 0.03f, 0.05f).During(2.0f, 8f);
            LabelSpec.Create("ON: scan rings (Tiling 0.3, Power 8)", 0.53f, 0.05f).During(2.0f, 8f);
            LabelSpec.Create("Tiling 0.2, Speed 0.5: a ring every 5 m, moving 2.5 m/s", 0.03f, 0.05f).During(8.3f, 10f);
            LabelSpec.Create("Tiling 0.8: a ring every 1.25 m", 0.03f, 0.05f).During(10f, 12f);
            LabelSpec.Create("Mask Radius 6, Hardness 1.0: hard cut-off", 0.03f, 0.05f).During(12f, 13.7f);
            LabelSpec.Create("Mask Radius 6, Hardness 0.2: soft falloff", 0.03f, 0.05f).During(13.7f, 15.4f);
            LabelSpec.Create("The origin is one world position: move it, the rings follow", 0.03f, 0.05f).During(15.4f, 19f);
            LabelSpec.Create("1/3  world position rebuilt from depth (1 m cells)", 0.03f, 0.05f).During(19f, 20.667f);
            LabelSpec.Create("2/3  distance to the origin, one line per metre", 0.03f, 0.05f).During(20.667f, 22.333f);
            LabelSpec.Create("3/3  rings = frac(distance x 0.4 - time x 0.5) ^ 5, x radius mask", 0.03f, 0.05f).During(22.333f, 24.1f);
            Debug.Log("[gt] scan scene built");
        }
    }
}
