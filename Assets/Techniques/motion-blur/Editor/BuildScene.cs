// Builds the motion blur scene from code: a dark showroom with neon strips, a standing fan, a turntable, a swinging
// light, a bouncing ball and the stand-in mannequin waving. A copy of the URP asset uses one renderer with the
// Kino Motion feature; each camera carries its own KinoMotionCamera settings.
using GameTech.KinoMotionURP;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GameTech.Posts
{
    public static class MotionScene
    {
        const string Dir = "Assets/Techniques/motion-blur/";
        const string Lit = "Universal Render Pipeline/Lit";

        static Material M(string name, Color c, float metal, float smooth)
        {
            var m = new Material(Shader.Find(Lit));
            m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth);
            AssetDatabase.CreateAsset(m, Dir + name + ".mat"); return m;
        }

        static Material Emit(string name, Color c, float intensity)
        {
            var m = new Material(Shader.Find(Lit));
            m.SetColor("_BaseColor", Color.black); m.SetFloat("_Smoothness", 0.3f);
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * intensity);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            AssetDatabase.CreateAsset(m, Dir + name + ".mat"); return m;
        }

        static GameObject P(string name, PrimitiveType type, Material m, Vector3 pos, Vector3 size, Transform parent = null, Vector3? euler = null)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = size; go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            var r = go.GetComponent<Renderer>(); r.sharedMaterial = m;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            return go;
        }

        static Transform Node(string name, Vector3 pos, Transform parent = null)
        {
            var t = new GameObject(name).transform; if (parent != null) t.SetParent(parent, false); t.localPosition = pos; return t;
        }

        static void Spot(string name, Color c, float intensity, float angle, Vector3 pos, Vector3 at, float range = 12f)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.type = LightType.Spot; l.color = c; l.intensity = intensity; l.range = range;
            l.spotAngle = angle; l.innerSpotAngle = angle * 0.5f; l.shadows = LightShadows.Soft;
            l.transform.position = pos; l.transform.LookAt(at);
        }

        public static void Build()
        {
            RenderSettings.skybox = null; RenderSettings.fog = false;
            SceneKit.Ambient(new Color(0.07f, 0.075f, 0.1f), new Color(0.05f, 0.05f, 0.06f), new Color(0.03f, 0.03f, 0.03f));
            var dir = new GameObject("Director").AddComponent<MotionDirector>();

            // pipeline copy: one renderer (copy of the project's PC renderer) with the Kino Motion feature
            var srcRp = (QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
            var srcRd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
            var rd = Object.Instantiate(srcRd); rd.name = "R_Motion";
            AssetDatabase.CreateAsset(rd, Dir + "R_Motion.asset");
            var feat = ScriptableObject.CreateInstance<KinoMotionFeature>(); feat.name = "KinoMotion";
            feat.shader = AssetDatabase.LoadAssetAtPath<Shader>(Dir + "KinoMotion/KinoMotionURP.shader");
            AssetDatabase.AddObjectToAsset(feat, rd);
            rd.rendererFeatures.Add(feat);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feat, out string _, out long lid);
            var so = new SerializedObject(rd);
            var map = so.FindProperty("m_RendererFeatureMap"); map.InsertArrayElementAtIndex(map.arraySize); map.GetArrayElementAtIndex(map.arraySize - 1).longValue = lid;
            so.ApplyModifiedPropertiesWithoutUndo();
            var rp = Object.Instantiate(srcRp); rp.name = "MotionPipeline";
            AssetDatabase.CreateAsset(rp, Dir + "MotionPipeline.asset");
            var rso = new SerializedObject(rp);
            var list = rso.FindProperty("m_RendererDataList"); list.ClearArray(); list.InsertArrayElementAtIndex(0); list.GetArrayElementAtIndex(0).objectReferenceValue = rd;
            rso.FindProperty("m_DefaultRendererIndex").intValue = 0;
            rso.FindProperty("m_SupportsHDR").boolValue = true;
            rso.FindProperty("m_MSAA").intValue = 1;
            rso.FindProperty("m_RenderScale").floatValue = 1f;
            var sd = rso.FindProperty("m_ShadowDistance"); if (sd != null) sd.floatValue = 25f;
            rso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rd); EditorUtility.SetDirty(rp); AssetDatabase.SaveAssets();
            dir.pipeline = rp;
            Debug.Log($"[gt] pipeline copy: features {rd.rendererFeatures.Count} ({feat.shader?.name}), HDR {rp.supportsHDR}, msaa {rp.msaaSampleCount}");

            // room: x -6..6, z -3..7.5, height 3.6, open at the front
            var floorM = M("Floor", new Color(0.05f, 0.05f, 0.055f), 0f, 0.82f);
            var wallM = M("Wall", new Color(0.13f, 0.13f, 0.15f), 0f, 0.25f);
            var panelM = M("Panel", new Color(0.2f, 0.19f, 0.18f), 0.2f, 0.5f);
            var metalM = M("Metal", new Color(0.7f, 0.7f, 0.72f), 1f, 0.75f);
            var darkM = M("Dark", new Color(0.03f, 0.03f, 0.035f), 0.4f, 0.6f);
            var redM = M("Red", new Color(0.75f, 0.08f, 0.06f), 0f, 0.55f);
            var yellowM = M("Yellow", new Color(0.95f, 0.65f, 0.08f), 0f, 0.5f);
            var tealM = M("Teal", new Color(0.05f, 0.55f, 0.6f), 0f, 0.5f);
            var whiteM = M("White", new Color(0.85f, 0.85f, 0.83f), 0f, 0.4f);
            var chromeM = M("Chrome", new Color(0.95f, 0.95f, 0.96f), 1f, 0.95f);
            var magentaN = Emit("NeonMagenta", new Color(1f, 0.15f, 0.6f), 3.2f);
            var cyanN = Emit("NeonCyan", new Color(0.15f, 0.8f, 1f), 3.2f);
            var amberN = Emit("NeonAmber", new Color(1f, 0.6f, 0.2f), 3.2f);
            var orbN = Emit("Orb", new Color(1f, 0.78f, 0.5f), 9f);

            P("Floor", PrimitiveType.Cube, floorM, new Vector3(0, -0.05f, 2.5f), new Vector3(14f, 0.1f, 14f));
            P("Back wall", PrimitiveType.Cube, wallM, new Vector3(0, 1.8f, 7.6f), new Vector3(14f, 3.8f, 0.2f));
            P("Left wall", PrimitiveType.Cube, wallM, new Vector3(-6.1f, 1.8f, 2.5f), new Vector3(0.2f, 3.8f, 10.5f));
            P("Right wall", PrimitiveType.Cube, wallM, new Vector3(6.1f, 1.8f, 2.5f), new Vector3(0.2f, 3.8f, 10.5f));
            P("Ceiling", PrimitiveType.Cube, wallM, new Vector3(0, 3.75f, 2.5f), new Vector3(14f, 0.1f, 10.5f));
            for (int i = 0; i < 7; i++)
            {
                float x = -4.8f + i * 1.6f;
                var n = i % 3 == 0 ? magentaN : (i % 3 == 1 ? cyanN : amberN);
                P("Neon " + i, PrimitiveType.Cube, n, new Vector3(x, 1.9f, 7.47f), new Vector3(0.06f, 2.6f, 0.04f));
            }
            P("Neon low", PrimitiveType.Cube, cyanN, new Vector3(0, 0.06f, 7.45f), new Vector3(12f, 0.04f, 0.04f));
            // for (int i = 0; i < 4; i++) P("Ceiling strip " + i, PrimitiveType.Cube, amberN, new Vector3(-3f + i * 2f, 3.68f, 3.5f), new Vector3(0.06f, 0.03f, 4.5f));

            // standing fan facing the camera
            var fan = Node("Fan", new Vector3(-2.4f, 0f, 3.9f));
            P("Fan base", PrimitiveType.Cylinder, darkM, new Vector3(0, 0.03f, 0), new Vector3(0.6f, 0.03f, 0.6f), fan);
            P("Fan pole", PrimitiveType.Cylinder, metalM, new Vector3(0, 0.8f, 0), new Vector3(0.06f, 0.8f, 0.06f), fan);
            P("Fan motor", PrimitiveType.Cylinder, darkM, new Vector3(0, 1.62f, 0.12f), new Vector3(0.22f, 0.12f, 0.22f), fan, new Vector3(90, 0, 0));
            P("Fan ring", PrimitiveType.Cylinder, darkM, new Vector3(0, 1.62f, -0.02f), new Vector3(1.55f, 0.01f, 1.55f), fan, new Vector3(90, 0, 0)).GetComponent<Renderer>().enabled = false;
            var rotor = Node("Rotor", new Vector3(0, 1.62f, -0.06f), fan);
            P("Hub", PrimitiveType.Sphere, chromeM, Vector3.zero, Vector3.one * 0.16f, rotor);
            for (int i = 0; i < 4; i++)
            {
                var arm = Node("Blade arm " + i, Vector3.zero, rotor); arm.localRotation = Quaternion.Euler(0, 0, i * 90f);
                P("Blade " + i, PrimitiveType.Cube, i % 2 == 0 ? redM : whiteM, new Vector3(0.38f, 0, 0), new Vector3(0.62f, 0.16f, 0.015f), arm, new Vector3(18f, 0, 0));
            }
            dir.fanRotor = rotor; dir.fanTip = Node("Blade tip", new Vector3(0.69f, 0, 0), rotor.GetChild(1));

            // turntable
            var tt = Node("Turntable base", new Vector3(2.5f, 0f, 3.8f));
            P("Plinth", PrimitiveType.Cylinder, panelM, new Vector3(0, 0.12f, 0), new Vector3(2.4f, 0.12f, 2.4f), tt);
            var top = Node("Turntable", new Vector3(0, 0.25f, 0), tt);
            P("Disc", PrimitiveType.Cylinder, darkM, new Vector3(0, 0.02f, 0), new Vector3(2.2f, 0.02f, 2.2f), top);
            P("Pillar yellow", PrimitiveType.Cube, yellowM, new Vector3(0.75f, 0.45f, 0), new Vector3(0.3f, 0.9f, 0.3f), top);
            P("Pillar teal", PrimitiveType.Cube, tealM, new Vector3(-0.75f, 0.35f, 0), new Vector3(0.3f, 0.7f, 0.3f), top);
            P("Pillar red", PrimitiveType.Cube, redM, new Vector3(0, 0.55f, 0.75f), new Vector3(0.3f, 1.1f, 0.3f), top);
            P("Pillar white", PrimitiveType.Cube, whiteM, new Vector3(0, 0.3f, -0.75f), new Vector3(0.3f, 0.6f, 0.3f), top);
            P("Chrome ball", PrimitiveType.Sphere, chromeM, new Vector3(0, 0.3f, 0), Vector3.one * 0.5f, top);
            dir.turntable = top;

            // pendulum light
            var pivot = Node("Pendulum", new Vector3(0.9f, 3.65f, 4.8f));
            P("Cable", PrimitiveType.Cylinder, darkM, new Vector3(0, -1.1f, 0), new Vector3(0.015f, 1.1f, 0.015f), pivot);
            P("Orb", PrimitiveType.Sphere, orbN, new Vector3(0, -2.3f, 0), Vector3.one * 0.26f, pivot);
            var orbLight = new GameObject("Orb light").AddComponent<Light>(); orbLight.type = LightType.Point; orbLight.color = new Color(1f, 0.75f, 0.45f);
            orbLight.intensity = 2.5f; orbLight.range = 5f; orbLight.shadows = LightShadows.None;
            orbLight.transform.SetParent(pivot, false); orbLight.transform.localPosition = new Vector3(0, -2.3f, 0);
            dir.pendulum = pivot;

            // bouncing ball
            var bb = Node("Ball base", new Vector3(1.05f, 0f, 2.5f));
            P("Ball", PrimitiveType.Sphere, M("Ball", new Color(0.95f, 0.35f, 0.1f), 0f, 0.6f), Vector3.zero, Vector3.one * 0.44f, bb);
            dir.ball = bb.GetChild(0);

            // mannequin, waving
            var man = Mannequin.Build(Lit, Dir, (m, part) =>
            {
                m.SetColor("_BaseColor", part == "skin" ? new Color(0.78f, 0.62f, 0.52f) : new Color(0.12f, 0.2f, 0.42f));
                m.SetFloat("_Smoothness", part == "skin" ? 0.4f : 0.3f);
            });
            man.transform.position = new Vector3(-0.35f, 0f, 3.1f);
            man.transform.rotation = Quaternion.Euler(0, 192f, 0);
            foreach (var smr in man.GetComponentsInChildren<SkinnedMeshRenderer>()) { smr.motionVectorGenerationMode = MotionVectorGenerationMode.Object; smr.skinnedMotionVectors = true; }
            dir.character = man.transform;

            Spot("Key", new Color(1f, 0.9f, 0.8f), 30f, 55f, new Vector3(-0.6f, 3.6f, 0.8f), new Vector3(-0.4f, 0.8f, 3.2f));
            Spot("Fan light", new Color(0.85f, 0.9f, 1f), 26f, 45f, new Vector3(-1.2f, 3.6f, 1.6f), new Vector3(-2.4f, 1.2f, 3.9f));
            Spot("Table light", new Color(1f, 0.85f, 0.7f), 26f, 50f, new Vector3(2.0f, 3.6f, 1.8f), new Vector3(2.5f, 0.4f, 3.8f));

            SceneKit.Grade(Dir, 0.7f, 0.2f, 12f, 8f, 0.25f, 0.12f);
            AssetDatabase.SaveAssets();

            var bg = new Color(0.01f, 0.01f, 0.015f);
            dir.camA = SceneKit.Cam("Cam A", new Rect(0, 0, 1, 1), 40f, bg, 0, true);
            dir.camB = SceneKit.Cam("Cam B", new Rect(0, 0, 1, 1), 40f, bg, 1, true);
            dir.camC = SceneKit.Cam("Cam C", new Rect(0, 0, 1, 1), 40f, bg, 2, true);
            foreach (var c in new[] { dir.camA, dir.camB, dir.camC })
            {
                c.farClipPlane = 40f; c.nearClipPlane = 0.1f;
                c.gameObject.AddComponent<KinoMotionCamera>();
            }
            var clear = SceneKit.Cam("Clear", new Rect(0, 0, 1, 1), 60f, Color.black, -10, false); clear.cullingMask = 0;

            dir.title = LabelSpec.Create("", 0.012f, 0.93f, 40);
            dir.l1 = LabelSpec.Create("", 0.012f, 0.058f, 34); dir.l2 = LabelSpec.Create("", 0.012f, 0.018f, 30);
            dir.m1 = LabelSpec.Create("", 0.512f, 0.058f, 34); dir.m2 = LabelSpec.Create("", 0.512f, 0.018f, 30);
            dir.r1 = LabelSpec.Create("", 0.68f, 0.058f, 34); dir.r2 = LabelSpec.Create("", 0.68f, 0.018f, 30);
        }
    }
}
