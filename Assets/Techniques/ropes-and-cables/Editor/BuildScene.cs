// 77 fake ropes: a pier at dusk, ropes between posts, one rope to a trolley that slides on a rail, one to a moored
// boat, one long soft rope between two masts. Every rope exists in four sets on four layers:
//   8 = left-half ropes (mesh), 9 = right-half ropes (mesh), 10 = straight cylinders, 11 = Line Renderer ropes.
// Layer 12 = debug markers. The rope components are the source's scripts, unchanged.
using System.Collections.Generic;
using GogoGaga.OptimizedRopesAndCables;
using UnityEditor;
using UnityEngine;

namespace GameTech.Posts
{
    public static class RopeScene
    {
        const string Dir = "Assets/Techniques/ropes-and-cables/";
        const string Lit = "Universal Render Pipeline/Lit";
        static Material ropeMat, ropeLineMat, darkM, lampM;

        static GameObject Box(string name, Vector3 c, Vector3 size, Material m, Transform parent = null)
        {
            return SceneKit.Part(name, new MeshKit().Box(Vector3.zero, size).Build(name), m, c, Quaternion.identity, parent);
        }
        static GameObject Pole(string name, Vector3 foot, float h, float r, Material m, Transform parent = null)
        {
            var mesh = new MeshKit().Lathe(new[] { new Vector2(0, 0), new Vector2(0, r), new Vector2(h, r * 0.9f), new Vector2(h, 0) }, 16).Build(name);
            return SceneKit.Part(name, mesh, m, foot, Quaternion.identity, parent);
        }
        static Transform Anchor(string name, Vector3 p, Transform parent = null)
        {
            var go = new GameObject(name); if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = p; return go.transform;
        }
        static Material Glow(string name, Color c)
        {
            var m = new Material(Shader.Find("GameTech/Glow")); m.SetColor("_BaseColor", c); AssetDatabase.CreateAsset(m, Dir + name + ".mat"); return m;
        }
        static void SetLayer(GameObject go, int layer) { foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer; }

        static Transform Lantern(int layer)
        {
            var root = new GameObject("Lantern");
            Box("Hanger", new Vector3(0, -0.07f, 0), new Vector3(0.02f, 0.14f, 0.02f), darkM, root.transform);
            Box("Cap", new Vector3(0, -0.15f, 0), new Vector3(0.22f, 0.04f, 0.22f), darkM, root.transform);
            Box("Lamp", new Vector3(0, -0.29f, 0), new Vector3(0.16f, 0.24f, 0.16f), lampM, root.transform)
                .GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            SetLayer(root, layer);
            return root.transform;
        }

        static Rope MakeRope(string name, Transform a, Transform b, float length, int layer, bool mesh, Transform mid, out RopeWindEffect wind)
        {
            var go = new GameObject(name); go.layer = layer;
            var rope = go.AddComponent<Rope>();                       // brings its Line Renderer
            var so = new SerializedObject(rope);
            so.FindProperty("startPoint").objectReferenceValue = a;
            so.FindProperty("endPoint").objectReferenceValue = b;
            if (mid != null) so.FindProperty("midPoint").objectReferenceValue = mid;
            so.ApplyModifiedPropertiesWithoutUndo();
            rope.ropeLength = length; rope.stiffness = 150f; rope.damping = 4f; rope.midPointWeight = 1f; rope.linePoints = 10; rope.ropeWidth = 0.1f;
            var lr = go.GetComponent<LineRenderer>();
            lr.sharedMaterial = ropeLineMat; lr.startWidth = lr.endWidth = 0.1f;
            if (mesh)
            {
                lr.enabled = false;
                var rm = go.AddComponent<RopeMesh>();                 // brings MeshFilter + MeshRenderer
                rm.OverallDivision = 24; rm.ropeWidth = 0.05f; rm.radialDivision = 8; rm.tilingPerMeter = 6f;
                go.GetComponent<MeshRenderer>().sharedMaterial = ropeMat;
            }
            wind = go.AddComponent<RopeWindEffect>(); wind.perpendicularWind = true; wind.windForce = 0f;
            return rope;
        }

