// Small procedural mesh builder for post scenes. Every shape it makes is a closed (watertight) solid.
// Winding is fixed automatically from an outward hint, normals are smoothed by angle.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameTech
{
    public class MeshKit
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<int> t = new List<int>();
        public int TriangleCount => t.Count / 3;

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            var n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-14f) return;                       // degenerate
            if (Vector3.Dot(n, outward) < 0) { var s = b; b = c; c = s; }
            int i = v.Count; v.Add(a); v.Add(b); v.Add(c); t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward) { Tri(a, b, c, outward); Tri(a, c, d, outward); }

        public MeshKit Box(Vector3 c, Vector3 size, bool inward = false)
        {
            Vector3 h = size * 0.5f; float s = inward ? -1 : 1;
            for (int ax = 0; ax < 3; ax++)
                for (int sg = -1; sg <= 1; sg += 2)
                {
                    Vector3 n = Vector3.zero; n[ax] = sg;
                    Vector3 u = Vector3.zero; u[(ax + 1) % 3] = h[(ax + 1) % 3];
                    Vector3 w = Vector3.zero; w[(ax + 2) % 3] = h[(ax + 2) % 3];
                    Vector3 o = c + n * h[ax];
                    Quad(o - u - w, o + u - w, o + u + w, o - u + w, n * s);
                }
            return this;
        }

        // Solid of revolution around the local Y axis. profile = closed loop of (axial y, radius). seg = angular steps.
        public MeshKit Lathe(Vector2[] profile, int seg = 48, float yOffset = 0)
        {
            int m = profile.Length; float area = 0;
            for (int k = 0; k < m; k++) { var a = profile[k]; var b = profile[(k + 1) % m]; area += a.x * b.y - b.x * a.y; }
            float sign = area > 0 ? 1 : -1;
            for (int k = 0; k < m; k++)
            {
                var a = profile[k]; var b = profile[(k + 1) % m];
                Vector2 n2 = sign * new Vector2(b.y - a.y, -(b.x - a.x));   // outward in (axial, radial)
                for (int j = 0; j < seg; j++)
                {
                    float t0 = j * Mathf.PI * 2 / seg, t1 = (j + 1) * Mathf.PI * 2 / seg, tm = (t0 + t1) * 0.5f;
                    Vector3 P(Vector2 p, float th) => new Vector3(p.y * Mathf.Cos(th), p.x + yOffset, p.y * Mathf.Sin(th));
                    var hint = new Vector3(n2.y * Mathf.Cos(tm), n2.x, n2.y * Mathf.Sin(tm));
                    Quad(P(a, t0), P(b, t0), P(b, t1), P(a, t1), hint);
                }
            }
            return this;
        }

        // Extruded ring around local Y: outer radius is a function of angle (gear teeth), inner radius constant.
        public MeshKit Annulus(Func<float, float> rOut, float rIn, float y0, float y1, int n)
        {
            for (int j = 0; j < n; j++)
            {
                float t0 = j * Mathf.PI * 2 / n, t1 = (j + 1) * Mathf.PI * 2 / n, tm = (t0 + t1) * 0.5f;
                Vector3 D(float th) => new Vector3(Mathf.Cos(th), 0, Mathf.Sin(th));
                Vector3 o0 = D(t0) * rOut(t0), o1 = D(t1) * rOut(t1), i0 = D(t0) * rIn, i1 = D(t1) * rIn;
                Vector3 a = Vector3.up * y0, b = Vector3.up * y1;
                Vector3 wall = Vector3.Cross(o1 - o0, Vector3.up); if (Vector3.Dot(wall, D(tm)) < 0) wall = -wall;
                Quad(o0 + a, o1 + a, o1 + b, o0 + b, wall);            // outer wall
                Quad(i0 + a, i1 + a, i1 + b, i0 + b, -D(tm));          // bore
                Quad(i0 + b, i1 + b, o1 + b, o0 + b, Vector3.up);      // top
                Quad(i0 + a, i1 + a, o1 + a, o0 + a, Vector3.down);    // bottom
            }
            return this;
        }

        public static Func<float, float> GearProfile(int teeth, float rRoot, float rTip, float phase)
        {
            return th =>
            {
                float x = teeth * (th - phase) / (Mathf.PI * 2);
                float f = Mathf.Abs(x - Mathf.Floor(x + 0.5f)) * 2;     // 0 at tooth centre, 1 at gap centre
                return Mathf.Lerp(rTip, rRoot, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.34f, 0.66f, f)));
            };
        }

        // Rectangular face (in the plane x = const, spanning y and z) with one round hole. Used for housing walls.
        public void HoleFaceX(float x, float cy, float cz, float halfY, float halfZ, float hy, float hz, float r, Vector3 outward, int n = 48)
        {
            var ang = new List<float>();
            for (int j = 0; j < n; j++) ang.Add(j * Mathf.PI * 2 / n);
            foreach (var sy in new[] { -1f, 1f }) foreach (var sz in new[] { -1f, 1f })
            { float a = Mathf.Atan2(cz + sz * halfZ - hz, cy + sy * halfY - hy); if (a < 0) a += Mathf.PI * 2; ang.Add(a); }
            ang.Sort();
            Vector3 C(float a) => new Vector3(x, hy + Mathf.Cos(a) * r, hz + Mathf.Sin(a) * r);
            Vector3 R(float a)
            {
                float dy = Mathf.Cos(a), dz = Mathf.Sin(a), best = 1e9f;
                if (dy > 1e-6f) best = Mathf.Min(best, (cy + halfY - hy) / dy); else if (dy < -1e-6f) best = Mathf.Min(best, (cy - halfY - hy) / dy);
                if (dz > 1e-6f) best = Mathf.Min(best, (cz + halfZ - hz) / dz); else if (dz < -1e-6f) best = Mathf.Min(best, (cz - halfZ - hz) / dz);
                return new Vector3(x, hy + dy * best, hz + dz * best);
            }
            for (int j = 0; j < ang.Count; j++)
            {
                float a0 = ang[j], a1 = ang[(j + 1) % ang.Count];
                Quad(C(a0), C(a1), R(a1), R(a0), outward);
            }
        }

        // Round bore along X between x0 and x1 (surface faces the axis).
        public void BoreX(float x0, float x1, float hy, float hz, float r, int n = 48)
        {
            for (int j = 0; j < n; j++)
            {
                float a0 = j * Mathf.PI * 2 / n, a1 = (j + 1) * Mathf.PI * 2 / n, am = (a0 + a1) * 0.5f;
                Vector3 P(float x, float a) => new Vector3(x, hy + Mathf.Cos(a) * r, hz + Mathf.Sin(a) * r);
                Quad(P(x0, a0), P(x1, a0), P(x1, a1), P(x0, a1), new Vector3(0, -Mathf.Cos(am), -Mathf.Sin(am)));
            }
        }

        public Mesh Build(string name, float smoothDeg = 35f)
        {
            int n = v.Count; var fn = new Vector3[n / 3];
            for (int f = 0; f < fn.Length; f++) fn[f] = Vector3.Cross(v[3 * f + 1] - v[3 * f], v[3 * f + 2] - v[3 * f]);
            var map = new Dictionary<Vector3Int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                var key = Vector3Int.RoundToInt(v[i] * 20000f);
                if (!map.TryGetValue(key, out var l)) { l = new List<int>(); map[key] = l; }
                l.Add(i);
            }
            var normals = new Vector3[n]; float cosT = Mathf.Cos(smoothDeg * Mathf.Deg2Rad);
            for (int i = 0; i < n; i++)
            {
                Vector3 mine = fn[i / 3].normalized, sum = Vector3.zero;
                foreach (int j in map[Vector3Int.RoundToInt(v[i] * 20000f)])
                    if (Vector3.Dot(mine, fn[j / 3].normalized) >= cosT) sum += fn[j / 3];
                normals[i] = sum.sqrMagnitude > 0 ? sum.normalized : mine;
            }
            var mesh = new Mesh { name = name };
            if (n > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.SetNormals(normals); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
