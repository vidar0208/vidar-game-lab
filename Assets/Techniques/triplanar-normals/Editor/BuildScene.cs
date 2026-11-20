// Builds the triplanar normal mapping scene from code: an undulating ground, three noise-displaced rocks, a standing
// torus and a rounded block, all without texture UVs, all sharing one procedural cobble normal map (rgb normal +
// height in alpha). Mesh tangents follow a top-down (x, z) mapping; only the naive method uses them.
// Before building, the five blends are evaluated on the CPU on 40,000 points of a sphere and compared with a
// surface-gradient reference; the numbers go to the log and to the labels.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GameTech.Posts
{
    public static class TriplanarScene
    {
        const string Dir = "Assets/Techniques/triplanar-normals/";
        const int TexN = 512;
        const float TexScale = 1.0f;     // repeats per metre
        const float BumpK = 14f;         // normal map strength (slope per texel of height)
        static Color[] texPx;

        // ---------- procedural cobble normal map ----------
        static float Hash(int x, int y, int s) { unchecked { int h = x * 374761393 + y * 668265263 + s * 2147483647; h = (h ^ (h >> 13)) * 1274126177; return ((h ^ (h >> 16)) & 0xffffff) / (float)0xffffff; } }

        static Texture2D CobbleTex()
        {
            int cells = 7; float cell = TexN / (float)cells;
            var pts = new Vector2[cells, cells]; var tone = new float[cells, cells];
            for (int i = 0; i < cells; i++)
                for (int j = 0; j < cells; j++)
                {
                    pts[i, j] = new Vector2((i + 0.18f + 0.64f * Hash(i, j, 1)) * cell, (j + 0.18f + 0.64f * Hash(i, j, 2)) * cell);
                    tone[i, j] = 0.82f + 0.18f * Hash(i, j, 3);
                }
            var h = new float[TexN * TexN];
            for (int y = 0; y < TexN; y++)
                for (int x = 0; x < TexN; x++)
                {
                    int ci = (int)(x / cell), cj = (int)(y / cell);
                    float f1 = 1e9f, f2 = 1e9f; float t1 = 1f;
                    for (int di = -1; di <= 1; di++)
                        for (int dj = -1; dj <= 1; dj++)
                        {
                            int ii = ci + di, jj = cj + dj;
                            int wi = (ii % cells + cells) % cells, wj = (jj % cells + cells) % cells;
                            var p = pts[wi, wj] + new Vector2((ii - wi), (jj - wj)) * cell;
                            float d = Vector2.Distance(p, new Vector2(x + 0.5f, y + 0.5f));
                            if (d < f1) { f2 = f1; f1 = d; t1 = tone[wi, wj]; } else if (d < f2) f2 = d;
                        }
                    float e = Mathf.Clamp01((f2 - f1) / (0.42f * cell));
                    float dome = 1f - (1f - e) * (1f - e);
                    float fine = 0.035f * Mathf.PerlinNoise(x * 0.11f, y * 0.11f);   // tiles: 512 * 0.11 is not whole, kept small
                    h[y * TexN + x] = dome * t1 + fine * dome;
                }
            var t = new Texture2D(TexN, TexN, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8, name = "CobbleTri" };
            texPx = new Color[TexN * TexN];
            for (int y = 0; y < TexN; y++)
                for (int x = 0; x < TexN; x++)
                {
                    float dx = (h[y * TexN + (x + 1) % TexN] - h[y * TexN + (x + TexN - 1) % TexN]) * 0.5f;
                    float dy = (h[((y + 1) % TexN) * TexN + x] - h[((y + TexN - 1) % TexN) * TexN + x]) * 0.5f;
                    var n = new Vector3(-dx * BumpK, -dy * BumpK, 1f).normalized;
                    texPx[y * TexN + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, h[y * TexN + x]);
                }
            t.SetPixels(texPx); t.Apply(true);
            AssetDatabase.CreateAsset(t, Dir + "CobbleTri.asset");
            return t;
        }

        // ---------- CPU copy of the five blends (same maths as TriplanarCommon.hlsl), for measurement ----------
        static Vector4 Samp(float u, float v)
        {
            int x = ((Mathf.FloorToInt(u * TexN) % TexN) + TexN) % TexN, y = ((Mathf.FloorToInt(v * TexN) % TexN) + TexN) % TexN;
            var c = texPx[y * TexN + x];
            return new Vector4(c.r * 2 - 1, c.g * 2 - 1, c.b * 2 - 1, c.a);
        }
        static Vector3 V(Vector4 v) => new Vector3(v.x, v.y, v.z);
        static Vector3 Rnm(Vector3 n1, Vector3 n2) { n1.z += 1; n2.x = -n2.x; n2.y = -n2.y; return n1 * Vector3.Dot(n1, n2) / n1.z - n2; }
        static Vector3 Perp(Vector3 v, Vector3 n) => v - n * Vector3.Dot(v, n);

        static Vector3 Blend(int mode, Vector3 P, Vector3 N, Vector3 T, float w, out Vector3 reference)
        {
            var tb = new Vector3(Mathf.Pow(Mathf.Abs(N.x), 4), Mathf.Pow(Mathf.Abs(N.y), 4), Mathf.Pow(Mathf.Abs(N.z), 4));
            tb /= Mathf.Max(tb.x + tb.y + tb.z, 0.0001f);
            var s = new Vector3(N.x < 0 ? -1 : 1, N.y < 0 ? -1 : 1, N.z < 0 ? -1 : 1);
            Vector2 uvX = new Vector2(P.z, P.y) * TexScale, uvY = new Vector2(P.x, P.z) * TexScale, uvZ = new Vector2(P.x, P.y) * TexScale;
            // reference: the slope of each projection laid onto the surface (surface gradient), blended by the same weights
            {
                Vector3 g = Vector3.zero;
                var a = Samp(uvX.x * s.x, uvX.y); g += tb.x * Perp(a.x / a.z * s.x * Vector3.forward + a.y / a.z * Vector3.up, N);
                var b = Samp(uvY.x * s.y, uvY.y); g += tb.y * Perp(b.x / b.z * s.y * Vector3.right + b.y / b.z * Vector3.forward, N);
                var c = Samp(uvZ.x * -s.z, uvZ.y); g += tb.z * Perp(c.x / c.z * -s.z * Vector3.right + c.y / c.z * Vector3.up, N);
                reference = (N + g).normalized;
            }
            if (mode == 1)
            {
                var tn = V(Samp(uvX.x, uvX.y)) * tb.x + V(Samp(uvY.x, uvY.y)) * tb.y + V(Samp(uvZ.x, uvZ.y)) * tb.z;
                var B = Vector3.Cross(N, T) * w;
                return (T * tn.x + B * tn.y + N * tn.z).normalized;
            }
            uvX.x *= s.x; uvY.x *= s.y; uvZ.x *= -s.z;
            Vector3 nX = V(Samp(uvX.x, uvX.y)), nY = V(Samp(uvY.x, uvY.y)), nZ = V(Samp(uvZ.x, uvZ.y));
            nX.x *= s.x; nY.x *= s.y; nZ.x *= -s.z;
            if (mode == 2) { nX = new Vector3(nX.x + N.z, nX.y + N.y, N.x); nY = new Vector3(nY.x + N.x, nY.y + N.z, N.y); nZ = new Vector3(nZ.x + N.x, nZ.y + N.y, N.z); }
            else if (mode == 3) { nX = new Vector3(nX.x + N.z, nX.y + N.y, nX.z * N.x); nY = new Vector3(nY.x + N.x, nY.y + N.z, nY.z * N.y); nZ = new Vector3(nZ.x + N.x, nZ.y + N.y, nZ.z * N.z); }
            else if (mode == 4)
            {
                nX = Rnm(new Vector3(N.z, N.y, Mathf.Abs(N.x)), nX); nY = Rnm(new Vector3(N.x, N.z, Mathf.Abs(N.y)), nY); nZ = Rnm(new Vector3(N.x, N.y, Mathf.Abs(N.z)), nZ);
                nX.z *= s.x; nY.z *= s.y; nZ.z *= s.z;
            }
            else
            {
                return (new Vector3(0, nX.y, nX.x) * tb.x + new Vector3(nY.x, 0, nY.y) * tb.y + new Vector3(nZ.x, nZ.y, 0) * tb.z + N).normalized;
            }
            return (new Vector3(nX.z, nX.y, nX.x) * tb.x + new Vector3(nY.x, nY.z, nY.y) * tb.y + new Vector3(nZ.x, nZ.y, nZ.z) * tb.z).normalized;
        }

        // tangent frame of a top-down (x, z) UV mapping: tangent along +x, bitangent along +z
        static void TopDownTangent(Vector3 n, out Vector3 t, out float w)
        {
            t = Perp(Vector3.right, n);
            if (t.sqrMagnitude < 1e-6f) t = Perp(Vector3.forward, n);
            t.Normalize();
            w = Vector3.Dot(Vector3.Cross(n, t), Vector3.forward) < 0 ? -1f : 1f;
        }

        static float[] Measure(out string log)
        {
            var rnd = new System.Random(99); var C = new Vector3(0.37f, 1.2f, -0.61f);
            var names = new[] { "", "naive", "UDN", "Whiteout", "RNM", "GPU Gems 3" };
            var err = new double[6]; var flip = new int[6]; int count = 0, slopeCount = 0; double bump = 0;
            var res = new float[12];
            for (int k = 0; k < 40000; k++)
            {
                float z = (float)rnd.NextDouble() * 2 - 1, a = (float)rnd.NextDouble() * Mathf.PI * 2, r = Mathf.Sqrt(1 - z * z);
                var N = new Vector3(r * Mathf.Cos(a), z, r * Mathf.Sin(a));
                if (Mathf.Abs(N.x) > 0.98f) continue;                      // top-down tangent undefined there
                TopDownTangent(N, out var T, out var w);
                var P = C + N * 0.8f;
                count++;
                Blend(4, P, N, T, w, out var refN);
                var refPert = Perp(refN, N); bool sloped = refPert.magnitude > 0.15f; if (sloped) slopeCount++;
                bump += Vector3.Angle(refN, N);
                for (int m = 1; m <= 5; m++)
                {
                    var n = Blend(m, P, N, T, w, out _);
                    err[m] += Vector3.Angle(n, refN);
                    if (sloped && Vector3.Dot(Perp(n, N), refPert) < 0) flip[m]++;
                }
            }
            log = $"[gt] blend test on a sphere, {count} points ({slopeCount} with bump slope > 0.15), mean bump {bump / count:F1} deg:";
            for (int m = 1; m <= 5; m++)
            {
                res[m] = (float)(err[m] / count); res[6 + m] = 100f * flip[m] / slopeCount;
                log += $" {names[m]} err {res[m]:F2} deg, flipped {res[6 + m]:F1}%;";
            }
            return res;
        }

        // ---------- meshes (no texture UVs used; uv0 = top-down x, z only so the tangents have a source) ----------
        static Mesh Finish(Mesh m, List<Vector3> v, List<Vector3> n, List<int> tri)
        {
            var tan = new List<Vector4>(); var uv = new List<Vector2>();
            for (int i = 0; i < v.Count; i++) { TopDownTangent(n[i], out var t, out var w); tan.Add(new Vector4(t.x, t.y, t.z, w)); uv.Add(new Vector2(v[i].x, v[i].z)); }
            m.indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            m.SetVertices(v); m.SetNormals(n); m.SetTangents(tan); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, Dir + "Mesh_" + m.name + ".asset");
            return m;
        }

        static float Noise3(Vector3 p, int seed)
        {
            return (Mathf.PerlinNoise(p.x + seed * 1.7f, p.y + 3.1f) + Mathf.PerlinNoise(p.y + seed * 2.3f, p.z + 7.7f) + Mathf.PerlinNoise(p.z + seed * 0.9f, p.x + 11.3f)) / 3f;
        }

        static Mesh Rock(string name, int seed, Vector3 scale)
        {
            // icosphere, 4 subdivisions
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3> { new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t), new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1) };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
            var f = new List<int> { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8, 3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            for (int s = 0; s < 4; s++)
            {
                var cache = new Dictionary<long, int>(); var nf = new List<int>();
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (cache.TryGetValue(key, out int idx)) return idx;
                    v.Add(((v[a] + v[b]) * 0.5f).normalized); cache[key] = v.Count - 1; return v.Count - 1;
                }
                for (int i = 0; i < f.Count; i += 3)
                {
                    int a = f[i], b = f[i + 1], c = f[i + 2], ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    nf.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                f = nf;
            }
            for (int i = 0; i < v.Count; i++)
            {
                var d = v[i];
                float r = 1f + 0.32f * (Noise3(d * 1.6f, seed) - 0.5f) + 0.12f * (Noise3(d * 4.1f, seed + 5) - 0.5f);
                var p = Vector3.Scale(d * r, scale);
                float floor = -0.42f * scale.y;
                if (p.y < floor) p.y = floor + (p.y - floor) * 0.15f;
                v[i] = p;
            }
            var m = new Mesh { name = name }; m.SetVertices(v); m.SetTriangles(f, 0); m.RecalculateNormals();
            var n = new List<Vector3>(); m.GetNormals(n);
            // icosphere winding above is clockwise seen from outside in Unity's left-handed space? check and flip if normals point inward
            if (Vector3.Dot(n[0], v[0]) < 0) { for (int i = 0; i < f.Count; i += 3) { int tmp = f[i + 1]; f[i + 1] = f[i + 2]; f[i + 2] = tmp; } m.SetTriangles(f, 0); m.RecalculateNormals(); m.GetNormals(n); }
            return Finish(m, v, n, f);
        }

        static Mesh Torus(string name, float R, float r)
        {
            int nu = 128, nv = 64; var v = new List<Vector3>(); var n = new List<Vector3>(); var tri = new List<int>();
            for (int i = 0; i <= nu; i++)
                for (int j = 0; j <= nv; j++)
                {
                    float a = i / (float)nu * Mathf.PI * 2, b = j / (float)nv * Mathf.PI * 2;
                    var c = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);           // standing ring in the x-y plane
                    var dir = c * Mathf.Cos(b) + Vector3.forward * Mathf.Sin(b);
                    v.Add(c * R + dir * r); n.Add(dir);
                }
            for (int i = 0; i < nu; i++)
                for (int j = 0; j < nv; j++)
                {
                    int a = i * (nv + 1) + j, b = a + nv + 1;
                    tri.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
                }
            var m = new Mesh { name = name }; m.SetVertices(v); m.SetTriangles(tri, 0); m.RecalculateNormals();
            var test = new List<Vector3>(); m.GetNormals(test);
            if (Vector3.Dot(test[5], n[5]) < 0) for (int i = 0; i < tri.Count; i += 3) { int tmp = tri[i + 1]; tri[i + 1] = tri[i + 2]; tri[i + 2] = tmp; }
            return Finish(m, v, n, tri);
        }

        // rounded block: superellipsoid |x|^p + |y|^p + |z|^p = 1, analytic normals (no seams between the six faces)
        static Mesh Block(string name, float p, Vector3 half)
        {
            int g = 48; var v = new List<Vector3>(); var n = new List<Vector3>(); var tri = new List<int>();
            var axes = new[] { (Vector3.right, Vector3.up, Vector3.forward), (Vector3.left, Vector3.up, Vector3.back), (Vector3.up, Vector3.forward, Vector3.right),
                               (Vector3.down, Vector3.forward, Vector3.left), (Vector3.forward, Vector3.up, Vector3.left), (Vector3.back, Vector3.up, Vector3.right) };
            foreach (var (fn, fu, fr) in axes)
            {
                int b0 = v.Count;
                for (int j = 0; j <= g; j++)
                    for (int i = 0; i <= g; i++)
                    {
                        float x = i / (float)g * 2 - 1, y = j / (float)g * 2 - 1;
                        var d = (fn + fr * Mathf.Tan(x * Mathf.PI / 4) + fu * Mathf.Tan(y * Mathf.PI / 4)).normalized;
                        float s = Mathf.Pow(Mathf.Pow(Mathf.Abs(d.x), p) + Mathf.Pow(Mathf.Abs(d.y), p) + Mathf.Pow(Mathf.Abs(d.z), p), -1f / p);
                        var q = d * s;
                        var nn = new Vector3(Mathf.Sign(q.x) * Mathf.Pow(Mathf.Abs(q.x), p - 1) / half.x, Mathf.Sign(q.y) * Mathf.Pow(Mathf.Abs(q.y), p - 1) / half.y, Mathf.Sign(q.z) * Mathf.Pow(Mathf.Abs(q.z), p - 1) / half.z);
                        v.Add(Vector3.Scale(q, half)); n.Add(nn.normalized);
                    }
                for (int j = 0; j < g; j++)
                    for (int i = 0; i < g; i++)
                    {
                        int a = b0 + j * (g + 1) + i, b = a + 1, c = a + g + 1, d = c + 1;
                        tri.AddRange(new[] { a, c, b, b, c, d });
                    }
            }
            var m = new Mesh { name = name }; m.indexFormat = IndexFormat.UInt32; m.SetVertices(v); m.SetTriangles(tri, 0); m.RecalculateNormals();
            var test = new List<Vector3>(); m.GetNormals(test);
            for (int k = 0; k < 6; k++)
            {
                int vi = k * (g + 1) * (g + 1) + (g / 2) * (g + 1) + g / 2;
                if (Vector3.Dot(test[vi], n[vi]) < 0)
                {
                    int t0 = k * g * g * 6;
                    for (int i = t0; i < t0 + g * g * 6; i += 3) { int tmp = tri[i + 1]; tri[i + 1] = tri[i + 2]; tri[i + 2] = tmp; }
                }
            }
            return Finish(m, v, n, tri);
        }

        public static float GroundH(float x, float z) => 0.16f * Mathf.Sin(0.55f * x + 0.3f) * Mathf.Cos(0.42f * z) + 0.09f * Mathf.Sin(0.9f * z + 0.7f * x);

        static Mesh Ground(string name, float size, int res)
        {
            var v = new List<Vector3>(); var tri = new List<int>();
            for (int j = 0; j <= res; j++)
                for (int i = 0; i <= res; i++)
                {
                    float x = (i / (float)res - 0.5f) * size, z = (j / (float)res - 0.5f) * size;
                    v.Add(new Vector3(x, GroundH(x, z), z));
                }
            for (int j = 0; j < res; j++)
                for (int i = 0; i < res; i++)
                {
                    int a = j * (res + 1) + i, b = a + 1, c = a + res + 1, d = c + 1;
                    tri.AddRange(new[] { a, c, b, b, c, d });
                }
            var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 }; m.SetVertices(v); m.SetTriangles(tri, 0); m.RecalculateNormals();
            var n = new List<Vector3>(); m.GetNormals(n);
            if (n[res + 2].y < 0) { for (int i = 0; i < tri.Count; i += 3) { int tmp = tri[i + 1]; tri[i + 1] = tri[i + 2]; tri[i + 2] = tmp; } m.SetTriangles(tri, 0); m.RecalculateNormals(); m.GetNormals(n); }
            return Finish(m, v, n, tri);
        }

        static Material Mat(string name, Texture tex, Color gap, Color top, float smooth)
        {
            var m = new Material(Shader.Find("GameTech/TriplanarLit"));
            m.SetTexture("_TriTex", tex); m.SetColor("_BaseColor", gap); m.SetColor("_BaseColor2", top);
            m.SetFloat("_Smoothness", smooth); m.SetFloat("_TexScale", TexScale);
            AssetDatabase.CreateAsset(m, Dir + name + ".mat"); return m;
        }

        public static void Build()
        {
            RenderSettings.skybox = null; RenderSettings.fog = false;
            SceneKit.Ambient(new Color(0.2f, 0.25f, 0.34f), new Color(0.12f, 0.12f, 0.14f), new Color(0.06f, 0.055f, 0.05f));
            var dir = new GameObject("Director").AddComponent<TriplanarDirector>();

            var tex = CobbleTex();
            var res = Measure(out var log); Debug.Log(log);
            dir.err = res;

            // pipeline copy: 4096 shadow map, 25 m shadows, no MSAA (cameras use SMAA)
            var srcRp = (QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
            var rp = Object.Instantiate(srcRp); rp.name = "TriplanarPipeline";
            AssetDatabase.CreateAsset(rp, Dir + "TriplanarPipeline.asset");
            var so = new SerializedObject(rp);
            void SetI(string p, int v) { var pr = so.FindProperty(p); if (pr != null) pr.intValue = v; else Debug.LogWarning("[gt] no property " + p); }
            void SetF(string p, float v) { var pr = so.FindProperty(p); if (pr != null) pr.floatValue = v; else Debug.LogWarning("[gt] no property " + p); }
            SetI("m_MainLightShadowmapResolution", 4096); SetF("m_ShadowDistance", 25f); SetI("m_MSAA", 1); SetF("m_RenderScale", 1f);
            var soft = so.FindProperty("m_SoftShadowsSupported"); if (soft != null) soft.boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(rp); AssetDatabase.SaveAssets();
            dir.pipeline = rp;

            var rockM = Mat("Rock", tex, new Color(0.22f, 0.2f, 0.18f), new Color(0.46f, 0.43f, 0.39f), 0.55f);
            var slateM = Mat("Slate", tex, new Color(0.17f, 0.19f, 0.22f), new Color(0.36f, 0.4f, 0.45f), 0.6f);
            var groundM = Mat("Ground", tex, new Color(0.12f, 0.11f, 0.1f), new Color(0.27f, 0.25f, 0.22f), 0.45f);

            SceneKit.Part("Ground", Ground("Ground", 36f, 220), groundM, Vector3.zero, Quaternion.identity);
            Vector3 OnGround(float x, float z, float lift) => new Vector3(x, GroundH(x, z) + lift, z);
            var r1 = SceneKit.Part("Rock big", Rock("RockBig", 3, new Vector3(1.25f, 0.95f, 1.05f)), rockM, OnGround(-1.75f, 0.7f, 0.36f), Quaternion.Euler(0, 25, 0));
            SceneKit.Part("Rock small", Rock("RockSmall", 11, new Vector3(0.55f, 0.42f, 0.5f)), rockM, OnGround(-0.55f, -0.35f, 0.16f), Quaternion.Euler(0, 70, 0));
            SceneKit.Part("Rock flat", Rock("RockFlat", 23, new Vector3(0.8f, 0.45f, 0.7f)), rockM, OnGround(-3.1f, -0.4f, 0.17f), Quaternion.Euler(0, -30, 0));
            var torus = SceneKit.Part("Torus", Torus("Torus", 0.72f, 0.3f), slateM, OnGround(0.55f, 0.85f, 1.0f), Quaternion.Euler(0, 28, 0));
            var block = SceneKit.Part("Block", Block("Block", 7f, new Vector3(0.62f, 0.62f, 0.62f)), slateM, OnGround(2.2f, 0.25f, 0.82f), Quaternion.Euler(22, 38, 16));
            foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)) { mr.shadowCastingMode = ShadowCastingMode.On; mr.receiveShadows = true; }
            dir.rock = r1.transform; dir.torus = torus.transform; dir.block = block.transform;

            SceneKit.Sun("Sun", new Vector3(17f, -62f, 0f), new Color(1f, 0.86f, 0.7f), 2.7f, true);
            SceneKit.Grade(Dir, 0.35f, 0.35f, 14f, 6f, 0.28f, 0.08f);
            AssetDatabase.SaveAssets();

            var bg = new Color(0.035f, 0.042f, 0.055f);
            dir.camA = SceneKit.Cam("Cam A", new Rect(0, 0, 1, 1), 40f, bg, 0, true);
            dir.camB = SceneKit.Cam("Cam B", new Rect(0, 0, 1, 1), 40f, bg, 1, true);
            dir.camC = SceneKit.Cam("Cam C", new Rect(0, 0, 1, 1), 40f, bg, 2, true);
            foreach (var c in new[] { dir.camA, dir.camB, dir.camC }) { c.nearClipPlane = 0.05f; c.farClipPlane = 80f; }
            var clear = SceneKit.Cam("Clear", new Rect(0, 0, 1, 1), 60f, Color.black, -10, false); clear.cullingMask = 0;

            dir.title = LabelSpec.Create("", 0.012f, 0.93f, 40);
            dir.l1 = LabelSpec.Create("", 0.012f, 0.058f, 34); dir.l2 = LabelSpec.Create("", 0.012f, 0.018f, 30);
            dir.m1 = LabelSpec.Create("", 0.512f, 0.058f, 34); dir.m2 = LabelSpec.Create("", 0.512f, 0.018f, 30);
            dir.r1 = LabelSpec.Create("", 0.68f, 0.058f, 34); dir.r2 = LabelSpec.Create("", 0.68f, 0.018f, 30);
            Debug.Log($"[gt] built: ground, 3 rocks, torus, block; tex {tex.width} mips {tex.mipmapCount}; scale {TexScale}/m, bump k {BumpK}");
        }
    }
}
