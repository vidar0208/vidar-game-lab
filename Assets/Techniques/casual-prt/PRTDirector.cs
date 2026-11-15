// Drives the Casual PRT post. Follows the source's pipeline:
// 1. Precompute (once, at start): every probe of a grid volume samples 512 directions and stores what it sees in each
//    one as a surfel (position, normal, albedo, sky mask). The source renders world-position / normal / albedo
//    cubemaps and samples them on the GPU; here the same 32 x 16 random directions (same formula) are ray cast against
//    the scene's colliders, which returns the nearest surface along each direction, i.e. what the cubemap texel holds.
// 2. Every frame: the source's relight compute shader lights each probe's surfels with the current sun (plus the
//    previous frame's probe GI for further bounces), projects the radiance onto 9 SH coefficients per colour channel
//    and writes them into the volume buffer. The shadow term per surfel is a ray cast toward the sun, made here.
// 3. Every frame: each band's direct-light frame gets the GI added per pixel from the 8 nearest probes
//    (the source's SampleSHVoxel), using world position / normal / albedo rendered with the source's G-buffer shaders.
// Nothing about the lighting is baked: moving or recolouring the sun changes the indirect light in the same frame.
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GameTech.Posts
{
    [DefaultExecutionOrder(-100)]
    public class PRTDirector : MonoBehaviour
    {
        public Camera cam;
        public Material compose, composite, posMat, nrmMat;
        public ComputeShader relight;
        public Light sun;
        public LabelSpec title;
        public LabelSpec[] l1, l2;
        public Vector3 corner = new Vector3(-3.75f, 0.25f, -3.75f);
        public Vector3Int fineSize = new Vector3Int(11, 5, 11), coarseSize = new Vector3Int(4, 2, 4);
        public float fineGrid = 0.75f, coarseGrid = 2.5f;
        public float giIntensity = 3.5f, exposure = 1.7f, skyIntensity = 1f;
        public Color skyColor = new Color(0.1f, 0.14f, 0.22f);

        const int TX = 32, TY = 16, RayNum = TX * TY;

        struct Surfel { public Vector3 position, normal, albedo; public float skyMask; }

        class Volume
        {
            public Vector3 corner; public Vector3Int size; public float grid;
            public Vector3[] probePos;
            public ComputeBuffer[] surfels, shadows;
            public Surfel[][] data;
            public ComputeBuffer cur, last;
            public int[] zeros;
            public NativeArray<RaycastCommand> cmds; public NativeArray<RaycastHit> hits;
            public float[] shadow;
            public int Count => size.x * size.y * size.z;
        }

        Volume fine, coarse;
        ComputeBuffer frozen, dummySH, dummyRadiance;
        readonly int[] zero27 = new int[27];
        int kernel;
        RenderTexture hdr, pos, nrm, alb;
        readonly RenderTexture[] rt = new RenderTexture[3];
        MeshRenderer[] renderers; Material[] litMats, unlitMats, posMats, nrmMats;
        int W, H, logged = -1; bool frozenSet;
        float sunIntensity; Color sunColor;

        void Start()
        {
            W = int.Parse(FrameCapturer.Arg("-gtW", "1920")); H = int.Parse(FrameCapturer.Arg("-gtH", "1080"));
            for (int i = 0; i < 3; i++) { rt[i] = new RenderTexture(W, H, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); rt[i].Create(); }
            hdr = new RenderTexture(W, H, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear); hdr.Create();
            pos = new RenderTexture(W, H, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); pos.Create();
            nrm = new RenderTexture(W, H, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear); nrm.Create();
            alb = new RenderTexture(W, H, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear); alb.Create();
            cam.enabled = false;
            var sh = new SphericalHarmonicsL2();
            RenderSettings.ambientMode = AmbientMode.Custom; RenderSettings.ambientProbe = sh; RenderSettings.reflectionIntensity = 0f;
            sunIntensity = sun.intensity; sunColor = sun.color;

            // material sets for the colour pass and the three G-buffer passes
            var all = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.InstanceID);
            var list = new List<MeshRenderer>();
            foreach (var r in all) if (r.gameObject.layer != 8) list.Add(r);
            renderers = list.ToArray();
            litMats = new Material[renderers.Length]; unlitMats = new Material[renderers.Length];
            posMats = new Material[renderers.Length]; nrmMats = new Material[renderers.Length];
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var cache = new Dictionary<Material, Material>();
            for (int i = 0; i < renderers.Length; i++)
            {
                var m = renderers[i].sharedMaterial; litMats[i] = m;
                if (!cache.TryGetValue(m, out var u)) { u = new Material(unlit); u.SetColor("_BaseColor", m.GetColor("_BaseColor")); cache[m] = u; }
                unlitMats[i] = u; posMats[i] = posMat; nrmMats[i] = nrmMat;
            }

            kernel = relight.FindKernel("CSMain");
            dummySH = new ComputeBuffer(27, sizeof(int));
            dummyRadiance = new ComputeBuffer(RayNum, sizeof(float) * 3);
            var t0 = Time.realtimeSinceStartup;
            fine = Capture(corner, fineSize, fineGrid, 1000);
            coarse = Capture(corner, coarseSize, coarseGrid, 2000);
            frozen = new ComputeBuffer(fine.Count * 27, sizeof(int));
            Debug.Log($"[gt] capture: fine {fine.Count} probes, coarse {coarse.Count} probes, {RayNum} surfels each, {Time.realtimeSinceStartup - t0:F2} s");
        }

        static float Rand(Vector2 uv)
        {
            float d = uv.x * 12.9898f + uv.y * 78.233f;
            float s = Mathf.Sin(d) * 43758.5453f;
            return s - Mathf.Floor(s);
        }

        // the source's UniformSphereSample, including its degrees() on the angle (the angle then wraps in radians)
        static Vector3 SphereSample(float u, float v)
        {
            float phi = Mathf.Rad2Deg * (2f * Mathf.PI * u);
            float cosT = 1f - 2f * v, sinT = Mathf.Sqrt(Mathf.Max(0f, 1f - cosT * cosT));
            return new Vector3(sinT * Mathf.Cos(phi), sinT * Mathf.Sin(phi), cosT);
        }

        Volume Capture(Vector3 c, Vector3Int size, float grid, int seed)
        {
            var v = new Volume { corner = c, size = size, grid = grid };
            int n = v.Count;
            v.probePos = new Vector3[n]; v.surfels = new ComputeBuffer[n]; v.shadows = new ComputeBuffer[n]; v.data = new Surfel[n][];
            v.cur = new ComputeBuffer(n * 27, sizeof(int)); v.last = new ComputeBuffer(n * 27, sizeof(int)); v.zeros = new int[n * 27];
            v.cur.SetData(v.zeros); v.last.SetData(v.zeros);
            v.cmds = new NativeArray<RaycastCommand>(n * RayNum, Allocator.Persistent);
            v.hits = new NativeArray<RaycastHit>(n * RayNum, Allocator.Persistent);
            v.shadow = new float[n * RayNum];
            var albedo = new Dictionary<int, Vector3>();
            var rng = new System.Random(seed);
            for (int x = 0; x < size.x; x++)
                for (int y = 0; y < size.y; y++)
                    for (int z = 0; z < size.z; z++)
                    {
                        int index = x * size.y * size.z + y * size.z + z;
                        var p = c + new Vector3(x, y, z) * grid; v.probePos[index] = p;
                        float seedOff = (float)rng.NextDouble();
                        var s = new Surfel[RayNum];
                        for (int ix = 0; ix < TX; ix++)
                            for (int iy = 0; iy < TY; iy++)
                            {
                                var xy = new Vector2(ix / (float)TX, iy / (float)TY) + Vector2.one * seedOff;
                                var dir = SphereSample(Rand(xy), Rand(xy * 2f)).normalized;
                                int si = ix * TY + iy;
                                if (Physics.Raycast(p, dir, out var hit, 200f))
                                {
                                    int id = hit.collider.GetInstanceID();
                                    if (!albedo.TryGetValue(id, out var a))
                                    {
                                        var col = hit.collider.GetComponent<MeshRenderer>().sharedMaterial.GetColor("_BaseColor").linear;
                                        a = new Vector3(col.r, col.g, col.b); albedo[id] = a;
                                    }
                                    s[si] = new Surfel { position = hit.point, normal = hit.normal, albedo = a, skyMask = 0f };
                                }
                                else s[si] = new Surfel { position = p + dir, normal = Vector3.zero, albedo = Vector3.zero, skyMask = 1f };
                            }
                        v.data[index] = s;
                        v.surfels[index] = new ComputeBuffer(RayNum, 40); v.surfels[index].SetData(s);
                        v.shadows[index] = new ComputeBuffer(RayNum, sizeof(float));
                    }
            return v;
        }

        // shadow term for every surfel: one ray toward the sun
        void Shadows(Volume v, Vector3 toLight)
        {
            var q = QueryParameters.Default;
            for (int k = 0; k < v.Count; k++)
            {
                var s = v.data[k];
                for (int i = 0; i < RayNum; i++)
                    v.cmds[k * RayNum + i] = new RaycastCommand(s[i].position + s[i].normal * 0.02f, toLight, q, 60f);
            }
            RaycastCommand.ScheduleBatch(v.cmds, v.hits, 256, 1).Complete();
            for (int i = 0; i < v.shadow.Length; i++) v.shadow[i] = v.hits[i].distance > 0f ? 0f : 1f;
            for (int k = 0; k < v.Count; k++) v.shadows[k].SetData(v.shadow, k * RayNum, 0, RayNum);
        }

        void Relight(Volume v, CommandBuffer cmd, Vector3 toLight, Color light)
        {
            (v.cur, v.last) = (v.last, v.cur);
            cmd.SetBufferData(v.cur, v.zeros);
            cmd.SetBufferData(dummySH, zero27);
            cmd.SetComputeFloatParam(relight, "_coefficientVoxelGridSize", v.grid);
            cmd.SetComputeVectorParam(relight, "_coefficientVoxelSize", new Vector4(v.size.x, v.size.y, v.size.z, 0));
            cmd.SetComputeVectorParam(relight, "_coefficientVoxelCorner", new Vector4(v.corner.x, v.corner.y, v.corner.z, 0));
            cmd.SetComputeFloatParam(relight, "_GIIntensity", giIntensity);
            cmd.SetComputeFloatParam(relight, "_skyLightIntensity", skyIntensity);
            cmd.SetComputeVectorParam(relight, "_SkyColor", skyColor.linear);
            cmd.SetComputeVectorParam(relight, "_LightDirection", toLight);
            cmd.SetComputeVectorParam(relight, "_LightColor", new Vector4(light.r, light.g, light.b, 0));
            cmd.SetComputeBufferParam(relight, kernel, "_coefficientVoxel", v.cur);
            cmd.SetComputeBufferParam(relight, kernel, "_lastFrameCoefficientVoxel", v.last);
            cmd.SetComputeBufferParam(relight, kernel, "_coefficientSH9", dummySH);
            cmd.SetComputeBufferParam(relight, kernel, "_surfelRadiance", dummyRadiance);
            for (int k = 0; k < v.Count; k++)
            {
                var p = v.probePos[k];
                cmd.SetComputeVectorParam(relight, "_probePos", new Vector4(p.x, p.y, p.z, 1));
                cmd.SetComputeBufferParam(relight, kernel, "_surfels", v.surfels[k]);
                cmd.SetComputeBufferParam(relight, kernel, "_surfelShadow", v.shadows[k]);
                cmd.SetComputeIntParam(relight, "_indexInProbeVolume", k);
                cmd.DispatchCompute(relight, kernel, 1, 1, 1);
            }
        }

        void Swap(Material[] mats) { for (int i = 0; i < renderers.Length; i++) renderers[i].sharedMaterial = mats[i]; }

        void RenderInputs()
        {
            var data = cam.GetUniversalAdditionalCameraData();
            cam.backgroundColor = new Color(0, 0, 0, 0);
            Swap(litMats); data.renderShadows = true; cam.targetTexture = hdr; cam.Render();
            data.renderShadows = false;
            Swap(posMats); cam.targetTexture = pos; cam.Render();
            Swap(nrmMats); cam.targetTexture = nrm; cam.Render();
            Swap(unlitMats); cam.targetTexture = alb; cam.Render();
            Swap(litMats); data.renderShadows = true;
        }

        void Band(int i, int mode, Volume v, ComputeBuffer coeff = null)
        {
            composite.SetTexture("_PosTex", pos); composite.SetTexture("_NrmTex", nrm); composite.SetTexture("_AlbTex", alb);
            composite.SetBuffer("_coefficientVoxel", coeff ?? v.cur);
            composite.SetFloat("_coefficientVoxelGridSize", v.grid);
            composite.SetVector("_coefficientVoxelSize", new Vector4(v.size.x, v.size.y, v.size.z, 0));
            composite.SetVector("_coefficientVoxelCorner", new Vector4(v.corner.x, v.corner.y, v.corner.z, 0));
            composite.SetFloat("_GIIntensity", giIntensity); composite.SetFloat("_Mode", mode); composite.SetFloat("_Exposure", exposure);
            Graphics.Blit(hdr, rt[i], composite);
            compose.SetTexture("_B" + i, rt[i]);
        }

        void Labels(string ttl, float s1, float s2, string[] a, string[] b)
        {
            title.text = ttl;
            float[] start = { 0f, s1, s2 };
            for (int i = 0; i < 3; i++)
            {
                l1[i].text = a != null && i < a.Length ? a[i] : ""; l1[i].anchor = new Vector2(start[i] + 0.012f, 0.058f);
                l2[i].text = b != null && i < b.Length ? b[i] : ""; l2[i].anchor = new Vector2(start[i] + 0.012f, 0.018f);
            }
            compose.SetFloat("_S1", s1); compose.SetFloat("_S2", s2);
        }

        // sun path: a slow drift, then a sweep across the room from 10 s to 16 s, then a colour change after 21 s
        public static Vector2 SunAngles(float t)
        {
            float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 10f, t)) * 0.12f
                    + Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(10f, 16f, t)) * 0.88f;
            float pitch = Mathf.Lerp(16f, 14f, a), yaw = Mathf.Lerp(-40f, -105f, a);
            return new Vector2(pitch, yaw);
        }

        void LateUpdate()
        {
            float t = FrameCapturer.T;
            var ang = SunAngles(t);
            sun.transform.rotation = Quaternion.Euler(ang.x, ang.y, 0f);
            float warm = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(21.5f, 25.5f, t));
            sun.color = Color.Lerp(sunColor, new Color(1f, 0.42f, 0.18f), warm);
            Vector3 toLight = -sun.transform.forward;
            var lc = sun.color.linear * sunIntensity;

            Shadows(fine, toLight); Shadows(coarse, toLight);
            var cmd = new CommandBuffer { name = "PRT relight" };
            Relight(fine, cmd, toLight, lc); Relight(coarse, cmd, toLight, lc);
            Graphics.ExecuteCommandBuffer(cmd); cmd.Release();
            if (t >= 10f && !frozenSet) { var snap = new int[fine.Count * 27]; fine.cur.GetData(snap); frozen.SetData(snap); frozenSet = true; }

            // camera: slow drift along the front of the room, looking into the red / green corner
            float u = Mathf.SmoothStep(0f, 1f, t / 26f);
            cam.transform.position = Vector3.Lerp(new Vector3(2.9f, 1.55f, -3.4f), new Vector3(1.2f, 1.35f, -3.5f), u);
            cam.transform.LookAt(Vector3.Lerp(new Vector3(-1.6f, 1.0f, 1.6f), new Vector3(-2.0f, 0.9f, 1.2f), u));
            RenderInputs();

            float third = 1f / 3f; int phase;
            string fineTxt = $"{fine.Count} probes, {fineGrid:0.##} m apart", coarseTxt = $"{coarse.Count} probes, {coarseGrid:0.#} m apart";
            if (t < 3f)
            {
                phase = 0; Band(0, 1, fine);
                compose.SetVector("_Off", Vector4.zero);
                Labels("Probe GI that follows the light: transfer precomputed, lighting computed every frame", 1f, 1f,
                    new[] { "Direct + probe GI" }, new[] { $"{fineTxt}, {RayNum} surfels each, 9 SH coefficients per colour" });
            }
            else if (t < 10f)
            {
                phase = 1; Band(0, 0, fine); Band(1, 1, fine);
                compose.SetVector("_Off", new Vector4(0.25f, -0.25f, 0, 0));
                Labels("Same frame, same sun (centre of the frame)", 0.5f, 1f,
                    new[] { "Direct light only", "Direct + probe GI" }, new[] { "no ambient, no bounce", "bounce from every surfel, relit this frame" });
            }
            else if (t < 16f)
            {
                phase = 2; Band(0, 1, fine, frozen); Band(1, 1, fine);
                compose.SetVector("_Off", new Vector4(0.25f, -0.25f, 0, 0));
                Labels("The sun moves: frozen GI vs GI relit every frame (centre of the frame)", 0.5f, 1f,
                    new[] { "GI frozen at 10 s", "Relit every frame" }, new[] { "like a bake: bounce stays where the sun was", "same probes, current sun" });
            }
            else if (t < 21f)
            {
                phase = 3; Band(0, 1, coarse); Band(1, 1, fine);
                compose.SetVector("_Off", new Vector4(0.25f, -0.25f, 0, 0));
                Labels("Probe spacing (centre of the frame)", 0.5f, 1f,
                    new[] { "Coarse grid", "Fine grid" }, new[] { coarseTxt, fineTxt });
            }
            else
            {
                phase = 4; Band(0, 2, fine); Band(1, 1, fine);
                compose.SetVector("_Off", new Vector4(0.25f, -0.25f, 0, 0));
                Labels("Change the sun's colour: the bounce changes with it (centre of the frame)", 0.5f, 1f,
                    new[] { "Probe GI only", "Direct + probe GI" }, new[] { "indirect light alone", "no rebake" });
            }
            if (phase != logged) { logged = phase; Debug.Log($"[gt] t {t:F2} phase {phase} sun {ang} '{title.text}' | '{l1[0].text}' '{l2[0].text}' | '{l1[1].text}' '{l2[1].text}' | '{l1[2].text}' '{l2[2].text}'"); }
        }

        void OnDestroy()
        {
            foreach (var v in new[] { fine, coarse })
            {
                if (v == null) continue;
                foreach (var b in v.surfels) b?.Release();
                foreach (var b in v.shadows) b?.Release();
                v.cur?.Release(); v.last?.Release();
                if (v.cmds.IsCreated) v.cmds.Dispose();
                if (v.hits.IsCreated) v.hits.Dispose();
            }
            frozen?.Release(); dummySH?.Release(); dummyRadiance?.Release();
        }
    }
}
