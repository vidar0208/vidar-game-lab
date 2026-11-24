// Turns a grid of tile indices into a small 3D town. Each cell is 1 x 1 units and each of its 3 x 3 pattern
// cells becomes a block: grass, road (a little lower), water (lower still), a wooden deck for bridges, and a house
// with a gable roof for H. Trees are placed on some grass blocks by a hash of the position.
// A cell that is not decided yet shows a dark plate. The heat map variant shows one bar per cell instead.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    public class WFCTown : MonoBehaviour
    {
        public Material[] mats;      // 0 grass, 1 road, 2 water, 3 wall, 4 roof, 5 deck, 6 leaves, 7 trunk
        public Material[] roofs;
        public Material plateMat, barMat, baseMat;

        int MX, MY;
        Renderer[] cellR, plateR, barR;
        Transform[] bars;
        MaterialPropertyBlock mpb;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor"), BaseId = Shader.PropertyToID("_BaseColor");

        const float S = 1f / 3f, HG = 0.25f, HR = 0.2f, HW = 0.08f;

        Vector3 CellPos(int i) => new Vector3(i % MX - MX * 0.5f + 0.5f, 0, (MY - 1 - i / MX) - MY * 0.5f + 0.5f);

        static float Hash(int a, int b, int c)
        {
            uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(c * 83492791);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return (h & 0xffffff) / (float)0x1000000;
        }

        void Plinth()
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(b.GetComponent<Collider>());
            b.name = "Base"; b.transform.SetParent(transform, false);
            b.transform.localPosition = new Vector3(0, -0.4f, 0); b.transform.localScale = new Vector3(MX + 0.3f, 0.8f, MY + 0.3f);
            b.GetComponent<Renderer>().sharedMaterial = baseMat;
        }

        public void BuildTiles(List<WFCTiles.Tile> tiles, int[] grid, int mx, int my, int seed)
        {
            MX = mx; MY = my; int N = MX * MY; mpb = new MaterialPropertyBlock();
            Plinth();
            cellR = new Renderer[N]; plateR = new Renderer[N];
            var cache = new Dictionary<int, Mesh>();
            for (int i = 0; i < N; i++)
            {
                var go = new GameObject("Cell " + i); go.transform.SetParent(transform, false); go.transform.localPosition = CellPos(i);
                go.AddComponent<MeshFilter>().sharedMesh = CellMesh(tiles[grid[i]].p, i, seed);
                var mr = go.AddComponent<MeshRenderer>();
                var m = (Material[])mats.Clone(); m[4] = roofs[(int)(Hash(i, 7, seed) * roofs.Length) % roofs.Length];
                mr.sharedMaterials = m; mr.shadowCastingMode = ShadowCastingMode.On;
                cellR[i] = mr;

                var pl = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(pl.GetComponent<Collider>());
                pl.name = "Plate " + i; pl.transform.SetParent(transform, false);
                pl.transform.localPosition = CellPos(i) + new Vector3(0, 0.03f, 0); pl.transform.localScale = new Vector3(0.92f, 0.06f, 0.92f);
                plateR[i] = pl.GetComponent<Renderer>(); plateR[i].sharedMaterial = plateMat;
            }
        }

        public void BuildBars(int mx, int my)
        {
            MX = mx; MY = my; int N = MX * MY; mpb = new MaterialPropertyBlock();
            Plinth();
            barR = new Renderer[N]; bars = new Transform[N];
            for (int i = 0; i < N; i++)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(b.GetComponent<Collider>());
                b.name = "Bar " + i; b.transform.SetParent(transform, false);
                bars[i] = b.transform; barR[i] = b.GetComponent<Renderer>(); barR[i].sharedMaterial = barMat;
            }
        }

        // shown[i]: cell is decided; glow[i] 0..1 lights a just-decided cell
        public void ShowTiles(bool[] shown, float[] glow)
        {
            for (int i = 0; i < cellR.Length; i++)
            {
                cellR[i].enabled = shown[i]; plateR[i].enabled = !shown[i];
                if (shown[i])
                {
                    float g = glow != null ? glow[i] : 0;
                    mpb.SetColor(EmissionId, new Color(1f, 0.85f, 0.45f) * (g * g * 2.2f));
                    cellR[i].SetPropertyBlock(mpb);
                }
            }
        }

        public static Color Ramp(float f)
        {
            Color a = new Color(0.32f, 0.08f, 0.55f), b = new Color(0.95f, 0.32f, 0.3f), c = new Color(1f, 0.88f, 0.3f);
            return f < 0.5f ? Color.Lerp(a, b, f * 2) : Color.Lerp(b, c, f * 2 - 1);
        }

        // counts: options left per cell; pick: the cell observed at this step (lit white)
        public void ShowBars(byte[] counts, int T, int pick)
        {
            for (int i = 0; i < bars.Length; i++)
            {
                int c = counts[i];
                float f = c <= 1 ? 0 : (c - 1f) / (T - 1f);
                float h = c <= 1 ? 0.06f : 0.12f + 1.5f * f;
                bars[i].localPosition = CellPos(i) + new Vector3(0, h * 0.5f, 0);
                bars[i].localScale = new Vector3(0.86f, h, 0.86f);
                if (i == pick) { h = 1.9f; bars[i].localPosition = CellPos(i) + new Vector3(0, h * 0.5f, 0); bars[i].localScale = new Vector3(0.86f, h, 0.86f); }
                mpb.SetColor(BaseId, i == pick ? Color.white : c <= 1 ? new Color(0.07f, 0.32f, 0.36f) : Ramp(f));
                mpb.SetColor(EmissionId, i == pick ? new Color(4f, 4f, 4f) : Color.black);
                barR[i].SetPropertyBlock(mpb);
            }
        }

        // ---------- mesh building ----------
        class MB
        {
            public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
            public readonly List<int>[] s;
            public MB(int subs) { s = new List<int>[subs]; for (int i = 0; i < subs; i++) s[i] = new List<int>(); }
            public void Poly(int sub, Vector3 outward, params Vector3[] p)
            {
                var nr = Vector3.Cross(p[1] - p[0], p[2] - p[0]).normalized;
                if (Vector3.Dot(nr, outward) < 0) { System.Array.Reverse(p); nr = -nr; }
                int b = v.Count;
                foreach (var q in p) { v.Add(q); n.Add(nr); }
                for (int k = 1; k + 1 < p.Length; k++) { s[sub].Add(b); s[sub].Add(b + k); s[sub].Add(b + k + 1); }
            }
            public void Box(int sub, Vector3 a, Vector3 b)
            {
                Vector3 c = (a + b) * 0.5f;
                Poly(sub, Vector3.up, new Vector3(a.x, b.y, a.z), new Vector3(a.x, b.y, b.z), new Vector3(b.x, b.y, b.z), new Vector3(b.x, b.y, a.z));
                Poly(sub, Vector3.left, new Vector3(a.x, a.y, a.z), new Vector3(a.x, b.y, a.z), new Vector3(a.x, b.y, b.z), new Vector3(a.x, a.y, b.z));
                Poly(sub, Vector3.right, new Vector3(b.x, a.y, a.z), new Vector3(b.x, b.y, a.z), new Vector3(b.x, b.y, b.z), new Vector3(b.x, a.y, b.z));
                Poly(sub, Vector3.back, new Vector3(a.x, a.y, a.z), new Vector3(b.x, a.y, a.z), new Vector3(b.x, b.y, a.z), new Vector3(a.x, b.y, a.z));
                Poly(sub, Vector3.forward, new Vector3(a.x, a.y, b.z), new Vector3(b.x, a.y, b.z), new Vector3(b.x, b.y, b.z), new Vector3(a.x, b.y, b.z));
            }
            public void Gable(int sub, Vector3 c, float w, float d, float h, bool alongX)
            {
                // base rectangle at c.y; ridge along x (or z) at c.y + h
                float hx = w * 0.5f, hz = d * 0.5f; float y0 = c.y, y1 = c.y + h;
                if (alongX)
                {
                    Vector3 a0 = c + new Vector3(-hx, 0, -hz), a1 = c + new Vector3(hx, 0, -hz), b0 = c + new Vector3(-hx, 0, hz), b1 = c + new Vector3(hx, 0, hz);
                    Vector3 r0 = new Vector3(c.x - hx, y1, c.z), r1 = new Vector3(c.x + hx, y1, c.z);
                    Poly(sub, new Vector3(0, 1, -1), a0, a1, r1, r0);
                    Poly(sub, new Vector3(0, 1, 1), b0, b1, r1, r0);
                    Poly(sub, Vector3.left, a0, b0, r0);
                    Poly(sub, Vector3.right, a1, b1, r1);
                }
                else
                {
                    Vector3 a0 = c + new Vector3(-hx, 0, -hz), a1 = c + new Vector3(-hx, 0, hz), b0 = c + new Vector3(hx, 0, -hz), b1 = c + new Vector3(hx, 0, hz);
                    Vector3 r0 = new Vector3(c.x, y1, c.z - hz), r1 = new Vector3(c.x, y1, c.z + hz);
                    Poly(sub, new Vector3(-1, 1, 0), a0, a1, r1, r0);
                    Poly(sub, new Vector3(1, 1, 0), b0, b1, r1, r0);
                    Poly(sub, Vector3.back, a0, b0, r0);
                    Poly(sub, Vector3.forward, a1, b1, r1);
                }
            }
            public void Cone(int sub, Vector3 c, float r, float h, int sides)
            {
                var top = c + Vector3.up * h;
                for (int k = 0; k < sides; k++)
                {
                    float a0 = k * Mathf.PI * 2 / sides, a1 = (k + 1) * Mathf.PI * 2 / sides;
                    var p0 = c + new Vector3(Mathf.Cos(a0) * r, 0, Mathf.Sin(a0) * r);
                    var p1 = c + new Vector3(Mathf.Cos(a1) * r, 0, Mathf.Sin(a1) * r);
                    var mid = (p0 + p1) * 0.5f - c; mid.y = r * 0.6f;
                    Poly(sub, mid, p0, p1, top);
                }
            }
            public Mesh Mesh(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(v); m.SetNormals(n); m.subMeshCount = s.Length;
                for (int i = 0; i < s.Length; i++) m.SetTriangles(s[i], i);
                m.RecalculateBounds(); return m;
            }
        }

        static bool Bridgey(char ch) => ch == 'B' || ch == 'R';

        static Mesh CellMesh(string p, int cell, int seed)
        {
            var mb = new MB(8);
            for (int sy = 0; sy < 3; sy++) for (int sx = 0; sx < 3; sx++)
            {
                char ch = p[sx + sy * 3];
                float cx = (sx - 1) * S, cz = -(sy - 1) * S, h = S * 0.5f;
                Vector3 lo = new Vector3(cx - h, 0, cz - h);
                int sub = sx + sy * 3;
                switch (ch)
                {
                    case 'G':
                    case 'H':
                        mb.Box(0, lo, new Vector3(cx + h, HG, cz + h));
                        if (ch == 'H')
                        {
                            bool tall = Hash(cell, sub, seed + 3) < 0.35f;
                            float wh = tall ? 0.3f : 0.19f;
                            mb.Box(3, new Vector3(cx - 0.11f, HG, cz - 0.1f), new Vector3(cx + 0.11f, HG + wh, cz + 0.1f));
                            mb.Gable(4, new Vector3(cx, HG + wh, cz), 0.27f, 0.25f, 0.13f, Hash(cell, sub, seed + 5) < 0.5f);
                        }
                        else if (Hash(cell, sub, seed) < 0.17f)
                        {
                            float sc = 0.8f + 0.45f * Hash(cell, sub, seed + 1);
                            float ox = (Hash(cell, sub, seed + 2) - 0.5f) * 0.12f, oz = (Hash(cell, sub, seed + 4) - 0.5f) * 0.12f;
                            var tc = new Vector3(cx + ox, HG, cz + oz);
                            mb.Box(7, tc + new Vector3(-0.018f, 0, -0.018f), tc + new Vector3(0.018f, 0.07f * sc, 0.018f));
                            mb.Cone(6, tc + new Vector3(0, 0.05f * sc, 0), 0.085f * sc, 0.26f * sc, 6);
                        }
                        break;
                    case 'R':
                        mb.Box(1, lo, new Vector3(cx + h, HR, cz + h));
                        break;
                    case 'W':
                        mb.Box(2, lo, new Vector3(cx + h, HW, cz + h));
                        break;
                    case 'B':
                        mb.Box(2, lo, new Vector3(cx + h, HW, cz + h));
                        bool vertical = (sy > 0 && Bridgey(p[sx + (sy - 1) * 3])) || (sy < 2 && Bridgey(p[sx + (sy + 1) * 3]));
                        float ax = vertical ? 0.36f * S : h, az = vertical ? h : 0.36f * S;
                        mb.Box(5, new Vector3(cx - ax, 0.16f, cz - az), new Vector3(cx + ax, 0.21f, cz + az));
                        break;
                }
            }
            return mb.Mesh("Cell " + cell);
        }
    }
}
