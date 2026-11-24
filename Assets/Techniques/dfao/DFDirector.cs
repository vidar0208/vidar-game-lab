// Drives the DFAO post (after ZephyrL/DFAO-unity, MIT). At Start it bakes one local SDF per prop (48^3, from the prop's
// analytic shape) and stacks them along z into one 3D atlas, like the source's DistanceFieldAtlas. Every frame it
// updates the per-volume transforms (one prop moves) and rebuilds the global field from the atlas with a compute
// kernel (the source's GDFUpdate). The scene shader marches that global field for AO and soft shadows. Two cameras
// render the same view with a different _GMode each; a full-screen quad shows them side by side.
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using GameTech;

namespace GameTech.Posts
{
    [System.Serializable] public class DFProp { public Transform t; public int shape; public Vector3 size; }

    [DefaultExecutionOrder(-100)]
    public class DFDirector : MonoBehaviour
    {
        public Camera camA, camB; public Material display; public ComputeShader build;
        public DFProp[] props; public Transform mover;
        public int localRes = 48; public float margin = 0.35f;
        public Vector3 gMin = new Vector3(-7f, -0.2f, -3.5f), gMax = new Vector3(7f, 4.6f, 8.5f); public Vector3Int gRes = new Vector3Int(140, 48, 120);
        public float aoStep = 0.1f, aoSteps = 5, aoIntensity = 0.22f, shK = 14f, shSteps = 128, shMax = 12f, shAcc = 0.001f;
        public LabelSpec title, l1, l2, m1, m2;

        [StructLayout(LayoutKind.Sequential)] struct VolumeData { public Matrix4x4 w2l; public Vector4 extIdx, center; }
        RenderTexture rtA, rtB, gdf; Texture3D atlas; ComputeBuffer vol; VolumeData[] vd; int phase = -1, kb;

        static float S(float x) { x = Mathf.Clamp01(x); return x * x * x * (x * (x * 6f - 15f) + 10f); }
        static RenderTexture RT(int w, int h) { var r = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { antiAliasing = 4 }; r.Create(); return r; }

        static float SdBox(Vector3 p, Vector3 b, float r)
        {
            Vector3 q = new Vector3(Mathf.Abs(p.x), Mathf.Abs(p.y), Mathf.Abs(p.z)) - b + Vector3.one * r;
            return Vector3.Max(q, Vector3.zero).magnitude + Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f) - r;
        }
        static float Sdf(DFProp pr, Vector3 p)
        {
            Vector3 h = pr.size * 0.5f;
            switch (pr.shape)
            {
                case 1: { float dx = new Vector2(p.x, p.z).magnitude - h.x, dy = Mathf.Abs(p.y) - h.y; return Mathf.Min(Mathf.Max(dx, dy), 0f) + new Vector2(Mathf.Max(dx, 0), Mathf.Max(dy, 0)).magnitude; }
                case 2: return p.magnitude - h.x;
                case 3:
                {
                    float d = 1e9f;
                    for (int k = 0; k < 3; k++)
                    {
                        float y0 = -h.y, y1 = -h.y + (k + 1) * pr.size.y / 3f, z0 = -h.z + k * pr.size.z / 3f, z1 = h.z;
                        var c = new Vector3(0, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f); var e = new Vector3(h.x, (y1 - y0) * 0.5f, (z1 - z0) * 0.5f);
                        d = Mathf.Min(d, SdBox(p - c, e, 0.02f));
                    }
                    return d;
                }
                default: return SdBox(p, h, 0.03f);
            }
        }

