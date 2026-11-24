// Drives the path tracing post. At Start it collects every PTObject's mesh in world space, builds one BVH per
// object, uploads triangles, nodes and materials, times one 1920x1080 sample, then accumulates two still renders
// (with and without the lamp outside the frame). During the video it shows the URP raster camera, those renders,
// and a live accumulation that starts from one sample. The camera does not move (accumulation needs a still view).
// Runs before FrameCapturer (execution order -100).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using GameTech;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class PTDirector : MonoBehaviour
    {
        public Camera cam; public Material display; public ComputeShader pt; public ReflectionProbe probe;
        public int maxBounceCount = 4, numRaysPerPixel = 1, preSamples = 2048, liveMax = 1024;
        public float divergeStrength = 0.3f;
        public LabelSpec title, l1, l2, m1, m2;

        struct GTri { public Vector4 a, b, c, na, nb, nc; }
        struct GNode { public Vector4 mn, mx; public int start, count, p0, p1; }
        struct GModel { public int node, tri, p0, p1; public Vector4 diffuse, emission, specular, absorption, p, q; }

        ComputeBuffer triB, nodeB, modelB; RenderTexture raster, frame, accLive, accFull, accNoLamp;
        int W = 1920, H = 1080, kTrace, kReset, nModels, nModelsNoLamp, phase = -1, liveDone, triCount;
        float msSample; int seed = 12345;

        static float S(float x) { x = Mathf.Clamp01(x); return x * x * x * (x * (x * 6f - 15f) + 10f); }
        static RenderTexture RTF(int w, int h) { var r = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { enableRandomWrite = true }; r.Create(); return r; }

        void Build()
        {
            var objs = new List<PTObject>(FindObjectsByType<PTObject>(FindObjectsSortMode.None));
            objs.Sort((a, b) => a.offscreen.CompareTo(b.offscreen)); // lamp last, so it can be left out by count
            var tris = new List<GTri>(); var nodes = new List<GNode>(); var models = new List<GModel>();
            foreach (var o in objs)
            {
                var mesh = o.GetComponent<MeshFilter>().sharedMesh; var m = o.transform.localToWorldMatrix;
                var v = mesh.vertices; var nrm = mesh.normals; var ind = mesh.triangles;
                for (int i = 0; i < v.Length; i++) { v[i] = m.MultiplyPoint3x4(v[i]); nrm[i] = m.MultiplyVector(nrm[i]).normalized; }
                var bvh = new PTBVH(v, ind);
                var gm = new GModel
                {
                    node = nodes.Count, tri = tris.Count,
                    diffuse = o.diffuse, emission = o.emission, specular = o.specular, absorption = o.absorption,
                    p = new Vector4(o.absorptionStrength, o.emissionStrength, o.smoothness, o.specularProbability), q = new Vector4(o.ior, o.flag, 0, 0)
                };
                foreach (int t in bvh.order)
                {
                    int a = ind[t * 3], b = ind[t * 3 + 1], c = ind[t * 3 + 2];
                    tris.Add(new GTri { a = v[a], b = v[b], c = v[c], na = nrm[a], nb = nrm[b], nc = nrm[c] });
                }
                foreach (var n in bvh.nodes) nodes.Add(new GNode { mn = n.min, mx = n.max, start = n.start, count = n.count });
                models.Add(gm);
            }
            nModels = models.Count; nModelsNoLamp = objs.FindAll(o => !o.offscreen).Count; triCount = tris.Count;
            triB = new ComputeBuffer(tris.Count, 96); triB.SetData(tris);
            nodeB = new ComputeBuffer(nodes.Count, 48); nodeB.SetData(nodes);
            modelB = new ComputeBuffer(models.Count, 112); modelB.SetData(models);
            Debug.Log($"[gt] pathtrace: {nModels} objects, {tris.Count} triangles, {nodes.Count} BVH nodes");
        }

        void Sample(RenderTexture acc, int frameIndex, int models)
        {
            float ph = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 2f;
            pt.SetBuffer(kTrace, "ModelInfo", modelB); pt.SetBuffer(kTrace, "Triangles", triB); pt.SetBuffer(kTrace, "Nodes", nodeB);
            pt.SetTexture(kTrace, "FrameRender", frame); pt.SetTexture(kTrace, "AccumulatedRender", acc);
            pt.SetInts("Resolution", W, H); pt.SetInt("MaxBounceCount", maxBounceCount); pt.SetInt("NumRaysPerPixel", numRaysPerPixel);
            pt.SetInt("Frame", frameIndex); pt.SetInt("renderSeed", seed); pt.SetInt("modelCount", models);
            pt.SetFloat("DivergeStrength", divergeStrength);
            pt.SetVector("ViewParams", new Vector3(ph * W / (float)H, ph, 1f));
            pt.SetMatrix("CamLocalToWorldMatrix", cam.transform.localToWorldMatrix);
            pt.Dispatch(kTrace, (W + 7) / 8, (H + 7) / 8, 1);
        }

        void Clear(RenderTexture acc) { pt.SetTexture(kReset, "AccumulatedRender", acc); pt.SetInts("Resolution", W, H); pt.Dispatch(kReset, (W + 7) / 8, (H + 7) / 8, 1); }

        void Start()
        {
            kTrace = pt.FindKernel("RayTrace"); kReset = pt.FindKernel("ResetAccumulated");
            raster = new RenderTexture(W, H, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { antiAliasing = 4 }; raster.Create();
            frame = RTF(W, H); accLive = RTF(W, H); accFull = RTF(W, H); accNoLamp = RTF(W, H);
            cam.targetTexture = raster; cam.enabled = false;
            if (probe) probe.RenderProbe();
            Build();

            Clear(accLive); Sample(accLive, 0, nModels); AsyncGPUReadback.Request(frame, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 1; i <= 20; i++) Sample(accLive, i, nModels);
            AsyncGPUReadback.Request(frame, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
            msSample = (float)sw.Elapsed.TotalMilliseconds / 20f;
            Debug.Log($"[gt] pathtrace: 1920x1080, 1 sample per pixel, {maxBounceCount} bounces: {msSample:F1} ms per sample (GPU, waited, avg of 20)");

            Clear(accFull); Clear(accNoLamp);
            for (int i = 0; i < preSamples; i++)
            {
                Sample(accFull, 1000 + i, nModels); Sample(accNoLamp, 1000 + i, nModelsNoLamp);
                if ((i & 31) == 31) AsyncGPUReadback.Request(frame, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
            }
            Clear(accLive); liveDone = 0;
        }

        void Labels(string t, string a1, string a2, string b1, string b2) { title.text = t; l1.text = a1; l2.text = a2; m1.text = b1; m2.text = b2; }

        void LateUpdate()
        {
            if (frame == null) return;
            float t = FrameCapturer.T;
            int p = t < 3f ? 0 : t < 11f ? 1 : t < 19f ? 2 : t < 23f ? 3 : 4;
            Texture A = raster, B = accFull; float sA = 1, sB = 1f / preSamples, split = 0.5f, line = 1;
            switch (p)
            {
                case 0:
                    split = 0; line = 0;
                    Labels("Path traced in a compute shader", $"Whole scene in a BVH: {triCount:N0} triangles, {maxBounceCount} bounces", $"{preSamples:N0} samples per pixel, averaged", "", "");
                    break;
                case 1:
                    cam.Render();
                    Labels("Same scene, same camera", "URP raster", "2 lights with shadow maps + reflection probe", "Path traced", "every bounce of light is traced");
                    break;
                case 2:
                {
                    int target = Mathf.Max(1, Mathf.RoundToInt(Mathf.Pow(liveMax, S((t - 12f) / 6.5f))));
                    while (liveDone < target) { Sample(accLive, 5000 + liveDone, nModels); liveDone++; }
                    A = frame; B = accLive; sB = 1f / liveDone;
                    Labels("Noise goes down as samples add up", "This frame only", "1 sample per pixel", "Average of all frames so far", liveDone == 1 ? "1 sample per pixel" : $"{liveDone:N0} samples per pixel");
                    break;
                }
                case 3:
                    A = accNoLamp; sA = 1f / preSamples;
                    Labels("Light from outside the frame", "Lamp outside the frame removed", "", "Lamp outside the frame", "its orange light still reaches the room");
                    break;
                default:
                    split = 0; line = 0;
                    Labels("Full-scene path tracing", "Unity 6.3 URP compute shader, after SebLague/Ray-Tracing (MIT)", $"1920x1080: {msSample:F0} ms per sample on an Apple M4 Mac mini (Metal), {preSamples:N0} samples = {preSamples * msSample / 1000f:F0} s", "", "");
                    break;
            }
            display.SetTexture("_TexA", A); display.SetTexture("_TexB", B); display.SetFloat("_ScaleA", sA); display.SetFloat("_ScaleB", sB);
            display.SetFloat("_Split", split); display.SetFloat("_Line", line);
            if (p != phase) { phase = p; Debug.Log($"[gt] t {t:F2} phase {p} '{title.text}' live {liveDone}"); }
        }

        void OnDestroy()
        {
            foreach (var b in new[] { triB, nodeB, modelB }) b?.Release();
            foreach (var r in new[] { raster, frame, accLive, accFull, accNoLamp }) if (r) r.Release();
        }
    }
}
