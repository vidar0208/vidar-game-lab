// Drives the deep shadow map post. Every frame: clear the per-texel lists, build the fibre points (they sway), splat
// them into the light's view (compute), draw the fibres, then render camera A with the ordinary one-depth map and
// camera B with the deep map (global _GMode). A full-screen quad shows the split. Runs before FrameCapturer (-100).
using UnityEngine;
using UnityEngine.Rendering;
using GameTech;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class DSMDirector : MonoBehaviour
    {
        public Camera camA, camB; public Material display, fibreMat; public ComputeShader cs; public Mesh strandMesh;
        public Vector3 lightTravel = new Vector3(0.45f, -0.75f, -0.5f), lightCentre = new Vector3(0f, 1.1f, 0f);
        public float mapHalf = 1.6f, depthRange = 6f, fibreAlpha = 0.18f, fibreLen = 1.05f;
        public int dim = 1024, maxFrags = 16, perTassel = 1400, segs = 256, solidLines = 120, solidSegs = 1024;
        public LabelSpec title, l1, l2, m1, m2;
        GraphicsBuffer count, nearest, frags, points; RenderTexture rtA, rtB; int phase = -1, kReset, kBuild, kSplat, total;

        static float S(float x) { x = Mathf.Clamp01(x); return x * x * x * (x * (x * 6f - 15f) + 10f); }
        static RenderTexture RT(int w, int h) { var r = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { antiAliasing = 4 }; r.Create(); return r; }
        static readonly float[] kt = { 0f, 14f, 19f, 25f };
        static readonly Vector3[] kp = { new Vector3(-0.9f, 1.35f, -1.6f), new Vector3(0.9f, 1.05f, -2.4f), new Vector3(1.4f, 0.6f, -2.2f), new Vector3(-0.3f, 1.5f, -3.4f) };
        static readonly Vector3[] kl = { new Vector3(0f, 1.15f, 0f), new Vector3(0.3f, 0.55f, -0.4f), new Vector3(0.8f, 0.05f, -0.7f), new Vector3(0.2f, 0.75f, 0f) };

        void Start()
        {
            int w = int.Parse(FrameCapturer.Arg("-gtW", "1920")), h = int.Parse(FrameCapturer.Arg("-gtH", "1080"));
            rtA = RT(w, h); rtB = RT(w, h);
            camA.targetTexture = rtA; camA.enabled = false; camB.targetTexture = rtB; camB.enabled = false;
            display.SetTexture("_TexA", rtA); display.SetTexture("_TexB", rtB);
            int strands = perTassel * 3; total = strands * segs + solidLines * solidSegs;
            count = new GraphicsBuffer(GraphicsBuffer.Target.Structured, dim * dim, 4);
            nearest = new GraphicsBuffer(GraphicsBuffer.Target.Structured, dim * dim, 4);
            frags = new GraphicsBuffer(GraphicsBuffer.Target.Structured, dim * dim * maxFrags, 8);
            points = new GraphicsBuffer(GraphicsBuffer.Target.Structured, total, 16);
            kReset = cs.FindKernel("KReset"); kBuild = cs.FindKernel("KBuild"); kSplat = cs.FindKernel("KSplat");
            foreach (int k in new[] { kReset, kBuild, kSplat })
            {
                cs.SetBuffer(k, "_Count", count); cs.SetBuffer(k, "_Nearest", nearest); cs.SetBuffer(k, "_Frags", frags); cs.SetBuffer(k, "_Points", points);
            }
            var f = lightTravel.normalized; var r = Vector3.Cross(Vector3.up, f).normalized; var u = Vector3.Cross(f, r);
            var lc = new Vector4(lightCentre.x, lightCentre.y, lightCentre.z, mapHalf); var lr = (Vector4)r; var lu = (Vector4)u; var lf = new Vector4(f.x, f.y, f.z, depthRange);
            cs.SetVector("_LC", lc); cs.SetVector("_LR", lr); cs.SetVector("_LU", lu); cs.SetVector("_LF", lf);
            Shader.SetGlobalVector("_LC", lc); Shader.SetGlobalVector("_LR", lr); Shader.SetGlobalVector("_LU", lu); Shader.SetGlobalVector("_LF", lf);
            cs.SetInt("_Dim", dim); cs.SetInt("_MaxFrags", maxFrags); cs.SetInt("_Strands", strands); cs.SetInt("_Segs", segs); cs.SetInt("_PerTassel", perTassel);
            cs.SetInt("_SolidLines", solidLines); cs.SetInt("_SolidSegs", solidSegs); cs.SetFloat("_FibreAlpha", fibreAlpha); cs.SetFloat("_Len", fibreLen);
            cs.SetVector("_Tassel0", new Vector4(-0.72f, 1.58f, 0f)); cs.SetVector("_Tassel1", new Vector4(0f, 1.58f, 0f)); cs.SetVector("_Tassel2", new Vector4(0.72f, 1.58f, 0f));
            Shader.SetGlobalBuffer("_Count", count); Shader.SetGlobalBuffer("_Nearest", nearest); Shader.SetGlobalBuffer("_Frags", frags);
            Shader.SetGlobalInteger("_Dim", dim); Shader.SetGlobalInteger("_MaxFrags", maxFrags); Shader.SetGlobalFloat("_DsmBias", 0.012f);
            Shader.SetGlobalVector("_GLightCol", new Vector4(2.8f, 2.3f, 1.75f, 0)); Shader.SetGlobalVector("_GAmb", new Vector4(0.07f, 0.08f, 0.1f, 0));
            fibreMat.SetBuffer("_Points", points); fibreMat.SetInteger("_Segs", segs);
            Debug.Log($"[gt] dsm {dim}x{dim}, {maxFrags} frags/texel, {strands} fibres x {segs} points, {total} points, frag buffer {dim * dim * maxFrags * 8 / (1024 * 1024)} MB");
        }

        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }

        void Update()
        {
            float t = FrameCapturer.T; int k = 0;
            while (k < kt.Length - 2 && t > kt[k + 1]) k++;
            float uu = S((t - kt[k]) / (kt[k + 1] - kt[k]));
            var pos = Vector3.Lerp(kp[k], kp[k + 1], uu); var look = Vector3.Lerp(kl[k], kl[k + 1], uu);
            foreach (var c in new[] { camA, camB }) c.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
            if (points == null) return;
            cs.SetFloat("_Time", t);
            cs.Dispatch(kReset, (dim * dim + 63) / 64, 1, 1);
            cs.Dispatch(kBuild, (total + 63) / 64, 1, 1);
            cs.Dispatch(kSplat, (total + 63) / 64, 1, 1);
        }

        void LateUpdate()
        {
            if (rtA == null) return;
            float t = FrameCapturer.T;
            int p = t < 3f ? 0 : t < 19f ? 1 : t < 22.5f ? 2 : 3;
            var rp = new RenderParams(fibreMat) { worldBounds = new Bounds(new Vector3(0, 1f, 0), new Vector3(4f, 3f, 3f)), shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
            Graphics.RenderMeshPrimitives(rp, strandMesh, 0, perTassel * 3);
            Shader.SetGlobalFloat("_GDebug", p == 2 ? 1 : 0);
            Shader.SetGlobalFloat("_GMode", 0); camA.Render();
            Shader.SetGlobalFloat("_GMode", 1); camB.Render();
            float split = 0.5f, line = 1;
            switch (p)
            {
                case 0:
                    split = 0f; line = 0;
                    Labels($"Light through {perTassel * 3:N0} fibres", "Deep shadow map", $"up to {maxFrags} depths stored per shadow texel", "", "");
                    break;
                case 1:
                    split = Mathf.Lerp(0f, 0.5f, S((t - 3f) / 0.8f));
                    Labels($"Same fibres, same light, same {dim} x {dim} map", "Ordinary shadow map", "nearest depth only: one fibre blocks all light", "Deep shadow map", $"every fibre on the ray lets {1f - fibreAlpha:P0} through");
                    break;
                case 2:
                    Labels($"Fibres stored per shadow texel (blue = few, red = {maxFrags})", "Ordinary map", "fibres: white = lit, black = shadowed", "Deep map", "same counts, light keeps going");
                    break;
                default:
                    split = 0f; line = 0;
                    Labels("Deep shadow maps", "Unity 6.3 URP, after ecidevilin/DeepShadowMap (MIT)", "compute shader fragment lists, no hair simulation", "", "");
                    break;
            }
            display.SetFloat("_Split", split); display.SetFloat("_Line", line);
            if (p != phase) { phase = p; Debug.Log($"[gt] t {t:F2} phase {p} '{title.text}'"); }
        }

        void OnDestroy() { count?.Release(); nearest?.Release(); frags?.Release(); points?.Release(); foreach (var r in new[] { rtA, rtB }) if (r) r.Release(); }
    }
}