        public static void Build()
        {
            // ---- dusk light
            SceneKit.Ambient(new Color(0.46f, 0.40f, 0.46f), new Color(0.44f, 0.31f, 0.27f), new Color(0.10f, 0.09f, 0.11f));
            SceneKit.Shadows(45f);
            var sun = SceneKit.Sun("Sun", new Vector3(7, 190, 0), new Color(1f, 0.62f, 0.36f), 1.8f, true);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.95f, 0.56f, 0.33f); RenderSettings.fogDensity = 0.005f;
            RenderSettings.skybox = null; RenderSettings.sun = sun;
            // sky backdrop: full-viewport quad at the far plane with a dusk gradient
            var skyQuad = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(skyQuad.GetComponent<Collider>());
            skyQuad.name = "Sky quad";
            var sqm = Object.Instantiate(skyQuad.GetComponent<MeshFilter>().sharedMesh); sqm.name = "SkyQuad"; sqm.bounds = new Bounds(Vector3.zero, Vector3.one * 5000f);
            skyQuad.GetComponent<MeshFilter>().sharedMesh = sqm;
            var skyM = new Material(Shader.Find("GameTech/DuskSky")); AssetDatabase.CreateAsset(skyM, Dir + "Sky.mat");
            var skr = skyQuad.GetComponent<Renderer>(); skr.sharedMaterial = skyM; skr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // ---- materials
            var woodM = SceneKit.Mat(Dir, "Wood", Lit, new Color(0.24f, 0.16f, 0.11f), 0f, 0.25f);
            var plankM = SceneKit.Mat(Dir, "Plank", Lit, new Color(0.33f, 0.24f, 0.17f), 0f, 0.3f);
            darkM = SceneKit.Mat(Dir, "Iron", Lit, new Color(0.07f, 0.07f, 0.08f), 0.7f, 0.45f);
            var seaM = SceneKit.Mat(Dir, "Sea", Lit, new Color(0.035f, 0.085f, 0.11f), 0f, 0.82f);
            // wave normal map for the sea: a few sine waves, generated (alpha 1 so the RGB unpack path reads it)
            var wn = new Texture2D(256, 256, TextureFormat.RGBA32, true, true) { name = "sea_waves", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 2 };
            float[] kx = { 1, -1, 2, -2, 3, 1, -4, 5 }, ky = { 3, 5, 8, 12, 17, 23, 29, 37 }, amp = { 0.30f, 0.19f, 0.12f, 0.08f, 0.058f, 0.043f, 0.034f, 0.027f }, ph = { 0f, 1.3f, 2.1f, 4.0f, 0.7f, 5.2f, 3.3f, 1.9f };
            for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++)
            {
                float u = x / 256f * 6.2832f, v = y / 256f * 6.2832f, dx = 0, dy = 0;
                for (int k = 0; k < 8; k++) { float c = Mathf.Cos(kx[k] * u + ky[k] * v + ph[k]) * amp[k]; dx += c * kx[k]; dy += c * ky[k]; }
                var nrm = new Vector3(-dx * 0.15f, -dy * 0.15f, 1f).normalized;
                wn.SetPixel(x, y, new Color(nrm.x * 0.5f + 0.5f, nrm.y * 0.5f + 0.5f, nrm.z * 0.5f + 0.5f, 1f));
            }
            wn.Apply(true); AssetDatabase.CreateAsset(wn, Dir + "SeaWaves.asset");
            seaM.SetTexture("_BumpMap", wn); seaM.EnableKeyword("_NORMALMAP"); seaM.SetFloat("_BumpScale", 1f);
            seaM.SetTextureScale("_BaseMap", new Vector2(110, 110));
            var hullM = SceneKit.Mat(Dir, "Hull", Lit, new Color(0.62f, 0.60f, 0.55f), 0f, 0.4f);
            var redM = SceneKit.Mat(Dir, "Trolley", Lit, new Color(0.62f, 0.12f, 0.08f), 0.3f, 0.5f);
            lampM = Glow("LampGlow", new Color(3.2f, 1.7f, 0.5f));

