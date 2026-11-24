// Drives the bent normal post. At Start it builds the props from patches, packs them into one atlas and runs the
// bake (after Fewes/BNAO): position/normal cache per texel, then for each of N directions on the sphere an
// orthographic depth view of all props and a compute pass that adds up which directions each texel can see.
// The result (bent normal + AO) is one ARGBHalf texture read by the props' shader. Two scene cameras render the same
// view with a different _GMode each and a full-screen quad shows them side by side. Runs before FrameCapturer (-100).
using UnityEngine;
using UnityEngine.Rendering;
using GameTech;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class BNDirector : MonoBehaviour
    {
        public Camera camA, camB; public Material display, depthMat; public ComputeShader bake;
        public MeshFilter[] groups; public Material[] litMats; public Cubemap cube;
        public int atlasSize = 2048, directions = 1024, depthRes = 2048;
        public LabelSpec title, l1, l2, m1, m2;
        RenderTexture rtA, rtB, result; int phase = -1; float texelsPerM, bakeMs;

        static float S(float x) { x = Mathf.Clamp01(x); return x * x * x * (x * (x * 6f - 15f) + 10f); }
        static RenderTexture RT(int w, int h) { var r = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { antiAliasing = 4 }; r.Create(); return r; }

        void Start()
        {
            int w = int.Parse(FrameCapturer.Arg("-gtW", "1920")), h = int.Parse(FrameCapturer.Arg("-gtH", "1080"));
            rtA = RT(w, h); rtB = RT(w, h);
            camA.targetTexture = rtA; camA.enabled = false; camB.targetTexture = rtB; camB.enabled = false;
            display.SetTexture("_TexA", rtA); display.SetTexture("_TexB", rtB);
            foreach (var m in litMats) m.SetTexture("_Cube", cube);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var patches = BNGeo.Patches();
            texelsPerM = BNGeo.Pack(patches, atlasSize);
            int nv = 0, nt = 0;
            for (int g = 0; g < groups.Length; g++) { var m = BNGeo.BuildMesh(patches, g, atlasSize); groups[g].sharedMesh = m; nv += m.vertexCount; nt += m.triangles.Length / 3; }
            var pos = new Color[atlasSize * atlasSize]; var nrm = new Color[atlasSize * atlasSize];
            BNGeo.FillCache(patches, atlasSize, pos, nrm);
            int used = 0; foreach (var c in pos) if (c.a > 0.5f) used++;
            var posT = new Texture2D(atlasSize, atlasSize, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point }; posT.SetPixels(pos); posT.Apply(false);
            var nrmT = new Texture2D(atlasSize, atlasSize, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point }; nrmT.SetPixels(nrm); nrmT.Apply(false);
            float cacheMs = (float)sw.Elapsed.TotalMilliseconds;
            Debug.Log($"[gt] bnao: {patches.Count} patches, {nv} verts, {nt} tris, atlas {atlasSize}, {texelsPerM:F0} texels/m, {used} texels used ({100f * used / (atlasSize * atlasSize):F0}%), cache {cacheMs:F0} ms");

            sw.Restart();
            var acc = new RenderTexture(atlasSize, atlasSize, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { enableRandomWrite = true }; acc.Create();
            result = new RenderTexture(atlasSize, atlasSize, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { enableRandomWrite = true, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp }; result.Create();
            var depth = new RenderTexture(depthRes, depthRes, 24, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Point }; depth.Create();
            var bounds = groups[1].sharedMesh.bounds; for (int g = 0; g < groups.Length; g++) bounds.Encapsulate(groups[g].sharedMesh.bounds);
            float R = bounds.extents.magnitude * 1.02f; Vector3 c0 = bounds.center;
            int kc = bake.FindKernel("Clear"), ka = bake.FindKernel("Accumulate"), kf = bake.FindKernel("Finalize"); int gs = (atlasSize + 7) / 8;
            var cmd = new CommandBuffer { name = "BNAO bake" };
            cmd.SetComputeIntParam(bake, "_Size", atlasSize);
            cmd.SetComputeFloatParam(bake, "_Offset", 0.004f); cmd.SetComputeFloatParam(bake, "_Bias", 0.004f);
            cmd.SetComputeFloatParam(bake, "_Samples", directions); cmd.SetComputeFloatParam(bake, "_AOBias", 0.5f);
            cmd.SetComputeTextureParam(bake, kc, "_Acc", acc); cmd.DispatchCompute(bake, kc, gs, gs, 1);
            cmd.SetComputeTextureParam(bake, ka, "_PosCache", posT); cmd.SetComputeTextureParam(bake, ka, "_NrmCache", nrmT);
            cmd.SetComputeTextureParam(bake, ka, "_Acc", acc); cmd.SetComputeTextureParam(bake, ka, "_Depth", depth);
            float inc = Mathf.PI * (3f - Mathf.Sqrt(5f)), off = 2f / directions;
            var P = Matrix4x4.Ortho(-R, R, -R, R, 0.01f, 2.2f * R); var Pg = GL.GetGPUProjectionMatrix(P, true);
            for (int i = 0; i < directions; i++)
            {
                // the source's PointsOnSphere: evenly spread points over the whole sphere
                float y = i * off - 1f + off * 0.5f, r = Mathf.Sqrt(1f - y * y), ph = i * inc;
                var d = new Vector3(Mathf.Cos(ph) * r, y, Mathf.Sin(ph) * r);
                var camPos = c0 + d * (1.1f * R); var fwd = -d;
                var rot = Quaternion.LookRotation(fwd, Mathf.Abs(d.y) > 0.99f ? Vector3.forward : Vector3.up);
                var Vm = Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.TRS(camPos, rot, Vector3.one).inverse;
                cmd.SetRenderTarget(depth); cmd.ClearRenderTarget(true, true, new Color(1e6f, 0, 0, 0));
                cmd.SetGlobalMatrix("_GDepthVP", Pg * Vm); cmd.SetGlobalVector("_GDepthOrigin", camPos); cmd.SetGlobalVector("_GDepthFwd", fwd);
                foreach (var mf in groups) cmd.DrawMesh(mf.sharedMesh, Matrix4x4.identity, depthMat, 0, 0);
                cmd.SetComputeMatrixParam(bake, "_SVP", P * Vm); cmd.SetComputeVectorParam(bake, "_Origin", camPos);
                cmd.SetComputeVectorParam(bake, "_Fwd", fwd); cmd.SetComputeVectorParam(bake, "_Dir", d);
                cmd.DispatchCompute(bake, ka, gs, gs, 1);
            }
            cmd.SetComputeTextureParam(bake, kf, "_PosCache", posT); cmd.SetComputeTextureParam(bake, kf, "_NrmCache", nrmT);
            cmd.SetComputeTextureParam(bake, kf, "_Acc", acc); cmd.SetComputeTextureParam(bake, kf, "_Out", result);
            cmd.DispatchCompute(bake, kf, gs, gs, 1);
            Graphics.ExecuteCommandBuffer(cmd); cmd.Release();
            var req = AsyncGPUReadback.Request(result, 0, 0, 1, 0, 1, 0, 1); req.WaitForCompletion();
            bakeMs = (float)sw.Elapsed.TotalMilliseconds;
            Shader.SetGlobalTexture("_GBNAO", result);
            acc.Release(); depth.Release(); Destroy(posT); Destroy(nrmT);
            Debug.Log($"[gt] bnao bake: {directions} dirs, depth {depthRes}, R {R:F2}, {bakeMs:F0} ms (GPU, waited)");
        }

        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }

        static Vector3 Orbit(Vector3 target, float ang, float rad, float hgt) => target + Quaternion.Euler(0, ang, 0) * new Vector3(0, hgt - target.y, -rad);

        void LateUpdate()
        {
            if (rtA == null) return;
            float t = FrameCapturer.T;
            int p = t < 3f ? 0 : t < 13f ? 1 : t < 18f ? 2 : t < 22.5f ? 3 : 4;

            // spheres on rings: orbit; bowl: look in from above; overview; end card
            var fT = BNGeo.PairA + new Vector3(0f, 0.12f, 0f);
            float ang = Mathf.Lerp(-40f, 38f, S((t - 1f) / 12f));
            float rad = Mathf.Lerp(0.9f, 1.15f, S((t - 2f) / 8f));
            Vector3 cp = Orbit(fT, ang, rad, 0.82f), ct = fT;
            var bc = BNGeo.BowlCenter;
            float sb = S((t - 12.6f) / 1.6f) - S((t - 17.6f) / 1.6f);
            float bAng = Mathf.Lerp(-20f, 25f, S((t - 13f) / 5f));
            cp = Vector3.Lerp(cp, Orbit(bc + new Vector3(0, 0.08f, 0), bAng, 0.62f, 0.62f), sb); ct = Vector3.Lerp(ct, bc + new Vector3(0, 0.08f, 0), sb);
            float so = S((t - 17.6f) / 1.6f);
            cp = Vector3.Lerp(cp, Orbit(fT, 12f, 1.05f, 1.0f), so); ct = Vector3.Lerp(ct, fT, so);
            float se = S((t - 22.2f) / 2.4f);
            cp = Vector3.Lerp(cp, new Vector3(-1.4f, 1.15f, -1.9f), se); ct = Vector3.Lerp(ct, new Vector3(-0.2f, 0.4f, 0.1f), se);
            foreach (var c in new[] { camA, camB }) c.transform.SetPositionAndRotation(cp, Quaternion.LookRotation(ct - cp));

            int ma = 2, mb = 2; float split = 0.5f, line = 1;
            switch (p)
            {
                case 0:
                    split = 0f; line = 0;
                    Labels("Bent normals: which way is open?", $"baked once at start: {directions} depth views, {atlasSize} x {atlasSize} atlas", "studio HDRI only, no lights", "", "");
                    break;
                case 1:
                    ma = 1; split = Mathf.Lerp(0f, 0.5f, S((t - 3f) / 0.8f));
                    Labels("Same props, same HDRI, same camera", "AO multiplier", "reflection x AO: same gray from every angle", "Bent normal", "reflection x lerp(max(dot(R, bent), 0), 1, AO)");
                    break;
                case 2:
                    ma = 0;
                    Labels("Inside the bowl", "No occlusion", "the reflection sees the studio through the wall", "Bent normal", "occluded where R points into the bowl");
                    break;
                case 3:
                    ma = 3; mb = 4;
                    Labels("The two baked maps", "AO", $"{directions} directions, clamped to the hemisphere", "Bent normal (world space)", "average of the unblocked directions");
                    break;
                default:
                    split = 0f; line = 0;
                    Labels("Bent normal + AO baking", "Unity 6.3 URP, after Fewes/BNAO (MIT)", "HDRI: Studio Small 09, Poly Haven (CC0)", "", "");
                    break;
            }
            Shader.SetGlobalFloat("_GMode", ma); camA.Render();
            Shader.SetGlobalFloat("_GMode", mb); camB.Render();
            display.SetFloat("_Split", split); display.SetFloat("_Line", line);
            if (p != phase) { phase = p; Debug.Log($"[gt] t {t:F2} phase {p} '{title.text}'"); }
        }

        void OnDestroy() { foreach (var r in new[] { rtA, rtB, result }) if (r) r.Release(); }
    }
}
