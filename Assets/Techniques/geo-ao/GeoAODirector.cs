// Drives the GeoAO post. At Start it runs the bake once (after nezix/Unity-GeoAO): for each of N sky directions it renders
// an orthographic depth view of every static mesh, then a compute pass tests every vertex against that view and adds
// up the visible directions. The result goes into one float per vertex in a GPU buffer that the scene shader reads by
// vertex id (each renderer gets its first index through a property block). Two scene cameras render the same view
// into textures with a different _GMode each (no AO, GeoAO, URP SSAO, AO-only views) and a full-screen quad shows
// them side by side. Runs before FrameCapturer (execution order -100).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using GameTech;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class GeoAODirector : MonoBehaviour
    {
        public Camera camA, camB; public Material display, depthMat; public ComputeShader bake;
        public MeshFilter[] meshes; public int directions = 256, depthRes = 2048;
        public float ssaoPow = 2f;
        public LabelSpec title, l1, l2, m1, m2;
        RenderTexture rtA, rtB; ComputeBuffer aoBuf; int phase = -1, nVert, nTri; float bakeMs;

        static float S(float x) { x = Mathf.Clamp01(x); return x * x * x * (x * (x * 6f - 15f) + 10f); }
        static RenderTexture RT(int w, int h) { var r = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { antiAliasing = 4 }; r.Create(); return r; }

        float Bake(bool apply)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var pos = new List<Vector3>(); var nrm = new List<Vector3>(); var bounds = new Bounds();
            nTri = 0;
            for (int m = 0; m < meshes.Length; m++)
            {
                var mesh = meshes[m].sharedMesh; pos.AddRange(mesh.vertices); nrm.AddRange(mesh.normals); nTri += mesh.triangles.Length / 3;
                if (m == 0) bounds = mesh.bounds; else bounds.Encapsulate(mesh.bounds);
            }
            nVert = pos.Count;
            var pb = new ComputeBuffer(nVert, 12); pb.SetData(pos);
            var nb = new ComputeBuffer(nVert, 12); nb.SetData(nrm);
            var ab = new ComputeBuffer(nVert, 8);
            var depth = new RenderTexture(depthRes, depthRes, 24, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Point }; depth.Create();

            float R = bounds.extents.magnitude * 1.02f; Vector3 c = bounds.center;
            int kc = bake.FindKernel("Clear"), ka = bake.FindKernel("Accumulate"); int groups = (nVert + 255) / 256;
            var cmd = new CommandBuffer { name = "GeoAO bake" };
            cmd.SetComputeIntParam(bake, "_Count", nVert);
            cmd.SetComputeFloatParam(bake, "_Bias", 0.02f); cmd.SetComputeFloatParam(bake, "_Offset", 0.01f);
            cmd.SetComputeBufferParam(bake, kc, "_Acc", ab); cmd.DispatchCompute(bake, kc, groups, 1, 1);
            cmd.SetComputeBufferParam(bake, ka, "_Pos", pb); cmd.SetComputeBufferParam(bake, ka, "_Nrm", nb); cmd.SetComputeBufferParam(bake, ka, "_Acc", ab);
            cmd.SetComputeTextureParam(bake, ka, "_Depth", depth);
            float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            var P = Matrix4x4.Ortho(-R, R, -R, R, 0.01f, 2.2f * R); var Pg = GL.GetGPUProjectionMatrix(P, true);
            for (int i = 0; i < directions; i++)
            {
                // Fibonacci points on the upper hemisphere (the source uses the full sphere)
                float y = 1f - (i + 0.5f) / directions, r = Mathf.Sqrt(1f - y * y), th = golden * i;
                var d = new Vector3(r * Mathf.Cos(th), y, r * Mathf.Sin(th));
                var camPos = c + d * (1.1f * R); var fwd = -d;
                var rot = Quaternion.LookRotation(fwd, Mathf.Abs(d.y) > 0.99f ? Vector3.forward : Vector3.up);
                var V = Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.TRS(camPos, rot, Vector3.one).inverse;
                cmd.SetRenderTarget(depth); cmd.ClearRenderTarget(true, true, new Color(1e6f, 0, 0, 0));
                cmd.SetGlobalMatrix("_GDepthVP", Pg * V); cmd.SetGlobalVector("_GDepthOrigin", camPos); cmd.SetGlobalVector("_GDepthFwd", fwd);
                foreach (var mf in meshes) for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) cmd.DrawMesh(mf.sharedMesh, Matrix4x4.identity, depthMat, s, 0);
                cmd.SetComputeMatrixParam(bake, "_SVP", P * V); cmd.SetComputeVectorParam(bake, "_Origin", camPos);
                cmd.SetComputeVectorParam(bake, "_Fwd", fwd); cmd.SetComputeVectorParam(bake, "_Ldir", d);
                cmd.DispatchCompute(bake, ka, groups, 1, 1);
            }
            Graphics.ExecuteCommandBuffer(cmd); cmd.Release();
            var acc = new Vector2[nVert]; ab.GetData(acc);
            float ms = (float)sw.Elapsed.TotalMilliseconds;
            if (apply)
            {
                int o = 0; float lo = 1, sum = 0; var aoAll = new float[nVert];
                foreach (var mf in meshes)
                {
                    var mesh = mf.sharedMesh; var col = new Color[mesh.vertexCount];
                    for (int k = 0; k < col.Length; k++)
                    {
                        var a = acc[o + k]; float ao = a.y > 1e-3f ? a.x / a.y : 0.25f;
                        col[k] = new Color(ao, ao, ao, 1); lo = Mathf.Min(lo, ao); sum += ao;
                    }
                    for (int k = 0; k < col.Length; k++) aoAll[o + k] = col[k].r;
                    var mpb = new MaterialPropertyBlock(); mpb.SetFloat("_AOBase", o); mf.GetComponent<MeshRenderer>().SetPropertyBlock(mpb);
                    float ms2 = 0; foreach (var cc in col) ms2 += cc.r;
                    Debug.Log($"[gt] geoao mesh {mf.name}: {col.Length} verts, mean {ms2 / col.Length:F2}");
                    o += col.Length;
                }
                aoBuf?.Release(); aoBuf = new ComputeBuffer(nVert, 4); aoBuf.SetData(aoAll); Shader.SetGlobalBuffer("_GAOBuf", aoBuf);
                Debug.Log($"[gt] geoao: {nVert} verts, {nTri} tris, {meshes.Length} meshes, {directions} dirs, depth {depthRes}, R {R:F2}, ao min {lo:F2} mean {sum / nVert:F2}");
            }
            pb.Release(); nb.Release(); ab.Release(); depth.Release();
            return ms;
        }

        void Start()
        {
            int w = int.Parse(FrameCapturer.Arg("-gtW", "1920")), h = int.Parse(FrameCapturer.Arg("-gtH", "1080"));
            rtA = RT(w, h); rtB = RT(w, h);
            camA.targetTexture = rtA; camA.enabled = false; camB.targetTexture = rtB; camB.enabled = false;
            display.SetTexture("_TexA", rtA); display.SetTexture("_TexB", rtB);
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(new Color(0.40f, 0.47f, 0.60f));
            sh.AddDirectionalLight(Vector3.up, new Color(0.55f, 0.7f, 1.0f), 2.4f);
            sh.AddDirectionalLight(Vector3.down, new Color(0.30f, 0.24f, 0.18f), 0.5f);
            RenderSettings.ambientMode = AmbientMode.Custom; RenderSettings.ambientProbe = sh;
            float first = Bake(false); bakeMs = Bake(true);
            Debug.Log($"[gt] geoao bake ms: first {first:F1} (includes first use), second {bakeMs:F1}");
            Shader.SetGlobalFloat("_GSSAOPow", ssaoPow);
        }

        struct Key { public Vector3 p, t; public Key(Vector3 a, Vector3 b) { p = a; t = b; } }
        static readonly Key K0 = new Key(new Vector3(-0.35f, 1.45f, -1.2f), new Vector3(-1.55f, 0.85f, 2.6f));
        static readonly Key K1 = new Key(new Vector3(1.0f, 1.35f, -1.5f), new Vector3(1.45f, 0.45f, 1.4f));
        static readonly Key K2 = new Key(new Vector3(-0.9f, 0.14f, -0.3f), new Vector3(-0.9f, -0.2f, 0.96f));
        static readonly Key K3 = new Key(new Vector3(0.2f, 2.3f, -2.4f), new Vector3(-0.2f, 0.6f, 1.6f));
        static Key Lerp(Key a, Key b, float s) => new Key(Vector3.Lerp(a.p, b.p, s), Vector3.Lerp(a.t, b.t, s));

        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }

        void LateUpdate()
        {
            if (rtA == null) return;
            float t = FrameCapturer.T;
            int p = t < 3f ? 0 : t < 12f ? 1 : t < 17.5f ? 2 : t < 22.5f ? 3 : 4;
            Key k = Lerp(K0, K1, S((t - 2f) / 10f));
            k = Lerp(k, K2, S((t - 11.5f) / 1.6f));
            k = Lerp(k, K3, S((t - 19.5f) / 2.2f));
            float drift = Mathf.Sin(t * 0.35f) * 0.05f;
            foreach (var c in new[] { camA, camB }) c.transform.SetPositionAndRotation(k.p + new Vector3(drift, 0, 0), Quaternion.LookRotation(k.t - k.p));

            int ma = 1, mb = 1; float split = 0.5f, line = 1;
            switch (p)
            {
                case 0:
                    split = 0f; line = 0;
                    Labels("Ambient occlusion baked into vertices", $"GeoAO: {directions} depth views, {nVert:N0} vertices, computed once at start", "warm sun + blue sky light", "", "");
                    break;
                case 1:
                    ma = 0; split = Mathf.Lerp(0f, 0.5f, S((t - 3f) / 0.8f));
                    Labels("Same scene, same light", "No AO", "sky light reaches every crease", "GeoAO", "sky visibility per vertex");
                    break;
                case 2:
                    ma = 2;
                    Labels("The occluder is out of frame", "URP SSAO", "bench seat off screen: no darkening under it", "GeoAO", "object space: the seat still counts");
                    break;
                case 3:
                    ma = 3; mb = 4;
                    Labels("AO term only", "URP SSAO", "this frame's depth, 0.3 m radius", "GeoAO", $"{nVert:N0} vertices, blended across triangles");
                    break;
                default:
                    split = 0f; line = 0;
                    Labels("GeoAO: per-vertex AO from depth views", "Unity 6.3 URP, after nezix/Unity-GeoAO (MIT)", $"static geometry: bake once ({bakeMs:F0} ms here), not per frame", "", "");
                    break;
            }
            Shader.SetGlobalFloat("_GMode", ma); camA.Render();
            Shader.SetGlobalFloat("_GMode", mb); camB.Render();
            display.SetFloat("_Split", split); display.SetFloat("_Line", line);
            if (p != phase) { phase = p; Debug.Log($"[gt] t {t:F2} phase {p} '{title.text}'"); }
        }

        void OnDestroy() { aoBuf?.Release(); foreach (var r in new[] { rtA, rtB }) if (r) r.Release(); }
    }
}
