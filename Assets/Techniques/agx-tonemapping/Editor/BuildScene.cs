// 93 AgX tone mapping: a room with a big sunlit window, two coloured neon tubes, bare bulbs, metal props and the
// stand-in mannequin, built from code so that highlights clip. The builder copies the project's URP asset with HDR
// colour grading and a renderer that carries the source's Custom Tone Mapping renderer feature (the URP package is not
// modified), and makes one volume profile per tone mapper, each on its own layer. Built from code; no external assets.
using CustomToneMapping.Baker.AgX;
using CustomToneMapping.URP;
using CustomToneMapping.URP.RendererFeatures;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using CTM = CustomToneMapping.URP.CustomToneMapping;

namespace GameTech.Posts
{
    public static class AgXScene
    {
        const string Dir = "Assets/Techniques/agx-tonemapping/";
        const string Lit = "Universal Render Pipeline/Lit";

        static Mesh Save(MeshKit k, string name, float smooth = 35f)
        {
            var m = k.Build(name, smooth);
            AssetDatabase.CreateAsset(m, Dir + name + ".asset"); return m;
        }

        // emission set in linear units: Material.SetColor treats the colour as sRGB and raises each channel to 2.2
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

        static Volume Profile(string name, int layer, System.Action<VolumeProfile> setup)
        {
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, Dir + name + ".asset");
            var b = SceneKit.Add<Bloom>(p); b.intensity.Override(0.25f); b.threshold.Override(1.0f); b.scatter.Override(0.65f);
            var ca = SceneKit.Add<ColorAdjustments>(p); ca.postExposure.Override(AgXDirector.BaseExposure); ca.contrast.Override(0f); ca.saturation.Override(0f);
            var vig = SceneKit.Add<Vignette>(p); vig.intensity.Override(0.25f); vig.smoothness.Override(0.45f);
            setup(p);
            EditorUtility.SetDirty(p);
            var go = new GameObject("Volume " + name); go.layer = layer;
            var v = go.AddComponent<Volume>(); v.isGlobal = true; v.sharedProfile = p;
            return v;
        }

        static void Urp(VolumeProfile p) { SceneKit.Add<Tonemapping>(p).mode.Override(TonemappingMode.ACES); }

        static void Custom(VolumeProfile p, ToneMappingMode mode)
        {
            SceneKit.Add<Tonemapping>(p).mode.Override(TonemappingMode.None);
            SceneKit.Add<CTM>(p).mode.Override(mode);
        }

        static void AgX(VolumeProfile p, AgXLookPreset look)
        {
            Custom(p, ToneMappingMode.AgX);
            var a = SceneKit.Add<CustomToneMapping.URP.AgXToneMapping>(p); a.look.Override(look); a.lookIntensity.Override(1f);
        }

        static RenderPipelineAsset Pipeline()
        {
            var srcRp = (QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
            var srcRd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
            var rd = Object.Instantiate(srcRd); rd.name = "AgXRenderer";
            rd.rendererFeatures.Clear();
            rd.useNativeRenderPass = false;
            AssetDatabase.CreateAsset(rd, Dir + "AgXRenderer.asset");
            var f = ScriptableObject.CreateInstance<CustomToneMappingRendererFeature>(); f.name = "CustomToneMapping";
            AssetDatabase.AddObjectToAsset(f, rd);
            var fso = new SerializedObject(f);
            fso.FindProperty("shader").objectReferenceValue = Shader.Find("Hidden/CustomToneMapChain");
            fso.ApplyModifiedPropertiesWithoutUndo();
            rd.rendererFeatures.Add(f);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(f, out string _, out long lid);
            var so = new SerializedObject(rd);
            var map = so.FindProperty("m_RendererFeatureMap"); map.ClearArray(); map.InsertArrayElementAtIndex(0); map.GetArrayElementAtIndex(0).longValue = lid;
            so.ApplyModifiedPropertiesWithoutUndo();

            var rp = Object.Instantiate(srcRp); rp.name = "AgXPipeline";
            AssetDatabase.CreateAsset(rp, Dir + "AgXPipeline.asset");
            var rso = new SerializedObject(rp);
            var list = rso.FindProperty("m_RendererDataList"); list.ClearArray(); list.InsertArrayElementAtIndex(0);
            list.GetArrayElementAtIndex(0).objectReferenceValue = rd;
            rso.FindProperty("m_DefaultRendererIndex").intValue = 0;
            rso.FindProperty("m_SupportsHDR").boolValue = true;
            rso.FindProperty("m_ColorGradingMode").intValue = (int)ColorGradingMode.HighDynamicRange;
            rso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rd); EditorUtility.SetDirty(rp); AssetDatabase.SaveAssets();
            Debug.Log($"[gt] pipeline copy of {srcRp.name}: HDR {rp.supportsHDR}, grading {rp.colorGradingMode}, features {rd.rendererFeatures.Count}, shader {(Shader.Find("Hidden/CustomToneMapChain") != null)}");
            return rp;
        }