        void BakeAtlas()
        {
            int n = localRes, c = props.Length;
            var data = new float[n * n * n * c];
            for (int i = 0; i < c; i++)
            {
                Vector3 ext = props[i].size * 0.5f + Vector3.one * margin; float diag = (ext * 2f).magnitude;
                for (int z = 0; z < n; z++) for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                {
                    var lp = new Vector3(((x + 0.5f) / n - 0.5f) * 2f * ext.x, ((y + 0.5f) / n - 0.5f) * 2f * ext.y, ((z + 0.5f) / n - 0.5f) * 2f * ext.z);
                    data[(i * n + z) * n * n + y * n + x] = Sdf(props[i], lp) / diag;
                }
            }
            atlas = new Texture3D(n, n, n * c, TextureFormat.RFloat, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            atlas.SetPixelData(data, 0); atlas.Apply(false, true);
        }

        void UpdateVolumes()
        {
            for (int i = 0; i < props.Length; i++)
            {
                Vector3 ext = props[i].size * 0.5f + Vector3.one * margin;
                vd[i] = new VolumeData { w2l = props[i].t.worldToLocalMatrix, extIdx = new Vector4(ext.x, ext.y, ext.z, i), center = Vector4.zero };
            }
            vol.SetData(vd);
            build.SetInt("bufferLength", props.Length); build.SetInt("textureCount", props.Length);
            build.SetVector("_AABBmin", gMin); build.SetVector("_AABBmax", gMax); build.SetVector("_Res", new Vector4(gRes.x, gRes.y, gRes.z, 0)); build.SetFloat("_GroundY", 0f);
            build.SetBuffer(kb, "_VolumeData", vol); build.SetTexture(kb, "_TextureAtlas", atlas); build.SetTexture(kb, "_GDF", gdf);
            build.Dispatch(kb, (gRes.x + 7) / 8, (gRes.y + 7) / 8, (gRes.z + 7) / 8);
        }

        void Start()
        {
            int w = int.Parse(FrameCapturer.Arg("-gtW", "1920")), h = int.Parse(FrameCapturer.Arg("-gtH", "1080"));
            rtA = RT(w, h); rtB = RT(w, h);
            camA.targetTexture = rtA; camA.enabled = false; camB.targetTexture = rtB; camB.enabled = false;
            display.SetTexture("_TexA", rtA); display.SetTexture("_TexB", rtB);
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(new Color(0.2f, 0.24f, 0.32f));
            sh.AddDirectionalLight(Vector3.up, new Color(0.5f, 0.62f, 0.9f), 0.9f);
            sh.AddDirectionalLight(Vector3.down, new Color(0.35f, 0.28f, 0.2f), 0.5f);
            RenderSettings.ambientMode = AmbientMode.Custom; RenderSettings.ambientProbe = sh;

            var sw = System.Diagnostics.Stopwatch.StartNew(); BakeAtlas(); float bakeMs = (float)sw.Elapsed.TotalMilliseconds;
            gdf = new RenderTexture(gRes.x, gRes.y, 0, RenderTextureFormat.RGFloat, RenderTextureReadWrite.Linear)
            { dimension = TextureDimension.Tex3D, volumeDepth = gRes.z, enableRandomWrite = true, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            gdf.Create();
            vd = new VolumeData[props.Length]; vol = new ComputeBuffer(props.Length, Marshal.SizeOf<VolumeData>()); kb = build.FindKernel("Build");
            UpdateVolumes();
            Shader.SetGlobalTexture("_GDFTex", gdf); Shader.SetGlobalVector("_GDFMin", gMin); Shader.SetGlobalVector("_GDFMax", gMax);
            Shader.SetGlobalFloat("_AOStep", aoStep); Shader.SetGlobalFloat("_AOSteps", aoSteps); Shader.SetGlobalFloat("_AOIntensity", aoIntensity);
            Shader.SetGlobalFloat("_ShK", shK); Shader.SetGlobalFloat("_ShSteps", shSteps); Shader.SetGlobalFloat("_ShMax", shMax); Shader.SetGlobalFloat("_ShAcc", shAcc);
            var v = (gMax - gMin); 
            Debug.Log($"[gt] dfao: {props.Length} local SDFs {localRes}^3 baked in {bakeMs:F0} ms (CPU), stride {Marshal.SizeOf<VolumeData>()}, global {gRes.x}x{gRes.y}x{gRes.z} over {v.x:F1}x{v.y:F1}x{v.z:F1} m ({v.x / gRes.x:F3} m)");
        }

        struct Key { public Vector3 p, t; public Key(Vector3 a, Vector3 b) { p = a; t = b; } }
        static readonly Key K0 = new Key(new Vector3(-1.0f, 0.85f, -1.4f), new Vector3(-0.8f, 0.35f, 1.6f));
        static readonly Key K1 = new Key(new Vector3(1.8f, 1.7f, -3.4f), new Vector3(0.2f, 0.9f, 2.8f));
        static readonly Key K2 = new Key(new Vector3(-0.5f, 3.4f, -3.8f), new Vector3(-0.8f, 0f, 1.2f));
        static readonly Key K3 = new Key(new Vector3(0.6f, 4.6f, -3.0f), new Vector3(0.0f, 0f, 2.4f));
        static readonly Key K4 = new Key(new Vector3(-3.6f, 2.2f, -2.6f), new Vector3(0.2f, 0.6f, 2.6f));
        static Key Lerp(Key a, Key b, float s) => new Key(Vector3.Lerp(a.p, b.p, s), Vector3.Lerp(a.t, b.t, s));
        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }

        void LateUpdate()
        {
            if (rtA == null) return;
            float t = FrameCapturer.T;
            mover.position = new Vector3(Mathf.Lerp(-1.1f, 0.9f, S(t / 25f)), mover.position.y, mover.position.z);
            mover.rotation = Quaternion.Euler(0, 0, -Mathf.Rad2Deg * mover.position.x / 0.55f);
            UpdateVolumes();

            int p = t < 3f ? 0 : t < 12f ? 1 : t < 18.5f ? 2 : t < 22.5f ? 3 : 4;
            Key k = Lerp(K0, K1, S((t - 2.5f) / 9f));
            k = Lerp(k, K2, S((t - 11.6f) / 1.8f));
            k = Lerp(k, K3, S((t - 18.2f) / 1.6f));
            k = Lerp(k, K4, S((t - 22.2f) / 2.5f));
            float drift = Mathf.Sin(t * 0.3f) * 0.06f;
            foreach (var c in new[] { camA, camB }) c.transform.SetPositionAndRotation(k.p + new Vector3(drift, 0, 0), Quaternion.LookRotation(k.t - k.p));

            int ma = 1, mb = 1; float split = 0.5f, line = 1;
            string gs = $"global field {gRes.x}x{gRes.y}x{gRes.z}, 10 cm cells";
            switch (p)
            {
                case 0:
                    split = 0f; line = 0;
                    Labels("Distance-field AO and soft shadows", $"{props.Length} local SDFs ({localRes}^3) + one {gs}", "one prop moves: the global field is rebuilt every frame", "", "");
                    break;
                case 1:
                    ma = 0; split = Mathf.Lerp(0f, 0.5f, S((t - 3f) / 0.8f));
                    Labels("Same scene, same sun", "DFAO off", "shadow map, no AO", "DFAO on", $"AO: {aoSteps} steps along the normal; shadow: sphere traced");
                    break;
                case 2:
                    ma = 0; mb = 2;
                    Labels("Shadows only, no AO", "Shadow map (URP soft shadows)", "same blur everywhere", "Distance-field soft shadow", $"sharp at contact, wider with distance (k = {shK})");
                    break;
                case 3:
                    ma = 3; mb = 4;
                    Labels("The two terms", "DF ambient occlusion", $"{aoSteps} samples, {aoStep * 100:F0} cm apart", "DF sun shadow", $"up to {shSteps} sphere-tracing steps");
                    break;
                default:
                    split = 0f; line = 0;
                    Labels("Two-level distance fields: AO + soft shadows", "Unity 6.3 URP, after ZephyrL/DFAO-unity (MIT)", "local SDFs per prop, one global field for marching", "", "");
                    break;
            }
            Shader.SetGlobalFloat("_GMode", ma); camA.Render();
            Shader.SetGlobalFloat("_GMode", mb); camB.Render();
            display.SetFloat("_Split", split); display.SetFloat("_Line", line);
            if (p != phase) { phase = p; Debug.Log($"[gt] t {t:F2} phase {p} '{title.text}'"); }
        }

        void OnDestroy() { vol?.Release(); foreach (var r in new[] { rtA, rtB, gdf }) if (r) r.Release(); }
    }
}
