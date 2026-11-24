// Bounding volume hierarchy, ported from SebLague/Ray-Tracing (Assets/Scripts/Types/BVH.cs, MIT): surface area
// heuristic with up to 5 evenly spaced split tests per axis (3 for small nodes), node cost = half surface area x
// triangle count, split only when it is cheaper than the parent, max depth 32, children stored next to each other.
using System.Collections.Generic;
using UnityEngine;

namespace GameTech.Posts
{
    public class PTBVH
    {
        public struct Node { public Vector3 min, max; public int start, count; }
        struct BT { public Vector3 c, mn, mx; public int idx; }

        public readonly List<Node> nodes = new List<Node>();
        public readonly int[] order;   // triangle indices in BVH order
        BT[] tris;

        public PTBVH(Vector3[] v, int[] ind)
        {
            int n = ind.Length / 3; tris = new BT[n];
            var mn = Vector3.one * float.MaxValue; var mx = -mn;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = v[ind[i * 3]], b = v[ind[i * 3 + 1]], c = v[ind[i * 3 + 2]];
                var t = new BT { c = (a + b + c) / 3f, mn = Vector3.Min(a, Vector3.Min(b, c)), mx = Vector3.Max(a, Vector3.Max(b, c)), idx = i };
                tris[i] = t; mn = Vector3.Min(mn, t.mn); mx = Vector3.Max(mx, t.mx);
            }
            nodes.Add(new Node { min = mn, max = mx });
            Split(0, 0, n, 0);
            order = new int[n]; for (int i = 0; i < n; i++) order[i] = tris[i].idx;
        }

        static float Cost(Vector3 size, int count) => count == 0 ? 0 : (size.x * size.y + size.x * size.z + size.y * size.z) * count;

        float Evaluate(int axis, float pos, int start, int count)
        {
            Vector3 lmn = Vector3.one * float.MaxValue, lmx = -lmn, rmn = lmn, rmx = lmx; int nl = 0, nr = 0;
            for (int i = start; i < start + count; i++)
            {
                var t = tris[i];
                if (t.c[axis] < pos) { lmn = Vector3.Min(lmn, t.mn); lmx = Vector3.Max(lmx, t.mx); nl++; }
                else { rmn = Vector3.Min(rmn, t.mn); rmx = Vector3.Max(rmx, t.mx); nr++; }
            }
            return Cost(lmx - lmn, nl) + Cost(rmx - rmn, nr);
        }

        void Split(int parent, int start, int count, int depth)
        {
            var p = nodes[parent]; var size = p.max - p.min;
            float parentCost = Cost(size, count), bestCost = float.MaxValue, bestPos = 0; int bestAxis = 0;
            if (count > 1)
            {
                int maxTests = count < 10 ? 3 : 5; float maxAxis = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
                for (int axis = 0; axis < 3; axis++)
                {
                    int tests = Mathf.Clamp(Mathf.CeilToInt(size[axis] / maxAxis * maxTests), 1, maxTests);
                    for (int i = 0; i < tests; i++)
                    {
                        float pos = p.min[axis] + size[axis] * (i + 1) / (tests + 1f);
                        float c = Evaluate(axis, pos, start, count);
                        if (c < bestCost) { bestCost = c; bestPos = pos; bestAxis = axis; }
                    }
                }
            }
            if (bestCost < parentCost && depth < 32)
            {
                Vector3 lmn = Vector3.one * float.MaxValue, lmx = -lmn, rmn = lmn, rmx = lmx; int nl = 0;
                for (int i = start; i < start + count; i++)
                {
                    var t = tris[i];
                    if (t.c[bestAxis] < bestPos)
                    {
                        lmn = Vector3.Min(lmn, t.mn); lmx = Vector3.Max(lmx, t.mx);
                        var s = tris[start + nl]; tris[start + nl] = t; tris[i] = s; nl++;
                    }
                    else { rmn = Vector3.Min(rmn, t.mn); rmx = Vector3.Max(rmx, t.mx); }
                }
                int li = nodes.Count; nodes.Add(new Node { min = lmn, max = lmx, start = start }); nodes.Add(new Node { min = rmn, max = rmx, start = start + nl });
                p.start = li; p.count = 0; nodes[parent] = p;
                Split(li, start, nl, depth + 1); Split(li + 1, start + nl, count - nl, depth + 1);
            }
            else { p.start = start; p.count = count; nodes[parent] = p; }
        }
    }
}