        public static void Build()
        {
            RenderSettings.skybox = null; RenderSettings.fog = false;
            var dir = new GameObject("Director").AddComponent<AgXDirector>();
            dir.pipeline = Pipeline();

            var floorM = SceneKit.Mat(Dir, "Floor", Lit, new Color(0.42f, 0.4f, 0.37f), 0f, 0.45f);
            var wallM = SceneKit.Mat(Dir, "Wall", Lit, new Color(0.62f, 0.58f, 0.52f), 0f, 0.15f);
            var ceilM = SceneKit.Mat(Dir, "Ceiling", Lit, new Color(0.5f, 0.5f, 0.5f), 0f, 0.1f);
            var frameM = SceneKit.Mat(Dir, "Frame", Lit, new Color(0.08f, 0.08f, 0.09f), 0.6f, 0.5f);
            var groundM = SceneKit.Mat(Dir, "Ground", Lit, new Color(0.7f, 0.66f, 0.55f), 0f, 0.2f);
            var woodM = SceneKit.Mat(Dir, "Wood", Lit, new Color(0.36f, 0.22f, 0.13f), 0f, 0.55f);
            var chromeM = SceneKit.Mat(Dir, "Chrome", Lit, new Color(0.95f, 0.95f, 0.96f), 1f, 0.95f);
            var copperM = SceneKit.Mat(Dir, "Copper", Lit, new Color(0.95f, 0.62f, 0.45f), 1f, 0.8f);
            var orangeM = SceneKit.Mat(Dir, "Orange", Lit, new Color(0.95f, 0.4f, 0.05f), 0f, 0.35f);
            var cyanM = SceneKit.Mat(Dir, "Cyan", Lit, new Color(0.05f, 0.6f, 0.75f), 0f, 0.35f);
            var sky = Emissive("Sky", new Color(0.4f, 0.62f, 1f), 2f);
            var red = Emissive("NeonRed", new Color(1f, 0.05f, 0.03f), 8f);
            var blue = Emissive("NeonBlue", new Color(0.05f, 0.22f, 1f), 8f);
            var bulb = Emissive("Bulb", new Color(1f, 0.72f, 0.42f), 40f);

            // room 10 x 3.4 x 9 m, back wall at z = 4 with a 3.6 x 2 m window
            var rk = new MeshKit();
            rk.Box(new Vector3(0, -0.05f, -0.5f), new Vector3(10f, 0.1f, 9f));
            SceneKit.Part("Floor", Save(rk, "Floor"), floorM, Vector3.zero, Quaternion.identity);
            var wk = new MeshKit();
            wk.Box(new Vector3(-3.4f, 1.7f, 4.1f), new Vector3(3.2f, 3.4f, 0.2f));
            wk.Box(new Vector3(3.4f, 1.7f, 4.1f), new Vector3(3.2f, 3.4f, 0.2f));
            wk.Box(new Vector3(0, 0.4f, 4.1f), new Vector3(3.6f, 0.8f, 0.2f));
            wk.Box(new Vector3(0, 3.1f, 4.1f), new Vector3(3.6f, 0.6f, 0.2f));
            wk.Box(new Vector3(-5.1f, 1.7f, -0.5f), new Vector3(0.2f, 3.4f, 9.2f));
            wk.Box(new Vector3(5.1f, 1.7f, -0.5f), new Vector3(0.2f, 3.4f, 9.2f));
            wk.Box(new Vector3(0, 1.7f, -5.1f), new Vector3(10.4f, 3.4f, 0.2f));
            SceneKit.Part("Walls", Save(wk, "Walls"), wallM, Vector3.zero, Quaternion.identity);
            var ck = new MeshKit(); ck.Box(new Vector3(0, 3.45f, -0.5f), new Vector3(10.4f, 0.1f, 9.2f));
            SceneKit.Part("Ceiling", Save(ck, "Ceiling"), ceilM, Vector3.zero, Quaternion.identity);
            var fk = new MeshKit();
            fk.Box(new Vector3(0, 1.8f, 4.02f), new Vector3(0.07f, 2.0f, 0.1f));
            fk.Box(new Vector3(0, 1.8f, 4.02f), new Vector3(3.6f, 0.07f, 0.1f));
            fk.Box(new Vector3(0, 0.83f, 3.98f), new Vector3(3.8f, 0.06f, 0.3f));
            SceneKit.Part("Window frame", Save(fk, "Frame"), frameM, Vector3.zero, Quaternion.identity);

            // outside: bright sky wall and sunlit ground
            var sk = new MeshKit(); sk.Quad(new Vector3(-30, -2, 16), new Vector3(30, -2, 16), new Vector3(30, 22, 16), new Vector3(-30, 22, 16), Vector3.back);
            var skyGo = SceneKit.Part("Sky", Save(sk, "Sky"), sky, Vector3.zero, Quaternion.identity);
            skyGo.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var gk = new MeshKit(); gk.Box(new Vector3(0, -0.05f, 10.1f), new Vector3(60f, 0.1f, 12f));
            SceneKit.Part("Ground", Save(gk, "Ground"), groundM, Vector3.zero, Quaternion.identity);

            // neon tubes on the back wall either side of the window, bulbs hanging in front of it
            var tube = new MeshKit();
            tube.Lathe(new[] { new Vector2(0, 0), new Vector2(0, 0.03f), new Vector2(2.2f, 0.03f), new Vector2(2.2f, 0) }, 24);
            var tubeMesh = Save(tube, "Tube", 60f);
            foreach (var (x, m, c) in new[] { (-2.15f, red, new Color(1f, 0.08f, 0.05f)), (2.15f, blue, new Color(0.1f, 0.3f, 1f)) })
            {
                SceneKit.Part("Neon " + m.name, tubeMesh, m, new Vector3(x, 0.7f, 3.93f), Quaternion.identity).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                var lg = new GameObject("Neon light " + m.name); lg.transform.position = new Vector3(x, 1.8f, 3.75f);
                var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.intensity = 3f; l.range = 5f;
            }
            var prof = new Vector2[34];
            for (int i = 0; i <= 32; i++) { float a = Mathf.PI * i / 32f; prof[i] = new Vector2(-Mathf.Cos(a), Mathf.Sin(a)); }
            prof[33] = new Vector2(1, 0);
            var sphere = new MeshKit(); sphere.Lathe(prof, 48); var sphereMesh = Save(sphere, "Sphere", 60f);
            foreach (var p in new[] { new Vector3(-0.95f, 2.35f, 1.1f), new Vector3(1.05f, 2.55f, 2.7f) })
            {
                var b = SceneKit.Part("Bulb", sphereMesh, bulb, p, Quaternion.identity); b.transform.localScale = Vector3.one * 0.06f;
                b.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                var cord = new MeshKit(); cord.Box(new Vector3(0, (3.4f - p.y) / 2f + 0.05f, 0), new Vector3(0.012f, 3.4f - p.y - 0.06f, 0.012f));
                SceneKit.Part("Cord", Save(cord, "Cord" + p.x), frameM, p, Quaternion.identity);
                var lg = new GameObject("Bulb light"); lg.transform.position = p;
                var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.75f, 0.5f); l.intensity = 2.5f; l.range = 7f;
            }

