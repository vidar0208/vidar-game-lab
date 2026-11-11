// Procedural walk for IA (feet planted in world space, two-bone leg IK) plus the foot-to-ground force model and the
// heightmap deformation of edualvarado/unity-footprints (MIT):
//   DeformTerrainMaster.CalculateForceModel  -> weight share, momentum force = impulse / contact time
//   PhysicalFootprint.DrawFootprint          -> contact cells, pressure, Young compression, Poisson ring volume, per-frame brush
using System.Collections.Generic;
using UnityEngine;

namespace GameTech.Posts
{
    public class FootWalker : MonoBehaviour
    {
        public SoftGround[] grounds;
        public bool deform = true;
        public float mass = 40f, speed = 0.7f, period = 1.0f, duty = 0.6f, lift = 0.10f, preRoll = 3f;

        public const float L0 = 0.3f;          // originalLengthZero in the source
        public const float Reach = 0.035f;     // source ray: starts 0.04 below the surface, length 0.075
        public const float BrushExtra = 0.2f;  // DeformTerrainMaster.offset: brush runs for contactTime + 0.2 s
        public const int Ring = 2;             // neighboursSearchArea
        const float MaskCell = 0.0025f;

        public class Foot
        {
            public int side;                   // -1 left, +1 right
            public Transform up, low, foot; public float l1, l2; public Quaternion bindRot;
            public float x, phase0;
            public bool[] mask; public int mw, mh; public float mx0, mz0;   // sole bitmap relative to the foot bone
            public bool planted; public float plantZ, fromZ, toZ, fromY, toY, tContact, vTouch, vy, prevY;
            public SoftGround g; public List<int> contact = new List<int>(), ring = new List<int>();
            public int bx0, bx1, bz0, bz1, filterDone;
            public float area, dhMax, bumpH, share, weightF, momentumF, totalF, pressure, soleY, depth, pitch;
            public Vector3 ankle;
        }
        public Foot[] feet = new Foot[2];
        public float simT, rootZ;
        IARig rig; Transform hips; float ankleH, hipY = -1, worldX, swaySm; Vector3 hipMidLocal; bool ready;

        static float Frac(float x) => x - Mathf.Floor(x);

        void Start()
        {
            rig = GetComponent<IARig>(); rig.idle = false;
            worldX = transform.position.x; transform.rotation = Quaternion.identity; transform.position = new Vector3(worldX, 0, 0);
            rig.ResetPose();
            hips = rig.Bone("Hips");

            // sole = triangles of the character mesh whose three vertices lie within Reach of its lowest point
            var meshes = new List<Vector3[]>(); var indices = new List<int[]>(); float minY = float.MaxValue;
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var m = new Mesh(); smr.BakeMesh(m, true);
                var v = m.vertices; var mat = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                for (int i = 0; i < v.Length; i++) { v[i] = mat.MultiplyPoint3x4(v[i]); if (v[i].y < minY) minY = v[i].y; }
                meshes.Add(v); indices.Add(m.triangles); Destroy(m);
            }
            var tris = new List<Vector3>();
            for (int k = 0; k < meshes.Count; k++)
            {
                var v = meshes[k]; var t = indices[k];
                for (int i = 0; i < t.Length; i += 3)
                    if (v[t[i]].y < minY + Reach && v[t[i + 1]].y < minY + Reach && v[t[i + 2]].y < minY + Reach) { tris.Add(v[t[i]]); tris.Add(v[t[i + 1]]); tris.Add(v[t[i + 2]]); }
            }
            for (int s = 0; s < 2; s++)
            {
                var f = feet[s] = new Foot(); f.side = s == 0 ? -1 : 1; string n = s == 0 ? "_L" : "_R";
                f.up = rig.Bone("Upperleg" + n); f.low = rig.Bone("Lowerleg" + n); f.foot = rig.Bone("Foot" + n);
                f.l1 = Vector3.Distance(f.up.position, f.low.position); f.l2 = Vector3.Distance(f.low.position, f.foot.position);
                f.bindRot = f.foot.rotation; f.x = f.foot.position.x; f.phase0 = s == 0 ? 0f : 0.5f;
                BuildMask(f, tris);
            }
            ankleH = feet[0].foot.position.y - minY;
            hipMidLocal = (feet[0].up.position + feet[1].up.position) * 0.5f - transform.position;
            Debug.Log($"[gt69] {name}: minY {minY:F4}, ankle height {ankleH:F4}, leg {feet[0].l1:F3}+{feet[0].l2:F3}, hip mid {hipMidLocal}, foot x {feet[0].x - worldX:F3}/{feet[1].x - worldX:F3}");

