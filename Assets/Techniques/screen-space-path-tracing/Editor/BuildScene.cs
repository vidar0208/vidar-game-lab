// Builds the screen-space path tracing scene from code: a room with coloured walls, a glossy floor, emissive strips,
// one spot light and a few props, a box-projected reflection probe for the fallback, and a copy of the URP asset with
// three deferred renderers (none / feature A / feature B). The source's renderer feature and shader are used as they are.
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GameTech.Posts
{
    public static class SSPTScene
    {
        const string Dir = "Assets/Techniques/screen-space-path-tracing/";
        const string Lit = "Universal Render Pipeline/Lit";

        static Material M(string name, Color c, float metal, float smooth)
        {
            var m = new Material(Shader.Find(Lit));
            m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_EnvironmentReflections", 0f); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            AssetDatabase.CreateAsset(m, Dir + name + ".mat"); return m;
        }

        static Material Emit(string name, Color c, float intensity)
        {
            var m = new Material(Shader.Find(Lit));
            m.SetColor("_BaseColor", Color.black); m.SetFloat("_Smoothness", 0.2f);
            m.SetFloat("_EnvironmentReflections", 0f); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * intensity);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            AssetDatabase.CreateAsset(m, Dir + name + ".mat"); return m;
        }

        static GameObject Box(string name, Material m, Vector3 pos, Vector3 size, float yaw = 0f, PrimitiveType type = PrimitiveType.Cube)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.position = pos; go.transform.localScale = size; go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            go.GetComponent<Renderer>().sharedMaterial = m;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ReflectionProbeStatic);
            return go;
        }

        static ScreenSpacePathTracingAccumulation Feature(UniversalRendererData rd, Material mat, string name)
        {
            var f = ScriptableObject.CreateInstance<ScreenSpacePathTracingAccumulation>(); f.name = name;
            var fso = new SerializedObject(f);
            fso.FindProperty("m_PathTracingMaterial").objectReferenceValue = mat;
            fso.FindProperty("progressBar").boolValue = false;
            fso.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.AddObjectToAsset(f, rd);
            rd.rendererFeatures.Add(f);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(f, out string _, out long lid);
            var so = new SerializedObject(rd);
            var map = so.FindProperty("m_RendererFeatureMap"); map.InsertArrayElementAtIndex(map.arraySize); map.GetArrayElementAtIndex(map.arraySize - 1).longValue = lid;
            so.ApplyModifiedPropertiesWithoutUndo();
            return f;
        }

        static UniversalRendererData Renderer(UniversalRendererData src, string name)
        {
            var rd = Object.Instantiate(src); rd.name = name;
            rd.rendererFeatures.Clear(); rd.useNativeRenderPass = false;
            AssetDatabase.CreateAsset(rd, Dir + name + ".asset");
            var so = new SerializedObject(rd);
            so.FindProperty("m_RendererFeatureMap").ClearArray();
            so.FindProperty("m_RenderingMode").intValue = (int)RenderingMode.Deferred;
            so.ApplyModifiedPropertiesWithoutUndo();
            return rd;
        }

        static VolumeProfile PT(string name)
        {
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, Dir + name + ".asset");
            var s = SceneKit.Add<ScreenSpacePathTracing>(p);
            s.state.Override(true); s.maximumSamples.Override(128); s.maximumDepth.Override(4); s.samplesPerPixel.Override(1);
            s.denoiser.Override(ScreenSpacePathTracing.DenoiserType.Temporal);
            EditorUtility.SetDirty(p); return p;
        }

        static void Spot(string name, Color c, float intensity, float angle, Vector3 pos, Vector3 at)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.type = LightType.Spot; l.color = c; l.intensity = intensity; l.range = 10f;
            l.spotAngle = angle; l.innerSpotAngle = angle * 0.45f; l.shadows = LightShadows.Soft;
            l.transform.position = pos; l.transform.LookAt(at);
        }

        public static void Build()
        {
            RenderSettings.skybox = null; RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            var dir = new GameObject("Director").AddComponent<SSPTDirector>();

            // materials: the source's path tracing material, one copy per renderer feature
            AssetDatabase.CopyAsset(Dir + "SSPT/ScreenSpacePathTracing.mat", Dir + "PT_A.mat");
            AssetDatabase.CopyAsset(Dir + "SSPT/ScreenSpacePathTracing.mat", Dir + "PT_B.mat");
            dir.matA = AssetDatabase.LoadAssetAtPath<Material>(Dir + "PT_A.mat");
            dir.matB = AssetDatabase.LoadAssetAtPath<Material>(Dir + "PT_B.mat");

            // pipeline copy: HDR 64-bit (offline accumulation above 64 samples), no MSAA, three deferred renderers
            var srcRp = (QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
            var srcRd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
            var rd0 = Renderer(srcRd, "R0_Off"); var rdA = Renderer(srcRd, "R1_PT_A"); var rdB = Renderer(srcRd, "R2_PT_B");
            dir.featA = Feature(rdA, dir.matA, "SSPT_A"); dir.featB = Feature(rdB, dir.matB, "SSPT_B");
            var rp = Object.Instantiate(srcRp); rp.name = "SSPTPipeline";
            AssetDatabase.CreateAsset(rp, Dir + "SSPTPipeline.asset");
            var rso = new SerializedObject(rp);
            var list = rso.FindProperty("m_RendererDataList"); list.ClearArray();
            var rds = new[] { rd0, rdA, rdB };
            for (int i = 0; i < 3; i++) { list.InsertArrayElementAtIndex(i); list.GetArrayElementAtIndex(i).objectReferenceValue = rds[i]; }
            rso.FindProperty("m_DefaultRendererIndex").intValue = 0;
            rso.FindProperty("m_SupportsHDR").boolValue = true;
            var prec = rso.FindProperty("m_HDRColorBufferPrecision"); if (prec != null) prec.intValue = 1;
            rso.FindProperty("m_MSAA").intValue = 1;
            rso.FindProperty("m_RenderScale").floatValue = 1f;
            rso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rd0); EditorUtility.SetDirty(rdA); EditorUtility.SetDirty(rdB); EditorUtility.SetDirty(rp); AssetDatabase.SaveAssets();
            dir.pipeline = rp;
            Debug.Log($"[gt] pipeline copy: HDR {rp.supportsHDR}, precision {(prec != null ? prec.intValue : -1)}, msaa {rp.msaaSampleCount}, renderers 3, features {rdA.rendererFeatures.Count}+{rdB.rendererFeatures.Count}");

            // room: inner faces x -3..3, y 0..3.2, z 0..7, open towards -z
            var floorM = M("Floor", new Color(0.07f, 0.07f, 0.075f), 0f, 0.88f);
            var backM = M("BackWall", new Color(0.78f, 0.76f, 0.72f), 0f, 0.15f);
            var leftM = M("LeftWall", new Color(0.78f, 0.24f, 0.1f), 0f, 0.1f);
            var rightM = M("RightWall", new Color(0.08f, 0.46f, 0.5f), 0f, 0.1f);
            var ceilM = M("Ceiling", new Color(0.8f, 0.8f, 0.8f), 0f, 0.05f);
            var whiteM = M("White", new Color(0.85f, 0.84f, 0.82f), 0f, 0.3f);
            var chromeM = M("Chrome", new Color(0.95f, 0.95f, 0.96f), 1f, 0.96f);
            var goldM = M("Gold", new Color(1f, 0.72f, 0.38f), 1f, 0.75f);
            var stripM = Emit("Strip", new Color(1f, 0.85f, 0.65f), 6f);
            var cyanM = Emit("Cyan", new Color(0.2f, 0.75f, 1f), 5f);
            Box("Floor", floorM, new Vector3(0, -0.05f, 3.5f), new Vector3(6.2f, 0.1f, 7.2f));
            Box("Ceiling", ceilM, new Vector3(0, 3.25f, 3.5f), new Vector3(6.2f, 0.1f, 7.2f));
            Box("Back", backM, new Vector3(0, 1.6f, 7.05f), new Vector3(6.2f, 3.4f, 0.1f));
            Box("Left", leftM, new Vector3(-3.05f, 1.6f, 3.5f), new Vector3(0.1f, 3.4f, 7.2f));
            Box("Right", rightM, new Vector3(3.05f, 1.6f, 3.5f), new Vector3(0.1f, 3.4f, 7.2f));
            Box("Ceiling strip", stripM, new Vector3(0, 3.19f, 4.6f), new Vector3(2.6f, 0.02f, 0.35f));
            Box("Back strip", cyanM, new Vector3(1.6f, 1.7f, 6.99f), new Vector3(0.12f, 2.6f, 0.02f));
            Box("Plinth", whiteM, new Vector3(-0.2f, 0.25f, 4.6f), new Vector3(1.6f, 0.5f, 1.1f), 12f);
            Box("Chrome ball", chromeM, new Vector3(-0.55f, 0.95f, 4.55f), Vector3.one * 0.9f, 0f, PrimitiveType.Sphere);
            Box("Block", whiteM, new Vector3(0.95f, 0.45f, 3.7f), Vector3.one * 0.9f, 28f);
            Box("Gold ball", goldM, new Vector3(0.95f, 1.2f, 3.7f), Vector3.one * 0.6f, 0f, PrimitiveType.Sphere);
            Box("Column", whiteM, new Vector3(-1.9f, 0.9f, 5.6f), new Vector3(0.5f, 1.8f, 0.5f), 0f, PrimitiveType.Cylinder);

            Spot("Key", new Color(1f, 0.86f, 0.7f), 14f, 62f, new Vector3(-1.4f, 3.05f, 3.2f), new Vector3(0.1f, 0f, 4.7f));
            Spot("Wash left", new Color(1f, 0.9f, 0.8f), 18f, 70f, new Vector3(1.2f, 3.0f, 2.2f), new Vector3(-3f, 0.9f, 4.6f));
            Spot("Wash right", new Color(1f, 0.95f, 0.9f), 12f, 60f, new Vector3(-1.0f, 2.9f, 5.6f), new Vector3(3f, 0.6f, 5.0f));
            // reflection probe for the fallback: realtime, rendered once at start by the director with renderer 0 (no path tracing)
            var pgo = new GameObject("Probe"); pgo.transform.position = new Vector3(0, 1.6f, 3.6f);
            var probe = pgo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime; probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.boxProjection = true; probe.size = new Vector3(6f, 3.2f, 7f);
            probe.center = new Vector3(0, 0f, -0.1f); probe.resolution = 256; probe.hdr = true;
            dir.probe = probe;

            // volumes: grading for every camera (layer 0), path tracing A on layer 20, B on layer 21
            SceneKit.Grade(Dir, 0.35f, 0.2f, 8f, 6f, 0.22f, 0f);
            var va = new GameObject("PT volume A") { layer = 20 }.AddComponent<Volume>(); va.isGlobal = true; va.sharedProfile = PT("PT_A");
            var vb = new GameObject("PT volume B") { layer = 21 }.AddComponent<Volume>(); vb.isGlobal = true; vb.sharedProfile = PT("PT_B");
            dir.volA = va; dir.volB = vb;
            AssetDatabase.SaveAssets();

            var clear = SceneKit.Cam("Clear", new Rect(0, 0, 1, 1), 60f, Color.black, -10, false); clear.cullingMask = 0;
            dir.camL = SceneKit.Cam("Left", new Rect(0, 0, 0.5f, 1), 62f, Color.black, 0, true);
            dir.camR = SceneKit.Cam("Right", new Rect(0.5f, 0, 0.5f, 1), 62f, Color.black, 1, true);
            dir.camL.GetUniversalAdditionalCameraData().volumeLayerMask = (1 << 0) | (1 << 20);
            dir.camR.GetUniversalAdditionalCameraData().volumeLayerMask = (1 << 0) | (1 << 21);

            dir.title = LabelSpec.Create("", 0.012f, 0.93f, 40);
            dir.l1 = LabelSpec.Create("", 0.012f, 0.058f, 34); dir.l2 = LabelSpec.Create("", 0.012f, 0.018f, 30);
            dir.r1 = LabelSpec.Create("", 0.512f, 0.058f, 34); dir.r2 = LabelSpec.Create("", 0.512f, 0.018f, 30);
        }
    }
}