            // table in the sunbeam with metal and saturated props
            var tk = new MeshKit();
            tk.Box(new Vector3(0, 0.75f, 1.3f), new Vector3(2.2f, 0.06f, 0.9f));
            foreach (var (lx, lz) in new[] { (-1f, 0.92f), (1f, 0.92f), (-1f, 1.68f), (1f, 1.68f) }) tk.Box(new Vector3(lx, 0.36f, lz), new Vector3(0.06f, 0.72f, 0.06f));
            SceneKit.Part("Table", Save(tk, "Table"), woodM, Vector3.zero, Quaternion.identity);
            var ball = SceneKit.Part("Chrome ball", sphereMesh, chromeM, new Vector3(-0.55f, 0.94f, 1.25f), Quaternion.identity); ball.transform.localScale = Vector3.one * 0.16f;
            var jk = new MeshKit();
            jk.Lathe(new[] { new Vector2(0, 0), new Vector2(0, 0.12f), new Vector2(0.02f, 0.13f), new Vector2(0.12f, 0.17f), new Vector2(0.24f, 0.17f),
                             new Vector2(0.34f, 0.12f), new Vector2(0.4f, 0.1f), new Vector2(0.46f, 0.12f), new Vector2(0.48f, 0.12f), new Vector2(0.48f, 0) }, 72);
            SceneKit.Part("Copper jug", Save(jk, "Jug", 40f), copperM, new Vector3(0.15f, 0.78f, 1.4f), Quaternion.identity);
            var bx = new MeshKit(); bx.Box(new Vector3(0, 0.12f, 0), new Vector3(0.36f, 0.24f, 0.36f));
            var boxMesh = Save(bx, "Box", 20f);
            SceneKit.Part("Orange box", boxMesh, orangeM, new Vector3(0.75f, 0.78f, 1.2f), Quaternion.Euler(0, 20, 0));
            SceneKit.Part("Cyan box", boxMesh, cyanM, new Vector3(-1.4f, 0f, 0.4f), Quaternion.Euler(0, -15, 0));