            // rope texture: three twisted strands, generated
            var tex = new Texture2D(256, 256, TextureFormat.RGBA32, true) { name = "rope_twist", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++)
            {
                float s = Mathf.Repeat(x / 256f * 3f + y / 256f, 1f);
                float v = 0.55f + 0.45f * Mathf.Sin(s * Mathf.PI);
                tex.SetPixel(x, y, new Color(v, v, v, 1));
            }
            tex.Apply(true); AssetDatabase.CreateAsset(tex, Dir + "RopeTwist.asset");
            ropeMat = SceneKit.Mat(Dir, "Rope", Lit, new Color(0.56f, 0.45f, 0.31f), 0f, 0.2f); ropeMat.SetTexture("_BaseMap", tex);
            ropeLineMat = SceneKit.Mat(Dir, "RopeLine", Lit, new Color(0.46f, 0.37f, 0.26f), 0f, 0.2f);

            // ---- sea and pier
            var sea = GameObject.CreatePrimitive(PrimitiveType.Plane); Object.DestroyImmediate(sea.GetComponent<Collider>());
            sea.name = "Sea"; sea.transform.localScale = Vector3.one * 200; sea.GetComponent<Renderer>().sharedMaterial = seaM;
            var planks = new MeshKit();
            for (int i = 0; i < 54; i++) planks.Box(new Vector3(0, 0.82f, -16f + i * 0.6f), new Vector3(4.4f, 0.14f, 0.54f));
            SceneKit.Part("Pier planks", planks.Build("Planks"), plankM, Vector3.zero, Quaternion.identity);
            Box("Pier beam a", new Vector3(-1.9f, 0.6f, 0), new Vector3(0.25f, 0.3f, 32.4f), woodM);
            Box("Pier beam b", new Vector3(1.9f, 0.6f, 0), new Vector3(0.25f, 0.3f, 32.4f), woodM);
            for (int i = 0; i < 9; i++)
            {
                Pole("Pile", new Vector3(-1.9f, -1.5f, -16f + i * 4f), 2.2f, 0.16f, woodM);
                Pole("Pile", new Vector3(1.9f, -1.5f, -16f + i * 4f), 2.2f, 0.16f, woodM);
            }

            // ---- posts on the pier edge (tops at y 3.9) and their rope anchors
            float[] pz = { -14f, -7f, 0f, 7f, 14f };
            var postTop = new Transform[pz.Length];
            for (int i = 0; i < pz.Length; i++)
            {
                Pole("Post", new Vector3(1.6f, 0.89f, pz[i]), 3.05f, 0.11f, woodM);
                Box("Post arm", new Vector3(1.6f, 3.9f, pz[i]), new Vector3(0.5f, 0.1f, 0.14f), woodM);
                postTop[i] = Anchor("Post top " + i, new Vector3(1.6f, 3.84f, pz[i]));
            }
            // two tall masts on the other edge for the long soft rope
            Pole("Mast a", new Vector3(-1.6f, 0.89f, -10f), 5.7f, 0.13f, woodM);
            Pole("Mast b", new Vector3(-1.6f, 0.89f, 8f), 5.7f, 0.13f, woodM);
            var mastA = Anchor("Mast top a", new Vector3(-1.6f, 6.5f, -10f));
            var mastB = Anchor("Mast top b", new Vector3(-1.6f, 6.5f, 8f));

