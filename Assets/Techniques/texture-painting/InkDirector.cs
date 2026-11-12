// Timeline for the texture painting post. Generates the base textures and brushes, fires ink blobs on fixed
// ballistic paths, ray-casts each blob step against the painted objects (mesh colliders, layer 9) and paints at
// RaycastHit.textureCoord. The left half of the split screen (layer 8) gets a flat decal quad per hit instead.
using System.Collections.Generic;
using UnityEngine;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(200)]
    public class InkDirector : MonoBehaviour
    {
        public Camera camL, camR, camFull, camRT;
        public InkSurface[] surfaces;                 // painted objects (layer 9)
        public Renderer[] plain;                      // their plain twins (layer 8), same order
        public string[] kinds;                        // base texture per surface
        public InkSurface sphere;
        public Renderer rtView;
        public Transform launcher;
        public Material blobMat, decalMat;
        public LabelSpec stampLabel, totalLabel;

        public const float Gravity = -9.81f, BlobSpeed = 13f, DecalLife = 2f;
        // HeightFluid settings on the drip panel (source defaults: 1, 1, 0.01; flowing force is 0..1 in the source)
        public const float EaseOfDripping = 0.4f, FlowingForce = 4f, HorizontalSpread = 0.05f;
        public static readonly Color[] Inks = { new Color(1f, 0.08f, 0.52f, 0.9f), new Color(0.0f, 0.82f, 1f, 0.9f), new Color(1f, 0.82f, 0.0f, 0.9f) };

        struct Shot { public float t; public Vector3 from, vel; public Color col; public int brush; public float size, rot; }
        class Blob { public Vector3 p, v; public Color col; public int brush; public float size, rot; public GameObject go; }
        class Decal { public GameObject go; public float born; public MaterialPropertyBlock mpb; }

        readonly List<Shot> shots = new List<Shot>();
        readonly List<Blob> blobs = new List<Blob>();
        readonly List<Decal> decals = new List<Decal>();
        InkBrush[] brushes; Mesh blobMesh, decalQuad;
        int next; float lastT = -1;
        public static int PanelStamps;

        // ---------------------------------------------------------------- textures
        static float Hs(int x, int y, int s) { unchecked { uint h = (uint)(x * 374761393 + y * 668265263 + s * 1442695041); h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16; return (h & 0xFFFFFF) / 16777216f; } }
        static float Nz(float x, float y, int s)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y); float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            return Mathf.Lerp(Mathf.Lerp(Hs(ix, iy, s), Hs(ix + 1, iy, s), fx), Mathf.Lerp(Hs(ix, iy + 1, s), Hs(ix + 1, iy + 1, s), fx), fy);
        }
        static float Fbm(float x, float y, int s) => 0.5f * Nz(x, y, s) + 0.25f * Nz(x * 2.03f, y * 2.03f, s + 1) + 0.125f * Nz(x * 4.1f, y * 4.1f, s + 2) + 0.125f * Nz(x * 8.3f, y * 8.3f, s + 3);

        delegate void Sample(float xm, float ym, out Color c, out float h);
        static void Gen(int W, int Hh, float mu, float mv, Sample f, out Texture2D col, out Texture2D nor, string name)
        {
            var cp = new Color32[W * Hh]; var hp = new float[W * Hh];
            for (int y = 0; y < Hh; y++) for (int x = 0; x < W; x++)
            {
                f((x + 0.5f) / W * mu, (y + 0.5f) / Hh * mv, out var c, out var h);
                cp[y * W + x] = c; hp[y * W + x] = h;
            }
            var np = new Color32[W * Hh]; float dx = mu / W, dy = mv / Hh;
            for (int y = 0; y < Hh; y++) for (int x = 0; x < W; x++)
            {
                float gx = (hp[y * W + Mathf.Min(x + 1, W - 1)] - hp[y * W + Mathf.Max(x - 1, 0)]) / (2 * dx);
                float gy = (hp[Mathf.Min(y + 1, Hh - 1) * W + x] - hp[Mathf.Max(y - 1, 0) * W + x]) / (2 * dy);
                var n = new Vector3(-gx, -gy, 1).normalized;
                np[y * W + x] = new Color32((byte)((n.x * 0.5f + 0.5f) * 255), (byte)((n.y * 0.5f + 0.5f) * 255), (byte)((n.z * 0.5f + 0.5f) * 255), 255);
            }
            col = new Texture2D(W, Hh, TextureFormat.RGBA32, true, false) { name = name, wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
            col.SetPixels32(cp); col.Apply(true);
            nor = new Texture2D(W, Hh, TextureFormat.RGBA32, true, true) { name = name + "_n", wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
            nor.SetPixels32(np); nor.Apply(true);
        }
        static Color G(float g, float smooth) => new Color(g, g, g, smooth);

        static void BaseFor(string kind, out Texture2D c, out Texture2D n)
        {
            switch (kind)
            {
                case "wall":
                case "panel":
                {   // concrete blocks 40 x 20 cm, running bond, 1 cm mortar
                    bool wall = kind == "wall"; float mu = wall ? 8f : 1.8f, mv = wall ? 4.5f : 1.8f; int W = wall ? 2048 : 1024, H = wall ? 1152 : 1024;
                    Gen(W, H, mu, mv, (float x, float y, out Color col, out float h) =>
                    {
                        int row = Mathf.FloorToInt(y / 0.2f); float bx = x + (row % 2) * 0.2f;
                        int bid = Mathf.FloorToInt(bx / 0.4f);
                        float fx = bx - bid * 0.4f, fy = y - row * 0.2f;
                        float m = Mathf.Min(Mathf.Min(fx, 0.4f - fx), Mathf.Min(fy, 0.2f - fy));
                        float mortar = Mathf.Clamp01((0.006f - m) / 0.004f);
                        float grain = Fbm(x * 9f, y * 9f, 3) - 0.5f, block = Hs(bid, row, 11) - 0.5f;
                        float g = Mathf.Lerp(0.30f + 0.05f * block + 0.06f * grain, 0.20f, mortar);
                        col = G(g, Mathf.Lerp(0.22f, 0.12f, mortar));
                        h = (1 - mortar) * 0.004f + grain * 0.0012f;
                    }, out c, out n, kind);
                    return;
                }
                case "floor":
                    Gen(2048, 2048, 8f, 8f, (float x, float y, out Color col, out float h) =>
                    {
                        float jx = Mathf.Abs(x - Mathf.Round(x / 2f) * 2f), jy = Mathf.Abs(y - Mathf.Round(y / 2f) * 2f);
                        float joint = Mathf.Clamp01((0.006f - Mathf.Min(jx, jy)) / 0.003f);
                        float f = Fbm(x * 1.5f, y * 1.5f, 5) - 0.5f, fine = Fbm(x * 14f, y * 14f, 6) - 0.5f;
                        col = G(Mathf.Lerp(0.20f + 0.06f * f + 0.03f * fine, 0.10f, joint), 0.32f + 0.1f * f);
                        h = -joint * 0.003f + fine * 0.0008f;
                    }, out c, out n, kind);
                    return;
                case "plaster":
                    Gen(1024, 512, 3.46f, 1.73f, (float x, float y, out Color col, out float h) =>
                    {
                        float f = Fbm(x * 6f, y * 6f, 8) - 0.5f;
                        col = new Color(0.66f + 0.04f * f, 0.64f + 0.04f * f, 0.60f + 0.04f * f, 0.3f); h = f * 0.0008f;
                    }, out c, out n, kind);
                    return;
                case "metal":
                    Gen(1024, 512, 2.51f, 1.0f, (float x, float y, out Color col, out float h) =>
                    {
                        float rib = Mathf.Max(Mathf.Clamp01(1 - Mathf.Abs(y - 0.3f) / 0.02f), Mathf.Clamp01(1 - Mathf.Abs(y - 0.7f) / 0.02f));
                        float f = Fbm(x * 5f, y * 5f, 9) - 0.5f;
                        col = new Color(0.07f + 0.02f * f, 0.20f + 0.03f * f, 0.22f + 0.03f * f, 0.5f + 0.1f * f); h = rib * 0.006f + f * 0.0005f;
                    }, out c, out n, kind);
                    return;
                default: // stone
                    Gen(512, 1024, 1.76f, 1.8f, (float x, float y, out Color col, out float h) =>
                    {
                        float f = Fbm(x * 4f, y * 4f, 12) - 0.5f, fine = Fbm(x * 20f, y * 20f, 13) - 0.5f;
                        col = new Color(0.52f + 0.08f * f, 0.49f + 0.07f * f, 0.45f + 0.06f * f, 0.2f); h = f * 0.004f + fine * 0.001f;
                    }, out c, out n, kind);
                    return;
            }
        }

        // ---------------------------------------------------------------- set-up
        void Start()
        {
            var cache = new Dictionary<string, Texture2D[]>();
            for (int i = 0; i < surfaces.Length; i++)
            {
                if (!cache.TryGetValue(kinds[i], out var tx)) { BaseFor(kinds[i], out var c, out var n); tx = new[] { c, n }; cache[kinds[i]] = tx; }
                var s = surfaces[i];
                s.baseColor = tx[0]; s.baseNormal = tx[1];
                var r = s.GetComponent<Renderer>(); s.target = new Material(r.sharedMaterial); r.sharedMaterial = s.target;
                s.target.SetTexture("_BaseMap", tx[0]); s.target.SetTexture("_BumpMap", tx[1]);
                s.Init();
                if (plain[i] != null) { var m = new Material(plain[i].sharedMaterial); m.SetTexture("_BaseMap", tx[0]); m.SetTexture("_BumpMap", tx[1]); m.EnableKeyword("_NORMALMAP"); plain[i].sharedMaterial = m; }
            }
            brushes = new[] { InkBrush.Make(1), InkBrush.Make(2), InkBrush.Make(3), InkBrush.Make(4) };
            decalMat.SetTexture("_BaseMap", brushes[0].alpha);
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere); blobMesh = tmp.GetComponent<MeshFilter>().sharedMesh; Destroy(tmp);
            tmp = GameObject.CreatePrimitive(PrimitiveType.Quad); decalQuad = tmp.GetComponent<MeshFilter>().sharedMesh; Destroy(tmp);
            rtView.sharedMaterial.SetTexture("_BaseMap", sphere.MainRT);
            Plan();
            Debug.Log($"[gt] ink: {surfaces.Length} surfaces, {shots.Count} shots planned");
            foreach (var s in surfaces) Debug.Log($"[gt] ink RT {s.name}: {s.MainRT.width} x {s.MainRT.height}" + (s.HeightRT ? $", height {s.HeightRT.width} x {s.HeightRT.height}" : ""));
        }

        // ballistic launch from a to b in time tau
        static Vector3 Aim(Vector3 a, Vector3 b) { float tau = Vector3.Distance(a, b) / BlobSpeed; return (b - a) / tau - 0.5f * new Vector3(0, Gravity, 0) * tau; }

        void Add(System.Random rnd, float t, Vector3 from, Vector3 to, Color col, float size)
        {
            shots.Add(new Shot { t = t, from = from, vel = Aim(from, to), col = col, brush = rnd.Next(4), size = size, rot = (float)rnd.NextDouble() * 360f });
        }
        float R(System.Random r, float a, float b) => a + (b - a) * (float)r.NextDouble();

        void Plan()
        {
            var rnd = new System.Random(79);
            Vector3 tip = launcher.position + launcher.forward * 0.5f;
            Vector3 sph = sphere.transform.position;
            // A: 0.2 - 8 s, the main scene, 7 shots a second
            for (float t = 0.2f; t < 8f; t += 1f / 7f)
            {
                int pick = rnd.Next(100); Vector3 to;
                if (pick < 30) to = new Vector3(R(rnd, -3.2f, 3.2f), R(rnd, 0.4f, 3.6f), -3f);
                else if (pick < 52) to = new Vector3(R(rnd, -3f, 3f), 0f, R(rnd, -2.6f, 2.0f));
                else if (pick < 68) to = sph + Quaternion.Euler(R(rnd, -50, 40), R(rnd, -60, 60), 0) * (tip - sph).normalized * 0.5f;
                else if (pick < 84) to = new Vector3(1.3f, R(rnd, 0.15f, 0.9f), -1.6f) + Quaternion.Euler(0, R(rnd, -60, 60), 0) * new Vector3(tip.x - 1.3f, 0, tip.z + 1.6f).normalized * 0.4f;
                else to = new Vector3(0.1f, R(rnd, 0.2f, 1.7f), -2.0f) + new Vector3(tip.x - 0.1f, 0, tip.z + 2f).normalized * 0.2f;
                Add(rnd, t, tip, to, Inks[rnd.Next(3)], R(rnd, 0.22f, 0.38f));
            }
            // B: 8.3 - 12.7 s, the same 22 shots on each of the three panels, one ink colour (the drip keeps one colour)
            var panelShots = new List<(float t, Vector3 from, Vector3 off, int brush, float size, float rot)>();
            for (int i = 0; i < 22; i++)
                panelShots.Add((8.3f + i * 0.2f, new Vector3(R(rnd, -0.5f, 0.5f), R(rnd, 0.4f, 0.9f), 3.4f), new Vector3(R(rnd, -0.65f, 0.65f), R(rnd, -0.3f, 0.7f), 0), rnd.Next(4), R(rnd, 0.17f, 0.27f), R(rnd, 0, 360)));
            foreach (var px in new[] { 27.9f, 30f, 32.1f })
                foreach (var p in panelShots)
                {
                    var c = new Vector3(px, 1.15f, -3f);
                    shots.Add(new Shot { t = p.t, from = c + p.from, vel = Aim(c + p.from, c + p.off), col = Inks[0], brush = p.brush, size = p.size, rot = p.rot });
                }
            // C: 15.8 - 20.8 s, the sphere, some shots right on its UV seam (local +x)
            Vector3 seam = sphere.transform.right;
            for (int i = 0; i < 20; i++)
            {
                float t = 15.8f + i * 0.25f;
                Vector3 d = (i % 4 == 1) ? Quaternion.AngleAxis(R(rnd, -6, 6), Vector3.up) * Quaternion.AngleAxis(R(rnd, -35, 35), sphere.transform.forward) * seam
                                         : Quaternion.Euler(R(rnd, -45, 35), R(rnd, -55, 55), 0) * seam;
                Add(rnd, t, tip, sph + d * 0.55f, Inks[(i + 1) % 3], R(rnd, 0.16f, 0.26f));
            }
            shots.Sort((a, b) => a.t.CompareTo(b.t));
        }

        // ---------------------------------------------------------------- simulation
        void Fire(Shot s)
        {
            var go = new GameObject("blob"); go.AddComponent<MeshFilter>().sharedMesh = blobMesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = blobMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var mpb = new MaterialPropertyBlock(); mpb.SetColor("_BaseColor", (Color)(new Vector4(s.col.r, s.col.g, s.col.b, 1) * 3f)); mr.SetPropertyBlock(mpb);
            go.transform.localScale = Vector3.one * 0.07f; go.transform.position = s.from;
            blobs.Add(new Blob { p = s.from, v = s.vel, col = s.col, brush = s.brush, size = s.size, rot = s.rot, go = go });
        }

        void Hit(Blob b, RaycastHit h)
        {
            var surf = h.collider.GetComponent<InkSurface>();
            if (surf == null) return;
            surf.Paint(brushes[b.brush], h.textureCoord, b.col, b.size, b.rot);
            if (h.point.x > 20f) { PanelStamps++; return; }
            // the left twin: one flat quad at the hit, along the hit normal
            var go = new GameObject("decal"); go.layer = 8;
            go.AddComponent<MeshFilter>().sharedMesh = decalQuad;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = decalMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var mpb = new MaterialPropertyBlock(); mpb.SetTexture("_BaseMap", brushes[b.brush].alpha); mpb.SetColor("_BaseColor", b.col); mpb.SetFloat("_Fade", 1); mr.SetPropertyBlock(mpb);
            go.transform.position = h.point + h.normal * 0.003f;
            go.transform.rotation = Quaternion.LookRotation(-h.normal) * Quaternion.Euler(0, 0, b.rot);
            go.transform.localScale = Vector3.one * b.size * 2f;
            decals.Add(new Decal { go = go, born = FrameCapturer.T, mpb = mpb });
        }

        static float S(float a, float b, float t) { float x = Mathf.Clamp01((t - a) / (b - a)); return x * x * (3 - 2 * x); }
        static void Look(Camera c, Vector3 from, Vector3 at) { c.transform.position = from; c.transform.LookAt(at); }

        void Update()
        {
            float t = FrameCapturer.T, dt = Time.deltaTime;
            if (t > lastT)
            {
                while (next < shots.Count && shots[next].t <= t) Fire(shots[next++]);
                for (int i = blobs.Count - 1; i >= 0; i--)
                {
                    var b = blobs[i]; var np = b.p + b.v * dt; b.v += new Vector3(0, Gravity, 0) * dt;
                    if (Physics.Raycast(b.p, np - b.p, out var h, (np - b.p).magnitude, 1 << 9)) { Hit(b, h); Destroy(b.go); blobs.RemoveAt(i); continue; }
                    b.p = np; b.go.transform.position = np;
                    if (np.y < -2f) { Destroy(b.go); blobs.RemoveAt(i); }
                }
                foreach (var s in surfaces) s.Step();
                lastT = t;
            }
            for (int i = decals.Count - 1; i >= 0; i--)
            {
                float age = t - decals[i].born, fade = 1 - Mathf.Clamp01((age - DecalLife + 0.5f) / 0.5f);
                if (fade <= 0) { Destroy(decals[i].go); decals.RemoveAt(i); continue; }
                decals[i].mpb.SetFloat("_Fade", fade); decals[i].go.GetComponent<MeshRenderer>().SetPropertyBlock(decals[i].mpb);
            }

            bool split = t < 8f;
            camL.enabled = camR.enabled = split; camFull.enabled = !split; camRT.enabled = t >= 15.5f && t < 21.5f;
            if (t < 8f)
            {
                float k = S(0, 8, t);
                var from = Vector3.Lerp(new Vector3(1.2f, 1.9f, 6.4f), new Vector3(-0.5f, 1.55f, 5.3f), k);
                Look(camL, from, new Vector3(0f, 0.95f, -1.4f)); Look(camR, from, new Vector3(0f, 0.95f, -1.4f));
            }
            else if (t < 15.5f)
            {
                float k = S(8, 15.5f, t);
                Look(camFull, Vector3.Lerp(new Vector3(30f, 1.3f, 1.55f), new Vector3(30f, 1.2f, 1.25f), k), new Vector3(30f, 1.05f, -3f));
            }
            else if (t < 21.5f)
            {
                float k = S(15.5f, 21.5f, t);
                Vector3 c = sphere.transform.position, d = sphere.transform.right;
                var f = -d; var right = Vector3.Cross(Vector3.up, f).normalized;
                var from = c + Quaternion.AngleAxis(Mathf.Lerp(-12f, 8f, k), Vector3.up) * d * 2.1f + new Vector3(0, 0.3f, 0);
                Look(camFull, from, c + right * 0.62f + new Vector3(0, 0.05f, 0));
            }
            else
            {
                float k = S(21.5f, 25.5f, t);
                Look(camFull, Vector3.Lerp(new Vector3(0.6f, 2.0f, 5.0f), new Vector3(0.1f, 2.6f, 6.8f), k), new Vector3(0f, 0.85f, -1.5f));
            }
            if (stampLabel != null) stampLabel.text = $"Stamps this frame: {InkSurface.ThisFrame}   total: {InkSurface.StampsTotal}";
            if (totalLabel != null) totalLabel.text = $"{InkSurface.StampsTotal} stamps, none removed: the ink lives in the textures";
        }
    }
}
