// Builds the FLIP scene: a cut-away tank the size of the simulation domain (12 x 8 x 6 units drawn at 0.25 m per unit,
// two solid walls, an edge frame on the open sides), a box pillar that is also the simulation's solid obstacle,
// three FLIPFluid instances with the source's parameters (flipness 0.96 or 0, cell 0.2 or 0.4), cameras, light, labels.
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace GameTech.Posts
{
    public static class FLIPScene
    {
        const string Dir = "Assets/Techniques/flip-fluid/";
        const string Lit = "Universal Render Pipeline/Lit";
        const float S = 0.25f;                                        // metres per simulation unit
        static readonly Vector3 Domain = new Vector3(12f, 8f, 6f);
        static readonly Vector3 BoxMin = new Vector3(1.2f, -4f, -1.0f), BoxMax = new Vector3(2.4f, 0f, 0.6f);

        static GameObject Box(string name, Material m, Vector3 pos, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name; Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.position = pos; go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            return go;
        }

        // icosphere, 1 subdivision (80 triangles), unit diameter
        static Mesh Ico()
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3> { new(-1,t,0), new(1,t,0), new(-1,-t,0), new(1,-t,0), new(0,-1,t), new(0,1,t), new(0,-1,-t), new(0,1,-t), new(t,0,-1), new(t,0,1), new(-t,0,-1), new(-t,0,1) };
            int[] f = { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8, 3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            var tris = new List<int>(); var mid = new Dictionary<long, int>();
            int Mid(int a, int b) { long k = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a; if (mid.TryGetValue(k, out int i)) return i; v.Add((v[a] + v[b]) * 0.5f); mid[k] = v.Count - 1; return v.Count - 1; }
            for (int i = 0; i < f.Length; i += 3)
            {
                int a = f[i], b = f[i + 1], c = f[i + 2], ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                tris.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            var verts = new Vector3[v.Count]; var norms = new Vector3[v.Count];
            for (int i = 0; i < v.Count; i++) { norms[i] = v[i].normalized; verts[i] = norms[i] * 0.5f; }
            var m = new Mesh { name = "Icosphere80", vertices = verts, normals = norms, triangles = tris.ToArray() };
            m.RecalculateBounds();
            return m;
        }

        static FLIPFluid Sim(string name, int layer, Mesh mesh, Material mat, Vector3 origin, float flipness, float spacing)
        {
            var f = new GameObject(name).AddComponent<FLIPFluid>();
            f.particleMesh = mesh; f.material = mat; f.layer = layer; f.origin = origin; f.worldScale = S;
            // source parameters (scene values: flipness 0.96, gravity 9.8, viscosity 0, linear kernel, forward Euler,
            // density projection on, Jacobi 15 / 15 / 30, time step 1/60)
            f.flipness = flipness; f.gravity = new Vector3(0, -9.8f, 0); f.viscosity = 0f;
            f.frameTime = 1f / 30f; f.stepsPerFrame = 2;
            // mine: box domain, spawn block against the left wall, pillar obstacle, cell size
            f.domainSize = Domain; f.gridSpacing = spacing;
            f.initMin = new Vector3(-6f, -4f, -3f); f.initMax = new Vector3(-2f, 1f, 3f);
            f.obstacle = true; f.obstacleMin = BoxMin; f.obstacleMax = BoxMax;
            return f;
        }

        public static void Build()
        {
            SceneKit.Shadows(20f);
            SceneKit.Ambient(new Color(0.40f, 0.46f, 0.56f), new Color(0.30f, 0.30f, 0.32f), new Color(0.12f, 0.11f, 0.10f));
            SceneKit.Sun("Sun", new Vector3(48f, -30f, 0f), new Color(1.0f, 0.93f, 0.84f), 1.8f, true);

            float Lx = Domain.x * S, Ly = Domain.y * S, Lz = Domain.z * S;   // 3 x 2 x 1.5 m
            var origin = new Vector3(0f, 0.03f + Ly / 2f, 0f);                // world position of the domain centre

            var floorM = SceneKit.Mat(Dir, "Floor", Lit, new Color(0.20f, 0.21f, 0.23f), 0, 0.35f);
            var tankM = SceneKit.Mat(Dir, "TankFloor", Lit, new Color(0.78f, 0.80f, 0.82f), 0, 0.5f);
            var wallM = SceneKit.Mat(Dir, "Wall", Lit, new Color(0.30f, 0.34f, 0.40f), 0, 0.7f);
            var frameM = SceneKit.Mat(Dir, "Frame", Lit, new Color(0.62f, 0.64f, 0.66f), 0.8f, 0.6f);
            var pillarM = SceneKit.Mat(Dir, "Pillar", Lit, new Color(0.72f, 0.42f, 0.20f), 0, 0.45f);
            Box("Ground", floorM, new Vector3(0, -0.1f, 0), new Vector3(30f, 0.2f, 30f));
            Box("Tank floor", tankM, new Vector3(0, -0.01f, 0), new Vector3(Lx + 0.12f, 0.04f, Lz + 0.12f));
            float H = Ly + 0.05f, e = 0.04f, hx = Lx / 2 + 0.03f, hz = Lz / 2 + 0.03f;
            Box("Back wall", wallM, new Vector3(0, H / 2, hz + 0.02f), new Vector3(Lx + 0.12f, H, 0.04f));
            Box("Right wall", wallM, new Vector3(hx + 0.02f, H / 2, 0), new Vector3(0.04f, H, Lz + 0.12f));
            Box("Edge front-left", frameM, new Vector3(-hx, H / 2, -hz), new Vector3(e, H, e));
            Box("Edge front-right", frameM, new Vector3(hx, H / 2, -hz), new Vector3(e, H, e));
            Box("Edge back-left", frameM, new Vector3(-hx, H / 2, hz), new Vector3(e, H, e));
            Box("Edge top-front", frameM, new Vector3(0, H, -hz), new Vector3(Lx + 0.1f, e, e));
            Box("Edge top-left", frameM, new Vector3(-hx, H, 0), new Vector3(e, e, Lz + 0.1f));
            Box("Edge bottom-front", frameM, new Vector3(0, 0.02f, -hz), new Vector3(Lx + 0.1f, e, e));
            Box("Edge bottom-left", frameM, new Vector3(-hx, 0.02f, 0), new Vector3(e, e, Lz + 0.1f));
            // the pillar: drawn exactly where the simulation's solid box is
            var bc = origin + (BoxMin + BoxMax) * 0.5f * S; var bs = (BoxMax - BoxMin) * S;
            Box("Pillar (solid obstacle)", pillarM, bc, bs);

            var mesh = Ico(); AssetDatabase.CreateAsset(mesh, Dir + "Icosphere80.asset");
            var pm = new Material(Shader.Find("GameTech/FLIPParticle"));
            AssetDatabase.CreateAsset(pm, Dir + "Particle.mat");
            var flip = Sim("FLIP 0.96, cell 0.2", 20, mesh, pm, origin, 0.96f, 0.2f);
            var pic = Sim("PIC (flipness 0), cell 0.2", 21, mesh, pm, origin, 0f, 0.2f);
            var low = Sim("FLIP 0.96, cell 0.4", 22, mesh, pm, origin, 0.96f, 0.4f);

            var bg = new Color(0.07f, 0.08f, 0.10f);
            var clear = SceneKit.Cam("Clear", new Rect(0, 0, 1, 1), 30, Color.black, -10, false); clear.cullingMask = 0;
            var camA = SceneKit.Cam("Cam A", new Rect(0, 0, 1, 1), 34, bg, 0);
            var camB = SceneKit.Cam("Cam B", new Rect(0.5f, 0, 0.5f, 1), 34, bg, 1);
            camA.tag = "MainCamera";
            foreach (var c in new[] { camA, camB }) { c.transform.position = new Vector3(-3.4f, 2.8f, -4.4f); c.nearClipPlane = 0.05f; c.farClipPlane = 60f; }
            SceneKit.Grade(Dir, 0.35f, 0f, 14f, 6f, 0.3f, 0.12f);

            var title = LabelSpec.Create("", 0.012f, 0.935f, 46);
            var l1 = LabelSpec.Create("", 0.012f, 0.03f, 30);
            var l2 = LabelSpec.Create("", 0.012f, 0.018f, 26);
            var m1 = LabelSpec.Create("", 0.512f, 0.058f, 30);
            var m2 = LabelSpec.Create("", 0.512f, 0.018f, 26);

            var d = new GameObject("Director").AddComponent<FLIPDirector>();
            d.camA = camA; d.camB = camB; d.flip = flip; d.pic = pic; d.low = low;
            d.title = title; d.l1 = l1; d.l2 = l2; d.m1 = m1; d.m2 = m2;
            d.tankCentre = new Vector3(0, Ly / 2, 0);
            Debug.Log("[gt] FLIP scene built");
        }
    }
}