            // ---- rail over the water with a trolley: the moving end of the hero rope
            Pole("Rail pile", new Vector3(8.95f, -1.5f, 0), 5.9f, 0.16f, woodM);
            Box("Rail", new Vector3(7.5f, 4.18f, 0), new Vector3(3.3f, 0.12f, 0.16f), darkM);
            Box("Rail stop", new Vector3(5.9f, 4.1f, 0), new Vector3(0.1f, 0.28f, 0.2f), darkM);
            var trolley = Box("Trolley", new Vector3(RopeDirector.TrolleyRestX, 4.0f, 0), new Vector3(0.34f, 0.26f, 0.3f), redM);
            var trolleyEnd = Anchor("Trolley hook", new Vector3(0, -0.16f, 0), trolley.transform);

            // ---- moored boat with a mast
            var boat = new GameObject("Boat"); boat.transform.position = new Vector3(6.6f, -0.12f, 9.5f);
            var hull = new MeshKit().Lathe(new[] { new Vector2(0, 0), new Vector2(0, 0.38f), new Vector2(0.28f, 0.78f), new Vector2(0.62f, 0.92f), new Vector2(0.62f, 0) }, 28).Build("Hull");
            SceneKit.Part("Hull", hull, hullM, Vector3.zero, Quaternion.identity, boat.transform).transform.localScale = new Vector3(0.85f, 1f, 2.3f);
            Pole("Boat mast", new Vector3(0, 0.6f, 0.3f), 3.4f, 0.05f, woodM, boat.transform);
            var boatTop = Anchor("Boat mast top", new Vector3(0, 3.98f, 0.3f), boat.transform);
            boat.transform.rotation = Quaternion.Euler(0, 20, 0);

            // ---- rope slots: start, end, length. Slot 0 is the hero rope, the last one is the long soft rope.
            var A = new List<Transform> { postTop[2], postTop[0], postTop[1], postTop[2], postTop[3], postTop[3], mastA };
            var B = new List<Transform> { trolleyEnd, postTop[1], postTop[2], postTop[3], postTop[4], boatTop, mastB };
            float[] len = { 6.6f, 8.2f, 8.2f, 8.2f, 8.2f, 7.4f, 22f };
            int n = A.Count;
            var dr = new GameObject("Director").AddComponent<RopeDirector>();
            dr.trolley = trolley.transform; dr.boat = boat.transform; dr.seaMat = seaM;
            dr.setL = new Rope[n]; dr.setR = new Rope[n]; dr.setLine = new Rope[n];
            dr.windL = new RopeWindEffect[n]; dr.windR = new RopeWindEffect[n];
            dr.straight = new Transform[n]; dr.straightA = A.ToArray(); dr.straightB = B.ToArray();
            for (int i = 0; i < n; i++)
            {
                bool lantern = i == 0 || i == n - 1;
                dr.setL[i] = MakeRope("Rope L " + i, A[i], B[i], len[i], 8, true, lantern ? Lantern(8) : null, out dr.windL[i]);
                dr.setR[i] = MakeRope("Rope R " + i, A[i], B[i], len[i], 9, true, lantern ? Lantern(9) : null, out dr.windR[i]);
                dr.setLine[i] = MakeRope("Rope line " + i, A[i], B[i], len[i], 11, false, lantern ? Lantern(11) : null, out _);
                var cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Object.DestroyImmediate(cyl.GetComponent<Collider>());
                cyl.name = "Straight " + i; cyl.layer = 10; cyl.GetComponent<Renderer>().sharedMaterial = ropeLineMat;
                dr.straight[i] = cyl.transform;
            }
            // the long rope keeps softer values of its own
            foreach (var r in new[] { dr.setL[n - 1], dr.setR[n - 1], dr.setLine[n - 1] }) { r.stiffness = 40f; r.damping = 1.5f; r.midPointWeight = 4f; }
            dr.straightLantern = Lantern(10);

