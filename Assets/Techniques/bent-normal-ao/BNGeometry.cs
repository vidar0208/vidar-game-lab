// Procedural props for the bent normal post, built from parametric patches so every surface point has a unique
// place in one texture atlas (the bake needs unwrapped UVs, like the source's UV-space data cache). Each patch knows
// its exact position and normal for any (u, v), so the position/normal cache is filled from the shapes instead of
// rasterising the meshes in UV space. Patches are shelf-packed into the atlas at one texel density (the floor at a
// lower one), with a 2-texel border that repeats the edge so bilinear sampling never reads an empty texel.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameTech.Posts
{
    public class BNPatch
    {
        public int group; public float density = 1f, w, h; public int nu = 1, nv = 1;
        public Func<float, float, Vector3> P, N;
        public int x0, y0, tw, th;
    }

    public static class BNGeo
    {
        public const int Pad = 2;
        public static readonly Vector3 BowlCenter = new Vector3(-1.05f, 0f, -0.45f);

        static void Quad(List<BNPatch> L, int g, Vector3 o, Vector3 a, Vector3 b, Vector3 n, float dens = 1f)
        {
            L.Add(new BNPatch { group = g, density = dens, w = a.magnitude, h = b.magnitude, P = (u, v) => o + a * u + b * v, N = (u, v) => n });
        }

        // axis-aligned box; the bottom face is left out (every box here stands on something)
        static void Box(List<BNPatch> L, int g, Vector3 c, Vector3 s)
        {
            var e = s * 0.5f;
            Vector3 X = new Vector3(s.x, 0, 0), Y = new Vector3(0, s.y, 0), Z = new Vector3(0, 0, s.z);
            Quad(L, g, c + new Vector3(e.x, -e.y, -e.z), Z, Y, Vector3.right);
            Quad(L, g, c + new Vector3(-e.x, -e.y, -e.z), Z, Y, Vector3.left);
            Quad(L, g, c + new Vector3(-e.x, e.y, -e.z), X, Z, Vector3.up);
            Quad(L, g, c + new Vector3(-e.x, -e.y, e.z), X, Y, Vector3.forward);
            Quad(L, g, c + new Vector3(-e.x, -e.y, -e.z), X, Y, Vector3.back);
        }

        static void Sphere(List<BNPatch> L, int g, Vector3 c, float r)
        {
            Func<float, float, Vector3> D = (u, v) => { float ph = u * 2f * Mathf.PI, th = Mathf.PI * (1f - v); return new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph)); };
            L.Add(new BNPatch { group = g, w = 2f * Mathf.PI * r, h = Mathf.PI * r, nu = 128, nv = 64, P = (u, v) => c + D(u, v) * r, N = D });
        }

        // ring lying flat (axis +y): R = ring radius, r = tube radius
        static void Torus(List<BNPatch> L, int g, Vector3 c, float R, float r)
        {
            Func<float, float, Vector3> D = (u, v) => { float a = u * 2f * Mathf.PI, b = v * 2f * Mathf.PI; return new Vector3(Mathf.Cos(b) * Mathf.Cos(a), Mathf.Sin(b), Mathf.Cos(b) * Mathf.Sin(a)); };
            L.Add(new BNPatch
            {
                group = g, w = 2f * Mathf.PI * (R + r), h = 2f * Mathf.PI * r, nu = 160, nv = 48,
                P = (u, v) => { float a = u * 2f * Mathf.PI; return c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * R + D(u, v) * r; }, N = D
            });
        }

        // surface of revolution around +y from a dense (r, y) profile, walked so that (dy, -dr) points outward
        static void Lathe(List<BNPatch> L, int g, Vector3 c, List<Vector2> prof, int nu, int nv)
        {
            int n = prof.Count; var len = new float[n]; var nr = new Vector2[n]; float rmax = 0;
            for (int i = 1; i < n; i++) len[i] = len[i - 1] + Vector2.Distance(prof[i], prof[i - 1]);
            for (int i = 0; i < n; i++)
            {
                var t = prof[Mathf.Min(i + 1, n - 1)] - prof[Mathf.Max(i - 1, 0)];
                nr[i] = new Vector2(t.y, -t.x).normalized; rmax = Mathf.Max(rmax, prof[i].x);
            }
            float total = len[n - 1];
            Func<float, int> Seg = s => { float d = s * total; int lo = 0, hi = n - 1; while (hi - lo > 1) { int m = (lo + hi) / 2; if (len[m] <= d) lo = m; else hi = m; } return lo; };
            Func<float, int, float> Frac = (s, i) => Mathf.Clamp01((s * total - len[i]) / Mathf.Max(1e-6f, len[i + 1] - len[i]));
            L.Add(new BNPatch
            {
                group = g, w = 2f * Mathf.PI * rmax, h = total, nu = nu, nv = nv,
                P = (u, v) => { int i = Seg(v); var p = Vector2.Lerp(prof[i], prof[i + 1], Frac(v, i)); float a = u * 2f * Mathf.PI; return c + new Vector3(p.x * Mathf.Cos(a), p.y, p.x * Mathf.Sin(a)); },
                N = (u, v) => { int i = Seg(v); var q = Vector2.Lerp(nr[i], nr[i + 1], Frac(v, i)).normalized; float a = u * 2f * Mathf.PI; return new Vector3(q.x * Mathf.Cos(a), q.y, q.x * Mathf.Sin(a)); }
            });
        }

        static List<Vector2> BowlProfile()
        {
            var p = new List<Vector2>();
            for (int i = 0; i <= 20; i++) p.Add(new Vector2(0.11f * i / 20f, 0f));                                   // foot, outward
            for (int i = 1; i <= 80; i++) { float t = i / 80f; p.Add(new Vector2(0.11f + 0.19f * (1f - (1f - t) * (1f - t)), 0.24f * t)); } // outer wall, up
            for (int i = 1; i < 24; i++) { float f = Mathf.PI * i / 24f; p.Add(new Vector2(0.29f + 0.01f * Mathf.Cos(f), 0.24f + 0.01f * Mathf.Sin(f))); } // rim
            for (int i = 80; i >= 0; i--) { float t = i / 80f; p.Add(new Vector2(0.10f + 0.18f * (1f - (1f - t) * (1f - t)), 0.035f + 0.205f * t)); } // inner wall, down
            for (int i = 1; i <= 20; i++) p.Add(new Vector2(0.10f * (1f - i / 20f), 0.035f));                        // inner floor, to the centre
            return p;
        }

        // groups: 0 floor, 1 stone base (4 slabs with 4 cm gaps), 2 chrome, 3 copper.
        // Spheres rest on rings (the gap between them is the cavity), a small chrome sphere sits in the copper bowl.
        public static readonly Vector3 PairA = new Vector3(-0.12f, 0.43f, 0.1f), PairB = new Vector3(0.52f, 0.41f, 0.2f);
        public static List<BNPatch> Patches()
        {
            var L = new List<BNPatch>();
            Quad(L, 0, new Vector3(-2.1f, 0f, -2.3f), new Vector3(4.4f, 0, 0), new Vector3(0, 0, 4.4f), Vector3.up, 0.3f);
            for (int i = 0; i < 4; i++) Box(L, 1, new Vector3(-0.39f + i * 0.36f, 0.18f, 0.1f), new Vector3(0.32f, 0.36f, 0.9f));
            Torus(L, 3, PairA, 0.22f, 0.07f);
            Sphere(L, 2, PairA + new Vector3(0, Mathf.Sqrt(0.27f * 0.27f - 0.22f * 0.22f), 0), 0.2f);
            Torus(L, 2, PairB, 0.15f, 0.05f);
            Sphere(L, 3, PairB + new Vector3(0, Mathf.Sqrt(0.19f * 0.19f - 0.15f * 0.15f), 0), 0.14f);
            Lathe(L, 3, BowlCenter, BowlProfile(), 160, 220);
            Sphere(L, 2, BowlCenter + new Vector3(0, 0.135f, 0), 0.1f);
            return L;
        }

        // shelf packing at one texel density; returns texels per metre
        public static float Pack(List<BNPatch> L, int size)
        {
            float area = 0; foreach (var p in L) area += p.w * p.h * p.density * p.density;
            float D = Mathf.Sqrt(0.62f * size * size / area);
            for (int tries = 0; tries < 40; tries++, D *= 0.96f)
            {
                foreach (var p in L) { p.tw = Mathf.Max(2, Mathf.CeilToInt(p.w * D * p.density)); p.th = Mathf.Max(2, Mathf.CeilToInt(p.h * D * p.density)); }
                var order = new List<BNPatch>(L); order.Sort((a, b) => b.th.CompareTo(a.th));
                int x = 0, y = 0, rowH = 0; bool ok = true;
                foreach (var p in order)
                {
                    int fw = p.tw + 2 * Pad, fh = p.th + 2 * Pad;
                    if (fw > size) { ok = false; break; }
                    if (x + fw > size) { x = 0; y += rowH; rowH = 0; }
                    if (y + fh > size) { ok = false; break; }
                    p.x0 = x + Pad; p.y0 = y + Pad; x += fw; rowH = Mathf.Max(rowH, fh);
                }
                if (ok) return D;
            }
            throw new Exception("atlas packing failed");
        }

        public static Mesh BuildMesh(List<BNPatch> L, int group, int size)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            foreach (var p in L)
            {
                if (p.group != group) continue;
                int b0 = v.Count;
                for (int j = 0; j <= p.nv; j++) for (int i = 0; i <= p.nu; i++)
                {
                    float u = (float)i / p.nu, w = (float)j / p.nv;
                    v.Add(p.P(u, w)); n.Add(p.N(u, w)); uv.Add(new Vector2((p.x0 + u * p.tw) / size, (p.y0 + w * p.th) / size));
                }
                for (int j = 0; j < p.nv; j++) for (int i = 0; i < p.nu; i++)
                {
                    int a = b0 + j * (p.nu + 1) + i, c = a + p.nu + 1;
                    foreach (var t in new[] { new[] { a, a + 1, c }, new[] { c, a + 1, c + 1 } })
                    {
                        // keep the triangle facing its normal (front face when cross(b - a, c - a) . n > 0)
                        var cr = Vector3.Cross(v[t[1]] - v[t[0]], v[t[2]] - v[t[0]]);
                        if (Vector3.Dot(cr, n[t[0]] + n[t[1]] + n[t[2]]) < 0) tri.AddRange(new[] { t[0], t[2], t[1] }); else tri.AddRange(t);
                    }
                }
            }
            var m = new Mesh { name = "BN group " + group, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateBounds();
            return m;
        }

        // position (w = 1 where a patch owns the texel) and normal caches, like the source's BNAO_DataCacher output
        public static void FillCache(List<BNPatch> L, int size, Color[] pos, Color[] nrm)
        {
            foreach (var p in L)
                for (int py = p.y0 - Pad; py < p.y0 + p.th + Pad; py++)
                    for (int px = p.x0 - Pad; px < p.x0 + p.tw + Pad; px++)
                    {
                        float u = Mathf.Clamp01((px + 0.5f - p.x0) / p.tw), w = Mathf.Clamp01((py + 0.5f - p.y0) / p.th);
                        var q = p.P(u, w); var nn = p.N(u, w); int k = py * size + px;
                        pos[k] = new Color(q.x, q.y, q.z, 1); nrm[k] = new Color(nn.x, nn.y, nn.z, 0);
                    }
        }
    }
}
