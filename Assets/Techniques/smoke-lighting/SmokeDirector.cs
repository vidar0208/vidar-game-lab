// Timeline for the smoke lighting post. Bakes the flipbook atlases at start (own bake, see SmokeBake), runs a small
// deterministic CPU particle system, rebuilds the billboard mesh every frame, moves the lamp, sets the lighting
// workflow per camera and writes the live read-outs of the debug panel.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(200)]
    public class SmokeDirector : MonoBehaviour
    {
        public Camera camL, camR, camA, camB, camC, camDbg, camPanel;
        public Shader bakeShader;
        public MeshFilter smokeMesh;
        public Light lamp;
        public LabelSpec[] tileLabels;      // Right, Left, Top, Bottom, Back, Front
        public LabelSpec dirLabel, distLabel, intLabel;

        public const int AtlasSize = 2048;
        public const float Sigma = 5f, Directionality = 0.9f, SoftDistance = 0.6f, EmitRate = 12f;
        public static readonly Vector3 Centre = new Vector3(0.15f, 2.05f, 0f);
        static readonly Vector3 Emitter = new Vector3(0f, 0.25f, 0f);

        struct P { public Vector3 pos, vel; public float age, life, size0, size1, roll, rollSpeed, phase, start, mirror; }
        readonly List<P> ps = new List<P>(128);
        readonly List<int> order = new List<int>(128);
        readonly System.Random rng = new System.Random(76);
        float emitAcc, simTime;
        Mesh mesh;
        RenderTexture rlt, bbf, nrm;
        Texture2D ramp;
        bool logged;

        readonly List<Vector3> vv = new List<Vector3>(); readonly List<Vector3> vn = new List<Vector3>(); readonly List<Vector4> vt = new List<Vector4>();
        readonly List<Vector2> u0 = new List<Vector2>(); readonly List<Vector4> u1 = new List<Vector4>(); readonly List<Vector3> u2 = new List<Vector3>();
        readonly List<Color> vc = new List<Color>(); readonly List<int> tri = new List<int>();

        void OnEnable() { RenderPipelineManager.beginCameraRendering += Begin; }
        void OnDisable() { RenderPipelineManager.beginCameraRendering -= Begin; }
        void Begin(ScriptableRenderContext ctx, Camera cam)
        {
            float mode = (cam == camL || cam == camA) ? 0f : cam == camB ? 1f : 2f;
            Shader.SetGlobalFloat("_Smoke_Mode", mode);
        }

        RenderTexture Bake(Material m, int pass, string name)
        {
            var rt = new RenderTexture(AtlasSize, AtlasSize, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            { name = name, useMipMap = true, autoGenerateMips = true, filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp };
            rt.Create();
            Graphics.Blit(null, rt, m, pass);
            return rt;
        }

        void Start()
        {
            var bm = new Material(bakeShader);
            bm.SetFloat("_Bake_Sigma", Sigma); bm.SetFloat("_Bake_FlipY", 0f);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            rlt = Bake(bm, 0, "smoke_RLT");

            // orientation check of the bake: the Top map must be brighter in the upper half, the Right map in the right half
            int ts = AtlasSize / 4;
            var rb = new Texture2D(ts, ts, TextureFormat.RGBAHalf, false, true);
            RenderTexture.active = rlt; rb.ReadPixels(new Rect(ts, ts, ts, ts), 0, 0); rb.Apply(); RenderTexture.active = null;
            double msOne = sw.Elapsed.TotalMilliseconds;
            bbf = Bake(bm, 1, "smoke_BBF"); nrm = Bake(bm, 2, "smoke_normal");
            float tu = 0, tl = 0, rr = 0, rl = 0, al = 0;
            var px = rb.GetPixels();
            for (int y = 0; y < ts; y++) for (int x = 0; x < ts; x++)
            {
                var c = px[y * ts + x]; al += c.a;
                if (y >= ts / 2) tu += c.b; else tl += c.b;
                if (x >= ts / 2) rr += c.r; else rl += c.r;
            }
            float half = ts * ts / 2f;
            Debug.Log($"[gt] smoke bake: 3 atlases {AtlasSize}x{AtlasSize}, 16 frames, first atlas incl. read-back {msOne:0} ms. Frame 5: Top map upper {tu / half:0.000} lower {tl / half:0.000}, Right map right {rr / half:0.000} left {rl / half:0.000}, mean opacity {al / (2 * half):0.000}");

            // attenuation ramp of the normal-map workflow (256x1, generated): dark side keeps a little light
            ramp = new Texture2D(256, 1, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int x = 0; x < 256; x++)
            {
                float u = x / 255f, s = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f, 0.95f, u));
                float v = Mathf.Lerp(0.04f, 1f, s); ramp.SetPixel(x, 0, new Color(v, v, v, 1));
            }
            ramp.Apply();

            Shader.SetGlobalTexture("_Smoke_RLT", rlt); Shader.SetGlobalTexture("_Smoke_BBF", bbf);
            Shader.SetGlobalTexture("_Smoke_Normal", nrm); Shader.SetGlobalTexture("_Smoke_Ramp", ramp);
            Shader.SetGlobalFloat("_Smoke_Directionality", Directionality);
            Shader.SetGlobalFloat("_Smoke_SoftDistance", SoftDistance);
            Shader.SetGlobalVector("_Smoke_Albedo", new Vector3(0.60f, 0.60f, 0.62f));
            Shader.SetGlobalFloat("_Smoke_UnlitGain", 1.0f);
            Shader.SetGlobalFloat("_Panel_Frame", 6f);
            Shader.SetGlobalFloat("_Panel_Aspect", 960f / 1080f);

            mesh = new Mesh { name = "SmokeBillboards" }; mesh.MarkDynamic();
            smokeMesh.sharedMesh = mesh;
            for (int k = 0; k < 220; k++) Step(1f / 30f);      // start with a full column
        }

        float Rnd() { return (float)rng.NextDouble(); }

        void Step(float dt)
        {
            simTime += dt;
            emitAcc += dt * EmitRate;
            while (emitAcc >= 1f)
            {
                emitAcc -= 1f;
                float a = Rnd() * 6.2832f, rad = Mathf.Sqrt(Rnd()) * 0.12f;
                ps.Add(new P
                {
                    pos = Emitter + new Vector3(Mathf.Cos(a) * rad, 0, Mathf.Sin(a) * rad),
                    vel = new Vector3((Rnd() - 0.5f) * 0.3f, 1.3f, (Rnd() - 0.5f) * 0.3f),
                    life = 5.5f + Rnd(), size0 = 0.32f, size1 = 0.95f + 0.3f * Rnd(),
                    roll = Rnd() * 6.2832f, rollSpeed = (Rnd() - 0.5f) * 0.5f, phase = Rnd() * 6.2832f,
                    start = Rnd() * 3f, mirror = Rnd() < 0.5f ? -1f : 1f
                });
            }
            for (int i = ps.Count - 1; i >= 0; i--)
            {
                var p = ps[i]; p.age += dt;
                if (p.age >= p.life) { ps.RemoveAt(i); continue; }
                float h = p.pos.y;
                var turb = new Vector3(Mathf.Sin(h * 1.3f + simTime * 0.7f + p.phase), 0, Mathf.Cos(h * 1.1f + simTime * 0.5f + p.phase * 1.7f)) * 0.22f;
                var target = new Vector3(0.04f + 0.025f * h, Mathf.Lerp(1.15f, 0.55f, Mathf.Clamp01(p.age / 3f)), 0f) + turb;
                p.vel = Vector3.Lerp(p.vel, target, 1f - Mathf.Exp(-1.5f * dt));
                p.pos += p.vel * dt;
                ps[i] = p;
            }
        }

        void BuildMesh(Camera cam)
        {
            Vector3 r = cam.transform.right, u = cam.transform.up, f = cam.transform.forward;
            order.Clear(); for (int i = 0; i < ps.Count; i++) order.Add(i);
            order.Sort((a, b) => Vector3.Dot(ps[b].pos, f).CompareTo(Vector3.Dot(ps[a].pos, f)));     // far to near
            vv.Clear(); vn.Clear(); vt.Clear(); u0.Clear(); u1.Clear(); u2.Clear(); vc.Clear(); tri.Clear();
            foreach (int i in order)
            {
                var p = ps[i];
                float size = Mathf.Lerp(p.size0, p.size1, 1f - Mathf.Exp(-p.age * 0.55f));
                float ang = p.roll + p.rollSpeed * p.age, c = Mathf.Cos(ang), s = Mathf.Sin(ang);
                Vector3 R = (r * c + u * s) * p.mirror, U = -r * s + u * c, n = -f;
                float fr = p.start + p.age / p.life * (15f - p.start); float fa = Mathf.Floor(fr), fb = Mathf.Min(fa + 1f, 15f);
                float fade = Mathf.SmoothStep(0f, 1f, p.age / 0.5f) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f * p.life, p.life, p.age))) * 0.7f;
                int b0 = vv.Count;
                vv.Add(p.pos + (-R - U) * size); vv.Add(p.pos + (R - U) * size); vv.Add(p.pos + (R + U) * size); vv.Add(p.pos + (-R + U) * size);
                u0.Add(new Vector2(0, 0)); u0.Add(new Vector2(1, 0)); u0.Add(new Vector2(1, 1)); u0.Add(new Vector2(0, 1));
                for (int k = 0; k < 4; k++)
                {
                    vn.Add(n); vt.Add(new Vector4(R.x, R.y, R.z, 1)); u2.Add(U);
                    u1.Add(new Vector4(fa, fb, fr - fa, fade)); vc.Add(Color.white);
                }
                tri.Add(b0); tri.Add(b0 + 1); tri.Add(b0 + 2); tri.Add(b0); tri.Add(b0 + 2); tri.Add(b0 + 3);
            }
            mesh.Clear();
            mesh.SetVertices(vv); mesh.SetNormals(vn); mesh.SetTangents(vt);
            mesh.SetUVs(0, u0); mesh.SetUVs(1, u1); mesh.SetUVs(2, u2); mesh.SetColors(vc);
            mesh.SetTriangles(tri, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 200f);
        }

        static float S(float a, float b, float t) { return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, t)); }

        static void Orbit(Camera c, float azDeg, float r, float h, Vector3 at)
        {
            float a = azDeg * Mathf.Deg2Rad;
            c.transform.position = new Vector3(at.x + Mathf.Sin(a) * r, h, at.z + Mathf.Cos(a) * r);
            c.transform.LookAt(at);
        }

        void Update()
        {
            float t = FrameCapturer.T;
            bool split = t < 9f, thirds = t >= 9f && t < 16.5f, debug = t >= 16.5f;
            camL.enabled = camR.enabled = split;
            camA.enabled = camB.enabled = camC.enabled = thirds;
            camDbg.enabled = camPanel.enabled = debug;

            Camera refCam;
            if (split)
            {
                float az = Mathf.Lerp(-8f, 8f, S(0f, 9f, t));
                Orbit(camL, az, 8.4f, 1.9f, Centre); Orbit(camR, az, 8.4f, 1.9f, Centre); refCam = camL;
            }
            else if (thirds)
            {
                float az = Mathf.Lerp(8f, -4f, S(9f, 16.5f, t));
                Orbit(camA, az, 10.5f, 2.0f, Centre); Orbit(camB, az, 10.5f, 2.0f, Centre); Orbit(camC, az, 10.5f, 2.0f, Centre); refCam = camA;
            }
            else
            {
                float az = Mathf.Lerp(-4f, 6f, S(16.5f, 24f, t));
                Orbit(camDbg, az, 8.4f, 1.9f, Centre); refCam = camDbg;
            }

            // the lamp circles the column and bobs up and down
            float phi = 0.85f * t + 0.6f;
            lamp.transform.position = new Vector3(Centre.x + 2.0f * Mathf.Sin(phi), 2.1f + 1.2f * Mathf.Sin(0.5f * t + 4f), 2.0f * Mathf.Cos(phi));

            Step(Time.deltaTime);
            BuildMesh(refCam);

            // read-outs: the same formula as the shader, for an unrolled billboard at the column centre
            Vector3 toLamp = lamp.transform.position - Centre; float dist = toLamp.magnitude;
            Vector3 d = toLamp / dist;
            var L = new Vector3(Vector3.Dot(d, refCam.transform.right), Vector3.Dot(d, refCam.transform.up), Vector3.Dot(d, -refCam.transform.forward));
            float[] w = { Mathf.Clamp01(L.x), Mathf.Clamp01(-L.x), Mathf.Clamp01(L.y), Mathf.Clamp01(-L.y), Mathf.Clamp01(-L.z), Mathf.Clamp01(L.z) };
            float d2 = dist * dist, fac = d2 / (lamp.range * lamp.range), smooth = Mathf.Clamp01(1f - fac * fac);
            float atten = smooth * smooth / Mathf.Max(d2, 0.0001f);
            Shader.SetGlobalVector("_Panel_W0", new Vector4(w[0], w[1], w[2], 0));
            Shader.SetGlobalVector("_Panel_W1", new Vector4(w[3], w[4], w[5], 0));
            Shader.SetGlobalVector("_Panel_Light", (Vector4)(lamp.color.linear * lamp.intensity * atten));
            string[] names = { "Right", "Left", "Top", "Bottom", "Back", "Front" };
            for (int k = 0; k < 6; k++) tileLabels[k].text = $"{names[k]} x {w[k]:0.00}";
            dirLabel.text = $"Light dir ({L.x:+0.00;-0.00}, {L.y:+0.00;-0.00}, {L.z:+0.00;-0.00})";
            distLabel.text = $"Lamp distance {dist:0.0} m";
            intLabel.text = $"Intensity x attenuation {lamp.intensity * atten:0.00}";

            if (!logged && t > 12f)
            {
                logged = true; float top = 0;
                foreach (var p in ps) top = Mathf.Max(top, p.pos.y);
                Debug.Log($"[gt] smoke: {ps.Count} billboards alive, highest centre {top:0.00} m, lamp intensity {lamp.intensity}, range {lamp.range}");
            }
        }
    }
}