            // ---- debug markers (layer 12)
            Transform Marker(string name, Color c, float size)
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Sphere); Object.DestroyImmediate(s.GetComponent<Collider>());
                s.name = name; s.layer = 12; s.transform.localScale = Vector3.one * size;
                var r = s.GetComponent<Renderer>(); r.sharedMaterial = Glow("Mk" + name, c); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                return s.transform;
            }
            LineRenderer Line(string name, Color c, float width, int count)
            {
                var go = new GameObject(name); go.layer = 12;
                var l = go.AddComponent<LineRenderer>(); l.positionCount = count; l.startWidth = l.endWidth = width;
                l.sharedMaterial = Glow("Ln" + name, c); l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; l.numCapVertices = 2;
                return l;
            }
            dr.mkStart = Marker("Start", new Color(2.5f, 2.5f, 2.5f), 0.24f);
            dr.mkEnd = Marker("End", new Color(2.5f, 2.5f, 2.5f), 0.24f);
            dr.mkCtrl = Marker("Control", new Color(3.5f, 2.6f, 0.2f), 0.32f);
            dr.mkTarget = Marker("Target", new Color(0.2f, 2.6f, 3.5f), 0.26f);
            dr.polyLine = Line("Polygon", new Color(1.6f, 1.6f, 1.6f), 0.035f, 3);
            dr.pullLine = Line("Pull", new Color(3.5f, 0.4f, 0.3f), 0.07f, 2);
            dr.windLine = Line("WindVector", new Color(0.4f, 3.0f, 0.6f), 0.05f, 2);

            // ---- cameras
            var bg = new Color(0.3f, 0.2f, 0.2f);
            dr.camL = SceneKit.Cam("Camera left", new Rect(0, 0, 0.5f, 1), 34, bg, 0);
            dr.camR = SceneKit.Cam("Camera right", new Rect(0.5f, 0, 0.5f, 1), 34, bg, 1);
            dr.camFull = SceneKit.Cam("Camera full", new Rect(0, 0, 1, 1), 34, bg, 2); dr.camFull.tag = "MainCamera";
            foreach (var cam in new[] { dr.camL, dr.camR, dr.camFull }) { cam.farClipPlane = 1500f; }

            SceneKit.Grade(Dir, 0.45f, 0.1f, 14f, 8f, 0.3f, 0f);

            LabelSpec.Create("OFF: straight cylinder, no dynamics", 0.03f, 0.05f).During(1.2f, 8f);
            LabelSpec.Create("ON: two ends + sprung midpoint", 0.53f, 0.05f).During(1.2f, 8f);
            LabelSpec.Create("stiffness 150, damping 4, weight 1, length 6.6 m", 0.53f, 0.115f, 30).During(1.2f, 8f);
            LabelSpec.Create("Stiff: stiffness 600, damping 20, weight 1", 0.03f, 0.05f, 34).During(8.2f, 14f);
            LabelSpec.Create("Soft: stiffness 60, damping 2, weight 6", 0.53f, 0.05f, 34).During(8.2f, 14f);
            LabelSpec.Create("Wind off", 0.03f, 0.05f).During(14.2f, 18.5f);
            LabelSpec.Create("Wind on: wind force 120", 0.53f, 0.05f).During(14.2f, 18.5f);
            LabelSpec.Create("Line Renderer: flat strip, 11 points", 0.03f, 0.05f, 36).During(18.7f, 21.2f);
            LabelSpec.Create("Rope mesh: tube, 24 x 8 divisions", 0.53f, 0.05f, 36).During(18.7f, 21.2f);
            LabelSpec.Create("yellow: sprung midpoint (curve control point)", 0.03f, 0.93f, 30).During(21.4f, 24.1f);
            LabelSpec.Create("blue: its target below the span", 0.03f, 0.875f, 30).During(21.4f, 24.1f);
            LabelSpec.Create("red: spring pull towards the target", 0.03f, 0.82f, 30).During(21.4f, 24.1f);
            dr.live = LabelSpec.Create("", 0.03f, 0.05f, 34).During(21.4f, 24.1f);
            dr.live2 = LabelSpec.Create("", 0.03f, 0.115f, 30).During(21.4f, 24.1f);
            Debug.Log($"[gt] rope scene built: {n} rope slots x 3 rope sets");
        }
    }
}
