// 69 dynamic footprints: IA walks over four ground strips (snow, mud, dry sand, snow) that deform from the foot force.
// Two identical worlds 80 m apart: x = 0 deforms (ON), x = 80 is rigid (OFF) for the split screen.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    public static class FootprintScene
    {
        const string Dir = "Assets/Techniques/dynamic-footprints/";
        const string Lit = "Universal Render Pipeline/Lit", Unlit = "Universal Render Pipeline/Unlit";
        const float Cell = 0.0125f, LaneW = 1.8f, LaneZ0 = -2.9f;

        struct Zone { public string name; public int nz; public double E; public float tau, nu; public int filt; public bool bump; public Material mat; }

        static void Flat(string name, Material m, float x0, float x1, float z0, float z1)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = new[] { new Vector3(x0, 0, z0), new Vector3(x0, 0, z1), new Vector3(x1, 0, z1), new Vector3(x1, 0, z0) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.uv = new[] { new Vector2(x0, z0), new Vector2(x0, z1), new Vector2(x1, z1), new Vector2(x1, z0) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            var go = SceneKit.Part(name, mesh, m, Vector3.zero, Quaternion.identity);
            go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        static MeshFilter Overlay(string name, Material m)
        {
            var go = new GameObject(name); var mf = go.AddComponent<MeshFilter>();
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            return mf;
        }

        public static void Build()
        {
            SceneKit.Ambient(new Color(0.62f, 0.70f, 0.86f), new Color(0.55f, 0.58f, 0.62f), new Color(0.36f, 0.36f, 0.37f));
            SceneKit.Shadows(18f);
            var sun = SceneKit.Sun("Sun", new Vector3(24, 45, 0), new Color(1f, 0.93f, 0.82f), 2.2f, true);
            RenderSettings.sun = sun;
            var sky = new Material(Shader.Find("Skybox/Procedural"));
            sky.SetFloat("_SunSize", 0.04f); sky.SetFloat("_AtmosphereThickness", 0.9f); sky.SetFloat("_Exposure", 1.2f);
            AssetDatabase.CreateAsset(sky, Dir + "Sky.mat");
            RenderSettings.skybox = sky;

            var snow = SceneKit.Mat(Dir, "Snow", Lit, new Color(0.90f, 0.92f, 0.96f), 0, 0.25f);
            var mud = SceneKit.Mat(Dir, "Mud", Lit, new Color(0.36f, 0.27f, 0.19f), 0, 0.42f);
            var sand = SceneKit.Mat(Dir, "Sand", Lit, new Color(0.76f, 0.63f, 0.43f), 0, 0.1f);
            // E, tau, Poisson and blur passes are the preset values in the source's DeformTerrainMaster; "bump" (ring) is my choice per strip
            var zones = new[]
            {
                new Zone { name = "snow", nz = 770, E = 200000, tau = 0.2f, nu = 0.1f, filt = 0, bump = false, mat = snow },
                new Zone { name = "mud", nz = 224, E = 350000, tau = 0.8f, nu = 0.4f, filt = 2, bump = true, mat = mud },
                new Zone { name = "dry sand", nz = 224, E = 600000, tau = 0.3f, nu = 0.2f, filt = 5, bump = true, mat = sand },
                new Zone { name = "snow", nz = 502, E = 200000, tau = 0.2f, nu = 0.1f, filt = 0, bump = true, mat = snow },
            };

            var ia = IAKit.Spawn(Lit, Dir, (m, name) =>
            {
                m.SetFloat("_Smoothness", 0.15f); m.SetFloat("_Cull", 0f);
                if (name == "face_alpha" || name == "Halo") { m.SetFloat("_AlphaClip", 1f); m.EnableKeyword("_ALPHATEST_ON"); m.SetFloat("_Cutoff", 0.5f); m.renderQueue = 2450; }
            });
            var iaOff = Object.Instantiate(ia);
            var walkers = new FootWalker[2];
            for (int wi = 0; wi < 2; wi++)
            {
                float wx = wi * 80f; bool on = wi == 0;
                var list = new List<SoftGround>(); float z = LaneZ0;
                for (int k = 0; k < zones.Length; k++)
                {
                    var zn = zones[k]; float z1 = z + zn.nz * Cell;
                    if (on || k < 2)
                    {
                        var go = new GameObject("Lane " + zn.name + (on ? " ON" : " OFF"));
                        go.transform.position = new Vector3(wx - LaneW * 0.5f, 0, z);
                        var g = go.AddComponent<SoftGround>();
                        g.nx = Mathf.RoundToInt(LaneW / Cell); g.nz = zn.nz; g.cell = Cell; g.presetName = zn.name; g.young = zn.E; g.tau = zn.tau; g.poisson = zn.nu;
                        g.filterIterations = zn.filt; g.bump = zn.bump; g.material = zn.mat;
                        list.Add(g);
                        Flat("Side", zn.mat, wx - 30f, wx - LaneW * 0.5f, z, z1); Flat("Side", zn.mat, wx + LaneW * 0.5f, wx + 30f, z, z1);
                    }
                    else Flat("Strip", zn.mat, wx - 30f, wx + 30f, z, z1);
                    z = z1;
                }
                Flat("Before", snow, wx - 30f, wx + 30f, -40f, LaneZ0); Flat("After", snow, wx - 30f, wx + 30f, z, 60f);
                var who = on ? ia : iaOff; who.name = on ? "IA ON" : "IA OFF"; who.transform.position = new Vector3(wx, 0, 0);
                var fw = who.AddComponent<FootWalker>(); fw.grounds = list.ToArray(); fw.deform = on; walkers[wi] = fw;
            }

            var d = new GameObject("Director").AddComponent<FootprintDirector>();
            d.on = walkers[0]; d.off = walkers[1];
            d.camL = SceneKit.Cam("Camera OFF", new Rect(0, 0, 0.5f, 1), 34, Color.black, 0);
            d.camR = SceneKit.Cam("Camera ON", new Rect(0.5f, 0, 0.5f, 1), 34, Color.black, 1);
            d.camFull = SceneKit.Cam("Camera full", new Rect(0, 0, 1, 1), 34, Color.black, 2); d.camFull.tag = "MainCamera";
            foreach (var c in new[] { d.camL, d.camR, d.camFull }) { c.clearFlags = CameraClearFlags.Skybox; c.nearClipPlane = 0.05f; c.farClipPlane = 300f; }
            SceneKit.Grade(Dir, 0.1f, -0.35f, 8f, 0f, 0.2f, 0.1f);

            d.contactCells = Overlay("Contact cells", SceneKit.Mat(Dir, "CellBlue", Unlit, new Color(0.10f, 0.45f, 1f)));
            d.ringCells = Overlay("Ring cells", SceneKit.Mat(Dir, "CellOrange", Unlit, new Color(1f, 0.55f, 0.05f)));
            var cone = new MeshKit().Lathe(new[] { new Vector2(0, 0), new Vector2(0.05f, 0.024f), new Vector2(0.05f, 0) }, 16).Build("Cone", 30f);
            var cols = new[] { SceneKit.Mat(Dir, "ArrowWeight", Unlit, new Color(0.10f, 0.30f, 1f)), SceneKit.Mat(Dir, "ArrowMomentum", Unlit, new Color(0.95f, 0.12f, 0.08f)), SceneKit.Mat(Dir, "ArrowTotal", Unlit, new Color(0.04f, 0.04f, 0.04f)) };
            for (int a = 0; a < 6; a++)
            {
                var head = SceneKit.Part("Arrow head", cone, cols[a % 3], Vector3.zero, Quaternion.identity);
                var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder); shaft.name = "Arrow shaft";
                Object.DestroyImmediate(shaft.GetComponent<Collider>()); shaft.GetComponent<Renderer>().sharedMaterial = cols[a % 3];
                foreach (var r in new[] { head.GetComponent<Renderer>(), shaft.GetComponent<Renderer>() }) { r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; }
                d.heads[a] = head.transform; d.shafts[a] = shaft.transform;
            }

            LabelSpec.Create("OFF: rigid ground", 0.03f, 0.05f).During(2.2f, 9f);
            LabelSpec.Create("ON: footprints from foot force (snow)", 0.53f, 0.05f).During(2.2f, 9f);
            LabelSpec.Create("mud preset: E 350 kPa, tau 0.8 s, Poisson 0.4, 2 blur passes, ring on", 0.03f, 0.05f, 36).During(9.5f, 13.5f);
            LabelSpec.Create("dry sand preset: E 600 kPa, tau 0.3 s, Poisson 0.2, 5 blur passes, ring on", 0.03f, 0.05f, 36).During(13.5f, 17.5f);
            LabelSpec.Create("snow preset: E 200 kPa, tau 0.2 s, Poisson 0.1, ring on", 0.03f, 0.05f, 36).During(17.5f, 24.1f);
            LabelSpec.Create("cells: contact (blue), ring (orange)   arrows: weight (blue), momentum (red), total (black)", 0.03f, 0.17f, 32).During(17f, 24.1f);
            d.live = LabelSpec.Create("", 0.03f, 0.11f, 36).During(9f, 24.1f);
        }
    }
}
