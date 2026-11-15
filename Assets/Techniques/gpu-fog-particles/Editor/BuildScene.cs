// 94 GPU fog particles: forest ruins at dusk built from code, a path through a broken arch, pines, and the stand-in
// mannequin walking through a fog bank. The fog is Unity particle systems (billboard quads, no texture) with
// materials on the source's URP fog shader; emitter and noise settings start from the source's Ground Fog and
// Falling Fog prefabs and are scaled to this scene. Built from code; no external assets.
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GameTech.Posts
{
    public static class FogScene
    {
        const string Dir = "Assets/Techniques/gpu-fog-particles/";
        const string Lit = "Universal Render Pipeline/Lit";
        const string FogShader = "Mirza Beig/GPU Fog (URP)";

        static Mesh Save(MeshKit k, string name, float smooth = 35f)
        {
            var m = k.Build(name, smooth);
            AssetDatabase.CreateAsset(m, Dir + name + ".asset"); return m;
        }

        static Color Enc(Color c) => new Color(Mathf.Pow(c.r, 1f / 2.2f), Mathf.Pow(c.g, 1f / 2.2f), Mathf.Pow(c.b, 1f / 2.2f), 1f);

        static Material Emissive(string name, Color c, float linear)
        {
            var m = new Material(Shader.Find(Lit));
            m.SetColor("_BaseColor", Color.black);
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Enc(c * linear));
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            AssetDatabase.CreateAsset(m, Dir + name + ".mat");
            return m;
        }

        // fog material on the source shader; values not set here keep the shader defaults
        static Material FogMat(string name, Color albedo, float simple, float simpleScale, float simplex, float simplexScale,
                               float voronoi, float voronoiRemap, Vector4 simpleAnim, Vector4 simplexAnim)
        {
            var sh = Shader.Find(FogShader);
            if (sh == null) Debug.LogError("[gt] fog shader not found");
            var m = new Material(sh);
            m.SetColor("_Albedo", albedo);
            m.SetFloat("_SimpleNoiseAmount", simple); m.SetFloat("_SimpleNoiseScale", simpleScale); m.SetFloat("_SimpleNoiseRemap", 0.5f);
            m.SetFloat("_SimplexNoiseAmount", simplex); m.SetFloat("_SimplexNoiseScale", simplexScale); m.SetFloat("_SimplexNoiseRemap", 0.5f);
            m.SetFloat("_VoronoiNoiseAmount", voronoi); m.SetFloat("_VoronoiScale", 5f); m.SetFloat("_VoronoiNoiseRemap", voronoiRemap);
            m.SetFloat("_CombinedNoiseRemap", 0f);
            m.SetVector("_SimpleNoiseAnimation", simpleAnim); m.SetVector("_SimplexNoiseAnimation", simplexAnim);
            m.SetVector("_VoronoiNoiseAnimation", Vector4.zero);
            m.SetFloat("_SurfaceDepthFade", 0.5f); m.SetFloat("_CameraDepthFadeRange", 2f); m.SetFloat("_CameraDepthFadeOffset", 2f);
            AssetDatabase.CreateAsset(m, Dir + name + ".mat");
            return m;
        }

        // the source prefabs' colour over lifetime: warm white fading in to white, alpha 0 -> 1 at half life -> 0
        static Gradient Life()
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.82f, 0.61f), 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        static ParticleSystem Emitter(string name, int layer, Material mat, Vector3 pos, Vector3 box, float rate, Vector2 life,
                                      Vector2 size, Vector2 gravity, float maxScreen, int max, uint seed)
        {
            var go = new GameObject(name); go.layer = layer; go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false; ps.randomSeed = seed;
            var main = ps.main;
            main.duration = 5f; main.loop = true; main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startRotation = 0f;
            main.startColor = Color.white;
            main.gravityModifier = new ParticleSystem.MinMaxCurve(gravity.x, gravity.y);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = max;
            var em = ps.emission; em.enabled = true; em.rateOverTime = rate;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = box;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = new ParticleSystem.MinMaxGradient(Life());
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortMode = ParticleSystemSortMode.Depth;
            r.minParticleSize = 0f; r.maxParticleSize = maxScreen;
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.SetActiveVertexStreams(new System.Collections.Generic.List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color,
                ParticleSystemVertexStream.UV, ParticleSystemVertexStream.StableRandomXYZ
            });
            return ps;
        }

        public static void Build()
        {
            RenderSettings.skybox = null; RenderSettings.fog = false;
            var dir = new GameObject("Director").AddComponent<FogDirector>();
            var rand = new System.Random(94);
            float R(float a, float b) => a + (float)rand.NextDouble() * (b - a);

            var groundM = SceneKit.Mat(Dir, "Ground", Lit, new Color(0.13f, 0.15f, 0.11f), 0f, 0.15f);
            var pathM = SceneKit.Mat(Dir, "Path", Lit, new Color(0.3f, 0.26f, 0.2f), 0f, 0.2f);
            var stoneM = SceneKit.Mat(Dir, "Stone", Lit, new Color(0.36f, 0.35f, 0.33f), 0f, 0.18f);
            var mossM = SceneKit.Mat(Dir, "MossStone", Lit, new Color(0.22f, 0.26f, 0.18f), 0f, 0.15f);
            var barkM = SceneKit.Mat(Dir, "Bark", Lit, new Color(0.12f, 0.09f, 0.07f), 0f, 0.1f);
            var pineM = SceneKit.Mat(Dir, "Pine", Lit, new Color(0.05f, 0.1f, 0.07f), 0f, 0.1f);
            var flame = Emissive("Lantern", new Color(1f, 0.6f, 0.28f), 30f);
            var skyM = Emissive("Sky", new Color(0.3f, 0.4f, 0.62f), 0.1f);

            // ground and the path along z
            var gk = new MeshKit(); gk.Box(new Vector3(0, -0.05f, 6f), new Vector3(80f, 0.1f, 80f));
            SceneKit.Part("Ground", Save(gk, "Ground"), groundM, Vector3.zero, Quaternion.identity);
            var pk = new MeshKit(); pk.Box(new Vector3(0, 0.005f, 6f), new Vector3(1.7f, 0.02f, 40f));
            SceneKit.Part("Path", Save(pk, "Path"), pathM, Vector3.zero, Quaternion.identity);

            // ruins: a broken colonnade either side of the path and an arch at z = 10
            var rk = new MeshKit(); var mk = new MeshKit();
            foreach (var (z, hL, hR) in new[] { (1.5f, 2.6f, 1.1f), (4.5f, 0.7f, 3.0f), (7.5f, 2.2f, 1.6f) })
            {
                rk.Box(new Vector3(-1.7f, hL / 2f, z), new Vector3(0.55f, hL, 0.55f));
                rk.Box(new Vector3(1.7f, hR / 2f, z), new Vector3(0.55f, hR, 0.55f));
                mk.Box(new Vector3(-1.7f, 0.12f, z), new Vector3(0.75f, 0.24f, 0.75f));
                mk.Box(new Vector3(1.7f, 0.12f, z), new Vector3(0.75f, 0.24f, 0.75f));
            }
            rk.Box(new Vector3(-1.45f, 1.7f, 10.5f), new Vector3(0.7f, 3.4f, 0.7f));
            rk.Box(new Vector3(1.45f, 1.7f, 10.5f), new Vector3(0.7f, 3.4f, 0.7f));
            rk.Box(new Vector3(0f, 3.65f, 10.5f), new Vector3(3.8f, 0.5f, 0.8f));
            mk.Box(new Vector3(-3.6f, 0.35f, 6f), new Vector3(0.5f, 0.7f, 6f));
            mk.Box(new Vector3(3.4f, 0.25f, 3.5f), new Vector3(0.5f, 0.5f, 4f));
            mk.Box(new Vector3(2.6f, 0.15f, 9f), new Vector3(1.1f, 0.3f, 0.6f));
            mk.Box(new Vector3(-2.4f, 0.2f, 12.5f), new Vector3(0.9f, 0.4f, 0.9f));
            SceneKit.Part("Ruins", Save(rk, "Ruins", 20f), stoneM, Vector3.zero, Quaternion.identity);
            SceneKit.Part("Ruins moss", Save(mk, "RuinsMoss", 20f), mossM, Vector3.zero, Quaternion.identity);

            // lanterns: one hanging under the arch, one on the colonnade
            var prof = new Vector2[34];
            for (int i = 0; i <= 32; i++) { float a = Mathf.PI * i / 32f; prof[i] = new Vector2(-Mathf.Cos(a), Mathf.Sin(a)); }
            prof[33] = new Vector2(1, 0);
            var sphere = new MeshKit(); sphere.Lathe(prof, 32); var sphereMesh = Save(sphere, "Sphere", 60f);
            foreach (var p in new[] { new Vector3(0f, 3.0f, 10.5f), new Vector3(1.7f, 3.15f, 4.5f) })
            {
                var b = SceneKit.Part("Lantern", sphereMesh, flame, p, Quaternion.identity); b.transform.localScale = Vector3.one * 0.07f;
                b.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                var l = new GameObject("Lantern light").AddComponent<Light>(); l.transform.position = p + Vector3.down * 0.1f;
                l.type = LightType.Point; l.color = new Color(1f, 0.62f, 0.32f); l.intensity = 3.2f; l.range = 9f; l.shadows = LightShadows.Soft;
            }

            // pines: trunk + two cone tiers, scattered off the path
            var tk = new MeshKit(); tk.Lathe(new[] { new Vector2(0, 0), new Vector2(0, 0.2f), new Vector2(6.5f, 0.08f), new Vector2(6.5f, 0) }, 14);
            var trunk = Save(tk, "Trunk", 60f);
            var ck = new MeshKit();
            ck.Lathe(new[] { new Vector2(1.8f, 0), new Vector2(1.8f, 1.9f), new Vector2(5.4f, 0) }, 16);
            ck.Lathe(new[] { new Vector2(4.0f, 0), new Vector2(4.0f, 1.35f), new Vector2(8.6f, 0) }, 16);
            var crown = Save(ck, "Crown", 30f);
            var trees = new GameObject("Pines").transform;
            int placed = 0;
            for (int n = 0; n < 400 && placed < 70; n++)
            {
                float x = R(-22f, 22f), z = R(-12f, 32f);
                if (Mathf.Abs(x) < 4.2f || (Mathf.Abs(x) < 5.5f && z > 0f && z < 13f)) continue;
                float s = R(0.75f, 1.35f); var rot = Quaternion.Euler(0, R(0, 360), 0);
                var tr = SceneKit.Part("Trunk", trunk, barkM, new Vector3(x, 0, z), rot, trees); tr.transform.localScale = new Vector3(s, s * R(0.9f, 1.2f), s);
                var cr = SceneKit.Part("Crown", crown, pineM, new Vector3(x, 0, z), rot, trees); cr.transform.localScale = tr.transform.localScale;
                placed++;
            }
            // dim sky wall far behind the arch so the pines read as silhouettes
            var sk = new MeshKit(); sk.Quad(new Vector3(-60, -2, 40), new Vector3(60, -2, 40), new Vector3(60, 30, 40), new Vector3(-60, 30, 40), Vector3.back);
            SceneKit.Part("Sky", Save(sk, "Sky"), skyM, Vector3.zero, Quaternion.identity).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            var man = Mannequin.Build(Lit, Dir, (m, part) =>
            {
                m.SetColor("_BaseColor", part == "skin" ? new Color(0.78f, 0.62f, 0.52f) : new Color(0.5f, 0.1f, 0.08f));
                m.SetFloat("_Smoothness", 0.3f);
            });
            man.transform.rotation = Quaternion.Euler(0, 180f, 0);
            man.transform.position = new Vector3(0, 0, dir.startZ);
            dir.character = man.transform;

            // moonlight from behind the arch, low, cool
            var moon = SceneKit.Sun("Moon", new Vector3(18f, 172f, 0f), new Color(0.62f, 0.72f, 1f), 1.1f, true);
            moon.shadowStrength = 0.85f;
            SceneKit.Shadows(40f);

            // fog: ground bank from the source's Ground Fog prefab (box, no speed, 4-10 s life, 1-3 m quads, rate scaled
            // with the box area), falling wisps from its Falling Fog prefab (small box, 1 m quads, gravity raised so they
            // reach the ground)
            var gMat = FogMat("FogGround", new Color(0.6f, 0.68f, 0.8f, 1f), 0.8f, 20f, 0.5f, 2f, 0.6f, 0.2f,
                              new Vector4(-0.1f, 0.05f, 0, 0), new Vector4(0, 0, 0.01f, 0));
            var fMat = FogMat("FogFalling", new Color(0.68f, 0.74f, 0.86f, 1f), 0.63f, 10f, 1f, 2f, 0.607f, 0f,
                              new Vector4(-0.01f, 0.05f, 0, 0), new Vector4(0, 0.05f, 0.01f, 0));
            dir.groundMat = gMat; dir.fallMat = fMat;
            var gBox = new Vector3(13f, 1.2f, 12f); float gRate = 160f;
            dir.ground = Emitter("Ground fog", FogDirector.GroundLayer, gMat, new Vector3(0, 0.6f, 5.5f), gBox, gRate,
                                 new Vector2(4f, 10f), new Vector2(1f, 3f), Vector2.zero, 1f, 2000, 9401);
            var fall = new System.Collections.Generic.List<ParticleSystem>();
            uint seed = 9410;
            foreach (var p in new[] { new Vector3(0f, 3.2f, 10.5f), new Vector3(-2.3f, 3.6f, 3f), new Vector3(2.4f, 3.8f, 6.5f),
                                      new Vector3(-2.6f, 3.4f, 8f), new Vector3(2.2f, 3.5f, 1f), new Vector3(0.6f, 3.9f, -1f) })
                fall.Add(Emitter("Falling fog", FogDirector.FallLayer, fMat, p, new Vector3(1f, 0.5f, 1f), 10f,
                                 new Vector2(4f, 4f), new Vector2(1f, 1f), new Vector2(0.04f, 0.06f), 0.5f, 1000, seed++));
            dir.falling = fall.ToArray();
            dir.groundSpec = $"{gBox.x:0} x {gBox.z:0} m box, {gRate:0}/s, 1-3 m quads, 4-10 s life";
            dir.fallSpec = $"{fall.Count} emitters, 10/s each, 1 m quads, 4 s life, gravity 0.04-0.06";

            dir.cams = new Camera[3];
            for (int i = 0; i < 3; i++)
            {
                var c = SceneKit.Cam("Band camera " + i, new Rect(0, 0, 1, 1), 48, new Color(0.03f, 0.04f, 0.06f), -10 - i);
                c.nearClipPlane = 0.05f; c.farClipPlane = 80f;
                c.GetUniversalAdditionalCameraData().requiresDepthTexture = true;
                dir.cams[i] = c;
            }
            SceneKit.Grade(Dir, bloom: 0.45f, exposure: 0.2f, contrast: 8f, saturation: 0f, vignette: 0.3f, grain: 0f);
            dir.ambient = new Color(0.09f, 0.11f, 0.15f);

            var display = SceneKit.Cam("Display camera", new Rect(0, 0, 1, 1), 40, Color.black, 10, false);
            display.cullingMask = 1 << 8; display.transform.position = new Vector3(0, -50, 0); display.tag = "MainCamera";
            var compose = new Material(Shader.Find("Hidden/GT/FogCompose")); AssetDatabase.CreateAsset(compose, Dir + "Compose.mat");
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Display quad"; Object.DestroyImmediate(quad.GetComponent<Collider>()); quad.layer = 8;
            var qm = Object.Instantiate(quad.GetComponent<MeshFilter>().sharedMesh); qm.name = "DisplayQuad"; qm.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
            AssetDatabase.CreateAsset(qm, Dir + "DisplayQuad.asset");
            quad.GetComponent<MeshFilter>().sharedMesh = qm; quad.GetComponent<Renderer>().sharedMaterial = compose;
            quad.transform.position = display.transform.position + Vector3.forward;

            dir.compose = compose;
            dir.title = LabelSpec.Create("", 0.012f, 0.935f, 30);
            dir.l1 = new LabelSpec[3]; dir.l2 = new LabelSpec[3];
            for (int i = 0; i < 3; i++) { dir.l1[i] = LabelSpec.Create("", 0.012f, 0.058f, 28); dir.l2[i] = LabelSpec.Create("", 0.012f, 0.018f, 22); }
            AssetDatabase.SaveAssets();
            Debug.Log($"[gt] fog scene: pines {placed}, falling emitters {fall.Count}, fog shader {(Shader.Find(FogShader) != null)}");
        }
    }
}
