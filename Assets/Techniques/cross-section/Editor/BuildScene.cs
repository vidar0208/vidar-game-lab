// 70 cross-section shader: procedural two-stage gearbox (all closed meshes), cut by one or three planes.
using UnityEditor;
using UnityEngine;

namespace GameTech.Posts
{
    public static class CrossSection
    {
        const string Dir = "Assets/Techniques/cross-section/";
        const string LitShader = "GameTech/CrossSectionLit";
        static int tris, parts;
        static Transform root;
        static readonly Quaternion AlongX = Quaternion.Euler(0, 0, -90);   // lathe/annulus axis (local Y) -> world X

        static GameObject Part(string name, MeshKit k, Material m, Vector3 pos, Quaternion rot)
        {
            tris += k.TriangleCount; parts++;
            return SceneKit.Part(name, k.Build(name), m, pos, rot, root);
        }

        static void Faces(MeshKit k, Vector3 size, bool inward, bool skipX)
        {
            Vector3 h = size * 0.5f; float s = inward ? -1 : 1;
            for (int ax = skipX ? 1 : 0; ax < 3; ax++)
                for (int sg = -1; sg <= 1; sg += 2)
                {
                    Vector3 n = Vector3.zero; n[ax] = sg;
                    Vector3 u = Vector3.zero; u[(ax + 1) % 3] = h[(ax + 1) % 3];
                    Vector3 w = Vector3.zero; w[(ax + 2) % 3] = h[(ax + 2) % 3];
                    Vector3 o = n * h[ax];
                    k.Quad(o - u - w, o + u - w, o + u + w, o - u + w, n * s);
                }
        }

        static Vector2[] Ring(float x0, float x1, float rIn, float rOut) =>
            new[] { new Vector2(x0, rIn), new Vector2(x1, rIn), new Vector2(x1, rOut), new Vector2(x0, rOut) };

        public static void Build()
        {
            tris = 0; parts = 0;
            root = new GameObject("Gearbox").transform;
            SceneKit.Ambient(new Color(0.30f, 0.34f, 0.44f), new Color(0.22f, 0.21f, 0.22f), new Color(0.10f, 0.09f, 0.08f));
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.075f, 0.085f, 0.105f); RenderSettings.fogDensity = 0.03f;
            SceneKit.Shadows(14f);
            SceneKit.Sun("Key light", new Vector3(44, 158, 0), new Color(1f, 0.93f, 0.82f), 2.7f, true);
            SceneKit.Sun("Fill from +X", new Vector3(28, -112, 0), new Color(1f, 0.86f, 0.72f), 1.2f, false);
            SceneKit.Sun("Rim light", new Vector3(22, -28, 0), new Color(0.50f, 0.66f, 1f), 1.2f, false);

            var enamel = SceneKit.Mat(Dir, "Enamel", LitShader, new Color(0.07f, 0.40f, 0.50f), 0.1f, 0.5f);
            var lidMat = SceneKit.Mat(Dir, "Lid", LitShader, new Color(0.20f, 0.22f, 0.25f), 0.6f, 0.5f);
            var steel = SceneKit.Mat(Dir, "Steel", LitShader, new Color(0.72f, 0.74f, 0.78f), 1f, 0.72f);
            var brass = SceneKit.Mat(Dir, "Brass", LitShader, new Color(0.95f, 0.66f, 0.26f), 1f, 0.70f);
            var bronze = SceneKit.Mat(Dir, "Bronze", LitShader, new Color(0.55f, 0.30f, 0.16f), 0.9f, 0.55f);
            var dark = SceneKit.Mat(Dir, "DarkSteel", LitShader, new Color(0.10f, 0.11f, 0.13f), 0.9f, 0.6f);

            // ---- housing: one closed mesh = outer box + inner box (facing the cavity) + two shaft bores
            float cy = 1.05f, wall = 0.07f, dy = 0.384f, rBore = 0.062f;
            Vector3 hs = new Vector3(1.5f, 1.7f, 0.8f), h = hs * 0.5f, hi = h - Vector3.one * wall;
            var k = new MeshKit();
            Faces(k, hs, false, true); Faces(k, hi * 2, true, true);
            k.HoleFaceX(-h.x, 0, 0, h.y, h.z, dy, 0, rBore, Vector3.left); k.HoleFaceX(-hi.x, 0, 0, hi.y, hi.z, dy, 0, rBore, Vector3.right); k.BoreX(-h.x, -hi.x, dy, 0, rBore);
            k.HoleFaceX(h.x, 0, 0, h.y, h.z, -dy, 0, rBore, Vector3.right); k.HoleFaceX(hi.x, 0, 0, hi.y, hi.z, -dy, 0, rBore, Vector3.left); k.BoreX(hi.x, h.x, -dy, 0, rBore);
            Part("Housing", k, enamel, new Vector3(0, cy, 0), Quaternion.identity);

