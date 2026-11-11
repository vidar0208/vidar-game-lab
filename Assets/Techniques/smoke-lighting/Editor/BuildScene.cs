// 76 smoke lighting: a dark hall built from code, a vent with a smoke column, one lamp that circles the column.
// Cameras: split pair (unlit | 6-way), three thirds (unlit | normal + ramp | 6-way), 6-way + debug panel.
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace GameTech.Posts
{
    public static class SmokeScene
    {
        const string Dir = "Assets/Techniques/smoke-lighting/";
        const string Lit = "Universal Render Pipeline/Lit";

        static GameObject Box(string name, Vector3 c, Vector3 size, Material m, float yaw = 0)
        {
            return SceneKit.Part(name, new MeshKit().Box(Vector3.zero, size).Build(name), m, c, Quaternion.Euler(0, yaw, 0));
        }

        public static void Build()
        {
            var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var so = new SerializedObject(rp);
            so.FindProperty("m_RequireDepthTexture").boolValue = true;
            so.ApplyModifiedProperties(); EditorUtility.SetDirty(rp);

            SceneKit.Ambient(new Color(0.11f, 0.13f, 0.19f), new Color(0.08f, 0.09f, 0.125f), new Color(0.045f, 0.045f, 0.055f));
            SceneKit.Shadows(30f);
            SceneKit.Sun("Moonlight", new Vector3(48, -35, 0), new Color(0.55f, 0.68f, 1f), 0.5f, true);
            RenderSettings.fog = false;

            // ---- hall
            var floorM = SceneKit.Mat(Dir, "Floor", Lit, new Color(0.16f, 0.16f, 0.17f), 0f, 0.6f);
            var wallM = SceneKit.Mat(Dir, "Wall", Lit, new Color(0.46f, 0.42f, 0.38f), 0f, 0.2f);
            var steelM = SceneKit.Mat(Dir, "Steel", Lit, new Color(0.10f, 0.11f, 0.12f), 0.7f, 0.45f);
            var woodM = SceneKit.Mat(Dir, "Wood", Lit, new Color(0.34f, 0.23f, 0.13f), 0f, 0.3f);
            var tealM = SceneKit.Mat(Dir, "Barrel", Lit, new Color(0.10f, 0.30f, 0.33f), 0.4f, 0.5f);
            var redM = SceneKit.Mat(Dir, "BarrelRed", Lit, new Color(0.45f, 0.10f, 0.07f), 0.4f, 0.5f);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); Object.DestroyImmediate(ground.GetComponent<Collider>());
            ground.name = "Floor"; ground.transform.localScale = Vector3.one * 4; ground.GetComponent<Renderer>().sharedMaterial = floorM;
            Box("Back wall", new Vector3(0, 3.5f, -6.6f), new Vector3(26, 7, 0.4f), wallM);
            Box("Left wall", new Vector3(-11, 3.5f, 0), new Vector3(0.4f, 7, 26), wallM);
            Box("Right wall", new Vector3(11, 3.5f, 0), new Vector3(0.4f, 7, 26), wallM);
            foreach (var p in new[] { new Vector3(-4.4f, 0, -4.6f), new Vector3(4.6f, 0, -4.6f), new Vector3(-7.6f, 0, -1.2f), new Vector3(7.8f, 0, -1.2f) })
            {
                Box("Pillar", p + new Vector3(0, 3.5f, 0), new Vector3(0.6f, 7, 0.6f), wallM);
                Box("Pillar foot", p + new Vector3(0, 0.15f, 0), new Vector3(0.9f, 0.3f, 0.9f), steelM);
            }
            Box("Beam a", new Vector3(0, 6.3f, -4.6f), new Vector3(22, 0.5f, 0.4f), steelM);
            Box("Beam b", new Vector3(0, 6.3f, -1.2f), new Vector3(22, 0.5f, 0.4f), steelM);
            var pipe = new MeshKit().Lathe(new[] { new Vector2(-10f, 0), new Vector2(-10f, 0.13f), new Vector2(10f, 0.13f), new Vector2(10f, 0) }, 16).Build("Pipe");
            SceneKit.Part("Pipe a", pipe, wallM, new Vector3(0, 2.6f, -6.25f), Quaternion.Euler(0, 0, 90));
            SceneKit.Part("Pipe b", pipe, wallM, new Vector3(0, 3.0f, -6.25f), Quaternion.Euler(0, 0, 90));

            // ---- props that catch the lamp
            Box("Crate 1", new Vector3(-2.7f, 0.45f, -1.6f), new Vector3(0.9f, 0.9f, 0.9f), woodM, 18);
            Box("Crate 2", new Vector3(-3.5f, 0.35f, -0.9f), new Vector3(0.7f, 0.7f, 0.7f), woodM, -25);
            Box("Crate 3", new Vector3(-2.8f, 1.2f, -1.6f), new Vector3(0.6f, 0.6f, 0.6f), woodM, 40);
            Box("Crate 4", new Vector3(3.3f, 0.4f, -2.4f), new Vector3(1.4f, 0.8f, 0.9f), woodM, -12);
            var barrel = new MeshKit().Lathe(new[] { new Vector2(0, 0), new Vector2(0, 0.30f), new Vector2(0.06f, 0.33f), new Vector2(0.86f, 0.33f), new Vector2(0.92f, 0.30f), new Vector2(0.92f, 0) }, 24).Build("Barrel");
            SceneKit.Part("Barrel 1", barrel, tealM, new Vector3(2.6f, 0, -1.3f), Quaternion.identity);
            SceneKit.Part("Barrel 2", barrel, redM, new Vector3(3.3f, 0, -1.0f), Quaternion.identity);
            SceneKit.Part("Barrel 3", barrel, tealM, new Vector3(-1.9f, 0, -2.6f), Quaternion.identity);
            SceneKit.Part("Barrel 4", barrel, tealM, new Vector3(3.0f, 0.33f, -3.4f), Quaternion.Euler(0, 30, 90));
            var vent = new MeshKit().Lathe(new[] { new Vector2(0, 0), new Vector2(0, 0.52f), new Vector2(0.34f, 0.44f), new Vector2(0.34f, 0.34f), new Vector2(0.08f, 0.34f), new Vector2(0.08f, 0) }, 32).Build("Vent");
            SceneKit.Part("Vent", vent, steelM, Vector3.zero, Quaternion.identity);

            // ---- the lamp: one point light with a small glowing ball
            var lamp = new GameObject("Lamp").AddComponent<Light>();
            lamp.type = LightType.Point; lamp.color = new Color(1f, 0.82f, 0.62f); lamp.intensity = 5f; lamp.range = 16f; lamp.shadows = LightShadows.None;
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere); Object.DestroyImmediate(ball.GetComponent<Collider>());
            ball.name = "Lamp ball"; ball.transform.SetParent(lamp.transform, false); ball.transform.localScale = Vector3.one * 0.16f;
            var glow = new Material(Shader.Find("GameTech/Glow")); glow.SetColor("_BaseColor", new Color(6f, 4.6f, 3.0f)); AssetDatabase.CreateAsset(glow, Dir + "LampGlow.mat");
            var br = ball.GetComponent<Renderer>(); br.sharedMaterial = glow; br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // ---- smoke billboards (mesh is rebuilt every frame by the director)
            var smokeMat = new Material(Shader.Find("GameTech/SmokeLit")); AssetDatabase.CreateAsset(smokeMat, Dir + "Smoke.mat");
            var smoke = new GameObject("Smoke");
            var mf = smoke.AddComponent<MeshFilter>();
            var mr = smoke.AddComponent<MeshRenderer>(); mr.sharedMaterial = smokeMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;

            // ---- debug panel quad (layer 9, panel camera only)
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Panel quad"; Object.DestroyImmediate(quad.GetComponent<Collider>()); quad.layer = 9;
            var qm = Object.Instantiate(quad.GetComponent<MeshFilter>().sharedMesh); qm.name = "PanelQuad"; qm.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
            quad.GetComponent<MeshFilter>().sharedMesh = qm;
            var pm = new Material(Shader.Find("GameTech/SmokePanel")); AssetDatabase.CreateAsset(pm, Dir + "Panel.mat");
            var qr = quad.GetComponent<Renderer>(); qr.sharedMaterial = pm; qr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var dr = new GameObject("Director").AddComponent<SmokeDirector>();
            dr.bakeShader = Shader.Find("Hidden/GameTech/SmokeBake"); dr.smokeMesh = mf; dr.lamp = lamp;
            var bg = new Color(0.01f, 0.012f, 0.016f);
            dr.camL = SceneKit.Cam("Camera unlit", new Rect(0, 0, 0.5f, 1), 34, bg, 0);
            dr.camR = SceneKit.Cam("Camera six-way", new Rect(0.5f, 0, 0.5f, 1), 34, bg, 1);
            dr.camA = SceneKit.Cam("Camera third unlit", new Rect(0, 0, 1f / 3f, 1), 34, bg, 2);
            dr.camB = SceneKit.Cam("Camera third normal", new Rect(1f / 3f, 0, 1f / 3f, 1), 34, bg, 3);
            dr.camC = SceneKit.Cam("Camera third six-way", new Rect(2f / 3f, 0, 1f / 3f, 1), 34, bg, 4);
            dr.camDbg = SceneKit.Cam("Camera debug six-way", new Rect(0, 0, 0.5f, 1), 34, bg, 5);
            dr.camPanel = SceneKit.Cam("Camera panel", new Rect(0.5f, 0, 0.5f, 1), 34, Color.black, 6, false);
            dr.camPanel.cullingMask = 1 << 9;
            dr.camR.tag = "MainCamera";
            foreach (var cam in new[] { dr.camL, dr.camR, dr.camA, dr.camB, dr.camC, dr.camDbg })
            {
                cam.cullingMask = ~(1 << 9);
                cam.GetComponent<UniversalAdditionalCameraData>().requiresDepthOption = CameraOverrideOption.On;
            }

            SceneKit.Grade(Dir, 0.35f, 0.5f, 12f, 5f, 0.3f, 0f);

            LabelSpec.Create("OFF: unlit flipbook, lamp ignored", 0.03f, 0.05f).During(1.5f, 9f);
            LabelSpec.Create("ON: 6-way lightmaps, same lamp", 0.53f, 0.05f).During(1.5f, 9f);
            LabelSpec.Create("Unlit flipbook", 0.02f, 0.05f, 34).During(9.2f, 16.5f);
            LabelSpec.Create("Normal map + ramp", 0.353f, 0.05f, 34).During(9.2f, 16.5f);
            LabelSpec.Create("6-way lightmaps", 0.687f, 0.05f, 34).During(9.2f, 16.5f);
            LabelSpec.Create("6-way lightmaps", 0.03f, 0.05f).During(16.7f, 24.1f);
            LabelSpec.Create("Six baked maps x weight (at the column centre)", 0.53f, 0.925f, 28).During(16.7f, 24.1f);
            float[] xs = { 0.53f, 0.685f, 0.84f };
            dr.tileLabels = new LabelSpec[6];
            for (int k = 0; k < 6; k++)
                dr.tileLabels[k] = LabelSpec.Create("", xs[k % 3], k < 3 ? 0.617f : 0.332f, 28).During(16.7f, 24.1f);
            LabelSpec.Create("= sum x lamp colour", 0.70f, 0.26f, 26).During(16.7f, 24.1f);
            dr.dirLabel = LabelSpec.Create("", 0.70f, 0.20f, 26).During(16.7f, 24.1f);
            dr.distLabel = LabelSpec.Create("", 0.70f, 0.14f, 26).During(16.7f, 24.1f);
            dr.intLabel = LabelSpec.Create("", 0.70f, 0.08f, 26).During(16.7f, 24.1f);
            Debug.Log("[gt] smoke scene built");
        }
    }
}