            simT = 0; rootZ = -preRoll * speed;
            float half = speed * duty * period * 0.5f;
            for (int s = 0; s < 2; s++)
            {
                var f = feet[s];
                f.toZ = rootZ + half - f.phase0 * speed * period; f.fromZ = f.toZ; f.toY = f.fromY = GroundY(f.x, f.toZ);
                f.ankle = new Vector3(f.x, f.toY + ankleH, f.toZ);
            }
            ready = true;
        }

        void BuildMask(Foot f, List<Vector3> tris)
        {
            float fz = f.foot.position.z, x0 = 1e9f, x1 = -1e9f, z0 = 1e9f, z1 = -1e9f; var mine = new List<Vector2>();
            for (int i = 0; i < tris.Count; i += 3)
            {
                float cx = (tris[i].x + tris[i + 1].x + tris[i + 2].x) / 3f;
                if ((cx - worldX) * f.side <= 0) continue;
                for (int k = 0; k < 3; k++)
                {
                    var p = new Vector2(tris[i + k].x - f.x, tris[i + k].z - fz); mine.Add(p);
                    x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); z0 = Mathf.Min(z0, p.y); z1 = Mathf.Max(z1, p.y);
                }
            }
            bool fallback = mine.Count < 9;
            if (fallback) { x0 = -0.0375f; x1 = 0.0375f; z0 = -0.07f; z1 = 0.14f; }
            f.mx0 = x0; f.mz0 = z0; f.mw = Mathf.CeilToInt((x1 - x0) / MaskCell) + 1; f.mh = Mathf.CeilToInt((z1 - z0) / MaskCell) + 1;
            f.mask = new bool[f.mw * f.mh];
            if (fallback)
            {
                for (int j = 0; j < f.mh; j++) for (int i = 0; i < f.mw; i++)
                {
                    float px = x0 + (i + 0.5f) * MaskCell, pz = z0 + (j + 0.5f) * MaskCell, cz = Mathf.Clamp(pz, z0 + 0.0375f, z1 - 0.0375f);
                    f.mask[j * f.mw + i] = px * px + (pz - cz) * (pz - cz) <= 0.0375f * 0.0375f;
                }
            }
            else
            {
                for (int t = 0; t < mine.Count; t += 3)
                {
                    Vector2 a = mine[t], b = mine[t + 1], c = mine[t + 2];
                    int i0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - x0) / MaskCell)), i1 = Mathf.Min(f.mw - 1, Mathf.CeilToInt((Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - x0) / MaskCell));
                    int j0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, Mathf.Min(b.y, c.y)) - z0) / MaskCell)), j1 = Mathf.Min(f.mh - 1, Mathf.CeilToInt((Mathf.Max(a.y, Mathf.Max(b.y, c.y)) - z0) / MaskCell));
                    for (int j = j0; j <= j1; j++) for (int i = i0; i <= i1; i++)
                    {
                        var p = new Vector2(x0 + (i + 0.5f) * MaskCell, z0 + (j + 0.5f) * MaskCell);
                        float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
                        bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
                        if (!(neg && pos)) f.mask[j * f.mw + i] = true;
                    }
                }
            }
            int n = 0; foreach (bool b in f.mask) if (b) n++;
            Debug.Log($"[gt69] {name} sole {(f.side < 0 ? "L" : "R")}: {(fallback ? "FALLBACK stadium" : mine.Count / 3 + " triangles")}, {x1 - x0:F3} x {z1 - z0:F3} m, z {z0:F3}..{z1:F3}, area {n * MaskCell * MaskCell * 1e4f:F1} cm2");
        }
        static float Cross(Vector2 p, Vector2 a, Vector2 b) => (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);

        SoftGround GroundAt(float z) { foreach (var g in grounds) if (z >= g.Z0 && z < g.Z1) return g; return null; }
        float GroundY(float x, float z) { var g = GroundAt(z); return g != null ? g.HeightAt(x, z) : 0f; }

        void Touchdown(Foot f)
        {
            f.planted = true; f.tContact = 0; f.plantZ = f.toZ; f.vTouch = Mathf.Max(0f, -f.vy); f.pitch = 0;
            f.contact.Clear(); f.ring.Clear(); f.dhMax = 0; f.bumpH = 0; f.filterDone = 0; f.depth = 0;
            var g = f.g = GroundAt(f.plantZ);
            f.soleY = f.toY; f.ankle = new Vector3(f.x, f.soleY + ankleH, f.plantZ);
            if (g == null) return;
            int w = g.W;
            int ix0 = g.Ix(f.x + f.mx0), ix1 = g.Ix(f.x + f.mx0 + f.mw * MaskCell), iz0 = g.Iz(f.plantZ + f.mz0), iz1 = g.Iz(f.plantZ + f.mz0 + f.mh * MaskCell);
            int bw = ix1 - ix0 + 1 + 2 * Ring, bh = iz1 - iz0 + 1 + 2 * Ring; var cls = new byte[bw * bh];
            Vector3 o = g.Origin; float sum = 0;
            for (int iz = iz0; iz <= iz1; iz++) for (int ix = ix0; ix <= ix1; ix++)
            {
                if (!g.InRange(ix, iz)) continue;
                int mx = Mathf.FloorToInt((o.x + ix * g.cell - f.x - f.mx0) / MaskCell), mz = Mathf.FloorToInt((o.z + iz * g.cell - f.plantZ - f.mz0) / MaskCell);
                if (mx < 0 || mx >= f.mw || mz < 0 || mz >= f.mh || !f.mask[mz * f.mw + mx]) continue;
                cls[(iz - iz0 + Ring) * bw + (ix - ix0 + Ring)] = 2; f.contact.Add(iz * w + ix); sum += g.h[iz * w + ix];
            }
            // contour: free cells with a contact cell within Ring cells (source: neighboursSearchArea)
            for (int jz = 0; jz < bh; jz++) for (int jx = 0; jx < bw; jx++)
            {
                if (cls[jz * bw + jx] != 0) continue;
                bool near = false;
                for (int dz = -Ring; dz <= Ring && !near; dz++) for (int dx = -Ring; dx <= Ring; dx++)
                {
                    int qx = jx + dx, qz = jz + dz;
                    if (qx >= 0 && qx < bw && qz >= 0 && qz < bh && cls[qz * bw + qx] == 2) { near = true; break; }
                }
                int ix = jx + ix0 - Ring, iz = jz + iz0 - Ring;
                if (near && g.InRange(ix, iz)) f.ring.Add(iz * w + ix);
            }
            f.bx0 = ix0 - Ring - 1; f.bx1 = ix1 + Ring + 1; f.bz0 = iz0 - Ring - 1; f.bz1 = iz1 + Ring + 1;
            f.area = f.contact.Count * g.cell * g.cell;
            if (f.contact.Count > 0) { f.soleY = o.y + sum / f.contact.Count; f.ankle.y = f.soleY + ankleH; }
        }

        void Press(Foot f, float dt, bool single)
        {
            var g = f.g; if (g == null || f.contact.Count == 0) return;
            // force model: weight share + momentum force (impulse of the landing foot / characteristic time)
            f.weightF = mass * 9.81f * f.share;
            f.momentumF = f.tContact <= g.tau ? mass * f.share * f.vTouch / g.tau : 0f;
            f.totalF = f.weightF + f.momentumF; f.pressure = f.totalF / f.area;
            var h = g.h; var h0 = g.h0;
            if (deform && f.tContact <= g.tau + BrushExtra)
            {
                // linear compression: dh = pressure * L0 / E, the largest value so far is kept (as in the source)
                float dh = (float)(f.pressure * L0 / g.young);
                if (dh >= f.dhMax)
                {
                    f.dhMax = dh;
                    // volume pushed sideways = A * dh - (1 - 2 nu) * A * dh = 2 nu A dh, spread evenly over the ring cells
                    f.bumpH = f.ring.Count > 0 ? 2f * g.poisson * f.area * dh / (f.ring.Count * g.cell * g.cell) : 0f;
                }
                float disp = dt * f.dhMax / g.tau, bdisp = dt * f.bumpH / g.tau;
                // the source subtracts a whole step while the cell is above the limit; here the step is clamped at the limit
                foreach (int c in f.contact) if (h[c] > h0[c] - f.dhMax) h[c] = Mathf.Max(h[c] - disp, h0[c] - f.dhMax);
                if (g.bump) foreach (int c in f.ring) if (h[c] < h0[c] + f.bumpH) h[c] = Mathf.Min(h[c] + bdisp, h0[c] + f.bumpH);
                if (single && f.filterDone < g.filterIterations) { g.Blur(f.bx0 + 1, f.bx1 - 1, f.bz0 + 1, f.bz1 - 1); f.filterDone++; }
                g.MarkDirty(f.bx0, f.bx1, f.bz0, f.bz1);
            }
            float sum = 0, dep = 0;
            foreach (int c in f.contact) { sum += h[c]; dep += h0[c] - h[c]; }
            f.soleY = g.Origin.y + sum / f.contact.Count; f.depth = dep / f.contact.Count; f.ankle.y = f.soleY + ankleH;
        }

        void Step(float dt)
        {
            simT += dt; rootZ = speed * (simT - preRoll);
            float stride = speed * period;
            foreach (var f in feet)
            {
                float ph = Frac(simT / period + f.phase0);
                if (ph < duty)
                {
                    if (!f.planted) Touchdown(f);
                    f.tContact += dt;
                }
                else
                {
                    if (f.planted)
                    {
                        if (deform && f.g != null && simT > preRoll) Debug.Log($"[gt69] print T {simT - preRoll - duty * period:F2} z {f.plantZ:F2} {f.g.presetName}: peak dh {f.dhMax * 100f:F2} cm, depth at lift-off {f.depth * 100f:F2} cm, ring height {f.bumpH * 100f:F2} cm, cells {f.contact.Count}/{f.ring.Count}, touchdown speed {f.vTouch:F2} m/s");
                        f.planted = false; f.fromZ = f.plantZ; f.toZ = f.plantZ + stride; f.fromY = f.soleY; f.toY = GroundY(f.x, f.toZ);
                        f.share = f.weightF = f.momentumF = f.totalF = f.pressure = 0; f.prevY = f.soleY;
                    }
                    float s = (ph - duty) / (1f - duty);
                    float y = Mathf.Lerp(f.fromY, f.toY, Mathf.SmoothStep(0, 1, s)) + lift * Mathf.Sin(Mathf.PI * s);
                    f.pitch = Mathf.Sin(Mathf.PI * s) * Mathf.Lerp(26f, -14f, s);
                    f.vy = (y - f.prevY) / dt; f.prevY = y; f.soleY = y;
                    f.ankle = new Vector3(f.x, y + ankleH, Mathf.Lerp(f.fromZ, f.toZ, Mathf.SmoothStep(0, 1, Mathf.Clamp01((s - 0.1f) / 0.9f))));
                }
            }
            // weight share: all on the single planted foot; during double support it moves linearly to the landing foot
            // (the source reads Animator.pivotWeight here)
            Foot a = feet[0], b = feet[1]; float ds = (duty - 0.5f) * period;
            if (a.planted && b.planted)
            {
                Foot land = a.tContact < b.tContact ? a : b, trail = land == a ? b : a;
                land.share = Mathf.Clamp01(land.tContact / ds); trail.share = 1f - land.share;
            }
            else { a.share = a.planted ? 1 : 0; b.share = b.planted ? 1 : 0; }
            if (a.planted) Press(a, dt, !b.planted);
            if (b.planted) Press(b, dt, !a.planted);
        }

        void SolveLeg(Foot f)
        {
            Vector3 a = f.up.position, d = f.ankle - a;
            float dist = Mathf.Clamp(d.magnitude, 0.05f, f.l1 + f.l2 - 0.0005f);
            Vector3 dn = d.normalized;
            float cosA = Mathf.Clamp((f.l1 * f.l1 + dist * dist - f.l2 * f.l2) / (2f * f.l1 * dist), -1f, 1f);
            Vector3 pole = Vector3.forward - Vector3.Dot(Vector3.forward, dn) * dn; pole.Normalize();
            Vector3 knee = a + dn * (f.l1 * cosA) + pole * (f.l1 * Mathf.Sqrt(1f - cosA * cosA));
            f.up.rotation = Quaternion.FromToRotation(f.low.position - a, knee - a) * f.up.rotation;
            f.low.rotation = Quaternion.FromToRotation(f.foot.position - f.low.position, a + dn * dist - f.low.position) * f.low.rotation;
            f.foot.rotation = Quaternion.AngleAxis(f.pitch, Vector3.right) * f.bindRot;
        }

        void Pose()
        {
            rig.ResetPose();
            transform.position = new Vector3(worldX, 0, rootZ);
            float c = Mathf.Cos(2f * Mathf.PI * Frac(simT / period));
            rig.Turn("Hips", 4f * c, Vector3.up);
            rig.Turn("Spine", 3f, Vector3.right);
            rig.Turn("Chest", -5f * c, Vector3.up);
            rig.ArmsDown(14f * c, -14f * c);
            // hips as high as both legs can reach (gives the up-down bob and follows a foot that sinks)
            float lr = 0.975f * (feet[0].l1 + feet[0].l2), hy = 1e9f;
            foreach (var f in feet)
            {
                float dz = f.ankle.z - (rootZ + hipMidLocal.z);
                hy = Mathf.Min(hy, f.ankle.y + Mathf.Sqrt(Mathf.Max(0.01f, lr * lr - dz * dz)));
            }
            hipY = hipY < 0 ? hy : Mathf.Min(hy, Mathf.Lerp(hipY, hy, 0.5f));
            swaySm = Mathf.Lerp(swaySm, 0.012f * (feet[1].share - feet[0].share), 0.2f);
            Vector3 target = new Vector3(worldX + swaySm, hipY, rootZ + hipMidLocal.z);
            hips.position += target - (feet[0].up.position + feet[1].up.position) * 0.5f;
            foreach (var f in feet) SolveLeg(f);
        }

        void Update()
        {
            if (!ready) return;
            float target = FrameCapturer.T + preRoll; int guard = 0;
            while (simT < target - 1e-4f && guard++ < 3000) Step(1f / 30f);
        }

        void LateUpdate() { if (ready) Pose(); }
    }
}