            float gap = 0.004f;   // parts never overlap: touching or intersecting solids break the per-pixel stencil test
            foreach (float sx in new[] { -0.5f, 0.5f })
                Part("Foot", new MeshKit().Box(Vector3.zero, new Vector3(0.26f, 0.2f - gap, 1.04f)), enamel, new Vector3(sx, (0.2f - gap) * 0.5f, 0), Quaternion.identity);
            float lidY = cy + h.y + gap + 0.025f;
            Part("Lid", new MeshKit().Box(Vector3.zero, new Vector3(1.58f, 0.05f, 0.88f)), lidMat, new Vector3(0, lidY, 0), Quaternion.identity);
            var bolt = new[] { new Vector2(0, 0), new Vector2(0, 0.034f), new Vector2(0.024f, 0.034f), new Vector2(0.024f, 0) };
            for (int i = 0; i < 8; i++)
            {
                float bx = (i % 4 - 1.5f) * 0.46f, bz = i < 4 ? -0.39f : 0.39f;
                Part("Bolt", new MeshKit().Lathe(bolt, 6), dark, new Vector3(bx, lidY + 0.025f + gap, bz), Quaternion.identity);
            }
            var breather = new[] { new Vector2(0, 0), new Vector2(0, 0.05f), new Vector2(0.05f, 0.05f), new Vector2(0.05f, 0.085f), new Vector2(0.08f, 0.085f), new Vector2(0.08f, 0) };
            Part("Breather", new MeshKit().Lathe(breather, 32), brass, new Vector3(0.2f, lidY + 0.025f + gap, 0), Quaternion.identity);

            // ---- shafts (solids of revolution, axis along X)
            float yIn = cy + dy, yMid = cy, yOut = cy - dy, rs = 0.05f;
            var inShaft = new[] { new Vector2(-1.00f, 0), new Vector2(-1.00f, 0.04f), new Vector2(-0.98f, 0.04f), new Vector2(-0.98f, 0.19f), new Vector2(-0.955f, 0.19f),
                new Vector2(-0.93f, 0.15f), new Vector2(-0.905f, 0.19f), new Vector2(-0.88f, 0.19f), new Vector2(-0.88f, rs), new Vector2(0.66f, rs), new Vector2(0.66f, 0) };
            var midShaft = new[] { new Vector2(-0.66f, 0), new Vector2(-0.66f, rs), new Vector2(-0.07f, rs), new Vector2(-0.07f, 0.08f), new Vector2(0.07f, 0.08f), new Vector2(0.07f, rs), new Vector2(0.66f, rs), new Vector2(0.66f, 0) };
            var outShaft = new[] { new Vector2(-0.66f, 0), new Vector2(-0.66f, rs), new Vector2(0.86f, rs), new Vector2(0.86f, 0.16f), new Vector2(0.90f, 0.16f), new Vector2(0.90f, 0.075f), new Vector2(1.0f, 0.075f), new Vector2(1.0f, 0) };
            Part("Input shaft", new MeshKit().Lathe(inShaft), steel, new Vector3(0, yIn, 0), AlongX);
            Part("Counter shaft", new MeshKit().Lathe(midShaft), steel, new Vector3(0, yMid, 0), AlongX);
            Part("Output shaft", new MeshKit().Lathe(outShaft), steel, new Vector3(0, yOut, 0), AlongX);

            // ---- gears: module 16 mm, 16 and 32 teeth, centre distance 384 mm, two reduction stages (4 : 1 overall)
            float mod = 0.016f, omega = 48f;
            GameObject Gear(string name, int teeth, float x, float y, float width, float phase, float spin, Material m)
            {
                float rp = teeth * mod * 0.5f;
                var g = new MeshKit().Annulus(MeshKit.GearProfile(teeth, rp - 1.25f * mod, rp + mod, phase), rs + gap, -width * 0.5f, width * 0.5f, teeth * 16);
                var go = Part(name, g, m, new Vector3(x, y, 0), AlongX);
                go.AddComponent<GearSpin>().degPerSec = spin;
                return go;
            }
            float lowPhase = Mathf.PI + Mathf.PI / 32f;       // lower gear: a tooth gap faces the upper gear's tooth
            Gear("Pinion 1 (16T)", 16, -0.32f, yIn, 0.16f, 0f, omega, steel);
            Gear("Wheel 1 (32T)", 32, -0.32f, yMid, 0.14f, lowPhase, -omega * 0.5f, brass);
            Gear("Pinion 2 (16T)", 16, 0.32f, yMid, 0.16f, 0f, -omega * 0.5f, steel);
            Gear("Wheel 2 (32T)", 32, 0.32f, yOut, 0.14f, lowPhase, omega * 0.25f, brass);

