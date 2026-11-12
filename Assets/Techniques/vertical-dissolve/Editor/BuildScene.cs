// 73 vertical dissolve: the character on a dark stage, then two hovering test pods for the coordinate and parameter comparisons.
// The stage floor sits at y = -0.70 so that the 1.39 m character spans about -0.70..0.69 and the source's fill remap
// (0..1 -> -1..1 in metres) covers her without changing the formula.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace GameTech.Posts
{
    public static class DissolveScene
    {
        const string Dir = "Assets/Techniques/vertical-dissolve/";
        const string Lit = "Universal Render Pipeline/Lit";
        const string VD = "GameTech/VerticalDissolve";

        static Material Glow(string name, Color c)
        {
            var m = new Material(Shader.Find("GameTech/Glow")); m.SetColor("_BaseColor", c);
            AssetDatabase.CreateAsset(m, Dir + name + ".mat");
            return m;
        }

        public static void Build()
        {
            const float floorY = -0.70f;
            SceneKit.Ambient(new Color(0.06f, 0.07f, 0.10f), new Color(0.045f, 0.045f, 0.06f), new Color(0.02f, 0.02f, 0.025f));
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.012f, 0.014f, 0.022f); RenderSettings.fogDensity = 0.05f;
            SceneKit.Shadows(12f);
            SceneKit.Sun("Key light", new Vector3(40, 222, 0), new Color(1f, 0.84f, 0.66f), 2.7f, true);
            SceneKit.Sun("Rim light", new Vector3(16, 28, 0), new Color(0.50f, 0.68f, 1f), 1.5f, false);
            SceneKit.Sun("Fill light", new Vector3(20, 150, 0), new Color(0.55f, 0.6f, 0.8f), 0.35f, false);

            // ---- stage
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor"; floor.transform.position = new Vector3(0, floorY - 0.16f, 0); floor.transform.localScale = Vector3.one * 6;
            floor.GetComponent<Renderer>().sharedMaterial = SceneKit.Mat(Dir, "Floor", Lit, new Color(0.05f, 0.055f, 0.065f), 0f, 0.6f);
            var ped = new MeshKit().Lathe(new[] { new Vector2(0, 0), new Vector2(0, 1.38f), new Vector2(0.07f, 1.38f), new Vector2(0.09f, 1.26f), new Vector2(0.16f, 1.26f), new Vector2(0.16f, 0) }, 72).Build("Pedestal");
            SceneKit.Part("Pedestal", ped, SceneKit.Mat(Dir, "Pedestal", Lit, new Color(0.15f, 0.16f, 0.19f), 0.1f, 0.32f), new Vector3(0, floorY - 0.16f, 0), Quaternion.identity);
            var ring = new MeshKit().Lathe(new[] { new Vector2(0.092f, 1.262f), new Vector2(0.092f, 1.30f), new Vector2(0.10f, 1.30f), new Vector2(0.10f, 1.262f) }, 72).Build("Ring");
            SceneKit.Part("Pedestal ring", ring, Glow("RingGlow", new Color(0.10f, 0.75f, 1.3f)), new Vector3(0, floorY - 0.16f, 0), Quaternion.identity)
                .GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // ---- backdrop: a dark wall and vertical light bars, blurred by depth of field
            SceneKit.Part("Back wall", new MeshKit().Box(new Vector3(0, 2f, -7.5f), new Vector3(30, 9, 0.2f)).Build("BackWall"),
                SceneKit.Mat(Dir, "BackWall", Lit, new Color(0.03f, 0.032f, 0.04f), 0f, 0.3f), Vector3.zero, Quaternion.identity);
            var warm = new MeshKit(); var cool = new MeshKit();
            for (int i = 0; i < 13; i++)
            {
                float x = (i - 6) * 1.25f + 0.2f * Mathf.Sin(i * 2.3f), h = 2.2f + 2.4f * Mathf.Abs(Mathf.Sin(i * 1.7f + 0.5f));
                (i % 2 == 0 ? warm : cool).Box(new Vector3(x, floorY - 0.16f + h / 2, -6.6f + 0.5f * Mathf.Sin(i * 3.1f)), new Vector3(0.09f, h, 0.09f));
            }
            SceneKit.Part("Bars warm", warm.Build("BarsWarm"), Glow("BarWarm", new Color(1.6f, 0.62f, 0.2f)), Vector3.zero, Quaternion.identity).GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            SceneKit.Part("Bars cool", cool.Build("BarsCool"), Glow("BarCool", new Color(0.12f, 0.7f, 1.2f)), Vector3.zero, Quaternion.identity).GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // ---- character with the dissolve shader on every material
            var chrMats = new List<Material>();
            var chr = CharacterKit.Spawn(VD, Dir, (m, n) => { m.SetFloat("_Smoothness", 0.2f); m.SetFloat("_Worldcoordinates", 1f); chrMats.Add(m); });
            chr.transform.position = new Vector3(0, floorY, 0);

            // ---- two test pods (closed solids of revolution, 1 m tall, centred on their own origin)
            var podMesh = new MeshKit().Lathe(new[] { new Vector2(-0.5f, 0), new Vector2(-0.5f, 0.2f), new Vector2(-0.44f, 0.29f), new Vector2(-0.1f, 0.33f),
                new Vector2(0.24f, 0.30f), new Vector2(0.42f, 0.2f), new Vector2(0.5f, 0.1f), new Vector2(0.5f, 0) }, 64).Build("Pod", 50f);
            Material PodMat(string name)
            {
                var m = new Material(Shader.Find(VD));
                m.SetColor("_BaseColor", new Color(0.80f, 0.74f, 0.64f)); m.SetFloat("_Metallic", 0.1f); m.SetFloat("_Smoothness", 0.55f);
                AssetDatabase.CreateAsset(m, Dir + name + ".mat");
                return m;
            }
            var dr = new GameObject("Director").AddComponent<DissolveDirector>();
            dr.podMatL = PodMat("PodLeft"); dr.podMatR = PodMat("PodRight");
            dr.podL = SceneKit.Part("Pod left", podMesh, dr.podMatL, new Vector3(0.85f, 0.05f, 0), Quaternion.identity).transform;
            dr.podR = SceneKit.Part("Pod right", podMesh, dr.podMatR, new Vector3(-0.85f, 0.05f, 0), Quaternion.identity).transform;
            dr.chr = chr.transform; dr.chrMats = chrMats.ToArray();

            var bg = new Color(0.012f, 0.014f, 0.022f);
            dr.camL = SceneKit.Cam("Camera OFF", new Rect(0, 0, 0.5f, 1), 30, bg, 0);
            dr.camR = SceneKit.Cam("Camera ON", new Rect(0.5f, 0, 0.5f, 1), 30, bg, 1);
            dr.camFull = SceneKit.Cam("Camera full", new Rect(0, 0, 1, 1), 30, bg, 2); dr.camFull.tag = "MainCamera";

            var profile = SceneKit.Grade(Dir, 0.6f, 0.35f, 12f, 8f, 0.32f, 0.2f);
            var dof = SceneKit.Add<DepthOfField>(profile);
            dof.mode.Override(DepthOfFieldMode.Gaussian); dof.gaussianStart.Override(5.5f); dof.gaussianEnd.Override(9.5f); dof.gaussianMaxRadius.Override(1.3f);
            EditorUtility.SetDirty(profile); AssetDatabase.SaveAssets();

            LabelSpec.Create("OFF: clip() at one height", 0.03f, 0.05f).During(2.0f, 9f);
            LabelSpec.Create("ON: wavy edge + border + inside tint", 0.53f, 0.05f).During(2.0f, 9f);
            LabelSpec.Create("Fill 0.87 -> 0.14: everything above the line is clipped", 0.03f, 0.05f).During(9.3f, 11f);
            LabelSpec.Create("Invert mask on: the part above the line is kept", 0.03f, 0.05f).During(11f, 13f);
            LabelSpec.Create("Vertex coords: the cut rides on the mesh", 0.03f, 0.05f).During(13.2f, 17f);
            LabelSpec.Create("World coords: mesh moves through the cut", 0.53f, 0.05f).During(13.2f, 17f);
            string[] left = { "Border width 0.35", "Noise scale 4", "Wave amplitude 0.04", "Fill color: blue" };
            string[] right = { "Border width 1.0", "Noise scale 10", "Wave amplitude 0.12", "Fill color: orange" };
            for (int i = 0; i < 4; i++)
            {
                float a = 17f + i * 1.75f, b = i == 3 ? 24.1f : a + 1.75f;
                LabelSpec.Create(left[i], 0.12f, 0.05f).During(a, b);
                LabelSpec.Create(right[i], 0.62f, 0.05f).During(a, b);
            }
            Debug.Log($"[gt] dissolve scene: {chrMats.Count} character materials");
        }
    }
}
