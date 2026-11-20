// Builds the dithered transparency scene from code: a sunny courtyard with two sheer curtains, three intersecting
// tinted glass panes, leaf-card bushes and a terracotta urn. Every see-through object is made three times
// (dithered on layer 9, alpha-blended on layer 8, shadow-only dithered on layer 0), see DitherDirector.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GameTech.Posts
{
    public static class DitherScene
    {
        const string Dir = "Assets/Techniques/dithered-transparency/";

        static Material Mat(string name, string shader, Texture tex, Color c, float smooth, float wind = 0f, Vector2? tiling = null)
        {
            var m = new Material(Shader.Find(shader));
            if (tex != null) m.SetTexture("_BaseMap", tex);
            if (tiling.HasValue) m.SetTextureScale("_BaseMap", tiling.Value);
            m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Wind")) m.SetFloat("_Wind", wind);
            AssetDatabase.CreateAsset(m, Dir + name + ".mat"); return m;
        }

        static Mesh Save(Mesh m) { AssetDatabase.CreateAsset(m, Dir + "Mesh_" + m.name + ".asset"); return m; }

        static Texture2D SaveTex(Texture2D t, string name) { t.Apply(true); AssetDatabase.CreateAsset(t, Dir + name + ".asset"); return t; }

        // ---------- textures ----------
        static Texture2D LeafTex()
        {
            int n = 256; var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float yy = Mathf.InverseLerp(0.03f, 0.97f, v);
                    float half = 0.40f * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01(yy)), 0.8f) * (yy < 0.5f ? 1f : Mathf.Lerp(1f, 0.85f, (yy - 0.5f) * 2f));
                    float d = half - Mathf.Abs(u - 0.5f);
                    float a = Mathf.Clamp01(d * n * 0.5f + 0.5f);
                    if (yy <= 0f || yy >= 1f) a = 0f;
                    float rib = Mathf.Clamp01(1f - Mathf.Abs(u - 0.5f) * 90f) * 0.25f;
                    float vein = Mathf.Clamp01(1f - Mathf.Abs(Mathf.Repeat((v - Mathf.Abs(u - 0.5f) * 0.9f) * 9f, 1f) - 0.5f) * 14f) * 0.08f;
                    var c = Color.Lerp(new Color(0.26f, 0.5f, 0.12f), new Color(0.55f, 0.78f, 0.22f), v) + new Color(rib + vein, rib + vein, rib * 0.5f);
                    c.a = a; t.SetPixel(x, y, c);
                }
            return SaveTex(t, "LeafTex");
        }

        static Texture2D VoileTex()
        {
            int n = 128; var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float s = Mathf.Repeat(x / (float)n * 4f, 1f);
                    float stripe = s < 0.18f ? 1f : (s < 0.24f ? 0.5f : 0f);
                    float a = 0.34f + 0.2f * stripe;
                    t.SetPixel(x, y, new Color(1f, 0.97f, 0.92f, a));
                }
            return SaveTex(t, "VoileTex");
        }

        static Texture2D StoneTex()
        {
            int n = 512; var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat };
            var rnd = new System.Random(7); var tone = new float[64];
            for (int i = 0; i < 64; i++) tone[i] = 0.8f + 0.2f * (float)rnd.NextDouble();
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int tx = x / 64, ty = y / 64; float fx = (x % 64) / 64f, fy = (y % 64) / 64f;
                    float grout = (fx < 0.035f || fy < 0.035f) ? 0.55f : 1f;
                    float k = tone[(tx * 7 + ty * 3) % 64] * grout * (0.95f + 0.05f * Mathf.PerlinNoise(x * 0.08f, y * 0.08f));
                    t.SetPixel(x, y, new Color(0.62f * k, 0.52f * k, 0.42f * k, 1f));
                }
            return SaveTex(t, "StoneTex");
        }

        // ---------- meshes (all with uv, colour, normals) ----------
        static Mesh Curtain(string name, float width, float height, float folds, float depth, int seed)
        {
            int nx = 48, ny = 36; var v = new List<Vector3>(); var uv = new List<Vector2>(); var col = new List<Color>(); var tri = new List<int>();
            for (int j = 0; j <= ny; j++)
                for (int i = 0; i <= nx; i++)
                {
                    float u = i / (float)nx, w = j / (float)ny;
                    float y = -height * (1f - w);                 // pivot at the rail, hanging down
                    float amp = depth * Mathf.Lerp(1.15f, 0.8f, w);
                    float z = amp * Mathf.Sin(u * Mathf.PI * 2f * folds + seed);
                    v.Add(new Vector3(u * width, y, z)); uv.Add(new Vector2(u * width * 2.2f, w * height)); col.Add(Color.white);
                }
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                    tri.AddRange(new[] { a, c, b, b, c, d });
                }
            var m = new Mesh { name = name }; m.SetVertices(v); m.SetUVs(0, uv); m.SetColors(col); m.SetTriangles(tri, 0);
            m.RecalculateNormals(); m.RecalculateBounds(); return Save(m);
        }

        static Mesh Bush(string name, int cards, Vector3 radius, int seed)
        {
            var rnd = new System.Random(seed); float R() => (float)rnd.NextDouble();
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var col = new List<Color>(); var nrm = new List<Vector3>(); var tri = new List<int>();
            for (int k = 0; k < cards; k++)
            {
                Vector3 p;
                do { p = new Vector3(R() * 2 - 1, R() * 2 - 1, R() * 2 - 1); } while (p.sqrMagnitude > 1f);
                float depth = p.magnitude;
                p = Vector3.Scale(p, radius);
                var rot = Quaternion.LookRotation(Vector3.Scale(p, new Vector3(1, 0.6f, 1)).normalized + new Vector3(R() - 0.5f, R() - 0.3f, R() - 0.5f) * 1.2f,
                                                  Quaternion.Euler(0, R() * 360f, 0) * Vector3.up);
                float s = 0.75f + 0.5f * R(); float w = 0.17f * s, h = 0.3f * s;
                var right = rot * Vector3.right * w; var up = rot * Vector3.up * h; var n = rot * Vector3.forward;
                int b = v.Count;
                v.Add(p - right - up * 0.1f); v.Add(p + right - up * 0.1f); v.Add(p + right + up * 0.9f); v.Add(p - right + up * 0.9f);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                float shade = Mathf.Lerp(0.6f, 1.1f, depth) * (0.85f + 0.3f * R());
                var c = new Color(shade, shade * (0.95f + 0.1f * R()), shade * 0.9f, 1f);
                for (int q = 0; q < 4; q++) { col.Add(c); nrm.Add((n * 0.4f + p.normalized).normalized); }
                tri.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            }
            var m = new Mesh { name = name }; m.SetVertices(v); m.SetUVs(0, uv); m.SetColors(col); m.SetNormals(nrm); m.SetTriangles(tri, 0);
            m.RecalculateBounds(); return Save(m);
        }

        static Mesh Pane(string name, float w, float h)
        {
            var m = new Mesh { name = name };
            m.SetVertices(new List<Vector3> { new Vector3(-w / 2, -h / 2, 0), new Vector3(w / 2, -h / 2, 0), new Vector3(w / 2, h / 2, 0), new Vector3(-w / 2, h / 2, 0) });
            m.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            m.SetColors(new List<Color> { Color.white, Color.white, Color.white, Color.white });
            m.SetNormals(new List<Vector3> { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            m.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0); m.RecalculateBounds(); return Save(m);
        }

        static Mesh Urn()
        {
            var prof = new[] { new Vector2(0f, 0f), new Vector2(0f, 0.2f), new Vector2(0.06f, 0.2f), new Vector2(0.1f, 0.13f), new Vector2(0.2f, 0.22f),
                new Vector2(0.38f, 0.32f), new Vector2(0.56f, 0.33f), new Vector2(0.74f, 0.24f), new Vector2(0.86f, 0.15f), new Vector2(0.93f, 0.17f),
                new Vector2(0.99f, 0.21f), new Vector2(1.02f, 0.21f), new Vector2(1.02f, 0f) };
            var m = new MeshKit().Lathe(prof, 64).Build("Urn", 40f);
            var col = new Color[m.vertexCount]; for (int i = 0; i < col.Length; i++) col[i] = Color.white;
            m.colors = col; m.uv = new Vector2[m.vertexCount];
            return Save(m);
        }

        static Mesh Plane(string name, float w, float d, float tile)
        {
            var m = new Mesh { name = name };
            m.SetVertices(new List<Vector3> { new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, 0, -d / 2), new Vector3(w / 2, 0, d / 2), new Vector3(-w / 2, 0, d / 2) });
            m.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(w / tile, 0), new Vector2(w / tile, d / tile), new Vector2(0, d / tile) });
            m.SetNormals(new List<Vector3> { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
            m.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0); m.RecalculateBounds(); return Save(m);
        }

        // ---------- objects ----------
        struct Pair { public Material dither, blend; }

        static Pair Pairs(string name, Texture tex, Color c, float smooth, float wind = 0f)
        {
            return new Pair { dither = Mat(name + "_Dither", "GameTech/DitheredLit", tex, c, smooth, wind), blend = Mat(name + "_Blend", "GameTech/BlendLit", tex, c, smooth, wind) };
        }

        static GameObject Triple(string name, Mesh mesh, Pair p, Vector3 pos, Quaternion rot, Vector3 scale, Transform parent)
        {
            GameObject Make(string suffix, Material m, int layer, ShadowCastingMode cast)
            {
                var go = SceneKit.Part(name + suffix, mesh, m, pos, rot, parent);
                go.transform.localScale = scale; go.layer = layer;
                var r = go.GetComponent<MeshRenderer>(); r.shadowCastingMode = cast; r.receiveShadows = true;
                return go;
            }
            var main = Make("", p.dither, 9, ShadowCastingMode.Off);
            Make(" (blend)", p.blend, 8, ShadowCastingMode.Off);
            Make(" (shadow)", p.dither, 0, ShadowCastingMode.ShadowsOnly);
            return main;
        }

        static GameObject Solid(string name, Mesh mesh, Material m, Vector3 pos, Vector3 scale)
        {
            var go = SceneKit.Part(name, mesh, m, pos, Quaternion.identity);
            go.transform.localScale = scale; return go;
        }

        public static void Build()
        {
            RenderSettings.skybox = null; RenderSettings.fog = false;
            SceneKit.Ambient(new Color(0.42f, 0.5f, 0.64f), new Color(0.36f, 0.33f, 0.3f), new Color(0.2f, 0.16f, 0.12f));
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            var dir = new GameObject("Director").AddComponent<DitherDirector>();

            // dither texture from the source repo: point filter, repeat, linear values, keep 470 px (no power-of-two scaling)
            string noisePath = Dir + "Textures/blue-noise.png";
            var imp = (TextureImporter)AssetImporter.GetAtPath(noisePath);
            imp.textureType = TextureImporterType.Default; imp.sRGBTexture = false; imp.mipmapEnabled = false; imp.npotScale = TextureImporterNPOTScale.None;
            imp.filterMode = FilterMode.Point; imp.wrapMode = TextureWrapMode.Repeat; imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.alphaSource = TextureImporterAlphaSource.None; imp.SaveAndReimport();
            dir.ditherTex = AssetDatabase.LoadAssetAtPath<Texture2D>(noisePath);

            // pipeline copy: 4096 shadow map, 20 m shadow distance, no MSAA
            var srcRp = (QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
            var rp = Object.Instantiate(srcRp); rp.name = "DitherPipeline";
            AssetDatabase.CreateAsset(rp, Dir + "DitherPipeline.asset");
            var so = new SerializedObject(rp);
            void SetI(string p, int v) { var pr = so.FindProperty(p); if (pr != null) pr.intValue = v; else Debug.LogWarning("[gt] no property " + p); }
            void SetF(string p, float v) { var pr = so.FindProperty(p); if (pr != null) pr.floatValue = v; else Debug.LogWarning("[gt] no property " + p); }
            SetI("m_MainLightShadowmapResolution", 4096); SetF("m_ShadowDistance", 20f); SetI("m_MSAA", 1); SetF("m_RenderScale", 1f);
            var soft = so.FindProperty("m_SoftShadowsSupported"); if (soft != null) soft.boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(rp); AssetDatabase.SaveAssets();
            dir.pipeline = rp;
            Debug.Log($"[gt] pipeline copy: shadow res {rp.mainLightShadowmapResolution}, distance {rp.shadowDistance}, msaa {rp.msaaSampleCount}");

            // opaque set
            var stone = StoneTex();
            var groundM = Mat("Ground", "Universal Render Pipeline/Lit", stone, Color.white, 0.25f);
            groundM.SetTextureScale("_BaseMap", Vector2.one);
            var plasterM = Mat("Plaster", "Universal Render Pipeline/Lit", null, new Color(0.64f, 0.5f, 0.4f), 0.1f);
            var woodM = Mat("Wood", "Universal Render Pipeline/Lit", null, new Color(0.3f, 0.18f, 0.1f), 0.35f);
            var plinthM = Mat("Plinth", "Universal Render Pipeline/Lit", null, new Color(0.78f, 0.76f, 0.72f), 0.2f);
            var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            Solid("Ground", Plane("Ground", 30f, 30f, 2.2f), groundM, new Vector3(0, 0, 2f), Vector3.one);
            Solid("Back wall", cube, plasterM, new Vector3(0, 1.9f, 4.35f), new Vector3(16f, 3.8f, 0.3f));
            Solid("Coping", cube, plinthM, new Vector3(0, 3.85f, 4.3f), new Vector3(16.2f, 0.14f, 0.45f));
            for (int i = -2; i <= 2; i++) Solid("Pilaster " + i, cube, plasterM, new Vector3(i * 3.4f + 0.4f, 1.9f, 4.12f), new Vector3(0.5f, 3.8f, 0.2f));
            Solid("Post L", cube, woodM, new Vector3(-3.2f, 1.42f, 2.0f), new Vector3(0.09f, 2.84f, 0.09f));
            Solid("Post R", cube, woodM, new Vector3(-0.6f, 1.42f, 2.0f), new Vector3(0.09f, 2.84f, 0.09f));
            Solid("Rail", cube, woodM, new Vector3(-1.9f, 2.8f, 2.0f), new Vector3(2.75f, 0.07f, 0.07f));
            Solid("Plinth", cube, plinthM, new Vector3(0.75f, 0.17f, 1.35f), new Vector3(0.95f, 0.34f, 0.95f));
            Solid("Urn base", cube, plinthM, new Vector3(2.3f, 0.06f, -0.4f), new Vector3(0.6f, 0.12f, 0.6f));
            foreach (var go in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)) go.shadowCastingMode = ShadowCastingMode.On;

            // see-through set
            var voile = VoileTex(); var leaf = LeafTex();
            var curtP = Pairs("Voile", voile, new Color(1f, 0.96f, 0.9f, 1f), 0.15f, 0.045f);
            var curtP2 = Pairs("Voile rose", voile, new Color(1f, 0.78f, 0.74f, 1f), 0.15f, 0.045f);
            var root = new GameObject("See-through").transform;
            var c1 = Triple("Curtain A", Curtain("CurtainA", 1.4f, 2.5f, 3.2f, 0.07f, 0), curtP, new Vector3(-3.12f, 2.76f, 1.92f), Quaternion.identity, Vector3.one, root);
            Triple("Curtain B", Curtain("CurtainB", 1.4f, 2.45f, 3.6f, 0.07f, 2), curtP2, new Vector3(-2.08f, 2.76f, 2.1f), Quaternion.identity, Vector3.one, root);
            dir.curtains = new GameObject("Curtain zoom point").transform; dir.curtains.position = new Vector3(-1.95f, 1.75f, 2.0f);

            var paneMesh = Pane("Pane", 1.15f, 1.15f);
            var paneCols = new[] { new Color(1f, 0.62f, 0.16f, 0.55f), new Color(0.12f, 0.72f, 0.78f, 0.55f), new Color(0.95f, 0.26f, 0.42f, 0.55f) };
            var starPos = new Vector3(0.75f, 0.92f, 1.35f);
            for (int i = 0; i < 3; i++)
                Triple("Pane " + i, paneMesh, Pairs("Pane" + i, null, paneCols[i], 0.75f), starPos, Quaternion.Euler(0, i * 60f + 15f, 0), Vector3.one, root);
            dir.star = new GameObject("Star").transform; dir.star.position = starPos;

            var leafP = Pairs("Leaves", leaf, Color.white, 0.3f);
            Triple("Bush 1", Bush("Bush1", 130, new Vector3(0.6f, 0.5f, 0.6f), 11), leafP, new Vector3(2.35f, 0.48f, 1.9f), Quaternion.identity, Vector3.one, root);
            Triple("Bush 2", Bush("Bush2", 110, new Vector3(0.55f, 0.45f, 0.55f), 23), leafP, new Vector3(3.2f, 0.42f, 3.1f), Quaternion.identity, Vector3.one * 0.9f, root);
            Triple("Bush 3", Bush("Bush3", 140, new Vector3(0.65f, 0.55f, 0.6f), 37), leafP, new Vector3(-4.3f, 0.5f, 1.5f), Quaternion.identity, Vector3.one * 1.1f, root);
            Triple("Bush 4", Bush("Bush4", 100, new Vector3(0.5f, 0.42f, 0.5f), 41), leafP, new Vector3(-0.15f, 0.4f, 3.2f), Quaternion.identity, Vector3.one * 0.8f, root);

            var urnP = Pairs("Urn", null, new Color(0.74f, 0.36f, 0.2f, 1f), 0.35f);
            var urnGo = Triple("Urn", Urn(), urnP, new Vector3(2.3f, 0.12f, -0.4f), Quaternion.identity, Vector3.one, root);
            dir.urn = urnGo.transform; dir.urnMats = new[] { urnP.dither, urnP.blend };

            SceneKit.Sun("Sun", new Vector3(30f, 32f, 0f), new Color(1f, 0.9f, 0.76f), 2.1f, true);
            SceneKit.Grade(Dir, 0.25f, -0.2f, 14f, 10f, 0.2f, 0f);
            AssetDatabase.SaveAssets();

            var sky = new Color(0.5f, 0.64f, 0.82f);
            dir.camA = SceneKit.Cam("Cam A", new Rect(0, 0, 1, 1), 40f, sky, 0, true);
            dir.camB = SceneKit.Cam("Cam B", new Rect(0, 0, 1, 1), 40f, sky, 1, true);
            dir.camC = SceneKit.Cam("Cam C", new Rect(0, 0, 1, 1), 40f, sky, 2, true);
            foreach (var c in new[] { dir.camA, dir.camB, dir.camC })
            {
                var d = c.GetUniversalAdditionalCameraData(); d.antialiasing = AntialiasingMode.None;
                c.nearClipPlane = 0.05f; c.farClipPlane = 80f;
            }
            var clear = SceneKit.Cam("Clear", new Rect(0, 0, 1, 1), 60f, Color.black, -10, false); clear.cullingMask = 0;

            dir.title = LabelSpec.Create("", 0.012f, 0.93f, 40);
            dir.l1 = LabelSpec.Create("", 0.012f, 0.058f, 34); dir.l2 = LabelSpec.Create("", 0.012f, 0.018f, 30);
            dir.m1 = LabelSpec.Create("", 0.512f, 0.058f, 34); dir.m2 = LabelSpec.Create("", 0.512f, 0.018f, 30);
            dir.r1 = LabelSpec.Create("", 0.68f, 0.058f, 34); dir.r2 = LabelSpec.Create("", 0.68f, 0.018f, 30);
            Debug.Log($"[gt] built: curtains {c1.name}, panes 3, bushes 4, urn; dither tex {dir.ditherTex?.width}");
        }
    }
}