            // ---- bearings and spacer sleeves
            float xi = hi.x;   // inner wall face
            foreach (float y in new[] { yIn, yMid, yOut })
            {
                Part("Bearing L", new MeshKit().Lathe(Ring(-xi + gap, -0.60f, rs + gap, 0.105f)), bronze, new Vector3(0, y, 0), AlongX);
                Part("Bearing R", new MeshKit().Lathe(Ring(0.60f, xi - gap, rs + gap, 0.105f)), bronze, new Vector3(0, y, 0), AlongX);
            }
            foreach (float y in new[] { yIn, yMid })
                Part("Spacer L", new MeshKit().Lathe(Ring(-0.60f + gap, -0.40f - gap, rs + gap, 0.078f)), dark, new Vector3(0, y, 0), AlongX);
            foreach (float y in new[] { yMid, yOut })
                Part("Spacer R", new MeshKit().Lathe(Ring(0.40f + gap, 0.60f - gap, rs + gap, 0.078f)), dark, new Vector3(0, y, 0), AlongX);

            // ---- workbench
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = Vector3.one * 30;
            ground.GetComponent<Renderer>().sharedMaterial = SceneKit.Mat(Dir, "Bench", "Universal Render Pipeline/Lit", new Color(0.17f, 0.175f, 0.19f), 0f, 0.22f);

            // ---- cap quads (layer 8, hidden from the "clip only" camera), ghost quad and stencil overlay for the debug steps
            Transform Cap(string name, int index, bool isGhost)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = name; Object.DestroyImmediate(q.GetComponent<Collider>());
                q.transform.localScale = Vector3.one * (isGhost ? 2.5f : 4.5f);
                var m = new Material(Shader.Find("GameTech/CrossSectionCap"));
                m.SetFloat("_Index", index);
                if (isGhost) { m.SetFloat("_StencilComp", 8f); m.SetFloat("_Alpha", 0.55f); m.SetFloat("_ZWrite", 0f); }
                AssetDatabase.CreateAsset(m, Dir + name + ".mat");
                var r = q.GetComponent<Renderer>(); r.sharedMaterial = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                q.layer = isGhost ? 0 : 8;
                return q.transform;
            }
            var d = new GameObject("Director").AddComponent<CrossSectionDirector>();
            d.cap0 = Cap("Cap0", 0, false); d.cap1 = Cap("Cap1", 1, false); d.cap2 = Cap("Cap2", 2, false); d.ghost = Cap("Ghost", 0, true);

            var bg = new Color(0.075f, 0.085f, 0.105f);
            d.camL = SceneKit.Cam("Camera OFF", new Rect(0, 0, 0.5f, 1), 38, bg, 0); d.camL.cullingMask = ~(1 << 8);
            d.camR = SceneKit.Cam("Camera ON", new Rect(0.5f, 0, 0.5f, 1), 38, bg, 1);
            d.camFull = SceneKit.Cam("Camera full", new Rect(0, 0, 1, 1), 38, bg, 2); d.camFull.tag = "MainCamera";

            var ovl = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ovl.name = "Stencil overlay"; Object.DestroyImmediate(ovl.GetComponent<Collider>());
            ovl.transform.SetParent(d.camFull.transform, false); ovl.transform.localPosition = new Vector3(0, 0, 1);
            var om = new Material(Shader.Find("GameTech/StencilOverlay")); AssetDatabase.CreateAsset(om, Dir + "Overlay.mat");
            var or = ovl.GetComponent<Renderer>(); or.sharedMaterial = om; or.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            d.overlay = ovl;

            SceneKit.Grade(Dir, 0.35f, 0.55f, 12f, 8f, 0.30f, 0.18f);

            LabelSpec.Create("OFF: clip() only", 0.03f, 0.05f).During(2.2f, 9f);
            LabelSpec.Create("ON: clip + stencil cap", 0.53f, 0.05f).During(2.2f, 9f);
            LabelSpec.Create($"1 plane, mesh untouched ({tris:N0} triangles)", 0.03f, 0.05f).During(9.4f, 13.4f);
            LabelSpec.Create("3 planes cut out one corner", 0.03f, 0.05f).During(13.8f, 17f);
            LabelSpec.Create("1/4  clip() only: a hollow shell", 0.03f, 0.05f).During(17f, 18.75f);
            LabelSpec.Create("2/4  back faces: flat colour, stencil = 255 (pink)", 0.03f, 0.05f).During(18.75f, 20.5f);
            LabelSpec.Create("3/4  the cap is one quad on the plane", 0.03f, 0.05f).During(20.5f, 22.25f);
            LabelSpec.Create("4/4  drawn only where stencil = 255", 0.03f, 0.05f).During(22.25f, 24.1f);
            Debug.Log($"[gt] cross-section scene: {parts} parts, {tris} triangles");
        }
    }
}
