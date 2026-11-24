// Builds the GeoAO scene from code: a concrete corner (floor, back wall, side wall), three steps, a pillar against the
// wall, stacked blocks with a 3 cm slot, a bench, and the stand-in mannequin posed against the wall and baked to a
// static mesh, plus a ledge on the back wall. Every surface is a finely subdivided grid (vertex AO needs vertices where the occlusion changes), and
// every mesh is stored in world space with an identity transform so the bake can draw it directly.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    public static class GeoAOScene
    {
        const string Dir = "Assets/Techniques/geo-ao/";
        const float Step = 0.05f;
        static readonly List<MeshFilter> All = new List<MeshFilter>();

        class MB { public List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(); public List<int> t = new List<int>(); }

        // grid on the rectangle o + a*u + b*v (u, v full edge vectors), facing nrm
        static void Grid(MB m, Vector3 o, Vector3 u, Vector3 v, Vector3 nrm)
        {
            int nu = Mathf.Max(1, Mathf.CeilToInt(u.magnitude / Step)), nv = Mathf.Max(1, Mathf.CeilToInt(v.magnitude / Step)), s = m.v.Count;
            for (int j = 0; j <= nv; j++) for (int i = 0; i <= nu; i++) { m.v.Add(o + u * (i / (float)nu) + v * (j / (float)nv)); m.n.Add(nrm); }
            bool flip = Vector3.Dot(Vector3.Cross(u, v), nrm) < 0;
            for (int j = 0; j < nv; j++) for (int i = 0; i < nu; i++)
            {
                int a = s + j * (nu + 1) + i, b = a + 1, c = a + nu + 1, d = c + 1;
                // front face: Cross(b - a, c - a) points along the normal
                if (!flip) m.t.AddRange(new[] { a, b, d, a, d, c }); else m.t.AddRange(new[] { a, d, b, a, c, d });
            }
        }
        static void Box(MB m, Vector3 c, Vector3 size, float yaw = 0, bool bottom = false)
        {
            var q = Quaternion.Euler(0, yaw, 0); Vector3 h = size * 0.5f;
            for (int ax = 0; ax < 3; ax++) for (int sg = -1; sg <= 1; sg += 2)
            {
                if (ax == 1 && sg < 0 && !bottom) continue;
                Vector3 n = Vector3.zero; n[ax] = sg;
                Vector3 u = Vector3.zero; u[(ax + 1) % 3] = size[(ax + 1) % 3];
                Vector3 w = Vector3.zero; w[(ax + 2) % 3] = size[(ax + 2) % 3];
                Vector3 o = n * h[ax] - u * 0.5f - w * 0.5f;
                Grid(m, c + q * o, q * u, q * w, q * n);
            }
        }
        static MeshFilter Obj(string name, MB m, Material mat)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(m.v); mesh.SetNormals(m.n); mesh.SetTriangles(m.t, 0);
            mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh, Dir + name + ".asset");
            var go = new GameObject(name); var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.On;
            All.Add(mf); return mf;
        }
        static Material M(string name, Color c, float noise, float smooth)
        {
            var m = new Material(Shader.Find("GameTech/GeoAOLit")); m.SetColor("_BaseColor", c); m.SetFloat("_Noise", noise); m.SetFloat("_Smoothness", smooth);
            AssetDatabase.CreateAsset(m, Dir + name + ".mat"); return m;
        }

        public static void Build()
        {
            All.Clear();
            RenderSettings.skybox = null; RenderSettings.fog = false;
            SceneKit.Shadows(14f);
            var concrete = M("Concrete", new Color(0.62f, 0.6f, 0.57f), 0.28f, 0.15f);
            var wallM = M("Wall", new Color(0.7f, 0.66f, 0.6f), 0.18f, 0.1f);
            var floorM = M("Floor", new Color(0.5f, 0.49f, 0.47f), 0.3f, 0.2f);
            var darkM = M("Dark stone", new Color(0.36f, 0.35f, 0.34f), 0.3f, 0.25f);

            var m = new MB(); Grid(m, new Vector3(-4f, 0, -2.5f), new Vector3(8f, 0, 0), new Vector3(0, 0, 5.5f), Vector3.up); Obj("Floor", m, floorM);
            m = new MB(); Grid(m, new Vector3(-4f, 0, 3f), new Vector3(8f, 0, 0), new Vector3(0, 2.4f, 0), Vector3.back); Obj("Back wall", m, wallM);
            m = new MB(); Grid(m, new Vector3(-2.6f, 0, -2.5f), new Vector3(0, 0, 5.5f), new Vector3(0, 2.4f, 0), Vector3.right); Obj("Side wall", m, wallM);
            m = new MB();
            Box(m, new Vector3(1.5f, 0.1f, 2.475f), new Vector3(2.2f, 0.2f, 1.05f));
            Box(m, new Vector3(1.5f, 0.3f, 2.65f), new Vector3(2.2f, 0.2f, 0.7f));
            Box(m, new Vector3(1.5f, 0.5f, 2.825f), new Vector3(2.2f, 0.2f, 0.35f));
            Obj("Steps", m, concrete);
            m = new MB(); Box(m, new Vector3(-0.3f, 1.65f, 2.8f), new Vector3(1.2f, 0.1f, 0.4f), 0, true); Obj("Ledge", m, concrete);
            m = new MB(); Box(m, new Vector3(-1.3f, 1.2f, 2.75f), new Vector3(0.5f, 2.4f, 0.5f)); Obj("Pillar", m, concrete);
            m = new MB();
            Box(m, new Vector3(1.5f, 0.25f, 0.5f), new Vector3(1.2f, 0.5f, 0.6f));
            Box(m, new Vector3(1.42f, 0.675f, 0.52f), new Vector3(0.8f, 0.35f, 0.45f), 12f, true);
            Box(m, new Vector3(2.365f, 0.35f, 0.5f), new Vector3(0.47f, 0.7f, 0.5f));
            Obj("Blocks", m, darkM);
            m = new MB();
            Box(m, new Vector3(-0.9f, 0.35f, 0.55f), new Vector3(1.6f, 0.1f, 0.8f), 0, true);
            Box(m, new Vector3(-1.54f, 0.15f, 0.55f), new Vector3(0.12f, 0.3f, 0.7f));
            Box(m, new Vector3(-0.26f, 0.15f, 0.55f), new Vector3(0.12f, 0.3f, 0.7f));
            Obj("Bench", m, concrete);

            // stand-in mannequin: arms down along the body, elbows a little forward, back against the wall; baked static
            Material costume = null, skin = null;
            var man = Mannequin.Build("GameTech/GeoAOLit", Dir, (mat, part) =>
            {
                mat.SetColor("_BaseColor", part == "skin" ? new Color(0.78f, 0.62f, 0.5f) : new Color(0.5f, 0.52f, 0.56f));
                mat.SetFloat("_Noise", 0.04f); mat.SetFloat("_Smoothness", 0.3f);
                if (part == "skin") skin = mat; else costume = mat;
            });
            var bones = new Dictionary<string, Transform>(); foreach (var tr in man.GetComponentsInChildren<Transform>()) bones[tr.name] = tr;
            void Turn(string b, float deg, Vector3 axis) { var tr = bones[b]; tr.rotation = Quaternion.AngleAxis(deg, axis) * tr.rotation; }
            Turn("Upperarm_L", 80f, Vector3.forward); Turn("Upperarm_R", -80f, Vector3.forward);
            Turn("Lowerarm_L", -22f, Vector3.right); Turn("Lowerarm_R", -22f, Vector3.right);
            Turn("Head", 8f, Vector3.right);
            man.transform.SetPositionAndRotation(new Vector3(-2.05f, 0f, 2.875f), Quaternion.Euler(0, 180f, 0));
            var smr = man.GetComponentInChildren<SkinnedMeshRenderer>();
            var baked = new Mesh(); smr.BakeMesh(baked, true);
            var l2w = smr.transform.localToWorldMatrix; var bv = baked.vertices; var bn = baked.normals;
            for (int i = 0; i < bv.Length; i++) { bv[i] = l2w.MultiplyPoint3x4(bv[i]); bn[i] = l2w.MultiplyVector(bn[i]).normalized; }
            var mm = new Mesh { name = "Mannequin", indexFormat = IndexFormat.UInt32 }; mm.vertices = bv; mm.normals = bn; mm.subMeshCount = baked.subMeshCount;
            for (int s = 0; s < baked.subMeshCount; s++) mm.SetTriangles(baked.GetTriangles(s), s);
            mm.RecalculateBounds();
            AssetDatabase.CreateAsset(mm, Dir + "MannequinBaked.asset");
            var mats = smr.sharedMaterials;
            var mgo = new GameObject("Mannequin (static)"); var mmf = mgo.AddComponent<MeshFilter>(); mmf.sharedMesh = mm;
            var mmr = mgo.AddComponent<MeshRenderer>(); mmr.sharedMaterials = mats; All.Add(mmf);
            Object.DestroyImmediate(man);
            float minY = float.MaxValue, maxZ = float.MinValue; foreach (var p in bv) { minY = Mathf.Min(minY, p.y); maxZ = Mathf.Max(maxZ, p.z); }

            SceneKit.Sun("Low sun", new Vector3(50f, -105f, 0f), new Color(1f, 0.8f, 0.6f), 0.9f, true);
            SceneKit.Grade(Dir, 0.25f, 0f, 14f, 6f, 0.3f, 0.05f);

            var dir = new GameObject("Director").AddComponent<GeoAODirector>();
            dir.meshes = All.ToArray();
            dir.depthMat = new Material(Shader.Find("GameTech/GeoAODepth")); AssetDatabase.CreateAsset(dir.depthMat, Dir + "Depth.mat");
            dir.bake = AssetDatabase.LoadAssetAtPath<ComputeShader>(Dir + "Shaders/GeoAOBake.compute");
            var bg = new Color(0.05f, 0.06f, 0.08f);
            dir.camA = SceneKit.Cam("Cam A", new Rect(0, 0, 1, 1), 42f, bg, -5, false);
            dir.camB = SceneKit.Cam("Cam B", new Rect(0, 0, 1, 1), 42f, bg, -6, false);
            foreach (var c in new[] { dir.camA, dir.camB }) { c.cullingMask = ~(1 << 10); c.nearClipPlane = 0.03f; c.farClipPlane = 40f; }

            var disp = new Material(Shader.Find("GameTech/GeoComposite")); AssetDatabase.CreateAsset(disp, Dir + "Display.mat"); dir.display = disp;
            var dcam = SceneKit.Cam("Display cam", new Rect(0, 0, 1, 1), 60f, Color.black, 0, true);
            dcam.orthographic = true; dcam.orthographicSize = 0.5f; dcam.cullingMask = 1 << 10; dcam.transform.position = new Vector3(500f, 0f, -1f);
            dcam.nearClipPlane = 0.01f; dcam.farClipPlane = 5f;
            var dq = GameObject.CreatePrimitive(PrimitiveType.Quad); dq.name = "Display quad"; dq.layer = 10; Object.DestroyImmediate(dq.GetComponent<Collider>());
            dq.transform.position = new Vector3(500f, 0f, 0f); dq.transform.localScale = new Vector3(16f / 9f, 1f, 1f);
            var dmr = dq.GetComponent<MeshRenderer>(); dmr.sharedMaterial = disp; dmr.shadowCastingMode = ShadowCastingMode.Off;

            dir.title = LabelSpec.Create("", 0.012f, 0.93f, 40);
            dir.l1 = LabelSpec.Create("", 0.012f, 0.058f, 34); dir.l2 = LabelSpec.Create("", 0.012f, 0.018f, 30);
            dir.m1 = LabelSpec.Create("", 0.512f, 0.058f, 34); dir.m2 = LabelSpec.Create("", 0.512f, 0.018f, 30);
            AssetDatabase.SaveAssets();
            Debug.Log($"[gt] built: {All.Count} meshes, mannequin {bv.Length} verts, feet y {minY:F3}, back z {maxZ:F3}, compute {(dir.bake != null)}, lit {Shader.Find("GameTech/GeoAOLit").isSupported}");
        }
    }
}