            var man = Mannequin.Build(Lit, Dir, (m, part) =>
            {
                m.SetColor("_BaseColor", part == "skin" ? new Color(0.78f, 0.6f, 0.5f) : new Color(0.62f, 0.12f, 0.1f));
                m.SetFloat("_Smoothness", part == "skin" ? 0.4f : 0.3f);
            });
            man.transform.position = new Vector3(1.55f, 0f, 1.75f);
            man.transform.rotation = Quaternion.Euler(0, 205f, 0);
            dir.character = man.transform;

            // sun through the window, low enough to throw a patch across the floor and table
            var sun = SceneKit.Sun("Sun", new Vector3(30f, 196f, 0f), new Color(1f, 0.9f, 0.76f), 7f, true);
            sun.shadowStrength = 1f;
            SceneKit.Shadows(25f);

            dir.cams = new Camera[3];
            for (int i = 0; i < 3; i++)
            {
                var c = SceneKit.Cam("Band camera " + i, new Rect(0, 0, 1, 1), 50, Color.black, -10 - i);
                c.clearFlags = CameraClearFlags.SolidColor; c.cullingMask = ~(1 << 8); c.farClipPlane = 60f; c.nearClipPlane = 0.05f;
                dir.cams[i] = c;
            }
            dir.vols = new[]
            {
                Profile("Post_URP_ACES", 20, Urp),
                Profile("Post_AgX", 21, p => AgX(p, AgXLookPreset.None)),
                Profile("Post_ACES2", 22, p => { Custom(p, ToneMappingMode.ACES2); SceneKit.Add<Aces2ToneMapping>(p); }),
                Profile("Post_AgX_Low", 23, p => AgX(p, AgXLookPreset.LowContrast)),
                Profile("Post_AgX_High", 24, p => AgX(p, AgXLookPreset.HighContrast)),
            };
            AssetDatabase.SaveAssets();
            dir.ambient = new Color(0.1f, 0.11f, 0.13f);

            var display = SceneKit.Cam("Display camera", new Rect(0, 0, 1, 1), 40, Color.black, 10, false);
            display.cullingMask = 1 << 8; display.transform.position = new Vector3(0, -50, 0); display.tag = "MainCamera";
            var compose = new Material(Shader.Find("Hidden/GT/AgXCompose")); AssetDatabase.CreateAsset(compose, Dir + "Compose.mat");
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
        }
    }
}
